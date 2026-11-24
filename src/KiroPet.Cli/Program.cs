using System.Diagnostics;
using System.Text.Json;
using KiroPet;

try
{
    string command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
    string? value = args.Skip(1).FirstOrDefault();
    switch (command)
    {
        case "start":
            Console.WriteLine(await Start());
            break;
        case "install":
            Console.WriteLine(await Installation.Install());
            break;
        case "uninstall":
            Console.WriteLine(await Installation.Uninstall());
            break;
        case "show":
        case "settings":
        case "tray":
            if (PetStore.Running() == null) await Start();
            Console.WriteLine(await PetCommands.Send(command == "tray" ? "sessions" : command));
            break;
        case "hide":
        case "stop":
            Console.WriteLine(await PetCommands.Send(command));
            break;
        case "select":
        case "import":
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"Use kiro-pet {command} <name or path>.");
            if (PetStore.Running() == null) await Start();
            Console.WriteLine(await PetCommands.Send(command, value));
            break;
        case "status":
        {
            var running = PetStore.Running();
            using var reader = new NativeReader(PetPaths.Native, PetPaths.InData("native-sessions.json"), false);
            var snapshot = reader.ReadNow();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                running = running != null,
                engine = running?.Engine,
                discovery = "automatic",
                dataRoot = PetPaths.Data,
                sessions = snapshot.Sessions.Length,
                health = snapshot.Health
            }, PetStore.Json));
            break;
        }
        case "sessions":
        case "connections":
        {
            using var reader = new NativeReader(PetPaths.Native, PetPaths.InData("native-sessions.json"), false);
            var snapshot = reader.ReadNow();
            if (command == "connections")
                foreach (var group in snapshot.Sessions.GroupBy(session => session.Workspace))
                    Console.WriteLine($"{group.Key} ({group.Count()} sessions)");
            else
                foreach (var session in snapshot.Sessions)
                    Console.WriteLine($"{session.Status,-12} {session.Source,-4} {session.Title ?? session.SessionId} · {session.Workspace}");
            break;
        }
        case "pets":
            Console.WriteLine("spark  Spark");
            string library = PetPaths.InData("pets");
            if (Directory.Exists(library))
                foreach (string file in Directory.EnumerateFiles(library, "pet.json", SearchOption.AllDirectories))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file));
                    Console.WriteLine($"{document.RootElement.GetProperty("key").GetString()}  {document.RootElement.GetProperty("displayName").GetString()}");
                }
            break;
        case "startup":
            Console.WriteLine(Startup.Set(value ?? "status"));
            break;
        default:
            Console.WriteLine("""
                Kiro Pet
                  kiro-pet install|uninstall
                  kiro-pet start|stop|show|hide|settings|status
                  kiro-pet select <name-or-key>
                  kiro-pet import <zip-or-folder>
                  kiro-pet pets|sessions|connections|tray
                  kiro-pet startup on|off|status
                Sessions are discovered automatically. Hide keeps tracking running.
                """);
            break;
    }
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static async Task<string> Start()
{
    if (PetStore.Running() != null) return "Kiro Pet is already running.";
    string executable = Environment.GetEnvironmentVariable("KIRO_PET_DESKTOP") ??
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "KiroPet.Desktop.exe" : "KiroPet.Desktop");
    if (!File.Exists(executable)) throw new FileNotFoundException("Desktop executable not found. Use an installed build, or set KIRO_PET_DESKTOP for development.", executable);
    var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
    using var process = Process.Start(info) ?? throw new InvalidOperationException("Desktop process could not start.");
    var startupError = process.StandardError.ReadToEndAsync();
    var deadline = DateTime.UtcNow.AddSeconds(30);
    while (DateTime.UtcNow < deadline)
    {
        if (PetStore.Running() != null) return "Kiro Pet started.";
        if (process.HasExited)
            throw new InvalidOperationException("Desktop process exited during startup. " + await startupError);
        await Task.Delay(100);
    }
    throw new TimeoutException("Kiro Pet did not start. Check the app log in " + PetPaths.Data);
}
