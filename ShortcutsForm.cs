using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MeetApp;

/// <summary>Lets the user record a new key combination for each action.</summary>
sealed class ShortcutsForm : Form
{
    [DllImport("user32.dll")] static extern short GetKeyState(int vk);
    static bool WinDown => (GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0;

    readonly Dictionary<MeetAction, Hotkey> map;
    readonly Dictionary<MeetAction, TextBox> boxes = new();
    readonly Label status = new() { AutoSize = true, ForeColor = Color.Firebrick, Padding = new Padding(0, 6, 0, 0) };

    public Dictionary<MeetAction, Hotkey> Result => map;

    public ShortcutsForm(Dictionary<MeetAction, Hotkey> current, IEnumerable<MeetAction> unavailable)
    {
        map = new Dictionary<MeetAction, Hotkey>(current);
        Text = "Keyboard shortcuts";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 9.5f);
        Padding = new Padding(12);

        var table = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
        table.Controls.Add(new Label
        {
            Text = "These work everywhere in Windows, even when Meet is in the background.\n" +
                   "Click a box and press the new combination. Backspace clears it.",
            AutoSize = true, Padding = new Padding(0, 0, 0, 10),
        }, 0, 0);
        table.SetColumnSpan(table.GetControlFromPosition(0, 0), 3);

        int row = 1;
        foreach (var (action, label) in HotkeySettings.Actions)
        {
            var box = new TextBox { ReadOnly = true, Width = 190, BackColor = SystemColors.Window, Text = map[action].ToString(), ShortcutsEnabled = false };
            box.KeyDown += (_, e) => RecordKey(action, box, e);
            box.Enter += (_, _) => BeginInvoke(new Action(() => box.SelectionLength = 0));
            var clear = new Button { Text = "Clear", AutoSize = true };
            clear.Click += (_, _) => { map[action] = default; box.Text = ""; Validate(); };
            boxes[action] = box;
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 0, 12, 0) }, 0, row);
            table.Controls.Add(box, 1, row);
            table.Controls.Add(clear, 2, row);
            row++;
        }
        table.Controls.Add(status, 0, row);
        table.SetColumnSpan(status, 3);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var reset = new Button { Text = "Reset to defaults", AutoSize = true };
        reset.Click += (_, _) =>
        {
            foreach (var kv in HotkeySettings.Defaults()) { map[kv.Key] = kv.Value; boxes[kv.Key].Text = kv.Value.ToString(); }
            Validate();
        };
        buttons.Controls.AddRange(new Control[] { cancel, ok, reset });
        table.Controls.Add(buttons, 0, row + 1);
        table.SetColumnSpan(buttons, 3);
        Controls.Add(table);
        // Plain Enter/Escape aren't valid hotkeys, so they can save/cancel.
        AcceptButton = ok;
        CancelButton = cancel;

        foreach (var a in unavailable) boxes[a].BackColor = Color.MistyRose;
        if (unavailable.Any()) status.Text = "Highlighted shortcuts are already used by another program. Pick different ones.";
    }

    void RecordKey(MeetAction action, TextBox box, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        var key = e.KeyCode;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return; // wait for the real key
        if ((key is Keys.Back or Keys.Delete) && e.Modifiers == Keys.None)
        {
            map[action] = default;
            box.Text = "";
            Validate();
            return;
        }
        var h = new Hotkey(key, e.Control, e.Alt, e.Shift, WinDown);
        if (!h.HasModifier && !h.IsStandaloneKey)
        {
            status.Text = "Add Ctrl, Alt, Shift or Win (plain letters would block typing everywhere).";
            return;
        }
        map[action] = h;
        box.Text = h.ToString();
        box.BackColor = SystemColors.Window;
        Validate();
    }

    new void Validate()
    {
        var dupes = map.Where(kv => !kv.Value.IsEmpty).GroupBy(kv => kv.Value).Where(g => g.Count() > 1).SelectMany(g => g).Select(kv => kv.Key).ToHashSet();
        foreach (var kv in boxes) kv.Value.ForeColor = dupes.Contains(kv.Key) ? Color.Firebrick : SystemColors.WindowText;
        status.Text = dupes.Count > 0 ? "The same shortcut is used twice." : "";
        foreach (var b in Controls.OfType<TableLayoutPanel>().SelectMany(t => t.Controls.OfType<FlowLayoutPanel>()).SelectMany(f => f.Controls.OfType<Button>()))
            if (b.DialogResult == DialogResult.OK) b.Enabled = dupes.Count == 0;
    }
}
