using System;
using System.Collections.Generic;
using System.IO;

namespace KeyboardTaskRunner;

public enum InputMode : byte
{
    Normal = 0,
    HID = 1,
}

public enum EventType : byte
{
    KeyDown = 0,
    KeyUp = 1,
    MouseMove = 2,
    MouseDown = 3,
    MouseUp = 4,
    MouseWheel = 5,
}

public enum MouseButton : byte
{
    Left = 0,
    Right = 1,
    Middle = 2,
    X1 = 3,
    X2 = 4,
}

public struct MacroEvent
{
    public EventType Type;
    public ushort ScanCode;
    public ushort VkCode;
    public int X;
    public int Y;
    public MouseButton Button;
    public short WheelDelta;
    public uint DelayMs;
}

public static class MacroFile
{
    private const uint Magic = 0x3152544B; // "KTR1"
    private const ushort Version = 1;

    public static void Save(string path, List<MacroEvent> events)
    {
        using var fs = new FileStream(path, FileMode.Create);
        Save(fs, events);
    }

    public static List<MacroEvent> Load(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        return Load(fs);
    }

    /// <summary>Writes events in the .ktr v1 binary format. The stream is left open.</summary>
    public static void Save(Stream stream, IReadOnlyList<MacroEvent> events)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(Version);
        w.Write(events.Count);
        foreach (var e in events)
        {
            w.Write((byte)e.Type);
            w.Write(e.ScanCode);
            w.Write(e.VkCode);
            w.Write(e.X);
            w.Write(e.Y);
            w.Write((byte)e.Button);
            w.Write(e.WheelDelta);
            w.Write(e.DelayMs);
        }
    }

    /// <summary>Size of one serialized event in bytes.</summary>
    private const int EventSize = 1 + 2 + 2 + 4 + 4 + 1 + 2 + 4;

    /// <summary>
    /// Reads a .ktr v1 stream. Throws <see cref="InvalidDataException"/> for bad headers,
    /// truncated data or undefined event values. The stream is left open.
    /// </summary>
    public static List<MacroEvent> Load(Stream stream)
    {
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        try
        {
            uint magic = r.ReadUInt32();
            ushort version = r.ReadUInt16();
            if (magic != Magic || version != Version)
                throw new InvalidDataException("不是有效的 KTR 巨集檔。");
            int count = r.ReadInt32();
            if (count < 0 || count > 10_000_000)
                throw new InvalidDataException("事件數量超出範圍。");
            // reject truncated files before allocating a huge list
            if (stream.CanSeek && stream.Length - stream.Position < (long)count * EventSize)
                throw new InvalidDataException("檔案不完整（可能已損毀）。");

            var events = new List<MacroEvent>(count);
            for (int i = 0; i < count; i++)
            {
                var e = new MacroEvent
                {
                    Type = (EventType)r.ReadByte(),
                    ScanCode = r.ReadUInt16(),
                    VkCode = r.ReadUInt16(),
                    X = r.ReadInt32(),
                    Y = r.ReadInt32(),
                    Button = (MouseButton)r.ReadByte(),
                    WheelDelta = r.ReadInt16(),
                    DelayMs = r.ReadUInt32(),
                };
                if (!Enum.IsDefined(e.Type) || !Enum.IsDefined(e.Button))
                    throw new InvalidDataException($"第 {i + 1} 筆事件資料無效（可能已損毀）。");
                events.Add(e);
            }
            return events;
        }
        catch (EndOfStreamException)
        {
            throw new InvalidDataException("檔案不完整（可能已損毀）。");
        }
    }
}
