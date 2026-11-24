using System.Text;
using System.Text.Json;
using KiroPet;

if (args.Length == 1 && args[0] == "--tcp-helper")
{
    var server = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    try
    {
        server.Start();
        Console.WriteLine(((System.Net.IPEndPoint)server.LocalEndpoint).Port);
        Console.Out.Flush();
        Console.ReadLine();
    }
    finally { server.Stop(); }
    return 0;
}

string temporary = Path.Combine(Path.GetTempPath(), "kiro-pet-csharp-check-" + Guid.NewGuid().ToString("N"));
string? oldData = Environment.GetEnvironmentVariable("KIRO_PET_HOME");
Directory.CreateDirectory(temporary);
Environment.SetEnvironmentVariable("KIRO_PET_HOME", Path.Combine(temporary, "data"));
NativeReader? reader = null;
System.Net.Sockets.TcpListener? windowServer = null;
System.Diagnostics.Process? agentHelper = null;
try
{
    var primary = new DisplayArea(0, 0, 1920, 1152, 1, true);
    Assert(PetPosition.Restore(734, 1905, 176, 222, [primary]) == new DesktopPoint(1720, 906),
        "Saved position on a removed monitor was not recovered.");
    Assert(PetPosition.Restore(734, 1905, 176, 222, [primary, new(0, 1200, 1920, 1152, 1)]) == new DesktopPoint(734, 1905),
        "Position on a connected secondary monitor changed.");
    Assert(PetPosition.Restore(-1000, 200, 176, 222, [primary, new(-1920, 0, 1920, 1080, 1.5)]) == new DesktopPoint(-1500, 300),
        "Secondary display scaling or negative coordinates were lost.");
    Assert(PetPosition.Restore(1900, 1100, 176, 222, [primary]) == new DesktopPoint(1744, 930),
        "Partially off-screen pet was not moved fully onto its display.");
    Assert(PetPosition.Restore(double.NaN, 200, 176, 222, [primary]) == new DesktopPoint(1720, 906),
        "Invalid coordinates did not recover.");
    Console.WriteLine("Monitor layout and saved position PASS");

    Directory.CreateDirectory(PetPaths.Data);
    File.WriteAllText(PetPaths.InData("settings.json"), """{"speed":0.2,"duration":45,"reducedMotion":false,"pets":{"usagi":{"blocked":["worry"]}}}""");
    var settings = PetStore.Settings();
    Assert(settings.Speed == 0.2 && settings.Duration == 45 && settings.Pets["usagi"]["blocked"][0] == "worry", "PowerShell settings were not preserved.");
    PetStore.Write("selected-pet.json", new Selection { Key = "usagi", DisplayName = "Usagi" });
    Assert(PetStore.Read("selected-pet.json", new Selection()).Key == "usagi", "Pet selection did not persist.");
    Console.WriteLine("Settings compatibility PASS");

    string native = Path.Combine(temporary, "native");
    string a = Fixture("sess_check-a", "kiro-ide");
    string b = Fixture("sess_check-b", "kiro-cli");
    Event(a, "turn_start", "a");
    Event(b, "turn_start", "b");
    reader = new NativeReader(native, Path.Combine(temporary, "cache.json"), true);
    var snapshot = reader.ReadNow();
    Assert(snapshot.Sessions.Length == 2 && snapshot.Sessions.All(session => session.Status == "working"), "Running turns were not recovered.");
    Assert(snapshot.Sessions.All(session => session.OwnerPid == Environment.ProcessId), "Native ownership was not retained for IDE window routing.");
    Event(a, "turn_end", "wrong", new() { ["stopReason"] = "end_turn" });
    Assert(Session(reader.ReadNow(), "sess_check-a").Status == "working", "Old execution finished a new turn.");
    Event(a, "pending_interaction", "a", new() { ["toolCallId"] = "approval" });
    Assert(SessionPolicy.Summarize(reader.ReadNow().Sessions, new()).Status == "waiting", "Approval priority failed.");
    Event(a, "interaction_resolved", "a", new() { ["toolCallId"] = "approval", ["outcome"] = "accepted" });
    Event(a, "turn_end", "a", new() { ["stopReason"] = "end_turn" });
    snapshot = reader.ReadNow();
    var done = Session(snapshot, "sess_check-a");
    Assert(done.CompletionId == "a:a", "Native completion identity changed.");
    var acknowledged = new Dictionary<string, string>();
    Assert(!SessionPolicy.MarkRead(snapshot.Sessions, acknowledged, done.Key, "old-completion"), "A stale click acknowledged a newer result.");
    Assert(SessionPolicy.MarkRead(snapshot.Sessions, acknowledged, done.Key, done.CompletionId), "The selected result could not be acknowledged.");
    Assert(!SessionPolicy.MarkRead(snapshot.Sessions, acknowledged, done.Key, done.CompletionId), "Repeated acknowledgement changed state.");
    Assert(SessionPolicy.Summarize(snapshot.Sessions, acknowledged).Status == "working", "One acknowledgement hid the other session.");
    using (var restarted = new NativeReader(native, Path.Combine(temporary, "cache.json"), false))
        Assert(Session(restarted.ReadNow(), "sess_check-a").CompletionId == done.CompletionId, "Restart changed completion identity.");
    Event(b, "sub_agent_start", "b", new() { ["subSessionId"] = "child", ["parentExecutionId"] = "b" });
    Event(b, "pending_interaction", "b", new() { ["toolCallId"] = "child-approval" }, "child");
    Assert(Session(reader.ReadNow(), "sess_check-b").Status == "waiting", "Delegated approval was not observed.");
    Event(b, "turn_end", "b", new() { ["stopReason"] = "end_turn" }, "child");
    Assert(Session(reader.ReadNow(), "sess_check-b").Status == "waiting", "Child completed its parent.");
    Event(b, "interaction_resolved", "b", new() { ["toolCallId"] = "child-approval", ["outcome"] = "accepted" }, "child");
    Event(b, "turn_end", "b", new() { ["stopReason"] = "failed" });
    snapshot = reader.ReadNow();
    var failure = Session(snapshot, "sess_check-b");
    Assert(SessionPolicy.Summarize(snapshot.Sessions, acknowledged).Status == "error", "Terminal error priority failed.");
    acknowledged[failure.Key] = failure.CompletionId;
    Assert(SessionPolicy.Summarize(snapshot.Sessions, acknowledged).Status == "idle", "Terminal error could not be acknowledged.");
    Console.WriteLine("Native session and acknowledgement PASS");

    var autoRead = new Dictionary<string, string>();
    Assert(!SessionPolicy.MarkViewed(snapshot.Sessions, autoRead, new IdeViewed(failure.SessionId, failure.CompletionId)), "An IDE focus event acknowledged a CLI session.");
    Assert(!SessionPolicy.MarkViewed(snapshot.Sessions, autoRead, new IdeViewed(done.SessionId, "stale")), "A stale IDE focus acknowledged a newer result.");
    Assert(SessionPolicy.MarkViewed(snapshot.Sessions, autoRead, new IdeViewed(done.SessionId, done.CompletionId)), "The visible IDE result was not acknowledged.");
    Assert(autoRead.Count == 1 && !autoRead.ContainsKey(failure.Key), "Viewing one IDE result dismissed another agent.");
    Console.WriteLine("Individual click and IDE focus acknowledgement PASS");

    using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
    long startedAt = new DateTimeOffset(currentProcess.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds();
    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    if (OperatingSystem.IsWindows())
    {
        string host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ??
            Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
        var helperStart = new System.Diagnostics.ProcessStartInfo(host)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        helperStart.ArgumentList.Add(typeof(Program).Assembly.Location);
        helperStart.ArgumentList.Add("--tcp-helper");
        agentHelper = System.Diagnostics.Process.Start(helperStart)!;
        var portLine = agentHelper.StandardOutput.ReadLineAsync();
        Assert(portLine.Wait(10000) && int.TryParse(portLine.Result, out _), "The TCP owner test helper did not start.");
        int agentPort = int.Parse(portLine.Result!);
        now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var endpoint = new IdeEndpoint(Environment.ProcessId, startedAt, now,
            "kiro://jaeskang0.kiroling-bridge/open?windowId=1", [temporary], [done.SessionId], 1, agentPort);
        string endpointFile = PetPaths.InData(Path.Combine("ide-endpoints", Environment.ProcessId + ".json"));
        var remoteAgent = new NativeSession { SessionId = done.SessionId, Source = "ide", Workspace = temporary, OwnerPid = agentHelper.Id };
        PetStore.WriteFile(endpointFile, endpoint);
        Assert(agentHelper.Id != endpoint.Pid && IdeBridge.Locate(remoteAgent)?.Pid == Environment.ProcessId,
            "The owning IDE window was not found when its agent and bridge use different processes.");
        Assert(IdeBridge.Locate(done) == null, "A different agent process was accepted for this IDE window.");
        string ambiguousFile = PetPaths.InData(Path.Combine("ide-endpoints", agentHelper.Id + ".json"));
        PetStore.WriteFile(ambiguousFile, endpoint with
        {
            Pid = agentHelper.Id,
            StartedAt = new DateTimeOffset(agentHelper.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds(),
            WindowId = 2, Uri = "kiro://jaeskang0.kiroling-bridge/open?windowId=2"
        });
        Assert(IdeBridge.Locate(remoteAgent) == null, "Ambiguous IDE windows silently selected the first match.");
        File.Delete(ambiguousFile);
        PetStore.WriteFile(endpointFile, endpoint with { UpdatedAt = now - 30000 });
        Assert(IdeBridge.Locate(remoteAgent) == null, "A stale IDE endpoint was accepted.");
        PetStore.WriteFile(endpointFile, endpoint with { Uri = "kiro://other-extension/open" });
        Assert(IdeBridge.Locate(remoteAgent) == null, "An unrelated extension link was accepted.");
        agentHelper.StandardInput.Close();
        Assert(agentHelper.WaitForExit(5000), "The TCP owner helper did not stop.");
        agentHelper.Dispose();
        agentHelper = null;
        windowServer = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        windowServer.Start();
        PetStore.WriteFile(endpointFile, endpoint with { AgentPort = ((System.Net.IPEndPoint)windowServer.LocalEndpoint).Port });
        Console.WriteLine("Separate IDE agent/bridge processes and stale endpoint rejection PASS");
    }

    string recovering = Fixture("sess_recovering", "kiro-ide", "working");
    File.Delete(Path.Combine(recovering, "messages.jsonl"));
    Event(a, "turn_start", "cache-refresh");
    Event(a, "turn_end", "cache-refresh", new() { ["stopReason"] = "end_turn" });
    Thread.Sleep(1050);
    var recoveringSnapshot = reader.ReadNow();
    Assert(recoveringSnapshot.Recovering, "Recovery fixture did not exercise a missing event file.");
    IdeBridge.Publish(recoveringSnapshot.Sessions);
    var shared = PetStore.Read("ide-status.json", Array.Empty<IdeStatus>());
    Assert(shared.Single(session => session.SessionId == "sess_check-a").CompletionId == "cache-refresh:cache-refresh",
        "A recovering session left another IDE completion stale in the shared status file.");
    Assert(!shared.Any(session => session.SessionId == "sess_check-b"), "The IDE bridge received a CLI session.");
    if (OperatingSystem.IsWindows())
        Assert(shared.Single(session => session.SessionId == "sess_check-a").WindowId == 1, "The current IDE status was not mapped to its window.");
    Console.WriteLine("Current bridge status during recovery PASS");

    File.WriteAllText(Path.Combine(a, "messages.jsonl"), "");
    Event(a, "turn_start", "replacement");
    Event(a, "turn_end", "replacement", new() { ["stopReason"] = "end_turn" });
    Assert(Session(reader.ReadNow(), "sess_check-a").CompletionId == "replacement:replacement", "Truncated file did not recover.");
    var lease = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(Path.Combine(native, "session-leases", "sess_check-b.lease")))!;
    var invalid = lease.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
    invalid["acquiredAt"] = "2000-01-01T00:00:00Z";
    PetStore.WriteFile(Path.Combine(native, "session-leases", "sess_check-b.lease"), invalid);
    Thread.Sleep(1050);
    Assert(Session(reader.ReadNow(), "sess_check-b").Disconnected, "Stale lease stayed live.");
    Console.WriteLine("Rotation and stale ownership PASS");
    return 0;

    string Fixture(string id, string client, string initialStatus = "idle")
    {
        string directory = Path.Combine(native, "sessions", "group", id);
        Directory.CreateDirectory(directory);
        PetStore.WriteFile(Path.Combine(directory, "session.json"), new
        {
            schemaVersion = "1.0.0", dataModelVersion = 1, id, title = id,
            workspacePaths = new[] { temporary }, status = initialStatus
        });
        PetStore.WriteFile(Path.Combine(native, "session-leases", id + ".lease"), new
        {
            pid = Environment.ProcessId, parentPid = Environment.ProcessId,
            clientName = client, acquiredAt = DateTime.UtcNow.ToString("O")
        });
        File.WriteAllText(Path.Combine(directory, "messages.jsonl"), "");
        return directory;
    }

    void Event(string directory, string type, string execution, Dictionary<string, object>? extra = null, string? child = null)
    {
        var payload = new Dictionary<string, object> { ["type"] = type, ["executionId"] = execution, ["turnId"] = execution };
        if (extra != null) foreach (var pair in extra) payload[pair.Key] = pair.Value;
        string file = child == null ? Path.Combine(directory, "messages.jsonl") : Path.Combine(directory, "sub-executions", child + ".jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.AppendAllText(file, JsonSerializer.Serialize(new { timestamp = DateTime.UtcNow.ToString("O"), payload }) + "\n", new UTF8Encoding(false));
    }
}
finally
{
    windowServer?.Stop();
    if (agentHelper != null)
    {
        if (!agentHelper.HasExited) agentHelper.Kill(true);
        agentHelper.Dispose();
    }
    reader?.Dispose();
    Environment.SetEnvironmentVariable("KIRO_PET_HOME", oldData);
    string full = Path.GetFullPath(temporary);
    if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
        Path.GetFileName(full).StartsWith("kiro-pet-csharp-check-"))
        Directory.Delete(full, true);
}

static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static NativeSession Session(NativeSnapshot snapshot, string id) => snapshot.Sessions.Single(session => session.SessionId == id);
