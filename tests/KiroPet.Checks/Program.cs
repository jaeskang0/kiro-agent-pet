using System.Text;
using System.Text.Json;
using KiroPet;

string temporary = Path.Combine(Path.GetTempPath(), "kiro-pet-csharp-check-" + Guid.NewGuid().ToString("N"));
string? oldData = Environment.GetEnvironmentVariable("KIRO_PET_HOME");
Directory.CreateDirectory(temporary);
Environment.SetEnvironmentVariable("KIRO_PET_HOME", Path.Combine(temporary, "data"));
NativeReader? reader = null;
try
{
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
    Event(a, "turn_end", "wrong", new() { ["stopReason"] = "end_turn" });
    Assert(Session(reader.ReadNow(), "sess_check-a").Status == "working", "Old execution finished a new turn.");
    Event(a, "pending_interaction", "a", new() { ["toolCallId"] = "approval" });
    Assert(SessionPolicy.Summarize(reader.ReadNow().Sessions, new()).Status == "waiting", "Approval priority failed.");
    Event(a, "interaction_resolved", "a", new() { ["toolCallId"] = "approval", ["outcome"] = "accepted" });
    Event(a, "turn_end", "a", new() { ["stopReason"] = "end_turn" });
    snapshot = reader.ReadNow();
    var done = Session(snapshot, "sess_check-a");
    Assert(done.CompletionId == "a:a", "Native completion identity changed.");
    var acknowledged = new Dictionary<string, string> { [done.Key] = done.CompletionId };
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

    string Fixture(string id, string client)
    {
        string directory = Path.Combine(native, "sessions", "group", id);
        Directory.CreateDirectory(directory);
        PetStore.WriteFile(Path.Combine(directory, "session.json"), new
        {
            schemaVersion = "1.0.0", dataModelVersion = 1, id, title = id,
            workspacePaths = new[] { temporary }, status = "idle"
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
    reader?.Dispose();
    Environment.SetEnvironmentVariable("KIRO_PET_HOME", oldData);
    string full = Path.GetFullPath(temporary);
    if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
        Path.GetFileName(full).StartsWith("kiro-pet-csharp-check-"))
        Directory.Delete(full, true);
}

static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static NativeSession Session(NativeSnapshot snapshot, string id) => snapshot.Sessions.Single(session => session.SessionId == id);
