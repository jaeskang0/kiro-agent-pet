using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KiroPet;

internal sealed record ProcessEntry(int Pid, int Parent, string Name);

internal static class ProcessAncestry
{
    public static List<ProcessEntry> Read(int pid)
    {
        if (!OperatingSystem.IsWindows()) return [];
        var entries = new Dictionary<int, ProcessEntry>();
        IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) return [];
        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (Process32First(snapshot, ref entry))
                do { entries[(int)entry.Pid] = new((int)entry.Pid, (int)entry.ParentPid, entry.Name); }
                while (Process32Next(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        var chain = new List<ProcessEntry>();
        var seen = new HashSet<int>();
        DateTime childStarted = DateTime.MaxValue;
        while (seen.Add(pid) && chain.Count < 32 && entries.TryGetValue(pid, out var process))
        {
            try
            {
                using var live = Process.GetProcessById(pid);
                DateTime started = live.StartTime.ToUniversalTime();
                if (live.HasExited || childStarted != DateTime.MaxValue && started > childStarted.AddSeconds(1)) break;
                childStarted = started;
            }
            catch (Exception error) when (error is ArgumentException or System.ComponentModel.Win32Exception) { break; }
            chain.Add(process);
            pid = process.Parent;
        }
        return chain;
    }

    public static bool IsHerdr(IEnumerable<ProcessEntry> chain) => Classify(chain) == CliHost.Herdr;

    public static CliHost Classify(int pid) => Classify(Read(pid));

    private static CliHost Classify(IEnumerable<ProcessEntry> chain)
    {
        string[] shells = ["pwsh.exe", "powershell.exe", "cmd.exe", "explorer.exe", "nu.exe", "bash.exe"];
        string[] launchers = ["KiroPet.Desktop.exe", "WindowsTerminal.exe", "explorer.exe"];
        bool shell = false;
        foreach (var process in chain)
        {
            if (process.Name.Equals("herdr-core.exe", StringComparison.OrdinalIgnoreCase)) return CliHost.Herdr;
            if (launchers.Contains(process.Name, StringComparer.OrdinalIgnoreCase)) return CliHost.Standalone;
            shell |= shells.Contains(process.Name, StringComparer.OrdinalIgnoreCase);
        }
        return shell ? CliHost.Standalone : CliHost.Unknown;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size, Usage, Pid;
        public UIntPtr Heap;
        public uint Module, Threads, ParentPid;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
    }
    [DllImport("kernel32.dll")] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
