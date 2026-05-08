using System;
using System.Runtime.InteropServices;

namespace KeyboardTaskRunner;

public class InjectedFlagCleaner
{
    private IntPtr _kbHook;
    private IntPtr _msHook;
    private NativeMethods.HookProc? _kbProc;
    private NativeMethods.HookProc? _msProc;

    public void Install()
    {
        if (_kbHook != IntPtr.Zero) return;

        _kbProc = KbProc;
        _msProc = MsProc;
        _kbHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _kbProc, IntPtr.Zero, 0);
        _msHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL, _msProc, IntPtr.Zero, 0);
    }

    public void Uninstall()
    {
        if (_kbHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_kbHook);
            _kbHook = IntPtr.Zero;
        }
        if (_msHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_msHook);
            _msHook = IntPtr.Zero;
        }
    }

    private IntPtr KbProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == NativeMethods.HC_ACTION)
        {
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((kb.flags & NativeMethods.LLKHF_INJECTED) != 0)
            {
                kb.flags &= ~NativeMethods.LLKHF_INJECTED;
                Marshal.StructureToPtr(kb, lParam, false);
            }
        }
        return NativeMethods.CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private IntPtr MsProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == NativeMethods.HC_ACTION)
        {
            var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if ((ms.flags & NativeMethods.LLMHF_INJECTED) != 0)
            {
                ms.flags &= ~NativeMethods.LLMHF_INJECTED;
                Marshal.StructureToPtr(ms, lParam, false);
            }
        }
        return NativeMethods.CallNextHookEx(_msHook, nCode, wParam, lParam);
    }
}
