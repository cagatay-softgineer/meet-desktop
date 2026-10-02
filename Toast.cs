using System;
using System.Drawing;
using System.Windows.Forms;

namespace MeetApp;

/// <summary>Small always-on-top notice ("Microphone off") that never steals focus.</summary>
sealed class Toast : Form
{
    readonly Label label = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.White, Font = new Font("Segoe UI", 12f, FontStyle.Bold),
    };
    readonly Panel accent = new() { Dock = DockStyle.Left, Width = 6 };
    readonly Timer timer = new() { Interval = 1600 };

    public Toast()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(32, 33, 36);
        Size = new Size(280, 52);
        Controls.Add(label);
        Controls.Add(accent);
        timer.Tick += (_, _) => { timer.Stop(); Hide(); };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8, CS_DROPSHADOW = 0x20000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    /// <param name="good">true = green accent (on/unmuted), false = red (off/muted), null = neutral.</param>
    public void ShowMessage(string text, bool? good)
    {
        label.Text = text;
        accent.BackColor = good switch { true => Color.FromArgb(52, 168, 83), false => Color.FromArgb(234, 67, 53), _ => Color.FromArgb(138, 180, 248) };
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Bottom - Height - 40);
        if (!Visible) Show();
        timer.Stop();
        timer.Start();
    }
}
