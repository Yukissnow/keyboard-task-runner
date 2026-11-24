using KeyboardTaskRunner;

namespace KeyboardTaskRunner.Tests;

public class PlaybackTimingTests
{
    private static MacroEvent Key(uint delay) =>
        new() { Type = EventType.KeyDown, ScanCode = 0x1E, VkCode = 0x41, DelayMs = delay };

    [Fact]
    public void FirstIteration_FirstEventUsesFixedStartDelay()
    {
        var events = new List<MacroEvent> { Key(5000), Key(50), Key(25) };
        var t = new long[3];

        PlaybackTiming.Compute(events, t, speed: 1f, jitter: false, jitterPct: 0, firstIteration: true, new Random(1));

        Assert.Equal(new long[] { 100_000, 150_000, 175_000 }, t);
    }

    [Fact]
    public void LaterIteration_KeepsRecordedFirstDelay()
    {
        var events = new List<MacroEvent> { Key(300), Key(50) };
        var t = new long[2];

        PlaybackTiming.Compute(events, t, 1f, false, 0, firstIteration: false, new Random(1));

        Assert.Equal(new long[] { 300_000, 350_000 }, t);
    }

    [Theory]
    [InlineData(2f, 25_000)]
    [InlineData(0.5f, 100_000)]
    public void Speed_ScalesDelays(float speed, long expectedSecondUs)
    {
        var events = new List<MacroEvent> { Key(0), Key(50) };
        var t = new long[2];

        PlaybackTiming.Compute(events, t, speed, false, 0, firstIteration: false, new Random(1));

        Assert.Equal(0, t[0]);
        Assert.Equal(expectedSecondUs, t[1]);
    }

    [Fact]
    public void Timeline_IsMonotonic()
    {
        var rng = new Random(42);
        var events = Enumerable.Range(0, 500).Select(_ => Key((uint)rng.Next(0, 200))).ToList();
        var t = new long[events.Count];

        PlaybackTiming.Compute(events, t, 1.7f, false, 0, true, new Random(7));

        for (int i = 1; i < t.Length; i++)
            Assert.True(t[i] >= t[i - 1]);
    }

    [Fact]
    public void ZeroSpeed_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlaybackTiming.Compute(new List<MacroEvent> { Key(1) }, new long[1], 0f, false, 0, true, new Random()));
    }
}

public class MacroSequenceTests
{
    private static MacroEvent Down(ushort sc, ushort vk) => new() { Type = EventType.KeyDown, ScanCode = sc, VkCode = vk, DelayMs = 10 };
    private static MacroEvent Up(ushort sc, ushort vk) => new() { Type = EventType.KeyUp, ScanCode = sc, VkCode = vk, DelayMs = 10 };

