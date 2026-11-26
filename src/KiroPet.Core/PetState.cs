using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KiroPet;

public static class PetPaths
{
    public static string Data => Path.GetFullPath(Environment.GetEnvironmentVariable("KIRO_PET_HOME") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KiroPet"));
    public static string Native => Path.GetFullPath(Environment.GetEnvironmentVariable("KIRO_NATIVE_HOME") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kiro"));
    public static string InData(string name) => Path.Combine(Data, name);
    public static string MutexName
    {
        get
        {
            string identity = Path.GetFullPath(Data).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16].ToLowerInvariant();
            return OperatingSystem.IsWindows() ? @"Local\KiroPetApp-" + hash : "KiroPetApp-" + hash;
        }
    }
}

public sealed class PetSettings
{
    public const int CurrentSpeedScale = 2;
    public const double MinimumSpeed = 0.1, MaximumSpeed = 10;
    public double Speed { get; set; } = 1;
    public int SpeedScaleVersion { get; set; }
    [JsonIgnore]
    public double FrameMilliseconds => 900 / Speed;
    public double Duration { get; set; } = 14;
    public bool ReducedMotion { get; set; }
    public Dictionary<string, Dictionary<string, string[]>> Pets { get; set; } = new();
    public Dictionary<string, bool> GeneratedMessages { get; set; } = new();

    public bool UseGeneratedMessages(string petKey) =>
        !GeneratedMessages.TryGetValue(petKey, out bool enabled) || enabled;

