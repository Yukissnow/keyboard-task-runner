using System;
using System.Runtime.InteropServices;
using System.Threading;
using InputInterceptorNS;

namespace KeyboardTaskRunner;

public interface IInputEmitter : IDisposable
{
    void EmitKeyboard(MacroEvent evt);
    void EmitMouseMove(int screenX, int screenY);
    void EmitMouseButton(MacroEvent evt, int screenX, int screenY);
    void EmitMouseWheel(short delta);
    void ReleaseKey(ushort scanCode);
    void ReleaseMouseButton(MouseButton button);
}

public class SendInputEmitter : IInputEmitter
{
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();
    private readonly INPUT[] _input = new INPUT[1];

    public void EmitKeyboard(MacroEvent evt)
    {
        _input[0] = default;
        _input[0].type = NativeMethods.INPUT_KEYBOARD;
        _input[0].u.ki.wScan = (ushort)(evt.ScanCode & 0xFF);
        _input[0].u.ki.dwFlags = NativeMethods.KEYEVENTF_SCANCODE;
        if ((evt.ScanCode & 0xE000) != 0)
            _input[0].u.ki.dwFlags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
        if (evt.Type == EventType.KeyUp)
            _input[0].u.ki.dwFlags |= NativeMethods.KEYEVENTF_KEYUP;
        NativeMethods.SendInput(1, _input, InputSize);
    }

    public void EmitMouseMove(int screenX, int screenY)
    {
        int sw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        int sh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
        _input[0] = default;
        _input[0].type = NativeMethods.INPUT_MOUSE;
        _input[0].u.mi.dx = (int)((long)screenX * 65535 / sw);
        _input[0].u.mi.dy = (int)((long)screenY * 65535 / sh);
        _input[0].u.mi.dwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE;
        NativeMethods.SendInput(1, _input, InputSize);
    }

    public void EmitMouseButton(MacroEvent evt, int screenX, int screenY)
    {
        int sw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        int sh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
        _input[0] = default;
        _input[0].type = NativeMethods.INPUT_MOUSE;
        _input[0].u.mi.dx = (int)((long)screenX * 65535 / sw);
        _input[0].u.mi.dy = (int)((long)screenY * 65535 / sh);
        _input[0].u.mi.dwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE;
        bool down = evt.Type == EventType.MouseDown;
        switch (evt.Button)
        {
            case MouseButton.Left:
                _input[0].u.mi.dwFlags |= down ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_LEFTUP; break;
            case MouseButton.Right:
                _input[0].u.mi.dwFlags |= down ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_RIGHTUP; break;
            case MouseButton.Middle:
                _input[0].u.mi.dwFlags |= down ? NativeMethods.MOUSEEVENTF_MIDDLEDOWN : NativeMethods.MOUSEEVENTF_MIDDLEUP; break;
            case MouseButton.X1:
                _input[0].u.mi.dwFlags |= down ? NativeMethods.MOUSEEVENTF_XDOWN : NativeMethods.MOUSEEVENTF_XUP;
                _input[0].u.mi.mouseData = NativeMethods.XBUTTON1; break;
            case MouseButton.X2:
                _input[0].u.mi.dwFlags |= down ? NativeMethods.MOUSEEVENTF_XDOWN : NativeMethods.MOUSEEVENTF_XUP;
                _input[0].u.mi.mouseData = NativeMethods.XBUTTON2; break;
        }
        NativeMethods.SendInput(1, _input, InputSize);
    }

    public void EmitMouseWheel(short delta)
    {
        _input[0] = default;
        _input[0].type = NativeMethods.INPUT_MOUSE;
        _input[0].u.mi.dwFlags = NativeMethods.MOUSEEVENTF_WHEEL;
        _input[0].u.mi.mouseData = (uint)(short)delta;
        NativeMethods.SendInput(1, _input, InputSize);
    }

    public void ReleaseKey(ushort scanCode)
    {
        _input[0] = default;
        _input[0].type = NativeMethods.INPUT_KEYBOARD;
        _input[0].u.ki.wScan = (ushort)(scanCode & 0xFF);
        _input[0].u.ki.dwFlags = NativeMethods.KEYEVENTF_SCANCODE | NativeMethods.KEYEVENTF_KEYUP;
        if ((scanCode & 0xE000) != 0)
            _input[0].u.ki.dwFlags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
        NativeMethods.SendInput(1, _input, InputSize);
    }