    [Fact]
    public void BalancedSequence_AppendsNothing()
    {
        var events = new List<MacroEvent> { Down(0x1E, 0x41), Up(0x1E, 0x41) };

        Assert.Equal(0, MacroSequence.AppendReleasesForHeldInputs(events));
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void HeldKeys_AreReleasedInReverseOrderWithZeroDelay()
    {
        // Ctrl down, Shift down, A down/up -> Ctrl and Shift still held
        var events = new List<MacroEvent>
        {
            Down(0x1D, 0xA2), Down(0x2A, 0xA0), Down(0x1E, 0x41), Up(0x1E, 0x41),
        };

        int added = MacroSequence.AppendReleasesForHeldInputs(events);

        Assert.Equal(2, added);
        Assert.Equal(EventType.KeyUp, events[4].Type);
        Assert.Equal((ushort)0x2A, events[4].ScanCode); // Shift first
        Assert.Equal((ushort)0xA0, events[4].VkCode);
        Assert.Equal((ushort)0x1D, events[5].ScanCode); // then Ctrl
        Assert.All(events.Skip(4), e => Assert.Equal(0u, e.DelayMs));
    }

    [Fact]
    public void ExtendedScanCode_IsPreserved()
    {
        var events = new List<MacroEvent> { Down(0xE04B, 0x25) };

        MacroSequence.AppendReleasesForHeldInputs(events);

        Assert.Equal((ushort)0xE04B, events[1].ScanCode);
    }

    [Fact]
    public void HeldMouseButton_IsReleasedAtLastPosition()
    {
        var events = new List<MacroEvent>
        {
            new() { Type = EventType.MouseDown, Button = MouseButton.Left, X = 1, Y = 2 },
            new() { Type = EventType.MouseMove, X = 300, Y = 400 },
        };

        MacroSequence.AppendReleasesForHeldInputs(events);

        var last = events[^1];
        Assert.Equal(EventType.MouseUp, last.Type);
        Assert.Equal(MouseButton.Left, last.Button);
        Assert.Equal((300, 400), (last.X, last.Y));
    }

    [Fact]
    public void HeldMouseButtonAndKey_MouseUpUsesLastMousePosition()
    {
        // Ctrl-drag stopped by F8: Ctrl and left button both still held
        var events = new List<MacroEvent>
        {
            Down(0x1D, 0xA2),
            new() { Type = EventType.MouseDown, Button = MouseButton.Left, X = 10, Y = 20 },
            new() { Type = EventType.MouseMove, X = 500, Y = 600 },
            Down(0x1E, 0x41), Up(0x1E, 0x41), // key events carry X/Y = 0
        };

        MacroSequence.AppendReleasesForHeldInputs(events);

        var mouseUp = events.Single(e => e.Type == EventType.MouseUp);
        Assert.Equal((500, 600), (mouseUp.X, mouseUp.Y));
        Assert.Contains(events, e => e.Type == EventType.KeyUp && e.ScanCode == 0x1D);
    }

    [Fact]
    public void EmptySequence_AppendsNothing()
    {
        var events = new List<MacroEvent>();
        Assert.Equal(0, MacroSequence.AppendReleasesForHeldInputs(events));
    }

    [Fact]
    public void CalledTwice_IsIdempotent()
    {
        var events = new List<MacroEvent> { Down(0x1E, 0x41) };
        MacroSequence.AppendReleasesForHeldInputs(events);

        Assert.Equal(0, MacroSequence.AppendReleasesForHeldInputs(events));
    }
}

public class ScreenMathTests
{
    [Fact]
    public void SingleMonitor_CornersMapToFullRange()
    {
        Assert.Equal((0, 0), ScreenMath.ToNormalizedAbsolute(0, 0, 0, 0, 1920, 1080));
        Assert.Equal((65535, 65535), ScreenMath.ToNormalizedAbsolute(1919, 1079, 0, 0, 1920, 1080));
    }

    [Fact]
    public void SecondaryMonitorOnLeft_NegativeCoordinatesMapInsideRange()
    {
        // virtual desktop: left monitor at x=-1920, primary at x=0; total 3840x1080
        var (x, y) = ScreenMath.ToNormalizedAbsolute(-1920, 0, -1920, 0, 3840, 1080);
        Assert.Equal((0, 0), (x, y));

        var (px, _) = ScreenMath.ToNormalizedAbsolute(0, 0, -1920, 0, 3840, 1080);
        Assert.InRange(px, 32760, 32780); // primary's left edge is the middle of the desktop
    }

    [Fact]
    public void OutOfBounds_IsClamped()
    {
        Assert.Equal((0, 65535), ScreenMath.ToNormalizedAbsolute(-50, 5000, 0, 0, 1920, 1080));
    }

    [Fact]
    public void DegenerateSize_ReturnsZero()
    {
        Assert.Equal((0, 0), ScreenMath.ToNormalizedAbsolute(10, 10, 0, 0, 0, 1));
    }
}

public class KeyRepeatFilterTests
{
    [Fact]
    public void AutoRepeat_OnlyFirstKeyDownCounts()
    {
        var f = new KeyRepeatFilter();

        Assert.True(f.OnKeyDown(0x77));
        Assert.False(f.OnKeyDown(0x77)); // typematic repeat
        Assert.False(f.OnKeyDown(0x77));
    }

    [Fact]
    public void AfterRelease_NextPressCountsAgain()
    {
        var f = new KeyRepeatFilter();
        f.OnKeyDown(0x77);
        f.OnKeyUp(0x77);

        Assert.True(f.OnKeyDown(0x77));
    }

    [Fact]
    public void KeysAreTrackedIndependently()
    {
        var f = new KeyRepeatFilter();
        f.OnKeyDown(0x77);

        Assert.True(f.OnKeyDown(0x7B));
    }
}
