using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MeetApp;

/// <summary>Main window: a single WebView2 locked to Google Meet.</summary>
public sealed class MainForm : Form
{
    const string Home = "https://meet.google.com/";
    const string SignInUrl = "https://accounts.google.com/ServiceLogin?continue=https%3A%2F%2Fmeet.google.com%2F";
    static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeetApp");
    static readonly string SettingsFile = Path.Combine(DataDir, "window.txt");

    readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(32, 33, 36) };
    readonly string startUrl;

    readonly bool checkUpdatesNow;

    public MainForm(string? arg, bool checkUpdatesNow = false)
    {
        startUrl = ToMeetUrl(arg);
        this.checkUpdatesNow = checkUpdatesNow;
        Text = "Google Meet";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        BackColor = Color.FromArgb(32, 33, 36);
        MinimumSize = new Size(480, 360);
        StartPosition = FormStartPosition.Manual;
        LoadBounds();
        Controls.Add(web);
        Load += async (_, _) => await InitAsync();
        FormClosing += (_, _) => SaveBounds();
    }

    // Accepts a full Meet URL, a meeting code ("abc-defg-hij"), or nothing.
    static string ToMeetUrl(string? arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return Home;
        arg = arg.Trim();
        if (Uri.TryCreate(arg, UriKind.Absolute, out var u) && u.Host == "meet.google.com") return u.ToString();
        return Regex.IsMatch(arg, "^[a-z]{3}-[a-z]{4}-[a-z]{3}$") ? Home + arg : Home;
    }

    async Task InitAsync()
    {
        CoreWebView2Environment env;
        try
        {
            var options = new CoreWebView2EnvironmentOptions
            {
                // Trim background work Meet doesn't need.
                AdditionalBrowserArguments =
                    "--disable-features=msSmartScreenProtection,msEdgeShoppingAssistant " +
                    "--disable-background-networking --disable-component-update --disable-sync",
            };
            env = await CoreWebView2Environment.CreateAsync(null, DataDir, options);
            await web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show("Microsoft Edge WebView2 Runtime is required.\nInstall it from https://go.microsoft.com/fwlink/p/?LinkId=2124703",
                "Google Meet", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        Configure(web.CoreWebView2, env, isPopup: false, owner: this);
        web.CoreWebView2.DocumentTitleChanged += (_, _) =>
            Text = string.IsNullOrWhiteSpace(web.CoreWebView2.DocumentTitle) ? "Google Meet" : web.CoreWebView2.DocumentTitle;
        // Ctrl+, inside the page opens the shortcut editor.
        await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
            "window.addEventListener('keydown', e => { if (e.ctrlKey && !e.altKey && e.key === ',') " +
            "{ e.preventDefault(); window.chrome.webview.postMessage('open-shortcuts'); } }, true);");
        web.CoreWebView2.WebMessageReceived += (_, e) =>
        {
            if (e.TryGetWebMessageAsString() == "open-shortcuts") BeginInvoke(new Action(OpenShortcuts));
        };
        web.CoreWebView2.Navigate(startUrl);
    }

    // ---------- Global hotkeys ----------

    readonly HotkeySettings hotkeys = new(Path.Combine(DataDir, "hotkeys.txt"));
    readonly Toast toast = new();
    GlobalHotkeys? globalHotkeys;
    List<MeetAction> unavailableHotkeys = new();
    FormWindowState restoreState = FormWindowState.Normal;
    NotifyIcon? tray;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        globalHotkeys = new GlobalHotkeys(Handle);
        unavailableHotkeys = globalHotkeys.Register(hotkeys.Map);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show Google Meet", null, (_, _) => ShowWindow());
        menu.Items.Add("Keyboard shortcuts...", null, (_, _) => OpenShortcuts());
        menu.Items.Add("Check for updates...", null, (_, _) => _ = CheckForUpdatesAsync(manual: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add($"Version {Updater.Current}", null).Enabled = false;
        menu.Items.Add("Exit", null, (_, _) => Close());
        tray = new NotifyIcon { Icon = Icon, Text = "Google Meet", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowWindow();
        tray.BalloonTipClicked += (_, _) => { if (pendingUpdate != null) ShowUpdateDialog(pendingUpdate); };
        if (unavailableHotkeys.Count > 0)
            tray.ShowBalloonTip(5000, "Google Meet", "Some keyboard shortcuts are used by another program. Right-click this icon > Keyboard shortcuts to change them.", ToolTipIcon.Warning);

        if (checkUpdatesNow) _ = CheckForUpdatesAsync(manual: true);
        else if (Updater.IsCheckDue(DataDir)) _ = DelayedUpdateCheckAsync();
    }

    // ---------- Updates ----------

    Updater.UpdateInfo? pendingUpdate;

    async Task DelayedUpdateCheckAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(15)); // let Meet load first
        await CheckForUpdatesAsync(manual: false);
    }

    async Task CheckForUpdatesAsync(bool manual)
    {
        Updater.UpdateInfo? update;
        try { update = await Updater.CheckAsync(); }
        catch (Exception ex)
        {
            Log($"update check failed: {ex.Message}");
            if (manual) MessageBox.Show(this, "Couldn't check for updates:\n" + ex.Message, "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Updater.SaveState(DataDir);
        if (update == null)
        {
            if (manual) MessageBox.Show(this, $"You're up to date (version {Updater.Current}).", "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (manual) { ShowUpdateDialog(update); return; }
        if (Updater.SkippedVersion(DataDir) == update.Version.ToString()) return;
        // Automatic check: a quiet tray notice, never a popup in the middle of a call.
        pendingUpdate = update;
        tray?.ShowBalloonTip(10000, "Update available", $"Google Meet Desktop {update.Version} is ready. Click here to install.", ToolTipIcon.Info);
    }

    void ShowUpdateDialog(Updater.UpdateInfo update)
    {
        if (OwnedForms.OfType<UpdateDialog>().Any()) return;
        using var dialog = new UpdateDialog(update);
        dialog.ShowDialog(this);
        if (dialog.Skipped) Updater.SaveState(DataDir, skipped: update.Version.ToString());
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState != FormWindowState.Minimized) restoreState = WindowState;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        globalHotkeys?.UnregisterAll();
        if (tray != null) tray.Visible = false;
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == GlobalHotkeys.WM_HOTKEY)
        {
            Log($"hotkey {GlobalHotkeys.ActionFromId(m.WParam)}");
            _ = RunActionAsync(GlobalHotkeys.ActionFromId(m.WParam));
        }
        base.WndProc(ref m);
    }

    void OpenShortcuts()
    {
        if (OwnedForms.OfType<ShortcutsForm>().Any()) return;
        globalHotkeys?.UnregisterAll(); // so the combos can be recorded instead of firing
        while (true)
        {
            using var dialog = new ShortcutsForm(hotkeys.Map, unavailableHotkeys);
            var saved = dialog.ShowDialog(this) == DialogResult.OK;
            if (saved) hotkeys.Save(dialog.Result);
            unavailableHotkeys = globalHotkeys?.Register(hotkeys.Map) ?? new();
            if (!saved || unavailableHotkeys.Count == 0) return;
            MessageBox.Show(this, "Some shortcuts are already used by another program (highlighted). Please choose different ones.",
                "Keyboard shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            globalHotkeys?.UnregisterAll();
        }
    }

    void ShowWindow()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = restoreState;
        Show();
        Activate();
        web.Focus();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        web.Focus(); // keep keyboard input going to Meet, not the empty form
    }

    void ToggleWindow()
    {
        if (ActiveForm == this && WindowState != FormWindowState.Minimized) WindowState = FormWindowState.Minimized;
        else ShowWindow();
    }

    // Toast only when Meet isn't in front; otherwise Meet's own UI already shows the change.
    void Notify(string text, bool? good)
    {
        if (ActiveForm == this && WindowState != FormWindowState.Minimized) return;
        toast.ShowMessage(text, good);
    }

    const string MutedStatesJs =
        "Array.from(document.querySelectorAll('[data-is-muted]')).map(e => e.getAttribute('data-is-muted')).join(',')";
    const string FindLeaveJs =
        "(document.querySelector('button[jsname=\"CQylAd\"]') || Array.from(document.querySelectorAll('button[aria-label]'))" +
        ".find(b => /leave call|görüşmeden (ayrıl|çık)/i.test(b.getAttribute('aria-label'))))";
    const string JustLeaveJs =
        "(() => { const all = /everyone|herkes/i, txt = x => x.innerText + ' ' + (x.getAttribute('aria-label') || '');" +
        " const d = Array.from(document.querySelectorAll('[role=dialog]')).find(d => Array.from(d.querySelectorAll('button')).some(x => all.test(txt(x))));" +
        " const b = d && Array.from(d.querySelectorAll('button')).find(x => !all.test(txt(x)));" +
        " if (b) b.click(); return !!b; })()";

    async Task RunActionAsync(MeetAction action)
    {
        try
        {
            if (action == MeetAction.ShowHide) { ToggleWindow(); return; }
            var core = web.CoreWebView2;
            if (core == null) return;
            switch (action)
            {
                case MeetAction.ToggleMic:
                    await ToggleDeviceAsync(core, "d", "KeyD", 0x44, "Microphone");
                    break;
                case MeetAction.ToggleCamera:
                    await ToggleDeviceAsync(core, "e", "KeyE", 0x45, "Camera");
                    break;
                case MeetAction.RaiseHand:
                    if (await core.ExecuteScriptAsync($"!!{FindLeaveJs}") != "true") { Notify("Not in a meeting", null); return; }
                    await SendKeyAsync(core, "h", "KeyH", 0x48, ctrl: true, alt: true);
                    Notify("Hand raised / lowered", null);
                    break;
                case MeetAction.LeaveCall:
                    var left = await core.ExecuteScriptAsync($"(b => {{ if (b) b.click(); return !!b; }})({FindLeaveJs})") == "true";
                    if (left)
                    {
                        // Hosts get "Just leave / End for everyone": pick just leave, never end it for all.
                        for (int i = 0; i < 10; i++)
                        {
                            await Task.Delay(100);
                            if (await core.ExecuteScriptAsync(JustLeaveJs) == "true") break;
                        }
                    }
                    Notify(left ? "Left the call" : "Not in a meeting", left ? false : null);
                    break;
            }
        }
        catch (Exception ex) { Log($"{action} failed: {ex.Message}"); }
    }

    // Sends Meet's own shortcut (Ctrl+D / Ctrl+E), then reads the button state to report on/off.
    async Task ToggleDeviceAsync(CoreWebView2 core, string key, string code, int vk, string name)
    {
        var before = await MutedStatesAsync(core);
        Log($"{name} before: [{before}]");
        if (before.Length == 0) { Notify("No meeting open", null); return; }
        await SendKeyAsync(core, key, code, vk, ctrl: true, alt: false);
        for (int i = 0; i < 30; i++) // camera can take a couple of seconds to switch
        {
            await Task.Delay(100);
            var after = await MutedStatesAsync(core);
            if (after == before) continue;
            Log($"{name} after: [{after}]");
            var b = before.Split(','); var a = after.Split(',');
            for (int j = 0; j < Math.Min(a.Length, b.Length); j++)
                if (a[j] != b[j]) { var muted = a[j] == "true"; Notify($"{name} {(muted ? "off" : "on")}", !muted); return; }
            break;
        }
        Notify($"{name} toggled", null);
    }

    static async Task<string> MutedStatesAsync(CoreWebView2 core) =>
        (await core.ExecuteScriptAsync(MutedStatesJs)).Trim('"');

    // Dispatches a trusted key press straight into the page (works while the window is unfocused).
    static async Task SendKeyAsync(CoreWebView2 core, string key, string code, int vk, bool ctrl, bool alt)
    {
        int mods = (alt ? 1 : 0) | (ctrl ? 2 : 0);
        foreach (var type in new[] { "rawKeyDown", "keyUp" })
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
                $"{{\"type\":\"{type}\",\"modifiers\":{mods},\"key\":\"{key}\",\"code\":\"{code}\"," +
                $"\"windowsVirtualKeyCode\":{vk},\"nativeVirtualKeyCode\":{vk}}}");
    }

    static void Log(string line)
    {
        if (Environment.GetEnvironmentVariable("MEETAPP_DEBUG") != "1") return;
        try { File.AppendAllText(Path.Combine(DataDir, "debug.log"), $"{DateTime.Now:HH:mm:ss} {line}\n"); } catch { }
    }

    /// <summary>Applies the shared lockdown/permission rules to a webview (main window or popup).</summary>
    static void Configure(CoreWebView2 core, CoreWebView2Environment env, bool isPopup, Form owner)
    {
        var s = core.Settings;
        s.AreDevToolsEnabled = false;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = true;
        s.AreBrowserAcceleratorKeysEnabled = true;

        // Keep Meet + Google sign-in inside the app; everything else goes to the default browser.
        core.NavigationStarting += (_, e) =>
        {
            if (IsInApp(e.Uri)) return;
            e.Cancel = true;
            // Signed-out users get bounced from Meet to the Workspace marketing page: sign in here instead.
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Host is "workspace.google.com" or "apps.google.com" && u.AbsolutePath.Contains("/meet"))
            {
                core.Navigate(SignInUrl);
                return;
            }
            OpenExternal(e.Uri);
            if (isPopup && !core.CanGoBack) owner.Close();
        };

        core.NewWindowRequested += async (_, e) =>
        {
            if (!IsInApp(e.Uri))
            {
                e.Handled = true;
                OpenExternal(e.Uri);
                return;
            }
            // In-app popup (e.g. Google sign-in): a real window so window.opener keeps working.
            var deferral = e.GetDeferral();
            var popup = new PopupForm();
            popup.Show(owner);
            await popup.Web.EnsureCoreWebView2Async(env);
            Configure(popup.Web.CoreWebView2, env, isPopup: true, owner: popup);
            popup.Web.CoreWebView2.WindowCloseRequested += (_, _) => popup.Close();
            popup.Web.CoreWebView2.DocumentTitleChanged += (_, _) => popup.Text = popup.Web.CoreWebView2.DocumentTitle;
            e.NewWindow = popup.Web.CoreWebView2;
            e.Handled = true;
            deferral.Complete();
        };

        // Auto-allow camera/mic/notifications/clipboard for Meet only, and remember the choice.
        core.PermissionRequested += (_, e) =>
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) || u.Host != "meet.google.com") return;
            if (e.PermissionKind is CoreWebView2PermissionKind.Camera or CoreWebView2PermissionKind.Microphone
                or CoreWebView2PermissionKind.Notifications or CoreWebView2PermissionKind.ClipboardRead
                or CoreWebView2PermissionKind.WindowManagement)
            {
                e.State = CoreWebView2PermissionState.Allow;
                e.SavesInProfile = true;
            }
        };
    }

    static bool IsInApp(string uri)
    {
        if (string.IsNullOrEmpty(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out var u)) return true;
        if (u.Scheme is not ("http" or "https")) return u.Scheme is "about" or "data" or "blob";
        var h = u.Host;
        return h == "meet.google.com"
            || h.StartsWith("accounts.google.")      // sign-in, incl. country domains
            || h == "accounts.youtube.com"           // sign-in cookie hop
            || h == "myaccount.google.com"
            || h == "gds.google.com"                 // 2-step verification prompts
            || h == "ogs.google.com"                 // account switcher
            || (h.StartsWith("www.google.") && (u.AbsolutePath.StartsWith("/accounts") || u.AbsolutePath == "/url"));
    }

    static void OpenExternal(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return;
        if (u.Scheme is not ("http" or "https" or "mailto" or "tel")) return;
        try { Process.Start(new ProcessStartInfo(u.ToString()) { UseShellExecute = true }); } catch { }
    }

    // window.txt holds "x,y,width,height,maximized".
    void LoadBounds()
    {
        try
        {
            var v = File.ReadAllText(SettingsFile).Split(',').Select(int.Parse).ToArray();
            var r = new Rectangle(v[0], v[1], v[2], v[3]);
            if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(r)))
            {
                Bounds = r;
                if (v[4] == 1) WindowState = FormWindowState.Maximized;
                return;
            }
        }
        catch { }
        var wa = Screen.PrimaryScreen!.WorkingArea;
        Size = new Size(Math.Min(1280, wa.Width), Math.Min(800, wa.Height));
        Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);
    }

    void SaveBounds()
    {
        try
        {
            var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(SettingsFile,
                $"{r.X},{r.Y},{r.Width},{r.Height},{(WindowState == FormWindowState.Maximized ? 1 : 0)}");
        }
        catch { }
    }
}

sealed class PopupForm : Form
{
    public readonly WebView2 Web = new() { Dock = DockStyle.Fill };

    public PopupForm()
    {
        Text = "Google";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Size = new Size(520, 680);
        StartPosition = FormStartPosition.CenterParent;
        Controls.Add(Web);
    }
}