    public string ClickReaction(string petKey, IEnumerable<string> availableActions)
    {
        var available = availableActions.ToHashSet();
        if (Pets.TryGetValue(petKey, out var mapping) && mapping.TryGetValue("click", out var selected) &&
            selected.FirstOrDefault(available.Contains) is { } action) return action;
        return available.Contains("waving") ? "waving" : "idle";
    }
}

public sealed class Selection
{
    public string Key { get; set; } = "spark";
    public string DisplayName { get; set; } = "Spark";
}

public sealed class RunningPet
{
    public int Pid { get; set; }
    public string Started { get; set; }
    public string Engine { get; set; } = "powershell";
}

public static class PetStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static T Read<T>(string name, T fallback)
    {
        string file = PetPaths.InData(name);
        if (!File.Exists(file)) return fallback;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json) ?? fallback; }
        catch (JsonException) { throw new InvalidDataException($"Invalid {name}. Restore or correct this settings file."); }
    }

    public static void Write<T>(string name, T value)
    {
        Directory.CreateDirectory(PetPaths.Data);
        WriteFile(PetPaths.InData(name), value);
    }

    public static void WriteFile<T>(string file, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false));
            for (int attempt = 0; ; attempt++)
            {
                try { File.Move(temporary, file, true); return; }
                catch (IOException) when (attempt < 4) { Thread.Sleep(40); }
                catch (UnauthorizedAccessException) when (attempt < 4) { Thread.Sleep(40); }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static PetSettings Settings()
    {
        var settings = Read("settings.json", new PetSettings { SpeedScaleVersion = PetSettings.CurrentSpeedScale });
        settings.GeneratedMessages ??= new();
        bool legacy = settings.SpeedScaleVersion == 0;
        if (!double.IsFinite(settings.Speed) || settings.Speed < PetSettings.MinimumSpeed ||
            settings.Speed > (legacy ? 2 : PetSettings.MaximumSpeed) ||
            !double.IsFinite(settings.Duration) || settings.Duration < 5 || settings.Duration > 120 || settings.Pets == null ||
            settings.SpeedScaleVersion is not (0 or PetSettings.CurrentSpeedScale))
            throw new InvalidDataException("Animation speed must be 0.1–10 and duration must be 5–120 seconds.");
        if (legacy)
        {
            settings.Speed *= 5;
            settings.SpeedScaleVersion = PetSettings.CurrentSpeedScale;
        }
        bool changed = legacy;
        foreach (var mapping in settings.Pets.Values)
            foreach (string status in mapping.Keys.ToArray())
            {
                string[] actions = mapping[status].Select(SpriteActions.Normalize).Distinct().ToArray();
                if (actions.SequenceEqual(mapping[status])) continue;
                mapping[status] = actions;
                changed = true;
            }
        if (changed) Write("settings.json", settings);
        return settings;
    }

    public static RunningPet Running()
    {
        var running = Read<RunningPet>("running.json", null);
        if (running == null) return null;
        try
        {
            using var process = Process.GetProcessById(running.Pid);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks.ToString() == running.Started ? running : null;
        }
        catch (ArgumentException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }
}

public sealed class SessionSummary
{
    public string Status { get; init; } = "idle";
    public NativeSession Target { get; init; }
    public Dictionary<string, int> Counts { get; init; } = new();
}

public sealed record SessionCursor(string Status, string Key)
{
    public static SessionCursor After(NativeSession session) => new(session.Status, session.Key);
}

public static class SessionPolicy
{
    private static readonly Dictionary<string, int> Priority = new()
    {
        ["waiting"] = 5, ["error"] = 4, ["done"] = 3, ["working"] = 2, ["idle"] = 1
    };

    public static bool MarkRead(IEnumerable<NativeSession> sessions, Dictionary<string, string> acknowledged, string key, string completionId)
    {
        var current = sessions.FirstOrDefault(session => session.Key == key);
        if (current == null || current.Host == CliHost.Herdr || current.Status is not ("done" or "error") ||
            string.IsNullOrEmpty(completionId) || current.CompletionId != completionId ||
            acknowledged.TryGetValue(key, out var previous) && previous == completionId) return false;
        acknowledged[key] = completionId;
        return true;
    }

    public static bool MarkViewed(IEnumerable<NativeSession> sessions, Dictionary<string, string> acknowledged, IdeViewed viewed)
    {
        var current = sessions.FirstOrDefault(session => session.SessionId == viewed.SessionId && session.Source == "ide");
        return current != null && MarkRead(sessions, acknowledged, current.Key, viewed.CompletionId);
    }

    public static int PriorityOf(NativeSession session, Dictionary<string, string> acknowledged)
    {
        if (session.Host == CliHost.Herdr) return 0;
        if (!Priority.TryGetValue(session.Status, out int rank)) return 0;
        if (session.Status is "done" or "error")
            return acknowledged.TryGetValue(session.Key, out var completion) && completion == session.CompletionId ? 0 : rank;
        return session.Closed || session.Disconnected ? 0 : rank;
    }

    public static SessionSummary Summarize(IEnumerable<NativeSession> sessions, Dictionary<string, string> acknowledged,
        SessionCursor cursor = null)
    {
        var unread = sessions.Where(session => PriorityOf(session, acknowledged) > 0)
            .OrderByDescending(session => PriorityOf(session, acknowledged))
            .ThenBy(session => session.Timestamp).ThenBy(session => session.Key, StringComparer.Ordinal).ToArray();
        var target = unread.FirstOrDefault();
        if (target != null && cursor?.Status == target.Status)
        {
            var peers = unread.Where(session => session.Status == cursor.Status)
                .OrderBy(session => session.Key, StringComparer.Ordinal).ToArray();
            target = peers.FirstOrDefault(session => string.CompareOrdinal(session.Key, cursor.Key) > 0) ?? peers[0];
        }
        return new SessionSummary
        {
            Status = unread.FirstOrDefault()?.Status ?? "idle",
            Target = target,
            Counts = Priority.Keys.ToDictionary(status => status, status => unread.Count(session => session.Status == status))
        };
    }
}

public sealed record PetCommand(string Command, string Value);
public sealed record PetReply(bool Ok, string Message, string Error = null);

public static class PetCommands
{
    public static async Task<string> Send(string command, string value = null)
    {
        if (PetStore.Running() == null) throw new InvalidOperationException("Kiro Agent Pet is not running. Use kiro-pet start.");
        string id = Guid.NewGuid().ToString("N");
        string directory = PetPaths.InData("commands");
        string replyFile = Path.Combine(directory, id + ".reply");
        PetStore.WriteFile(Path.Combine(directory, id + ".json"), new PetCommand(command, value));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(replyFile))
            {
                var reply = JsonSerializer.Deserialize<PetReply>(File.ReadAllText(replyFile), PetStore.Json);
                File.Delete(replyFile);
                if (reply?.Ok != true) throw new InvalidOperationException(reply?.Error ?? "Pet command failed.");
                return reply.Message;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException("Kiro Agent Pet did not answer within ten seconds.");
    }
}
