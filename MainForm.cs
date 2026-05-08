using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyboardTaskRunner;

public class MainForm : Form
{
    private ComboBox cmbWindow = null!, cmbMode = null!;
    private Button btnRefresh = null!, btnSave = null!, btnLoad = null!, btnDiag = null!;
    private Button btnRec = null!, btnPlay = null!;
    private NumericUpDown nudSpeed = null!, nudRepeat = null!, nudJitter = null!;
    private CheckBox chkInfinite = null!, chkJitter = null!, chkMouse = null!;

    private readonly InputRecorder recorder = new();
    private readonly InputPlayer player = new();
    private readonly InjectedFlagCleaner flagCleaner = new();
    public static bool DiagnosticOpen;
    private List<MacroEvent> events = new();
    private List<WindowInfo> windowList = new();
    private bool hasMacro;

    private IntPtr _hotkeyHook;
    private NativeMethods.HookProc? _hotkeyProc;

    public MainForm()
    {
        InitUI();
        player.PlaybackFinished += () =>
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(new Action(OnPlaybackFinished));
        };
        player.PlaybackError += (msg) =>
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(new Action(() =>
                    MessageBox.Show(this, $"播放失敗：{msg}", "KTR", MessageBoxButtons.OK, MessageBoxIcon.Error)));
        };
    }

    private void InitUI()
    {
        Text = "KTR";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        Font = new Font("Microsoft JhengHei UI", 9f);

        int y = 5, lx = 5;

        // Row 1: window + refresh + save + load
        cmbWindow = new ComboBox
        {
            Location = new Point(lx, y), Size = new Size(280, 22),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        Controls.Add(cmbWindow);
        btnRefresh = MakeBtn("↻", lx + 284, y, 26, 23);
        btnRefresh.Click += (_, _) => RefreshWindowList();
        btnSave = MakeBtn("💾", lx + 314, y, 26, 23);
        btnSave.Click += (_, _) => DoSave();
        btnLoad = MakeBtn("📂", lx + 344, y, 26, 23);
        btnLoad.Click += (_, _) => DoLoad();
        cmbMode = new ComboBox
        {
            Location = new Point(lx + 374, y), Size = new Size(72, 22),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        cmbMode.Items.AddRange(new object[] { "一般", "HID" });
        cmbMode.SelectedIndex = 0;
        cmbMode.SelectedIndexChanged += (_, _) => OnModeChanged();
        Controls.Add(cmbMode);
        btnDiag = MakeBtn("🔍", lx + 450, y, 26, 23);
        btnDiag.Click += (_, _) => new DiagnosticForm().Show(this);
        y += 27;

        // Row 2: rec + play + speed + repeat + jitter
        btnRec = new Button
        {
            Text = "⏺ F8", Location = new Point(lx, y), Size = new Size(58, 26),
            FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.Gray,
        };
        btnRec.FlatAppearance.BorderSize = 0;
        btnRec.Click += (_, _) => ToggleRecord();
        Controls.Add(btnRec);

        btnPlay = new Button
        {
            Text = "▶ F12", Location = new Point(lx + 62, y), Size = new Size(60, 26),
            FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.Gray,
        };
        btnPlay.FlatAppearance.BorderSize = 0;
        btnPlay.Click += (_, _) => TogglePlay();
        Controls.Add(btnPlay);

        AddLabel("速度", lx + 130, y + 5);
        nudSpeed = new NumericUpDown
        {
            Location = new Point(lx + 160, y + 1), Size = new Size(45, 20),
            Minimum = 0.1m, Maximum = 99m, Value = 1.0m, DecimalPlaces = 1, Increment = 0.1m,
            TextAlign = HorizontalAlignment.Center
        };
        Controls.Add(nudSpeed);

        AddLabel("重複", lx + 210, y + 5);
        nudRepeat = new NumericUpDown
        {
            Location = new Point(lx + 240, y + 1), Size = new Size(50, 20),
            Minimum = 1, Maximum = 99999, Value = 100,
            TextAlign = HorizontalAlignment.Center
        };
        Controls.Add(nudRepeat);
        chkInfinite = new CheckBox { Text = "∞", Location = new Point(lx + 293, y + 3), Size = new Size(32, 20), Checked = true };
        Controls.Add(chkInfinite);

        chkJitter = new CheckBox { Text = "抖", Location = new Point(lx + 322, y + 3), Size = new Size(34, 20), Checked = true };
        Controls.Add(chkJitter);
        nudJitter = new NumericUpDown
        {
            Location = new Point(lx + 352, y + 1), Size = new Size(38, 20),
            Minimum = 1, Maximum = 50, Value = 8,
            TextAlign = HorizontalAlignment.Center
        };
        Controls.Add(nudJitter);

        chkMouse = new CheckBox { Text = "鼠", Location = new Point(lx + 393, y + 3), Size = new Size(42, 20) };
        Controls.Add(chkMouse);

        ClientSize = new Size(482, y + 28);
    }

    private Button MakeBtn(string text, int x, int y, int w, int h)
    {
        var btn = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.System };
        Controls.Add(btn);
        return btn;
    }

    private Label AddLabel(string text, int x, int y)
    {
        var lbl = new Label { Text = text, Location = new Point(x, y), AutoSize = true };
        Controls.Add(lbl);
        return lbl;
    }

    private void UpdateButtonStates()
    {
        if (recorder.IsRecording)
        {
            btnRec.BackColor = Color.Red;
            btnRec.Text = "■ F8";
            btnPlay.BackColor = Color.Gray;
            btnPlay.Enabled = false;
        }
        else if (player.IsPlaying)
        {
            btnRec.BackColor = Color.Gray;
            btnRec.Enabled = false;
            btnPlay.BackColor = Color.Orange;
            btnPlay.Text = "■ F12";
        }
        else
        {
            btnRec.Enabled = true;
            btnPlay.Enabled = true;
            btnRec.BackColor = Color.Gray;
            btnRec.Text = "⏺ F8";
            btnPlay.BackColor = hasMacro ? Color.Green : Color.Gray;
            btnPlay.Text = "▶ F12";
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        InstallHotkeyHook();
        RefreshWindowList();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        RemoveHotkeyHook();
        flagCleaner.Uninstall();
        recorder.Stop();
        player.Stop();
        base.OnFormClosed(e);
    }

    private void InstallHotkeyHook()
    {
        _hotkeyProc = HotkeyHookCallback;
        _hotkeyHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _hotkeyProc, IntPtr.Zero, 0);
    }

    private void RemoveHotkeyHook()
    {
        if (_hotkeyHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hotkeyHook);
            _hotkeyHook = IntPtr.Zero;
        }
    }

    private IntPtr HotkeyHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == NativeMethods.HC_ACTION)
        {
            uint msg = (uint)wParam.ToInt64();
            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if (kb.vkCode == 0x77) // F8
                    BeginInvoke(new Action(ToggleRecord));
                else if (kb.vkCode == 0x7B) // F12
                    BeginInvoke(new Action(TogglePlay));
            }
        }
        return NativeMethods.CallNextHookEx(_hotkeyHook, nCode, wParam, lParam);
    }

    private void RefreshWindowList()
    {
        cmbWindow.Items.Clear();
        windowList = WindowHelper.EnumVisibleWindows(Handle);
        foreach (var w in windowList)
            cmbWindow.Items.Add(WindowHelper.FormatTitle(w));
    }

    private IntPtr SelectedWindow()
    {
        int i = cmbWindow.SelectedIndex;
        return i >= 0 && i < windowList.Count ? windowList[i].Hwnd : IntPtr.Zero;
    }

    private void ToggleRecord()
    {
        if (player.IsPlaying) return;

        if (recorder.IsRecording)
        {
            recorder.Stop();
            events = recorder.GetEvents();
            hasMacro = events.Count > 0;
        }
        else
        {
            events.Clear();
            hasMacro = false;
            recorder.Start(SelectedWindow(), chkMouse.Checked);
        }
        UpdateButtonStates();
    }

    private void TogglePlay()
    {
        if (recorder.IsRecording) return;

        if (player.IsPlaying)
        {
            player.Stop();
            flagCleaner.Uninstall();
            return;
        }

        if (events.Count == 0) return;

        float speed = (float)nudSpeed.Value;
        int repeat = (int)nudRepeat.Value;
        bool infinite = chkInfinite.Checked;
        bool jitter = chkJitter.Checked;
        int jPct = (int)nudJitter.Value;

        var mode = (InputMode)cmbMode.SelectedIndex;
        if (mode == InputMode.Normal && !DiagnosticOpen)
            flagCleaner.Install();
        player.Start(events, SelectedWindow(), speed, repeat, infinite, jitter, jPct, mode);
        UpdateButtonStates();
    }

    private void OnPlaybackFinished()
    {
        flagCleaner.Uninstall();
        UpdateButtonStates();
    }

    private void OnModeChanged() { }

    private void DoSave()
    {
        if (events.Count == 0) return;
        using var dlg = new SaveFileDialog { Filter = "KTR 巨集|*.ktr", DefaultExt = "ktr" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            try { MacroFile.Save(dlg.FileName, events); }
            catch { MessageBox.Show(this, "儲存失敗", "KTR", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }

    private void DoLoad()
    {
        using var dlg = new OpenFileDialog { Filter = "KTR 巨集|*.ktr" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                events = MacroFile.Load(dlg.FileName);
                hasMacro = events.Count > 0;
                UpdateButtonStates();
            }
            catch { MessageBox.Show(this, "載入失敗", "KTR", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
