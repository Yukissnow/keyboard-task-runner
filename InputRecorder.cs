using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace KeyboardTaskRunner;

public class InputRecorder
{
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private IntPtr _targetWindow;
    private readonly List<MacroEvent> _events = new();
    private long _lastTime;
    private long _freq;
    private bool _active;

    private NativeMethods.HookProc? _keyboardProc;
    private NativeMethods.HookProc? _mouseProc;

    private static readonly HashSet<uint> IgnoredKeys = new() { 0x77, 0x7B };
    private readonly HashSet<uint> _keysDown = new();
    private int _lastMouseX, _lastMouseY;
    private long _lastMouseTime;
    private bool _recordMouse;

    public bool IsRecording => _active;

    public void Start(IntPtr targetWindow, bool recordMouse = true)
    {
        _events.Clear();
        _keysDown.Clear();
        _targetWindow = targetWindow;
        _recordMouse = recordMouse;
        _active = true;

        NativeMethods.QueryPerformanceFrequency(out _freq);
        NativeMethods.QueryPerformanceCounter(out _lastTime);

        _keyboardProc = KeyboardProc;

        _keyboardHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _keyboardProc, IntPtr.Zero, 0);

        if (_recordMouse)
        {
            _mouseProc = MouseProc;
            _mouseHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_MOUSE_LL, _mouseProc, IntPtr.Zero, 0);
        }
    }

    public void Stop()
    {
        _active = false;
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    public List<MacroEvent> GetEvents() => new(_events);

    private uint ElapsedMs()
    {
        NativeMethods.QueryPerformanceCounter(out long now);
        uint ms = (uint)((now - _lastTime) * 1000 / _freq);
        _lastTime = now;
        return ms;
    }

    private POINT ToClient(int screenX, int screenY)
    {
        var pt = new POINT { X = screenX, Y = screenY };
        if (_targetWindow != IntPtr.Zero && NativeMethods.IsWindow(_targetWindow))
            NativeMethods.ScreenToClient(_targetWindow, ref pt);
        return pt;
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == NativeMethods.HC_ACTION && _active)
        {
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((kb.flags & NativeMethods.LLKHF_INJECTED) == 0 && !IgnoredKeys.Contains(kb.vkCode))
            {
                uint msg = (uint)wParam.ToInt64();
                bool isDown = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;

                // skip repeated KeyDown (key already held)
                if (isDown && !_keysDown.Add(kb.vkCode))
                    return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
                if (!isDown)
                    _keysDown.Remove(kb.vkCode);

                var evt = new MacroEvent
                {
                    Type = isDown ? EventType.KeyDown : EventType.KeyUp,
                    VkCode = (ushort)kb.vkCode,
                    ScanCode = (ushort)kb.scanCode,
                    DelayMs = ElapsedMs(),
                };
                if ((kb.flags & NativeMethods.LLKHF_EXTENDED) != 0)
                    evt.ScanCode |= 0xE000;
                _events.Add(evt);
            }
        }
        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == NativeMethods.HC_ACTION && _active)
        {
            var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if ((ms.flags & NativeMethods.LLMHF_INJECTED) == 0)
            {
                uint msg = (uint)wParam.ToInt64();

                // throttle mouse move: skip if <5px change and <8ms
                if (msg == NativeMethods.WM_MOUSEMOVE)
                {
                    int dx = ms.pt.X - _lastMouseX;
                    int dy = ms.pt.Y - _lastMouseY;
                    NativeMethods.QueryPerformanceCounter(out long nowTick);
                    long elapsedUs = (nowTick - _lastMouseTime) * 1_000_000 / _freq;
                    if (dx * dx + dy * dy < 25 && elapsedUs < 8000)
                        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                    _lastMouseX = ms.pt.X;
                    _lastMouseY = ms.pt.Y;
                    _lastMouseTime = nowTick;
                }

                var pt = ToClient(ms.pt.X, ms.pt.Y);
                var evt = new MacroEvent
                {
                    X = pt.X,
                    Y = pt.Y,
                    DelayMs = ElapsedMs(),
                };

                switch (msg)
                {
                    case NativeMethods.WM_MOUSEMOVE:
                        evt.Type = EventType.MouseMove; break;
                    case NativeMethods.WM_LBUTTONDOWN:
                        evt.Type = EventType.MouseDown; evt.Button = MouseButton.Left; break;
                    case NativeMethods.WM_LBUTTONUP:
                        evt.Type = EventType.MouseUp; evt.Button = MouseButton.Left; break;
                    case NativeMethods.WM_RBUTTONDOWN:
                        evt.Type = EventType.MouseDown; evt.Button = MouseButton.Right; break;
                    case NativeMethods.WM_RBUTTONUP:
                        evt.Type = EventType.MouseUp; evt.Button = MouseButton.Right; break;
                    case NativeMethods.WM_MBUTTONDOWN:
                        evt.Type = EventType.MouseDown; evt.Button = MouseButton.Middle; break;
                    case NativeMethods.WM_MBUTTONUP:
                        evt.Type = EventType.MouseUp; evt.Button = MouseButton.Middle; break;
                    case NativeMethods.WM_MOUSEWHEEL:
                        evt.Type = EventType.MouseWheel;
                        evt.WheelDelta = (short)(ms.mouseData >> 16);
                        break;
                    case NativeMethods.WM_XBUTTONDOWN:
                        evt.Type = EventType.MouseDown;
                        evt.Button = (ms.mouseData >> 16) == NativeMethods.XBUTTON1
                            ? MouseButton.X1 : MouseButton.X2;
                        break;
                    case NativeMethods.WM_XBUTTONUP:
                        evt.Type = EventType.MouseUp;
                        evt.Button = (ms.mouseData >> 16) == NativeMethods.XBUTTON1
                            ? MouseButton.X1 : MouseButton.X2;
                        break;
                    default:
                        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                }
                _events.Add(evt);
            }
        }
        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }
}
