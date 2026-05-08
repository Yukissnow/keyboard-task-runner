using System;
using System.Collections.Generic;
using System.Threading;

namespace KeyboardTaskRunner;

public class InputPlayer
{
    private Thread? _thread;
    private volatile bool _stopFlag;
    private volatile bool _playing;
    private readonly ManualResetEventSlim _stopEvent = new(false);
    private readonly Random _rng = new();

    public bool IsPlaying => _playing;
    public event Action? PlaybackFinished;
    public event Action<string>? PlaybackError;

    public void Start(List<MacroEvent> events, IntPtr targetWindow,
        float speed, int repeatCount, bool infinite, bool jitter, int jitterPercent,
        InputMode mode = InputMode.Normal)
    {
        if (_playing) return;
        _stopFlag = false;
        _stopEvent.Reset();
        _playing = true;

        var copy = new List<MacroEvent>(events);
        _thread = new Thread(() =>
        {
            NativeMethods.timeBeginPeriod(1);
            try
            {
                Run(copy, targetWindow, speed, repeatCount, infinite, jitter, jitterPercent, mode);
            }
            catch (Exception ex)
            {
                PlaybackError?.Invoke($"{ex.Message}\n\n{ex.GetType().Name}\n{ex.StackTrace}");
            }
            finally
            {
                NativeMethods.timeEndPeriod(1);
                _playing = false;
                PlaybackFinished?.Invoke();
            }
        })
        { IsBackground = true };
        _thread.Start();
    }

    public void Stop()
    {
        _stopFlag = true;
        _stopEvent.Set();
        var t = _thread;
        if (t != null && t.IsAlive)
            t.Join(500);
    }

    private static readonly HashSet<ushort> NoJitterKeys = new()
    {
        0x25, 0x26, 0x27, 0x28, 0x20,
    };

    private static bool IsNoJitterEvent(MacroEvent evt) =>
        (evt.Type == EventType.KeyDown || evt.Type == EventType.KeyUp)
        && NoJitterKeys.Contains(evt.VkCode);

    private void ComputeTimestamps(List<MacroEvent> events, long[] absTimesUs,
        float speed, bool jitter, int jitterPct, bool firstIteration)
    {
        long cumUs = 0;
        for (int i = 0; i < events.Count; i++)
        {
            uint delayMs = events[i].DelayMs;
            if (i == 0 && firstIteration) delayMs = 100;
            double scaledMs = delayMs / (double)speed;
            if (jitter && scaledMs > 0 && !IsNoJitterEvent(events[i]))
            {
                double factor = 1.0 + (_rng.Next(2 * jitterPct + 1) - jitterPct) / 100.0;
                scaledMs *= factor;
            }
            cumUs += (long)(scaledMs * 1000);
            absTimesUs[i] = cumUs;
        }
    }

    private void Run(List<MacroEvent> events, IntPtr target,
        float speed, int repeatCount, bool infinite, bool jitter, int jitterPct,
        InputMode mode)
    {
        bool hasTarget = target != IntPtr.Zero && NativeMethods.IsWindow(target);
        IntPtr originalFg = IntPtr.Zero;
        int iteration = 0;

        if (hasTarget)
        {
            originalFg = NativeMethods.GetForegroundWindow();
            NativeMethods.SetForegroundWindow(target);
            Thread.Sleep(50);
        }

        var absTimesUs = new long[events.Count];
        ComputeTimestamps(events, absTimesUs, speed, jitter, jitterPct, true);

        NativeMethods.QueryPerformanceFrequency(out long freq);
        var heldKeys = new HashSet<ushort>();
        var heldButtons = new HashSet<MouseButton>();

        using IInputEmitter emitter = mode == InputMode.HID
            ? new InterceptionEmitter()
            : new SendInputEmitter();

        try
        {
            while (!_stopFlag && (infinite || iteration < repeatCount))
            {
                NativeMethods.QueryPerformanceCounter(out long loopStart);

                for (int i = 0; i < events.Count; i++)
                {
                    if (_stopFlag) break;

                    long targetTick = loopStart + absTimesUs[i] * freq / 1_000_000;
                    WaitUntil(targetTick, freq);
                    if (_stopFlag) break;

                    var evt = events[i];
                    if (evt.Type == EventType.KeyDown) heldKeys.Add(evt.ScanCode);
                    else if (evt.Type == EventType.KeyUp) heldKeys.Remove(evt.ScanCode);
                    else if (evt.Type == EventType.MouseDown) heldButtons.Add(evt.Button);
                    else if (evt.Type == EventType.MouseUp) heldButtons.Remove(evt.Button);

                    EmitEvent(evt, target, emitter);
                }
                iteration++;

                if (!_stopFlag && (infinite || iteration < repeatCount))
                    ComputeTimestamps(events, absTimesUs, speed, jitter, jitterPct, false);
            }
        }
        finally
        {
            ReleaseAll(heldKeys, heldButtons, emitter);
            if (hasTarget && originalFg != IntPtr.Zero && NativeMethods.IsWindow(originalFg))
                NativeMethods.SetForegroundWindow(originalFg);
        }
    }

    private void WaitUntil(long targetTick, long freq)
    {
        while (!_stopFlag)
        {
            NativeMethods.QueryPerformanceCounter(out long now);
            if (now >= targetTick) return;

            long remainUs = (targetTick - now) * 1_000_000 / freq;
            if (remainUs > 5000)
            {
                _stopEvent.Wait((int)(remainUs / 1000 - 2));
                return;
            }
            else if (remainUs > 500)
                Thread.Sleep(0);
            else
                Thread.SpinWait(20);
        }
    }

    private static void ReleaseAll(HashSet<ushort> heldKeys, HashSet<MouseButton> heldButtons, IInputEmitter emitter)
    {
        foreach (var sc in heldKeys)
            emitter.ReleaseKey(sc);
        heldKeys.Clear();

        foreach (var btn in heldButtons)
            emitter.ReleaseMouseButton(btn);
        heldButtons.Clear();
    }

    private static POINT ToScreen(int cx, int cy, IntPtr hwnd)
    {
        var pt = new POINT { X = cx, Y = cy };
        if (hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd))
            NativeMethods.ClientToScreen(hwnd, ref pt);
        return pt;
    }

    private static void EmitEvent(MacroEvent evt, IntPtr target, IInputEmitter emitter)
    {
        switch (evt.Type)
        {
            case EventType.KeyDown:
            case EventType.KeyUp:
                emitter.EmitKeyboard(evt);
                break;
            case EventType.MouseMove:
            {
                var pt = ToScreen(evt.X, evt.Y, target);
                emitter.EmitMouseMove(pt.X, pt.Y);
                break;
            }
            case EventType.MouseDown:
            case EventType.MouseUp:
            {
                var pt = ToScreen(evt.X, evt.Y, target);
                emitter.EmitMouseButton(evt, pt.X, pt.Y);
                break;
            }
            case EventType.MouseWheel:
                emitter.EmitMouseWheel(evt.WheelDelta);
                break;
        }
    }
}
