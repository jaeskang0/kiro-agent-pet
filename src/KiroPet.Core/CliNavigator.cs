using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KiroPet;

public sealed record CliOpenResult(bool Opened, string Message);

public static class CliNavigator
{
    private sealed record Owner(int Pid, long Started, long Acquired);
    private static readonly SemaphoreSlim Opening = new(1, 1);

    public static async Task<CliOpenResult> Open(NativeSession session)
    {
        if (session?.Source != "cli") return new(false, "This is not a Kiro CLI session.");
        if (session.Host == CliHost.Herdr) return new(false, "Herdr-hosted sessions are excluded from Kiroling.");
        if (!OperatingSystem.IsWindows()) return new(false, "CLI terminal opening currently supports Windows only.");
        if (!Regex.IsMatch(session.SessionId ?? "", "^sess_[a-zA-Z0-9-]{1,80}$"))
            return new(false, "The CLI session ID is invalid.");
        await Opening.WaitAsync();
        try { return await Task.Run(() => OpenWindows(session)); }
        catch (Exception error) when (error is IOException or JsonException or System.ComponentModel.Win32Exception or COMException or UnauthorizedAccessException)
        { return new(false, "Could not open this CLI terminal. " + error.Message); }
        finally { Opening.Release(); }
    }

    private static CliOpenResult OpenWindows(NativeSession session)
    {
        var owner = ReadOwner(session.SessionId);
        bool resumed = owner == null && (session.Closed || session.Disconnected);
        if (resumed) owner = Resume(session);
        if (owner == null) return new(false, "Kiro did not reopen this CLI session. Its result stays unread.");
        if (!resumed && (session.Closed || session.Disconnected || session.OwnerPid != owner.Pid ||
            session.OwnerStartedUtcTicks != owner.Started || session.LeaseAcquiredAt != owner.Acquired))
            return new(false, "This CLI session's process changed. Refresh its session row and try again.");
        var chain = ProcessAncestry.Read(owner.Pid);
        if (ProcessAncestry.IsHerdr(chain))
            return new(false, "Herdr-hosted sessions are excluded from Kiroling.");
        return OpenConsole(session.SessionId, owner, chain);
    }

