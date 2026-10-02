using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MeetApp;

enum MeetAction { ToggleMic, ToggleCamera, RaiseHand, LeaveCall, ShowHide }

/// <summary>A key + modifier combination, stored as text like "Ctrl+Alt+Shift+M".</summary>
readonly struct Hotkey
{
    public readonly Keys Key;
    public readonly bool Ctrl, Alt, Shift, Win;

    public Hotkey(Keys key, bool ctrl, bool alt, bool shift, bool win)
    {
        Key = key; Ctrl = ctrl; Alt = alt; Shift = shift; Win = win;
    }

    public bool IsEmpty => Key == Keys.None;
    public bool HasModifier => Ctrl || Alt || Shift || Win;

    // Keys that are safe to use alone as a system-wide hotkey.
    public bool IsStandaloneKey => Key is >= Keys.F1 and <= Keys.F24 or Keys.Pause or Keys.Scroll
        or Keys.MediaPlayPause or Keys.MediaNextTrack or Keys.MediaPreviousTrack or Keys.MediaStop
        or Keys.VolumeMute or Keys.LaunchApplication1 or Keys.LaunchApplication2;

    public override string ToString()
    {
        if (IsEmpty) return "";
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    static string KeyName(Keys k) => k switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (k - Keys.D0))).ToString(),
        Keys.Oemcomma => ",", Keys.OemPeriod => ".", Keys.OemMinus => "-", Keys.Oemplus => "=",
        _ => k.ToString(),
    };

    public static Hotkey Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;
        bool ctrl = false, alt = false, shift = false, win = false;
        Keys key = Keys.None;
        foreach (var raw in text!.Split('+'))
        {
            var p = raw.Trim();
            switch (p.ToLowerInvariant())
            {
                case "ctrl": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                case "win": win = true; break;
                case ",": key = Keys.Oemcomma; break;
                case ".": key = Keys.OemPeriod; break;
                case "-": key = Keys.OemMinus; break;
                case "=": key = Keys.Oemplus; break;
                default:
                    if (p.Length == 1 && char.IsDigit(p[0])) key = Keys.D0 + (p[0] - '0');
                    else if (Enum.TryParse<Keys>(p, true, out var k)) key = k;
                    break;
            }
        }
        return new Hotkey(key, ctrl, alt, shift, win);
    }

    public override bool Equals(object? o) => o is Hotkey h && h.ToString() == ToString();
    public override int GetHashCode() => ToString().GetHashCode();
}

/// <summary>User-editable hotkey map, persisted to hotkeys.txt ("Action=Ctrl+Alt+Shift+M" per line).</summary>
sealed class HotkeySettings
{
    public static readonly (MeetAction Action, string Label)[] Actions =
    {
        (MeetAction.ToggleMic, "Mute / unmute microphone"),
        (MeetAction.ToggleCamera, "Turn camera on / off"),
        (MeetAction.RaiseHand, "Raise / lower hand"),
        (MeetAction.LeaveCall, "Leave call"),
        (MeetAction.ShowHide, "Show / hide Meet window"),
    };

    // Ctrl+Alt+Shift combos almost never clash with other apps (and avoid AltGr characters).
    public static Dictionary<MeetAction, Hotkey> Defaults() => new()
    {
        [MeetAction.ToggleMic] = Hotkey.Parse("Ctrl+Alt+Shift+M"),
        [MeetAction.ToggleCamera] = Hotkey.Parse("Ctrl+Alt+Shift+V"),
        [MeetAction.RaiseHand] = Hotkey.Parse("Ctrl+Alt+Shift+H"),
        [MeetAction.LeaveCall] = Hotkey.Parse("Ctrl+Alt+Shift+L"),
        [MeetAction.ShowHide] = Hotkey.Parse("Ctrl+Alt+Shift+G"),
    };

    readonly string file;
    public Dictionary<MeetAction, Hotkey> Map { get; private set; } = Defaults();

    public HotkeySettings(string file)
    {
        this.file = file;
        try
        {
            if (!File.Exists(file)) return;
            foreach (var line in File.ReadAllLines(file))
            {
                var i = line.IndexOf('=');
                if (i > 0 && Enum.TryParse<MeetAction>(line.Substring(0, i), out var a))
                    Map[a] = Hotkey.Parse(line.Substring(i + 1));
            }
        }
        catch { }
    }

    public void Save(Dictionary<MeetAction, Hotkey> map)
    {
        Map = map;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllLines(file, map.Select(kv => $"{kv.Key}={kv.Value}"));
        }
        catch { }
    }
}

/// <summary>Registers system-wide hotkeys on a window handle (they fire even when the app isn't focused).</summary>
sealed class GlobalHotkeys
{
    public const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    readonly IntPtr hwnd;
    readonly List<int> registered = new();

    public GlobalHotkeys(IntPtr hwnd) => this.hwnd = hwnd;

    /// <summary>Registers all hotkeys; returns the actions whose combo is already taken by another app.</summary>
    public List<MeetAction> Register(Dictionary<MeetAction, Hotkey> map)
    {
        UnregisterAll();
        var failed = new List<MeetAction>();
        foreach (var kv in map)
        {
            var h = kv.Value;
            if (h.IsEmpty) continue;
            uint mods = MOD_NOREPEAT | (h.Ctrl ? MOD_CONTROL : 0) | (h.Alt ? MOD_ALT : 0) | (h.Shift ? MOD_SHIFT : 0) | (h.Win ? MOD_WIN : 0);
            int id = (int)kv.Key + 1;
            if (RegisterHotKey(hwnd, id, mods, (uint)h.Key)) registered.Add(id);
            else failed.Add(kv.Key);
        }
        return failed;
    }

    public void UnregisterAll()
    {
        foreach (var id in registered) UnregisterHotKey(hwnd, id);
        registered.Clear();
    }

    public static MeetAction ActionFromId(IntPtr wParam) => (MeetAction)((int)wParam - 1);
}
