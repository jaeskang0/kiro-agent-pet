using System.Runtime.InteropServices;

namespace KiroPet.Desktop;

public sealed class WindowsHotkey : IDisposable
{
    private readonly Action _action;
    private readonly Thread? _thread;
    private readonly WindowProcedure _procedure;
    private IntPtr _window;
    public bool Available { get; private set; }

    public WindowsHotkey(Action action)
    {
        _action = action;
        _procedure = Procedure;
        if (!OperatingSystem.IsWindows()) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "Kiroling shortcut" };
        _thread.Start();
    }

    private void Run()
    {
        string name = "KiroPetHotkey-" + Environment.ProcessId;
        var type = new WindowClass
        {
            Procedure = Marshal.GetFunctionPointerForDelegate(_procedure),
            Instance = GetModuleHandle(null),
            ClassName = name
        };
        if (RegisterClass(ref type) == 0) return;
        _window = CreateWindowEx(0, name, "", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, type.Instance, IntPtr.Zero);
        if (_window == IntPtr.Zero) return;
        Available = RegisterHotKey(_window, 1, 0x4009, 0x50);
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
        _window = IntPtr.Zero;
    }

    private IntPtr Procedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == 0x0312) { _action(); return IntPtr.Zero; }
        if (message == 0x0002) { UnregisterHotKey(window, 1); PostQuitMessage(0); return IntPtr.Zero; }
        return DefWindowProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_window != IntPtr.Zero) PostMessage(_window, 0x0010, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(1000);
    }

    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public IntPtr Procedure;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Id;
        public IntPtr WParam, LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WindowClass type);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint exStyle, string className, string name, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr window, uint minimum, uint maximum);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
