using Avalonia;
using KiroPet;

namespace KiroPet.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var mutex = new Mutex(false, PetPaths.MutexName);
        bool owns;
        try { owns = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { owns = true; }
        if (!owns) return;
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception error)
        {
            Directory.CreateDirectory(PetPaths.Data);
            File.WriteAllText(PetPaths.InData("app.stderr.log"), error.ToString());
        }
        finally { mutex.ReleaseMutex(); }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<PetApp>().UsePlatformDetect().LogToTrace();
}
