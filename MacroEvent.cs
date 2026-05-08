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
        using var w = new BinaryWriter(fs);
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

    public static List<MacroEvent> Load(string path)
    {
        using var fs = new FileStream(path, FileMode.Open);
        using var r = new BinaryReader(fs);
        uint magic = r.ReadUInt32();
        ushort version = r.ReadUInt16();
        if (magic != Magic || version != Version)
            throw new InvalidDataException("Invalid macro file format.");
        int count = r.ReadInt32();
        if (count < 0 || count > 10_000_000)
            throw new InvalidDataException("Event count out of range.");
        var events = new List<MacroEvent>(count);
        for (int i = 0; i < count; i++)
        {
            events.Add(new MacroEvent
            {
                Type = (EventType)r.ReadByte(),
                ScanCode = r.ReadUInt16(),
                VkCode = r.ReadUInt16(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Button = (MouseButton)r.ReadByte(),
                WheelDelta = r.ReadInt16(),
                DelayMs = r.ReadUInt32(),
            });
        }
        return events;
    }
}
