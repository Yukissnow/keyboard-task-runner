using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace KeyboardTaskRunner;

public class DiagnosticForm : Form
{
    private ListBox listBox = null!;
    private Button btnClear = null!, btnCopy = null!, btnPause = null!;
    private IntPtr _kbHook;
    private IntPtr _msHook;
    private NativeMethods.HookProc? _kbProc;
    private NativeMethods.HookProc? _msProc;
    private bool _paused;

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT pt);

    public DiagnosticForm()
    {
        Text = "KTR 診斷 — 按鍵/滑鼠 INJECTED 旗標";
        Size = new Size(560, 420);
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        Font = new Font("Microsoft JhengHei UI", 9f);

        var label = new Label
        {
            Text = "綠色=真實硬體輸入  紅色=INJECTED（軟體模擬）  操作此視窗時不會記錄",
            Dock = DockStyle.Top,
            Height = 25,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.LightYellow,
        };
        Controls.Add(label);

        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 30 };
        Controls.Add(bottomPanel);

        btnClear = new Button { Text = "清除", Location = new Point(5, 3), Size = new Size(70, 24) };
        btnClear.Click += (_, _) => listBox.Items.Clear();
        bottomPanel.Controls.Add(btnClear);

        btnCopy = new Button { Text = "複製全部", Location = new Point(80, 3), Size = new Size(80, 24) };
        btnCopy.Click += (_, _) => CopyAll();
        bottomPanel.Controls.Add(btnCopy);

        btnPause = new Button { Text = "暫停", Location = new Point(165, 3), Size = new Size(70, 24) };
        btnPause.Click += (_, _) =>
        {
            _paused = !_paused;
            btnPause.Text = _paused ? "繼續" : "暫停";
        };
        bottomPanel.Controls.Add(btnPause);

        listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9f),
            SelectionMode = SelectionMode.MultiExtended,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 16,
        };
        listBox.DrawItem += DrawItem;
        Controls.Add(listBox);
    }

    private void CopyAll()
    {
        var sb = new StringBuilder();
        foreach (var item in listBox.Items)
            sb.AppendLine(item?.ToString());
        if (sb.Length > 0)
            Clipboard.SetText(sb.ToString());
    }

    private void DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        e.DrawBackground();
        var text = listBox.Items[e.Index]?.ToString() ?? "";
        var color = text.Contains("[INJECTED]") ? Color.Red : Color.Green;
        using var brush = new SolidBrush(color);
        e.Graphics.DrawString(text, listBox.Font, brush, e.Bounds);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        MainForm.DiagnosticOpen = true;
        _kbProc = KbProc;
        _msProc = MsProc;
        _kbHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _kbProc, IntPtr.Zero, 0);
        _msHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _msProc, IntPtr.Zero, 0);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        MainForm.DiagnosticOpen = false;
        if (_kbHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_kbHook);
        if (_msHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_msHook);
        base.OnFormClosed(e);
    }

    private bool ShouldSkip()
    {
        if (_paused) return true;
        var fg = NativeMethods.GetForegroundWindow();
        return fg == Handle;
    }

    private IntPtr KbProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == NativeMethods.HC_ACTION && !ShouldSkip())
        {
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint msg = (uint)wParam.ToInt64();
            bool isDown = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
            bool injected = (kb.flags & NativeMethods.LLKHF_INJECTED) != 0;
            string label = injected ? "[INJECTED]" : "[REAL    ]";
            string action = isDown ? "↓" : "↑";
            string line = $"{DateTime.Now:HH:mm:ss.fff} {label} KEY{action} VK=0x{kb.vkCode:X2} SC=0x{kb.scanCode:X2}";
            AddLine(line);
        }
        return NativeMethods.CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private IntPtr MsProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == NativeMethods.HC_ACTION && !ShouldSkip())
        {
            uint msg = (uint)wParam.ToInt64();
            // skip mouse move and wheel spam
            if (msg == NativeMethods.WM_MOUSEMOVE || msg == NativeMethods.WM_MOUSEWHEEL)
                return NativeMethods.CallNextHookEx(_msHook, nCode, wParam, lParam);

            var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            bool injected = (ms.flags & NativeMethods.LLMHF_INJECTED) != 0;
            string label = injected ? "[INJECTED]" : "[REAL    ]";
            string evt = msg switch
            {
                NativeMethods.WM_LBUTTONDOWN => "LBTN↓",
                NativeMethods.WM_LBUTTONUP => "LBTN↑",
                NativeMethods.WM_RBUTTONDOWN => "RBTN↓",
                NativeMethods.WM_RBUTTONUP => "RBTN↑",
                NativeMethods.WM_MBUTTONDOWN => "MBTN↓",
                NativeMethods.WM_MBUTTONUP => "MBTN↑",
                _ => $"0x{msg:X4}",
            };
            string line = $"{DateTime.Now:HH:mm:ss.fff} {label} {evt} @({ms.pt.X},{ms.pt.Y})";
            AddLine(line);
        }
        return NativeMethods.CallNextHookEx(_msHook, nCode, wParam, lParam);
    }

    private void AddLine(string line)
    {
        if (!IsHandleCreated || IsDisposed) return;
        BeginInvoke(new Action(() =>
        {
            listBox.Items.Add(line);
            if (listBox.Items.Count > 500) listBox.Items.RemoveAt(0);
            listBox.TopIndex = listBox.Items.Count - 1;
        }));
    }
}