    public void ReleaseMouseButton(MouseButton button)
    {
        _input[0] = default;
        _input[0].type = NativeMethods.INPUT_MOUSE;
        switch (button)
        {
            case MouseButton.Left: _input[0].u.mi.dwFlags = NativeMethods.MOUSEEVENTF_LEFTUP; break;
            case MouseButton.Right: _input[0].u.mi.dwFlags = NativeMethods.MOUSEEVENTF_RIGHTUP; break;
            case MouseButton.Middle: _input[0].u.mi.dwFlags = NativeMethods.MOUSEEVENTF_MIDDLEUP; break;
            case MouseButton.X1:
                _input[0].u.mi.dwFlags = NativeMethods.MOUSEEVENTF_XUP;
                _input[0].u.mi.mouseData = NativeMethods.XBUTTON1; break;
            case MouseButton.X2:
                _input[0].u.mi.dwFlags = NativeMethods.MOUSEEVENTF_XUP;
                _input[0].u.mi.mouseData = NativeMethods.XBUTTON2; break;
        }
        NativeMethods.SendInput(1, _input, InputSize);
    }

    public void Dispose() { }
}

public class InterceptionEmitter : IInputEmitter
{
    private readonly KeyboardHook _keyboard;
    private readonly MouseHook _mouse;

    public InterceptionEmitter()
    {
        if (!InputInterceptor.Initialize())
            throw new InvalidOperationException(
                "InputInterceptor 初始化失敗。請確認以系統管理員身分執行。");

        if (!InputInterceptor.CheckDriverInstalled())
            throw new InvalidOperationException(
                "Interception 驅動未安裝。請選 HID 模式以引導安裝，或重新開機後再試。");

        try
        {
            _keyboard = new KeyboardHook(callback: (ref KeyStroke _) => { });
            _mouse = new MouseHook(callback: (ref MouseStroke _) => { });
        }
        catch (Exception ex)
        {
            _keyboard?.Dispose();
            _mouse?.Dispose();
            throw new InvalidOperationException(
                $"建立 Hook 失敗：{ex.Message}\n請確認驅動已載入（重新開機後）。");
        }

        // brief settle time for hook threads
        Thread.Sleep(50);

        if (_keyboard.HasException)
        {
            var ex = _keyboard.Exception?.Message ?? "unknown";
            _keyboard.Dispose();
            _mouse.Dispose();
            throw new InvalidOperationException($"鍵盤 Hook 例外：{ex}");
        }
        if (_mouse.HasException)
        {
            var ex = _mouse.Exception?.Message ?? "unknown";
            _keyboard.Dispose();
            _mouse.Dispose();
            throw new InvalidOperationException($"滑鼠 Hook 例外：{ex}");
        }
    }

    public void EmitKeyboard(MacroEvent evt)
    {
        var state = evt.Type == EventType.KeyUp ? KeyState.Up : KeyState.Down;
        if ((evt.ScanCode & 0xE000) != 0)
            state |= KeyState.E0;
        _keyboard.SetKeyState((KeyCode)(evt.ScanCode & 0xFF), state);
    }

    public void EmitMouseMove(int screenX, int screenY)
    {
        _mouse.SetCursorPosition(screenX, screenY, false);
    }

    public void EmitMouseButton(MacroEvent evt, int screenX, int screenY)
    {
        _mouse.SetCursorPosition(screenX, screenY, false);
        bool down = evt.Type == EventType.MouseDown;
        switch (evt.Button)
        {
            case MouseButton.Left:
                if (down) _mouse.SimulateLeftButtonDown(); else _mouse.SimulateLeftButtonUp(); break;
            case MouseButton.Right:
                if (down) _mouse.SimulateRightButtonDown(); else _mouse.SimulateRightButtonUp(); break;
            case MouseButton.Middle:
                if (down) _mouse.SimulateMiddleButtonDown(); else _mouse.SimulateMiddleButtonUp(); break;
            case MouseButton.X1:
                _mouse.SetMouseState(down ? MouseState.ExtraButton1Down : MouseState.ExtraButton1Up, 0); break;
            case MouseButton.X2:
                _mouse.SetMouseState(down ? MouseState.ExtraButton2Down : MouseState.ExtraButton2Up, 0); break;
        }
    }

    public void EmitMouseWheel(short delta)
    {
        if (delta > 0) _mouse.SimulateScrollUp(delta);
        else _mouse.SimulateScrollDown((short)-delta);
    }

    public void ReleaseKey(ushort scanCode)
    {
        var state = KeyState.Up;
        if ((scanCode & 0xE000) != 0) state |= KeyState.E0;
        _keyboard.SetKeyState((KeyCode)(scanCode & 0xFF), state);
    }

    public void ReleaseMouseButton(MouseButton button)
    {
        switch (button)
        {
            case MouseButton.Left: _mouse.SimulateLeftButtonUp(); break;
            case MouseButton.Right: _mouse.SimulateRightButtonUp(); break;
            case MouseButton.Middle: _mouse.SimulateMiddleButtonUp(); break;
            case MouseButton.X1: _mouse.SetMouseState(MouseState.ExtraButton1Up, 0); break;
            case MouseButton.X2: _mouse.SetMouseState(MouseState.ExtraButton2Up, 0); break;
        }
    }

    public void Dispose()
    {
        _keyboard.Dispose();
        _mouse.Dispose();
    }
}
