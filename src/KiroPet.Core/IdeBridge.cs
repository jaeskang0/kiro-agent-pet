using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace KiroPet;

public sealed record IdeOpenResult(bool Opened, string Message);
public sealed record IdeReply(string RequestId, string SessionId, int BridgePid, bool Opened, string Message);
public sealed record IdeViewed(string SessionId, string CompletionId);
public sealed record IdeStatus(string SessionId, string Status, string CompletionId, int WindowId, int AgentPid);
public sealed record IdeEndpoint(int Pid, long StartedAt, long UpdatedAt, string Uri, string[] WorkspacePaths, string[] SessionIds, int WindowId, int AgentPort);
public sealed record IdeSetupResult(bool Changed, bool ReloadRequired, string Message);

public static class IdeBridge
{
    private static string Editor => Environment.GetEnvironmentVariable("KIRO_EDITOR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Kiro", "Kiro.exe");

    public static void Publish(IEnumerable<NativeSession> sessions)
    {
        var endpoints = ReadEndpoints();
        var owners = SocketOwners.Read();
        PetStore.Write("ide-status.json", sessions.Where(session => session.Source == "ide").Select(session =>
        {
            var endpoint = Locate(session, endpoints, owners);
            return new IdeStatus(session.SessionId, session.Status, session.CompletionId,
                endpoint?.WindowId ?? 0, endpoint == null ? 0 : owners.GetValueOrDefault(endpoint.AgentPort));
        }).ToArray());
    }

