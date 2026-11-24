using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace KiroPet;

public static class Installation
{
    private static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "KiroPet");

    public static async Task<string> Install()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The per-user installer currently supports Windows.");
        string source = Path.GetFullPath(AppContext.BaseDirectory);
        if (!File.Exists(Path.Combine(source, "KiroPet.Desktop.exe")))
            throw new InvalidOperationException("Run install from the unpacked portable ZIP containing both executables.");
        string version = typeof(Installation).Assembly.GetName().Version!.ToString(3);
        string directory = Path.Combine(Root, "versions", version + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Installation files cannot be symbolic links.");
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(directory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(file, target);
        }
        var running = PetStore.Running();
        if (running != null)
        {
            await PetCommands.Send("stop");
            try
            {
                using var process = Process.GetProcessById(running.Pid);
                if (!process.WaitForExit(10000)) throw new TimeoutException("Close the running pet before installing.");
            }
            catch (ArgumentException) { }
        }
        string bin = PetPaths.InData("bin");
        Directory.CreateDirectory(bin);
        string shim = Path.Combine(bin, "kiro-pet.cmd");
        if (File.Exists(shim) && File.ReadAllText(shim).Contains("kiro-pet.ps1"))
            File.Copy(shim, Path.Combine(bin, "kiro-pet-powershell.cmd"), true);
        string executable = Path.Combine(directory, "kiro-pet.exe");
        if (executable.Contains('%') || executable.Contains('"')) throw new InvalidDataException("Installation path contains unsupported characters.");
        File.WriteAllText(shim, $"@echo off\r\n\"{executable}\" %*\r\n", new UTF8Encoding(false));
        string userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
        string[] entries = userPath.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => !string.Equals(path.TrimEnd('\\'), bin.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)).Append(bin).ToArray();
        Environment.SetEnvironmentVariable("Path", string.Join(';', entries), EnvironmentVariableTarget.User);
        if (Startup.Enabled) Startup.Set("on", executable);
        PetStore.Write("installation.json", new { executable, directory, version });
        return "Installed for this user. Open a new terminal and run kiro-pet start. Imported pets and settings were preserved.";
    }

    public static async Task<string> Uninstall()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The per-user installer currently supports Windows.");
        if (PetStore.Running() != null) await PetCommands.Send("stop");
        Startup.Set("off");
        string bin = PetPaths.InData("bin");
        string shim = Path.Combine(bin, "kiro-pet.cmd");
        if (File.Exists(shim)) File.Delete(shim);
        string userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
        Environment.SetEnvironmentVariable("Path", string.Join(';', userPath.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => !string.Equals(path.TrimEnd('\\'), bin.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))), EnvironmentVariableTarget.User);
        return "Removed command and sign-in startup. Pet data and installed version folders were kept.";
    }
}
