using System.Runtime.InteropServices;

namespace KiroPet;

internal static class TerminalAutomation
{
    public static bool Focus(string marker, IEnumerable<IntPtr> windows)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var type = Type.GetTypeFromCLSID(new Guid("ff48dba4-60ef-4201-aa87-54103eef594e"), true);
        var automation = (Automation)Activator.CreateInstance(type);
        try
        {
            automation.CreateTrueCondition(out object condition);
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(4);
                while (DateTime.UtcNow < deadline)
                {
                    foreach (IntPtr window in windows)
                    {
                        automation.ElementFromHandle(window, out var root);
                        try
                        {
                            root.FindAll(4, condition, out var elements);
                            try
                            {
                                elements.Length(out int length);
                                for (int index = 0; index < Math.Min(length, 4096); index++)
                                {
                                    elements.GetElement(index, out var element);
                                    try
                                    {
                                        element.GetCurrentPropertyValue(30005, out object name);
                                        element.GetCurrentPropertyValue(30003, out object control);
                                        if (name is not string title || !title.Contains(marker, StringComparison.Ordinal) ||
                                            control is not int kind || kind is not (50019 or 50030)) continue;
                                        if (!CliNavigator.FocusWindow(window)) continue;
                                        if (kind == 50019)
                                        {
                                            element.GetCurrentPattern(10010, out object pattern);
                                            try { ((SelectionItem)pattern).Select(); }
                                            finally { Release(pattern); }
                                        }
                                        else element.SetFocus();
                                        if (Confirmed(automation, root, condition, marker, window)) return true;
                                    }
                                    catch (COMException) { }
                                    finally { Release(element); }
                                }
                            }
                            finally { Release(elements); }
                        }
                        finally { Release(root); }
                    }
                    Thread.Sleep(100);
                }
            }
            finally { Release(condition); }
            return false;
        }
        finally { Release(automation); }
    }

    private static bool Confirmed(Automation automation, Element root, object condition, string marker, IntPtr window)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(800);
        while (DateTime.UtcNow < deadline)
        {
            root.FindAll(4, condition, out var elements);
            var terminals = new List<Element>();
            try
            {
                bool selectedTab = false;
                elements.Length(out int length);
                for (int index = 0; index < Math.Min(length, 4096); index++)
                {
                    elements.GetElement(index, out var element);
                    try
                    {
                        element.GetCurrentPropertyValue(30012, out object className);
                        if (className is string terminalClass && terminalClass == "TermControl")
                        {
                            terminals.Add(element);
                            element = null;
                            continue;
                        }
                        element.GetCurrentPropertyValue(30005, out object name);
                        element.GetCurrentPropertyValue(30003, out object control);
                        if (name is not string title || !title.Contains(marker, StringComparison.Ordinal) || control is not int kind || kind != 50019) continue;
                        element.GetCurrentPropertyValue(30079, out object selected);
                        selectedTab |= selected is true;
                    }
                    catch (COMException) { }
                    finally { Release(element); }
                }
                if (selectedTab && terminals.Count == 1)
                {
                    terminals[0].SetFocus();
                    automation.GetFocusedElement(out var focused);
                    try
                    {
                        automation.CompareElements(terminals[0], focused, out bool same);
                        if (same && CliNavigator.GetForegroundWindow() == window) return true;
                    }
                    finally { Release(focused); }
                }
            }
            finally
            {
                foreach (var terminal in terminals) Release(terminal);
                Release(elements);
            }
            Thread.Sleep(50);
        }
        return false;
    }

    private static void Release(object value)
    {
        if (OperatingSystem.IsWindows() && value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface Automation
    {
        void CompareElements(Element first, Element second, [MarshalAs(UnmanagedType.Bool)] out bool same);
        void CompareRuntimeIds();
        void GetRootElement(out Element element);
        void ElementFromHandle(IntPtr window, out Element element);
        void ElementFromPoint();
        void GetFocusedElement(out Element element);
        void GetRootElementBuildCache();
        void ElementFromHandleBuildCache();
        void ElementFromPointBuildCache();
        void GetFocusedElementBuildCache();
        void CreateTreeWalker();
        void ControlViewWalker();
        void ContentViewWalker();
        void RawViewWalker();
        void RawViewCondition();
        void ControlViewCondition();
        void ContentViewCondition();
        void CreateCacheRequest();
        void CreateTrueCondition([MarshalAs(UnmanagedType.Interface)] out object condition);
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface Element
    {
        void SetFocus();
        void GetRuntimeId();
        void FindFirst();
        void FindAll(int scope, [MarshalAs(UnmanagedType.Interface)] object condition, out ElementArray elements);
        void FindFirstBuildCache();
        void FindAllBuildCache();
        void BuildUpdatedCache();
        void GetCurrentPropertyValue(int id, [MarshalAs(UnmanagedType.Struct)] out object value);
        void GetCurrentPropertyValueEx();
        void GetCachedPropertyValue();
        void GetCachedPropertyValueEx();
        void GetCurrentPatternAs();
        void GetCachedPatternAs();
        void GetCurrentPattern(int id, [MarshalAs(UnmanagedType.IUnknown)] out object pattern);
    }

    [ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ElementArray
    {
        void Length(out int count);
        void GetElement(int index, out Element element);
    }

    [ComImport, Guid("a8efa66a-0fda-421a-9194-38021f3578ea"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface SelectionItem { void Select(); }
}