    private static List<IdeEndpoint> ReadEndpoints()
    {
        string directory = PetPaths.InData("ide-endpoints");
        if (!Directory.Exists(directory)) return [];
        var candidates = new List<IdeEndpoint>();
        foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                if (new FileInfo(file).Length > 8192) continue;
                var endpoint = JsonSerializer.Deserialize<IdeEndpoint>(File.ReadAllText(file), PetStore.Json);
                if (endpoint == null || endpoint.Pid <= 0 || endpoint.WorkspacePaths == null ||
                    endpoint.WindowId <= 0 || endpoint.AgentPort is <= 0 or > 65535 ||
                    endpoint.StartedAt <= 0 || endpoint.UpdatedAt <= 0 ||
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - endpoint.UpdatedAt is < -1000 or > 10000 ||
                    !Uri.TryCreate(endpoint.Uri, UriKind.Absolute, out var uri) ||
                    uri.Scheme != "kiro" || uri.Host != "jaeskang0.kiroling-bridge" || uri.AbsolutePath != "/open") continue;
                using var process = Process.GetProcessById(endpoint.Pid);
                long started = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds();
                if (process.HasExited || Math.Abs(started - endpoint.StartedAt) > 5000) continue;
                if (!endpoint.WorkspacePaths.All(Path.IsPathFullyQualified)) continue;
                candidates.Add(endpoint with { WorkspacePaths = endpoint.WorkspacePaths.Select(Path.GetFullPath).ToArray() });
            }
            catch (Exception error) when (error is IOException or JsonException or ArgumentException or System.ComponentModel.Win32Exception) { }
        }
        return candidates;
    }

    public static IdeEndpoint Locate(NativeSession session) => Locate(session, ReadEndpoints(), SocketOwners.Read());

    private static IdeEndpoint Locate(NativeSession session, IEnumerable<IdeEndpoint> endpoints, IReadOnlyDictionary<int, int> owners)
    {
        var candidates = endpoints.Where(endpoint => owners.GetValueOrDefault(endpoint.AgentPort) > 0 &&
            endpoint.WorkspacePaths.Any(path => string.Equals(path.TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(session.Workspace).TrimEnd(Path.DirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))).ToList();
        if (session.OwnerPid > 0 && !session.Closed)
        {
            var owned = candidates.Where(endpoint => owners.GetValueOrDefault(endpoint.AgentPort) == session.OwnerPid).ToArray();
            if (owned.Length == 1) return owned[0];
            var open = owned.Where(endpoint => endpoint.SessionIds?.Contains(session.SessionId) == true).ToArray();
            return open.Length == 1 ? open[0] : null;
        }
        var visible = candidates.Where(endpoint => endpoint.SessionIds?.Contains(session.SessionId) == true).ToArray();
        return visible.Length == 1 ? visible[0] : candidates.Count == 1 ? candidates[0] : null;
    }

    public static async Task<IdeOpenResult> Open(NativeSession session)
    {
        if (session.Source != "ide") return new(false, "Exact CLI terminal navigation is not available yet.");
        var endpoint = Locate(session);
        if (endpoint == null && session.Closed && OperatingSystem.IsWindows())
        {
            try
            {
                if (!File.Exists(Editor)) return new(false, "Kiro IDE is not installed at the expected location.");
                var start = new ProcessStartInfo(Editor) { UseShellExecute = false };
                start.ArgumentList.Add("--new-window");
                start.ArgumentList.Add(session.Workspace);
                using var launched = Process.Start(start);
                var connectionDeadline = DateTime.UtcNow.AddSeconds(30);
                while (endpoint == null && DateTime.UtcNow < connectionDeadline)
                {
                    await Task.Delay(200);
                    endpoint = Locate(session);
                }
            }
            catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception)
            {
                return new(false, error.Message);
            }
        }
        if (endpoint == null) return new(false, "The helper for this Kiro window is not connected. If automatic setup is still running, wait for it to finish. Otherwise reload this IDE window. Connection health in the pet menu shows setup errors.");
        int agentPid = SocketOwners.Read().GetValueOrDefault(endpoint.AgentPort);
        if (agentPid <= 0) return new(false, "Kiro's agent connection changed. Try opening the chat again.");
        string id = Guid.NewGuid().ToString("N");
        string replyFile = PetPaths.InData(Path.Combine("ide-replies", id + ".json"));
        var address = new UriBuilder(endpoint.Uri);
        string query = address.Query.TrimStart('?');
        address.Query = (query.Length > 0 ? query + "&" : "") +
            "sessionId=" + Uri.EscapeDataString(session.SessionId) + "&requestId=" + id +
            "&ownerPid=" + agentPid +
            "&targetWindowId=" + endpoint.WindowId + "&agentPort=" + endpoint.AgentPort;
        string link = address.Uri.AbsoluteUri;
        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(replyFile))
                {
                    if (new FileInfo(replyFile).Length > 4096) return new(false, "The IDE returned an invalid reply.");
                    var reply = JsonSerializer.Deserialize<IdeReply>(File.ReadAllText(replyFile), PetStore.Json);
                    if (reply?.RequestId != id || reply.SessionId != session.SessionId || reply.BridgePid != endpoint.Pid)
                        return new(false, "The IDE did not confirm the requested chat.");
                    return new(reply.Opened, reply.Message ?? "");
                }
                await Task.Delay(100);
            }
            return new(false, "Kiro did not confirm opening this chat. Reload this IDE window, then try again.");
        }
        catch (Exception error) when (error is IOException or JsonException or System.ComponentModel.Win32Exception)
        {
            return new(false, error.Message);
        }
        finally { if (File.Exists(replyFile)) File.Delete(replyFile); }
    }

    public static IEnumerable<IdeViewed> TakeViewed()
    {
        string directory = PetPaths.InData("ide-viewed");
        if (!Directory.Exists(directory)) yield break;
        foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
        {
            IdeViewed viewed = null;
            try
            {
                if (new FileInfo(file).Length <= 4096)
                    viewed = JsonSerializer.Deserialize<IdeViewed>(File.ReadAllText(file), PetStore.Json);
                File.Delete(file);
            }
            catch (IOException) { continue; }
            catch (JsonException) { File.Delete(file); }
            if (viewed?.SessionId != null && viewed.CompletionId != null) yield return viewed;
        }
    }

    public static async Task<IdeSetupResult> EnsureInstalled()
    {
        if (!OperatingSystem.IsWindows())
            return new(false, false, "Automatic IDE helper setup currently supports Windows only.");
        string editor = Editor;
        string cli = Path.Combine(Path.GetDirectoryName(editor), "resources", "app", "out", "cli.js");
        string vsix = Environment.GetEnvironmentVariable("KIRO_PET_IDE_VSIX") ?? Path.Combine(AppContext.BaseDirectory, "kiroling-bridge.vsix");
        if (!File.Exists(editor) || !File.Exists(cli))
            return new(false, false, "Kiro IDE was not found. CLI monitoring works independently. Restart Kiro Agent Pet after installing Kiro IDE.");
        if (!File.Exists(vsix)) throw new FileNotFoundException("The IDE bridge is missing. Use the complete Kiro Agent Pet ZIP.");
        bool editorRunning = false;
        foreach (var candidate in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(editor)))
        {
            using (candidate)
            {
                try { editorRunning |= string.Equals(candidate.MainModule?.FileName, editor, StringComparison.OrdinalIgnoreCase); }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        }
        bool changed = await EnsureInstalled(vsix, arguments => RunEditorCli(editor, cli, arguments));
        return new(changed, changed && editorRunning, changed
            ? editorRunning
                ? "Kiro helper installed or updated. In each open Kiro window, use Ctrl+Shift+P → Developer: Reload Window."
                : "Kiro helper installed or updated. Open Kiro normally."
            : "Kiro helper is up to date.");
    }

    internal static async Task<bool> EnsureInstalled(string vsix, Func<IReadOnlyList<string>, Task<string>> run)
    {
        using var archive = ZipFile.OpenRead(vsix);
        var entry = archive.GetEntry("extension/package.json") ?? throw new InvalidDataException("The bundled IDE helper has no manifest.");
        using var stream = entry.Open();
        using var manifest = JsonDocument.Parse(stream);
        var package = manifest.RootElement;
        const string id = "jaeskang0.kiroling-bridge";
        if (package.GetProperty("publisher").GetString() + "." + package.GetProperty("name").GetString() != id ||
            !Version.TryParse(package.GetProperty("version").GetString(), out var bundled))
            throw new InvalidDataException("The bundled IDE helper has an invalid identity or version.");
        async Task<Version> InstalledVersion()
        {
            string output = await run(["--list-extensions", "--show-versions"]);
            Version newest = null;
            foreach (string line in output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.StartsWith(id + "@", StringComparison.OrdinalIgnoreCase)) continue;
                if (!Version.TryParse(line[(id.Length + 1)..], out var version))
                    throw new InvalidDataException("Kiro reported an invalid helper version; the installed helper was left alone.");
                if (newest == null || version > newest) newest = version;
            }
            return newest;
        }
        if (await InstalledVersion() is { } installed && installed >= bundled) return false;
        await run(["--install-extension", vsix, "--force"]);
        if (await InstalledVersion() is not { } updated || updated < bundled)
            throw new InvalidOperationException("Kiro did not confirm installing the IDE helper. Restart Kiro Agent Pet to retry.");
        return true;
    }

    private static async Task<string> RunEditorCli(string editor, string cli, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(editor)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Environment["ELECTRON_RUN_AS_NODE"] = "1";
        start.ArgumentList.Add(cli);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Kiro's extension setup command did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            throw new TimeoutException("Kiro helper setup timed out. Restart Kiro Agent Pet to retry.");
        }
        if (process.ExitCode != 0) throw new InvalidOperationException("Kiro helper setup failed. " + await error);
        return await output;
    }
}
