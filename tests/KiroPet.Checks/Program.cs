using System.Text;
using System.Text.Json;
using System.IO.Compression;
using KiroPet;

if (args.Length == 1 && args[0] == "--ide-setup")
{
    Console.WriteLine(JsonSerializer.Serialize(await IdeBridge.EnsureInstalled()));
    return 0;
}

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
    string helperArchive = Path.Combine(temporary, "helper.vsix");
    using (var archive = ZipFile.Open(helperArchive, ZipArchiveMode.Create))
    using (var writer = new StreamWriter(archive.CreateEntry("extension/package.json").Open()))
        writer.Write("""{"publisher":"jaeskang0","name":"kiro-agent-pet-bridge","version":"0.4.0"}""");
    foreach (string? initial in new string?[] { null, "0.3.9", "0.4.0", "0.4.1" })
    {
        string? current = initial;
        int installs = 0;
        Task<string> RunHelper(IReadOnlyList<string> arguments)
        {
            if (arguments[0] == "--list-extensions")
            {
                Assert(arguments.SequenceEqual(new[] { "--list-extensions", "--show-versions" }), "Helper check omitted installed versions.");
                return Task.FromResult("other.extension@99.0.0\r\n" +
                    (current == null ? "" : "jaeskang0.kiro-agent-pet-bridge@" + current + "\r\n"));
            }
            Assert(arguments.SequenceEqual(new[] { "--install-extension", helperArchive, "--force" }), "Helper installation used the wrong package.");
            installs++; current = "0.4.0";
            return Task.FromResult("installed");
        }
        bool expected = initial is null or "0.3.9";
        Assert(await IdeBridge.EnsureInstalled(helperArchive, RunHelper) == expected, "Incorrect helper update decision.");
        Assert(installs == (expected ? 1 : 0), "Current or newer helper was reinstalled.");
        Assert(!await IdeBridge.EnsureInstalled(helperArchive, RunHelper), "Repeat startup reinstalled the helper.");
        Assert(installs == (expected ? 1 : 0), "Repeat startup changed the installed helper.");
    }
    foreach (bool failCommand in new[] { false, true })
    {
        bool failed = false;
        try
        {
            await IdeBridge.EnsureInstalled(helperArchive, arguments =>
            {
                if (arguments[0] == "--install-extension" && failCommand)
                    throw new InvalidOperationException("Installer failed.");
                return Task.FromResult("");
            });
        }
        catch (InvalidOperationException) { failed = true; }
        Assert(failed, "Failed or unconfirmed helper installation was reported as successful.");
    }
    bool invalidVersionRejected = false;
    try
    {
        await IdeBridge.EnsureInstalled(helperArchive, arguments =>
        {
            Assert(arguments[0] == "--list-extensions", "Invalid installed version caused an installation.");
            return Task.FromResult("jaeskang0.kiro-agent-pet-bridge@invalid");
        });
    }
    catch (InvalidDataException) { invalidVersionRejected = true; }
    Assert(invalidVersionRejected, "Invalid installed helper version was accepted.");
    foreach (string? initial in new string?[] { null, "0.4.0", "0.4.1" })
    {
        string? current = initial;
        bool oldPresent = true;
        int installs = 0, removals = 0;
        Task<string> RunReplacement(IReadOnlyList<string> arguments)
        {
            if (arguments[0] == "--list-extensions")
                return Task.FromResult((current == null ? "" : "jaeskang0.kiro-agent-pet-bridge@" + current + "\n") +
                    (oldPresent ? "jaeskang0.kiroling-bridge@0.4.1\n" : ""));
            if (arguments[0] == "--install-extension")
            {
                installs++;
                current = "0.4.0";
                return Task.FromResult("installed");
            }
            Assert(arguments.SequenceEqual(new[] { "--uninstall-extension", "jaeskang0.kiroling-bridge" }),
                "Replacement removed the wrong extension.");
            Assert(current != null && Version.Parse(current) >= new Version(0, 4, 0),
                "The old helper was removed before the new helper was verified.");
            removals++;
            oldPresent = false;
            return Task.FromResult("removed");
        }
        Assert(await IdeBridge.EnsureInstalled(helperArchive, RunReplacement), "Old helper replacement was not reported.");
        Assert(!oldPresent && removals == 1 && installs == (initial == null ? 1 : 0), "Incorrect clean-install replacement.");
        Assert(!await IdeBridge.EnsureInstalled(helperArchive, RunReplacement), "Clean-install replacement was not idempotent.");
        Assert(removals == 1, "Repeat startup removed the old helper again.");
    }
    foreach (bool failInstall in new[] { false, true })
    {
        bool removalAttempted = false;
        bool failed = false;
        try
        {
            await IdeBridge.EnsureInstalled(helperArchive, arguments =>
            {
                if (arguments[0] == "--list-extensions")
                    return Task.FromResult((failInstall ? "" : "jaeskang0.kiro-agent-pet-bridge@0.4.0\n") +
                        "jaeskang0.kiroling-bridge@0.4.1\n");
                if (arguments[0] == "--uninstall-extension") removalAttempted = true;
                return Task.FromResult("");
            });
        }
        catch (InvalidOperationException) { failed = true; }
        Assert(failed, "An unconfirmed clean install was reported as successful.");
        Assert(removalAttempted != failInstall, "A failed new install removed the old helper.");
    }
    Console.WriteLine("Automatic IDE helper setup and repeat startup PASS");

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
    Assert(settings.Speed == 1 && settings.FrameMilliseconds == 900 && settings.Duration == 45 &&
        settings.Pets["usagi"]["blocked"][0] == "failed", "Legacy animation pace or row selections were not preserved.");
    Assert(PetStore.Settings().Speed == 1, "Animation speed was converted again on a second load.");
    Assert(new PetSettings().Speed == 1 && new PetSettings().FrameMilliseconds == 900, "Normal playback did not use the calmer baseline.");
    Assert(SpriteActions.Normalize("eat") == "waiting" && SpriteActions.Normalize("excited") == "running" &&
        SpriteActions.Normalize("read") == "review", "Saved actions were not mapped to their actual sprite rows.");
    foreach (double oldSpeed in new[] { 0.1, 0.6, 1.0, 2.0 })
    {
        PetStore.Write("settings.json", new { speed = oldSpeed, duration = 30, reducedMotion = false, pets = new Dictionary<string, object>() });
        var converted = PetStore.Settings();
        Assert(Math.Abs(converted.FrameMilliseconds - 180 / oldSpeed) < 0.001, "Speed conversion changed actual frame pacing.");
        Assert(converted.Speed >= PetSettings.MinimumSpeed && converted.Speed <= PetSettings.MaximumSpeed, "Converted speed fell outside the slider range.");
        Assert(PetStore.Settings().Speed == converted.Speed, "Converted speed changed after reopening settings.");
    }
    PetStore.Write("selected-pet.json", new Selection { Key = "usagi", DisplayName = "Usagi" });
    Assert(PetStore.Read("selected-pet.json", new Selection()).Key == "usagi", "Pet selection did not persist.");
    Console.WriteLine("Settings compatibility PASS");
    var clickSettings = new PetSettings { SpeedScaleVersion = PetSettings.CurrentSpeedScale };
    string[] clickActions = ["idle", "waving", "jumping"];
    Assert(clickSettings.ClickReaction("spark", clickActions) == "waving", "Default click reaction was not waving.");
    clickSettings.Pets["spark"] = new() { ["click"] = ["jumping"] };
    Assert(clickSettings.ClickReaction("spark", clickActions) == "jumping", "Saved click reaction was ignored.");
    Assert(clickSettings.ClickReaction("usagi", clickActions) == "waving", "Click reaction leaked between pets.");
    PetStore.Write("settings.json", clickSettings);
    Assert(PetStore.Settings().ClickReaction("spark", clickActions) == "jumping", "Click reaction did not persist.");
    Assert(clickSettings.ClickReaction("spark", ["idle", "waving"]) == "waving", "Unavailable click animation did not fall back.");
    Console.WriteLine("Per-pet click reaction and persistence PASS");
    var phrasePack = PhrasePacks.ParseGenerated(JsonSerializer.Serialize(new { phrases = ExamplePhrases("messages-a").Phrases }), "messages-a");
    PhrasePacks.Save(phrasePack);
    Assert(PhrasePacks.Load("messages-a")?.Phrases["done"].Length == 3, "Generated phrase pack did not persist.");
    Assert(PhrasePacks.Load("messages-b") == null, "Phrase packs leaked between pets.");
    var speech = new PhraseSpeaker(new Random(21));
    speech.Reload("messages-a", true);
    string firstLine = speech.Say("done", "agent-a");
    for (int poll = 0; poll < 100; poll++)
        Assert(speech.Say("done", "agent-a") == firstLine, "Bubble phrase changed during status polling.");
    speech.Reset();
    Assert(speech.Say("done", "agent-a") != firstLine, "Consecutive speech events repeated the same phrase.");
    speech.Reload("messages-a", false);
    Assert(PhrasePacks.Defaults("done").Contains(speech.Say("done", "agent-a")), "Default-message preference was ignored.");
    foreach (bool overlong in new[] { false, true })
    {
        var invalidPack = ExamplePhrases("invalid");
        if (overlong) invalidPack.Phrases["done"][0] = new string('x', 33);
        else invalidPack.Phrases.Remove("waiting");
        bool rejected = false;
        try { PhrasePacks.Validate(invalidPack, "invalid"); }
        catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "Malformed generated messages were accepted.");
    }
    File.WriteAllText(PetPaths.InData(Path.Combine("personalities", "broken.json")), "not json");
    Assert(PhrasePacks.Load("broken") == null, "Broken message cache did not fall back.");
    var streamed = new StringBuilder();
    KiroPhraseGenerator.AppendEvent("not-json", streamed);
    KiroPhraseGenerator.AppendEvent("""{"type":"sessionUpdate","data":{"update":{"sessionUpdate":"agent_thought_chunk","content":{"type":"text","text":"hidden"}}}}""", streamed);
    KiroPhraseGenerator.AppendEvent("""{"type":"sessionUpdate","data":{"update":{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"hello"}}}}""", streamed);
    Assert(streamed.ToString() == "hello", "Phrase generator collected reasoning or discarded message chunks.");
    string privatePath = @"C:\Users\someone\private.txt";
    var requestA = new PersonalityRequest("queued-a", "Pet A", privatePath, "animal", Path.Combine(PhrasePacks.JobsRoot, "fake.png"), PhrasePacks.JobsRoot);
    Assert(!KiroPhraseGenerator.Prompt(requestA).Contains(privatePath), "Metadata could introduce an automatic file attachment.");
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    int calls = 0;
    using (var generation = new PhraseGeneration(async (request, cancellation) =>
    {
        Interlocked.Increment(ref calls);
        started.TrySetResult();
        await gate.Task.WaitAsync(cancellation);
        return ExamplePhrases(request.PetKey);
    }))
    {
        Assert(generation.Enqueue(requestA), "First generation job was rejected.");
        Assert(!generation.Enqueue(requestA), "Duplicate generation job was admitted.");
        Assert(generation.Enqueue(requestA with { PetKey = "queued-b" }), "Second pet could not queue.");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert(calls == 1, "Generation jobs ran concurrently.");
        gate.SetResult();
        for (int attempt = 0; attempt < 100 && (generation.IsPending("queued-a") || generation.IsPending("queued-b")); attempt++) await Task.Delay(20);
        Assert(calls == 2 && PhrasePacks.Load("queued-a") != null && PhrasePacks.Load("queued-b") != null, "Queued packs were not saved.");
        Assert(!generation.Enqueue(requestA), "A cached pet regenerated without being requested.");
    }
    var cancelStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var cancelGeneration = new PhraseGeneration(async (request, cancellation) =>
    {
        cancelStarted.SetResult();
        await Task.Delay(Timeout.Infinite, cancellation);
        return ExamplePhrases(request.PetKey);
    });
    cancelGeneration.Enqueue(requestA with { PetKey = "cancelled" });
    await cancelStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    cancelGeneration.Dispose();
    for (int attempt = 0; attempt < 100 && cancelGeneration.IsPending("cancelled"); attempt++) await Task.Delay(20);
    Assert(!cancelGeneration.IsPending("cancelled") && PhrasePacks.Load("cancelled") == null, "Cancellation saved an incomplete pack.");
    Assert(PhrasePacks.IsJobWorkspace(Path.Combine(PhrasePacks.JobsRoot, "job")) &&
        !PhrasePacks.IsJobWorkspace(PhrasePacks.JobsRoot + "-project"), "Generation-workspace exclusion is too broad.");
    Console.WriteLine("Phrase validation, cache, stable speech, queue, cancellation, and default mode PASS");

    string native = Path.Combine(temporary, "native");
    string a = Fixture("sess_check-a", "kiro-ide");
    string b = Fixture("sess_check-b", "kiro-cli");
    Event(a, "turn_start", "a");
    Event(b, "turn_start", "b");
    reader = new NativeReader(native, Path.Combine(temporary, "cache.json"), true, _ => CliHost.Standalone);
    var snapshot = reader.ReadNow();
    Assert(snapshot.Sessions.Length == 2 && snapshot.Sessions.All(session => session.Status == "working"), "Running turns were not recovered.");
    Assert(snapshot.Sessions.All(session => session.OwnerPid == Environment.ProcessId), "Native ownership was not retained for IDE window routing.");
    string phraseJob = Fixture("sess_phrase-job", "kiro-cli");
    PetStore.WriteFile(Path.Combine(phraseJob, "session.json"), new
    {
        schemaVersion = "1.0.0", dataModelVersion = 1, id = "sess_phrase-job", title = "Internal phrase generation",
        workspacePaths = new[] { Path.Combine(PhrasePacks.JobsRoot, "test-job") }, status = "idle"
    });
    Event(phraseJob, "turn_start", "phrase");
    Event(phraseJob, "turn_end", "phrase", new() { ["stopReason"] = "end_turn" });
    Assert(!reader.ReadNow().Sessions.Any(session => session.SessionId == "sess_phrase-job"), "Phrase generation appeared as an agent task.");
    Console.WriteLine("Internal phrase job excluded from native tasks PASS");
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
    foreach (string state in new[] { "waiting", "error", "done", "working" })
    {
        var sameState = new[] { "a", "b", "c" }.Select((name, index) => new NativeSession
        {
            Key = "kiro:cycle-" + name, SessionId = "sess_cycle-" + name, Source = "cli",
            Status = state, CompletionId = "result-" + name, Timestamp = (index + 1) * 10
        }).ToArray();
        var seen = new Dictionary<string, string>();
        var first = SessionPolicy.Summarize(sameState, seen);
        Assert(first.Target.Key == sameState[0].Key && first.Counts[state] == 3, $"First {state} target or count changed.");
        var second = SessionPolicy.Summarize(sameState, seen, SessionCursor.After(first.Target));
        var third = SessionPolicy.Summarize(sameState, seen, SessionCursor.After(second.Target));
        var wrapped = SessionPolicy.Summarize(sameState, seen, SessionCursor.After(third.Target));
        Assert(second.Target.Key == sameState[1].Key && third.Target.Key == sameState[2].Key &&
            wrapped.Target.Key == sameState[0].Key, $"{state} clicks did not cycle through each agent.");
        sameState[0].Timestamp = 25;
        Assert(SessionPolicy.Summarize(sameState, seen, SessionCursor.After(sameState[1])).Target.Key == sameState[2].Key,
            $"{state} activity updates caused a repeated agent before the end of the cycle.");
        if (state is "done" or "error")
        {
            seen[sameState[1].Key] = sameState[1].CompletionId;
            Assert(SessionPolicy.Summarize(sameState, seen, SessionCursor.After(sameState[1])).Target.Key == sameState[2].Key,
                $"Checking one {state} result skipped the next unread result.");
        }
        Assert(SessionPolicy.Summarize(sameState.Take(1), seen, SessionCursor.After(sameState[0])).Target.Key == sameState[0].Key,
            $"One {state} agent did not remain the target.");
    }
    var urgent = new NativeSession { Key = "kiro:urgent", SessionId = "sess_urgent", Status = "waiting", Timestamp = 100 };
    var working = new NativeSession { Key = "kiro:working", SessionId = "sess_working", Status = "working", Timestamp = 10 };
    Assert(SessionPolicy.Summarize([working, urgent], new(), SessionCursor.After(working)).Target.Key == urgent.Key,
        "An urgent status did not interrupt working-agent cycling.");
    var sameTime = new[] { "c", "a", "b" }.Select(name => new NativeSession
    {
        Key = "kiro:tie-" + name, SessionId = "sess_tie-" + name, Status = "working", Timestamp = 1
    }).ToArray();
    Assert(SessionPolicy.Summarize(sameTime, new(), SessionCursor.After(sameTime[0])).Target.Key == "kiro:tie-a",
        "Same-time targets did not wrap in stable key order.");
    Console.WriteLine("Per-click cycling within each status PASS");
    using (var restarted = new NativeReader(native, Path.Combine(temporary, "cache.json"), false, _ => CliHost.Standalone))
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

    var excluded = new NativeSession
    {
        Key = "kiro:sess_host-hidden", SessionId = "sess_host-hidden", Source = "cli",
        Host = CliHost.Herdr, Status = "done", CompletionId = "hidden:turn"
    };
    var visibleCli = new NativeSession
    {
        Key = "kiro:sess_host-visible", SessionId = "sess_host-visible", Source = "cli",
        Host = CliHost.Standalone, Status = "done", CompletionId = "visible:turn"
    };
    foreach (string state in new[] { "waiting", "error", "done", "working", "idle" })
    {
        excluded.Status = state;
        Assert(SessionPolicy.PriorityOf(excluded, new()) == 0, $"Herdr {state} appeared in the pet's priorities.");
    }
    excluded.Status = "done";
    Assert(!SessionPolicy.MarkRead([excluded], new(), excluded.Key, excluded.CompletionId),
        "An excluded Herdr result was acknowledged.");
    Assert(!CliNavigator.Open(excluded).GetAwaiter().GetResult().Opened,
        "Herdr navigation was still available.");
    string hostCache = Path.Combine(temporary, "host-cache.json");
    File.WriteAllText(hostCache, JsonSerializer.Serialize(new[] { excluded, visibleCli }));
    using (var cachedHosts = new NativeReader(Path.Combine(temporary, "absent-native"), hostCache, false))
    {
        var visible = cachedHosts.ReadNow().Sessions;
        Assert(visible.Length == 1 && visible[0].SessionId == visibleCli.SessionId,
            "Cached Herdr results returned to the pet after restart.");
    }
    string liveHostCache = Path.Combine(temporary, "live-host-cache.json");
    using (var hosted = new NativeReader(native, liveHostCache, true, _ => CliHost.Herdr))
    {
        var monitored = hosted.ReadNow();
        Assert(monitored.Sessions.All(session => session.Source != "cli") &&
            monitored.Sessions.Any(session => session.Source == "ide"), "Herdr filtering removed IDE sessions or retained CLI sessions.");
    }
    using (var hostedRestart = new NativeReader(Path.Combine(temporary, "absent-native"), liveHostCache, false))
        Assert(hostedRestart.ReadNow().Sessions.All(session => session.Source != "cli"),
            "An excluded live Herdr session returned from the cache after restarting.");
    Console.WriteLine("Herdr session exclusion and cached host marker PASS");

    if (OperatingSystem.IsWindows())
    {
        var cli = Session(snapshot, "sess_check-b");
        Assert(cli.OwnerStartedUtcTicks > 0 && cli.LeaseAcquiredAt > 0, "CLI ownership identity was not retained.");
        var stale = new NativeSession
        {
            SessionId = cli.SessionId, Source = "cli", OwnerPid = cli.OwnerPid,
            OwnerStartedUtcTicks = cli.OwnerStartedUtcTicks + 1, LeaseAcquiredAt = cli.LeaseAcquiredAt
        };
        string? previousNative = Environment.GetEnvironmentVariable("KIRO_NATIVE_HOME");
        Environment.SetEnvironmentVariable("KIRO_NATIVE_HOME", native);
        try
        {
            var reusedPid = CliNavigator.Open(stale).GetAwaiter().GetResult();
            Assert(!reusedPid.Opened && reusedPid.Message.Contains("process changed"), "A changed CLI process identity was accepted.");
            stale.OwnerStartedUtcTicks = cli.OwnerStartedUtcTicks;
            stale.LeaseAcquiredAt++;
            var replacedLease = CliNavigator.Open(stale).GetAwaiter().GetResult();
            Assert(!replacedLease.Opened && replacedLease.Message.Contains("process changed"), "A replaced CLI lease was accepted.");
            Assert(!CliNavigator.Open(new NativeSession { Source = "cli", SessionId = "../invalid" }).GetAwaiter().GetResult().Opened,
                "An invalid CLI session ID was accepted.");
            Assert(!CliNavigator.Open(done).GetAwaiter().GetResult().Opened, "CLI navigation accepted an IDE session.");
        }
        finally { Environment.SetEnvironmentVariable("KIRO_NATIVE_HOME", previousNative); }
        Console.WriteLine("CLI ownership identity and stale navigation rejection PASS");
    }

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
            "kiro://jaeskang0.kiro-agent-pet-bridge/open?windowId=1", [temporary], [done.SessionId], 1, agentPort);
        string endpointFile = PetPaths.InData(Path.Combine("ide-endpoints", Environment.ProcessId + ".json"));
        var remoteAgent = new NativeSession { SessionId = done.SessionId, Source = "ide", Workspace = temporary, OwnerPid = agentHelper.Id };
        PetStore.WriteFile(endpointFile, endpoint);
        Assert(agentHelper.Id != endpoint.Pid && IdeBridge.Locate(remoteAgent)?.Pid == Environment.ProcessId,
            "The owning IDE window was not found when its agent and bridge use different processes.");
        PetStore.WriteFile(endpointFile, endpoint with { Uri = "kiro://jaeskang0.kiroling-bridge/open?windowId=1" });
        Assert(IdeBridge.Locate(remoteAgent) == null, "The retired bridge was accepted as the new helper.");
        PetStore.WriteFile(endpointFile, endpoint);
        Assert(IdeBridge.Locate(done) == null, "A different agent process was accepted for this IDE window.");
        string ambiguousFile = PetPaths.InData(Path.Combine("ide-endpoints", agentHelper.Id + ".json"));
        PetStore.WriteFile(ambiguousFile, endpoint with
        {
            Pid = agentHelper.Id,
            StartedAt = new DateTimeOffset(agentHelper.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds(),
            WindowId = 2, Uri = "kiro://jaeskang0.kiro-agent-pet-bridge/open?windowId=2"
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

static PhrasePack ExamplePhrases(string key) => new()
{
    PetKey = key, GeneratedAt = DateTimeOffset.UtcNow,
    Phrases = PhrasePacks.Contexts.ToDictionary(context => context, context => new[] { context + " one", context + " two", context + " three" })
};