    private static Owner Resume(NativeSession session)
    {
        if (!Path.IsPathFullyQualified(session.Workspace ?? "") || !Directory.Exists(session.Workspace))
            throw new IOException("The saved CLI workspace is unavailable.");
        string cli = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kiro-Cli", "kiro-cli.exe");
        if (!File.Exists(cli)) throw new IOException("Kiro CLI is not installed at the expected location.");
        long launchedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var start = new ProcessStartInfo(cli) { UseShellExecute = true, WorkingDirectory = session.Workspace };
        start.ArgumentList.Add("chat");
        start.ArgumentList.Add("--resume-id");
        start.ArgumentList.Add(session.SessionId);
        using var launched = Process.Start(start);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        Owner previous = null;
        DateTime stableSince = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            var owner = ReadOwner(session.SessionId);
            if (owner != previous) { previous = owner; stableSince = DateTime.UtcNow; }
            if (owner != null && owner.Acquired >= launchedAt && DateTime.UtcNow - stableSince >= TimeSpan.FromMilliseconds(500))
                return owner;
            Thread.Sleep(100);
        }
        return null;
    }

    private static Owner ReadOwner(string sessionId)
    {
        string file = Path.Combine(PetPaths.Native, "session-leases", sessionId + ".lease");
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length > 8192) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var lease = document.RootElement;
            string client = NativeReader.Text(lease, "clientName") ?? "";
            if (!client.Contains("kiro", StringComparison.OrdinalIgnoreCase) || !client.Contains("cli", StringComparison.OrdinalIgnoreCase) ||
                !lease.TryGetProperty("pid", out var pidValue) || pidValue.ValueKind != JsonValueKind.Number ||
                !pidValue.TryGetInt32(out int pid) || pid <= 0) return null;
            long acquired = NativeReader.Time(lease, "acquiredAt");
            if (acquired <= 0) return null;
            using var process = Process.GetProcessById(pid);
            long started = process.StartTime.ToUniversalTime().Ticks;
            if (process.HasExited || started > DateTimeOffset.FromUnixTimeMilliseconds(acquired).UtcTicks + TimeSpan.TicksPerSecond) return null;
            if (lease.TryGetProperty("parentPid", out var parentValue) && parentValue.ValueKind == JsonValueKind.Number &&
                parentValue.TryGetInt32(out int parentPid) && parentPid > 0)
            {
                using var parent = Process.GetProcessById(parentPid);
                if (parent.HasExited || parent.StartTime.ToUniversalTime() > DateTimeOffset.FromUnixTimeMilliseconds(acquired).UtcDateTime.AddSeconds(1)) return null;
            }
            return new(pid, started, acquired);
        }
        catch (Exception error) when (error is IOException or JsonException or ArgumentException or System.ComponentModel.Win32Exception)
        { return null; }
    }

    private static bool StillOwns(string sessionId, Owner owner) => ReadOwner(sessionId) == owner;

    private static CliOpenResult OpenConsole(string sessionId, Owner owner, IEnumerable<ProcessEntry> chain)
    {
        if (GetConsoleWindow() != IntPtr.Zero)
            return new(false, "This terminal host does not expose a selectable console. The result stays unread.");
        var inspected = new HashSet<IntPtr>();
        foreach (var anchor in chain)
        {
            if (!AttachConsole((uint)anchor.Pid)) continue;
            try
            {
                IntPtr window = GetConsoleWindow();
                if (window == IntPtr.Zero || !inspected.Add(window)) continue;
                if (!StillOwns(sessionId, owner) || !ProcessAncestry.Read(owner.Pid).Any(process => process.Pid == anchor.Pid))
                    return new(false, "The CLI process changed before its terminal could be opened.");
                if (!ConsoleHas(sessionId, owner, anchor.Pid))
                    return new(false, "The CLI console is unavailable or shared by multiple sessions. Its result stays unread.");
                var windowClass = new StringBuilder(256);
                GetClassName(window, windowClass, windowClass.Capacity);
                if (windowClass.ToString() == "ConsoleWindowClass" && IsWindowVisible(window) && GetAncestor(window, 2) == window)
                {
                    bool focused = FocusWindow(window) && ConsoleHas(sessionId, owner, anchor.Pid) && StillOwns(sessionId, owner);
                    if (focused) return new(true, "");
                }
                var title = new StringBuilder(32768);
                GetConsoleTitle(title, (uint)title.Capacity);
                string originalTitle = title.ToString();
                string marker = "Kiroling-" + Guid.NewGuid().ToString("N");
                if (!SetConsoleTitle(marker)) continue;
                try
                {
                    bool selected = TerminalAutomation.Focus(marker, Windows("WindowsTerminal"));
                    if (selected && ConsoleHas(sessionId, owner, anchor.Pid) && StillOwns(sessionId, owner))
                        return new(true, "");
                }
                finally
                {
                    var currentTitle = new StringBuilder(32768);
                    GetConsoleTitle(currentTitle, (uint)currentTitle.Capacity);
                    if (currentTitle.ToString() == marker) SetConsoleTitle(originalTitle);
                }
            }
            finally { FreeConsole(); }
        }
        return new(false, "The terminal host did not expose the exact CLI tab or pane. Its result stays unread.");
    }

    private static bool ConsoleHas(string sessionId, Owner owner, int anchorPid)
    {
        uint[] processes = new uint[64];
        uint count = GetConsoleProcessList(processes, (uint)processes.Length);
        if (count > processes.Length) { processes = new uint[count]; count = GetConsoleProcessList(processes, count); }
        if (count == 0 || count > processes.Length || !processes.Take((int)count).Contains((uint)anchorPid) ||
            !ProcessAncestry.Read(owner.Pid).Any(process => process.Pid == anchorPid)) return false;
        var members = processes.Take((int)count).ToHashSet();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(PetPaths.Native, "session-leases"), "sess_*.lease"))
        {
            string otherId = Path.GetFileNameWithoutExtension(file);
            if (otherId == sessionId) continue;
            var other = ReadOwner(otherId);
            if (other != null && (other.Pid == owner.Pid ||
                ProcessAncestry.Read(other.Pid).Any(process => members.Contains((uint)process.Pid)))) return false;
        }
        return true;
    }

    internal static List<IntPtr> Windows(string processName)
    {
        var windows = new List<IntPtr>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            GetWindowThreadProcessId(window, out uint pid);
            try
            {
                using var process = Process.GetProcessById((int)pid);
                if (process.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)) windows.Add(window);
            }
            catch (ArgumentException) { }
            catch (System.ComponentModel.Win32Exception) { }
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    internal static bool FocusWindow(IntPtr window)
    {
        if (IsIconic(window)) ShowWindow(window, 9);
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        uint current = GetCurrentThreadId();
        uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint target = GetWindowThreadProcessId(window, out _);
        bool first = foreground != current && AttachThreadInput(current, foreground, true);
        bool second = target != current && target != foreground && AttachThreadInput(current, target, true);
        try { SetForegroundWindow(window); return GetForegroundWindow() == window; }
        finally
        {
            if (second) AttachThreadInput(current, target, false);
            if (first) AttachThreadInput(current, foreground, false);
        }
    }

    private delegate bool EnumerateWindow(IntPtr window, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowMessage
    {
        public IntPtr Window;
        public uint Id;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll")] private static extern bool FreeConsole();
    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll")] private static extern uint GetConsoleProcessList([Out] uint[] processes, uint count);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetConsoleTitle(StringBuilder title, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool SetConsoleTitle(string title);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumerateWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out WindowMessage message, IntPtr window, uint first, uint last, uint flags);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
}
