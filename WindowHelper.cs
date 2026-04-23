using System;
using System.Collections.Generic;

namespace KeyboardTaskRunner;

public record WindowInfo(IntPtr Hwnd, string Title, string ClassName, uint Pid);

public static class WindowHelper
{
    public static List<WindowInfo> EnumVisibleWindows(IntPtr excludeHwnd)
    {
        var list = new List<WindowInfo>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            if (hwnd == excludeHwnd) return true;

            var titleBuf = new char[256];
            int len = NativeMethods.GetWindowText(hwnd, titleBuf, 256);
            if (len == 0) return true;
            string title = new(titleBuf, 0, len);

            long exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
            IntPtr owner = NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER);
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0 && owner == IntPtr.Zero)
                return true;

            var clsBuf = new char[128];
            int clsLen = NativeMethods.GetClassName(hwnd, clsBuf, 128);
            string cls = clsLen > 0 ? new string(clsBuf, 0, clsLen) : "";

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            list.Add(new WindowInfo(hwnd, title, cls, pid));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static string FormatTitle(WindowInfo info) =>
        $"[{info.Pid}] {info.Title}";
}
