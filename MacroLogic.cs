using System;
using System.Collections.Generic;

namespace KeyboardTaskRunner;

/// <summary>Playback timeline calculation (no Win32 dependency, unit-testable).</summary>
public static class PlaybackTiming
{
    /// <summary>Delay used for the first event of the first iteration, in ms.</summary>
    public const uint FirstEventDelayMs = 100;

    private static readonly HashSet<ushort> NoJitterKeys = new()
    {
        0x25, 0x26, 0x27, 0x28, 0x20,
    };

    private static bool IsNoJitterEvent(MacroEvent evt) =>
        (evt.Type == EventType.KeyDown || evt.Type == EventType.KeyUp)
        && NoJitterKeys.Contains(evt.VkCode);

    /// <summary>
    /// Fills <paramref name="absTimesUs"/> with each event's absolute offset (µs) from loop start.
    /// </summary>
    public static void Compute(IReadOnlyList<MacroEvent> events, long[] absTimesUs,
        float speed, bool jitter, int jitterPct, bool firstIteration, Random rng)
    {
        if (speed <= 0) throw new ArgumentOutOfRangeException(nameof(speed));
        if (absTimesUs.Length < events.Count) throw new ArgumentException("Buffer too small.", nameof(absTimesUs));

        long cumUs = 0;
        for (int i = 0; i < events.Count; i++)
        {
            uint delayMs = events[i].DelayMs;
            if (i == 0 && firstIteration) delayMs = FirstEventDelayMs;
            double scaledMs = delayMs / (double)speed;
            if (jitter && scaledMs > 0 && !IsNoJitterEvent(events[i]))
            {
                double factor = 1.0 + (rng.Next(2 * jitterPct + 1) - jitterPct) / 100.0;
                scaledMs *= factor;
            }
            cumUs += (long)(scaledMs * 1000);
            absTimesUs[i] = cumUs;
        }
    }
}

/// <summary>Post-processing of recorded event sequences.</summary>
public static class MacroSequence
{
    /// <summary>
    /// Appends KeyUp / MouseUp events (delay 0) for every key or mouse button that is
    /// still held at the end of the sequence, so repeated playback never leaves input stuck.
    /// Returns the number of events appended.
    /// </summary>
    public static int AppendReleasesForHeldInputs(List<MacroEvent> events)
    {
        var heldKeys = new Dictionary<ushort, ushort>(); // scanCode -> vkCode
        var keyOrder = new List<ushort>();
        var heldButtons = new List<MouseButton>();
        int lastMouseX = 0, lastMouseY = 0; // key events carry X/Y = 0, so track mouse events only

        foreach (var e in events)
        {
            if (e.Type is EventType.MouseMove or EventType.MouseDown or EventType.MouseUp)
            {
                lastMouseX = e.X;
                lastMouseY = e.Y;
            }
            switch (e.Type)
            {
                case EventType.KeyDown:
                    if (heldKeys.TryAdd(e.ScanCode, e.VkCode)) keyOrder.Add(e.ScanCode);
                    break;
                case EventType.KeyUp:
                    if (heldKeys.Remove(e.ScanCode)) keyOrder.Remove(e.ScanCode);
                    break;
                case EventType.MouseDown:
                    if (!heldButtons.Contains(e.Button)) heldButtons.Add(e.Button);
                    break;
                case EventType.MouseUp:
                    heldButtons.Remove(e.Button);
                    break;
            }
        }

        int appended = 0;
        // release in reverse press order (e.g. Ctrl+Shift+A -> A, Shift, Ctrl)
        for (int i = keyOrder.Count - 1; i >= 0; i--)
        {
            ushort sc = keyOrder[i];
            events.Add(new MacroEvent { Type = EventType.KeyUp, ScanCode = sc, VkCode = heldKeys[sc], DelayMs = 0 });
            appended++;
        }

        for (int i = heldButtons.Count - 1; i >= 0; i--)
        {
            events.Add(new MacroEvent
            {
                Type = EventType.MouseUp, Button = heldButtons[i], X = lastMouseX, Y = lastMouseY, DelayMs = 0,
            });
            appended++;
        }
        return appended;
    }
}

/// <summary>Screen coordinate helpers.</summary>
public static class ScreenMath
{
    /// <summary>
    /// Maps a pixel position on the virtual desktop to SendInput's normalized 0..65535 range
    /// (used with MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK).
    /// </summary>
    public static (int X, int Y) ToNormalizedAbsolute(int x, int y,
        int virtualLeft, int virtualTop, int virtualWidth, int virtualHeight)
    {
        return (Normalize(x - virtualLeft, virtualWidth), Normalize(y - virtualTop, virtualHeight));
    }

    private static int Normalize(int offset, int size)
    {
        if (size <= 1) return 0;
        double v = Math.Round(offset * 65535.0 / (size - 1));
        return (int)Math.Clamp(v, 0, 65535);
    }
}

/// <summary>
/// Filters OS typematic auto-repeat: only the first KeyDown of a press counts until the key is released.
/// </summary>
public sealed class KeyRepeatFilter
{
    private readonly HashSet<uint> _down = new();

    /// <summary>Returns true only for the first KeyDown since the key was last released.</summary>
    public bool OnKeyDown(uint vkCode) => _down.Add(vkCode);

    /// <summary>Marks the key as released so its next KeyDown counts again.</summary>
    public void OnKeyUp(uint vkCode) => _down.Remove(vkCode);
}
