using KeyboardTaskRunner;

namespace KeyboardTaskRunner.Tests;

public class MacroFileTests
{
    private static List<MacroEvent> Sample() => new()
    {
        new() { Type = EventType.KeyDown, ScanCode = 0x1E, VkCode = 0x41, DelayMs = 0 },
        new() { Type = EventType.KeyUp, ScanCode = 0x1E, VkCode = 0x41, DelayMs = 120 },
        new() { Type = EventType.KeyDown, ScanCode = 0xE04B, VkCode = 0x25, DelayMs = 33 },
        new() { Type = EventType.MouseMove, X = -1920, Y = 1079, DelayMs = 8 },
        new() { Type = EventType.MouseDown, Button = MouseButton.X2, X = 10, Y = 20, DelayMs = 5 },
        new() { Type = EventType.MouseWheel, WheelDelta = -120, DelayMs = uint.MaxValue },
    };

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var events = Sample();
        using var ms = new MemoryStream();
        MacroFile.Save(ms, events);
        ms.Position = 0;

        var loaded = MacroFile.Load(ms);

        Assert.Equal(events, loaded);
    }

    [Fact]
    public void Save_WritesKtr1HeaderAndFixedSizeEvents()
    {
        using var ms = new MemoryStream();
        MacroFile.Save(ms, Sample());
        var bytes = ms.ToArray();

        Assert.Equal("KTR1"u8.ToArray(), bytes[..4]);
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 4));
        Assert.Equal(6, BitConverter.ToInt32(bytes, 6));
        Assert.Equal(4 + 2 + 4 + 6 * 20, bytes.Length); // format unchanged: 20 bytes/event
    }

    [Fact]
    public void Load_EmptyMacro_ReturnsEmptyList()
    {
        using var ms = new MemoryStream();
        MacroFile.Save(ms, new List<MacroEvent>());
        ms.Position = 0;

        Assert.Empty(MacroFile.Load(ms));
    }

    [Fact]
    public void Load_WrongMagic_Throws()
    {
        using var ms = new MemoryStream(new byte[] { 1, 2, 3, 4, 1, 0, 0, 0, 0, 0 });
        Assert.Throws<InvalidDataException>(() => MacroFile.Load(ms));
    }

    [Fact]
    public void Load_TruncatedFile_ThrowsInvalidData()
    {
        using var ms = new MemoryStream();
        MacroFile.Save(ms, Sample());
        var truncated = ms.ToArray()[..^7];

        Assert.Throws<InvalidDataException>(() => MacroFile.Load(new MemoryStream(truncated)));
    }

    [Fact]
    public void Load_CountLargerThanData_ThrowsBeforeAllocating()
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(0x3152544Bu);
        w.Write((ushort)1);
        w.Write(9_000_000);
        w.Flush();
        ms.Position = 0;

        Assert.Throws<InvalidDataException>(() => MacroFile.Load(ms));
    }

    [Fact]
    public void Load_UndefinedEventType_Throws()
    {
        using var ms = new MemoryStream();
        MacroFile.Save(ms, new List<MacroEvent> { new() { Type = EventType.KeyDown } });
        var bytes = ms.ToArray();
        bytes[10] = 99; // first byte of first event = Type

        Assert.Throws<InvalidDataException>(() => MacroFile.Load(new MemoryStream(bytes)));
    }

    [Fact]
    public void SaveAndLoad_ViaFilePath()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ktr-test-{Guid.NewGuid():N}.ktr");
        try
        {
            MacroFile.Save(path, Sample());
            Assert.Equal(Sample(), MacroFile.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
