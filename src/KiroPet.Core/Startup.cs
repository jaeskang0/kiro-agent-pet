using System.Diagnostics;
using System.Text;

namespace KiroPet;

public static class Startup
{
    public static bool Enabled => OperatingSystem.IsWindows() &&
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Kiro Pet.cmd"));

    public static string Set(string value, string executableOverride = null)
    {
        if (value == "status") return "Start at sign-in: " + Enabled;
        if (value is not ("on" or "off")) throw new ArgumentException("Use kiro-pet startup on|off|status.");
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Sign-in startup is currently available on Windows.");
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Kiro Pet.cmd");
        if (value == "off")
        {
            if (File.Exists(file)) File.Delete(file);
            return "Start at sign-in disabled.";
        }
        string executable = executableOverride ?? Environment.GetEnvironmentVariable("KIRO_PET_CLI") ??
            Path.Combine(AppContext.BaseDirectory, "kiro-pet.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("Installed CLI executable not found.", executable);
        if (executable.Contains('%') || executable.Contains('"')) throw new InvalidDataException("Startup paths cannot contain quotes or percent signs.");
        File.WriteAllText(file, $"@echo off\r\nstart \"\" /b \"{executable}\" start\r\n", new UTF8Encoding(false));
        return "Start at sign-in enabled.";
    }
}
