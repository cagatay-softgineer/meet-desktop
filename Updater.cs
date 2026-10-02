using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MeetApp;

/// <summary>Checks GitHub Releases for a newer installer and runs it.</summary>
static class Updater
{
    public const string Repo = "cagatay-softgineer/meet-desktop";
    const string AssetName = "GoogleMeetSetup.exe";
    static readonly TimeSpan CheckInterval = TimeSpan.FromHours(20);

#pragma warning disable CS0649 // fields are filled by the JSON serializer
    [DataContract]
    sealed class GitHubRelease
    {
        [DataMember(Name = "tag_name")] public string Tag = "";
        [DataMember(Name = "html_url")] public string PageUrl = "";
        [DataMember(Name = "body")] public string? Notes;
        [DataMember(Name = "assets")] public GitHubAsset[]? Assets;
    }

    [DataContract]
    sealed class GitHubAsset
    {
        [DataMember(Name = "name")] public string Name = "";
        [DataMember(Name = "browser_download_url")] public string Url = "";
    }
#pragma warning restore CS0649

    public sealed class UpdateInfo
    {
        public Version Version = new(0, 0, 0);
        public string Notes = "", DownloadUrl = "", PageUrl = "";
    }

    public static Version Current => Normalize(Assembly.GetExecutingAssembly().GetName().Version);

    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    static WebClient NewClient()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; // .NET Framework defaults can be older
        var wc = new WebClient { Encoding = Encoding.UTF8 };
        wc.Headers[HttpRequestHeader.UserAgent] = "MeetDesktop/" + Current;
        wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
        return wc;
    }

    /// <summary>Returns the latest release if it is newer than this build, otherwise null.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        string json;
        using (var wc = NewClient())
            json = await wc.DownloadStringTaskAsync($"https://api.github.com/repos/{Repo}/releases/latest");

        GitHubRelease release;
        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            release = (GitHubRelease)new DataContractJsonSerializer(typeof(GitHubRelease)).ReadObject(ms);

        if (!Version.TryParse(release.Tag.TrimStart('v', 'V'), out var latest)) return null;
        latest = Normalize(latest);
        var asset = release.Assets?.FirstOrDefault(a => a.Name.Equals(AssetName, StringComparison.OrdinalIgnoreCase));
        if (latest <= Current || asset == null) return null;
        return new UpdateInfo { Version = latest, Notes = release.Notes ?? "", DownloadUrl = asset.Url, PageUrl = release.PageUrl };
    }

    // ---------- "check at most once a day" + "skip this version" state ----------

    static string StateFile(string dataDir) => Path.Combine(dataDir, "update.txt");

    public static bool IsCheckDue(string dataDir)
    {
        try
        {
            var lines = File.ReadAllLines(StateFile(dataDir));
            return DateTime.UtcNow - DateTime.Parse(lines[0], null, System.Globalization.DateTimeStyles.RoundtripKind) > CheckInterval;
        }
        catch { return true; }
    }

    public static string SkippedVersion(string dataDir)
    {
        try { return File.ReadAllLines(StateFile(dataDir)).ElementAtOrDefault(1) ?? ""; } catch { return ""; }
    }

    public static void SaveState(string dataDir, string? skipped = null)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            File.WriteAllLines(StateFile(dataDir), new[] { DateTime.UtcNow.ToString("o"), skipped ?? SkippedVersion(dataDir) });
        }
        catch { }
    }

    // ---------- install ----------

    /// <summary>Downloads the installer and runs it silently; the installer closes this app and relaunches it.</summary>
    public static async Task DownloadAndInstallAsync(UpdateInfo update, Action<int> progress)
    {
        var path = Path.Combine(Path.GetTempPath(), $"GoogleMeetSetup-{update.Version}.exe");
        using (var wc = NewClient())
        {
            wc.DownloadProgressChanged += (_, e) => progress(e.ProgressPercentage);
            await wc.DownloadFileTaskAsync(new Uri(update.DownloadUrl), path);
        }

        // Keep the install scope the user originally picked (per-user vs. all users).
        bool allUsers = IsUnder(Application.StartupPath, Environment.SpecialFolder.ProgramFiles)
                     || IsUnder(Application.StartupPath, Environment.SpecialFolder.ProgramFilesX86);
        Process.Start(new ProcessStartInfo(path,
            $"/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS {(allUsers ? "/ALLUSERS" : "/CURRENTUSER")} /RELAUNCH=1")
        { UseShellExecute = true });
        Application.Exit();
    }

    static bool IsUnder(string path, Environment.SpecialFolder folder)
    {
        var root = Environment.GetFolderPath(folder);
        return root.Length > 0 && path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>"Version X is available" dialog with Update now / Skip / Later.</summary>
sealed class UpdateDialog : Form
{
    public bool Skipped { get; private set; }

    public UpdateDialog(Updater.UpdateInfo update)
    {
        Text = "Update available";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(460, 300);
        Padding = new Padding(14);

        var title = new Label
        {
            Text = $"Google Meet Desktop {update.Version} is available (you have {Updater.Current}).",
            Dock = DockStyle.Top, Height = 30, Font = new Font(Font, FontStyle.Bold),
        };
        var notes = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(update.Notes) ? "No release notes."
                : update.Notes.Replace("**", "").Replace("\r\n", "\n").Replace("\n", "\r\n"),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = SystemColors.Window,
        };
        var bar = new ProgressBar { Dock = DockStyle.Bottom, Height = 8, Visible = false };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
        var install = new Button { Text = "Update now", AutoSize = true };
        var later = new Button { Text = "Later", AutoSize = true, DialogResult = DialogResult.Cancel };
        var skip = new Button { Text = "Skip this version", AutoSize = true };
        var page = new LinkLabel { Text = "Release page", AutoSize = true, Padding = new Padding(0, 6, 40, 0) };
        page.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(update.PageUrl) { UseShellExecute = true });
        skip.Click += (_, _) => { Skipped = true; DialogResult = DialogResult.Cancel; };
        install.Click += async (_, _) =>
        {
            install.Enabled = later.Enabled = skip.Enabled = false;
            install.Text = "Downloading...";
            bar.Visible = true;
            try
            {
                await Updater.DownloadAndInstallAsync(update, p => bar.Value = Math.Min(100, Math.Max(0, p)));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Update failed: " + ex.Message, "Update", MessageBoxButtons.OK, MessageBoxIcon.Error);
                install.Enabled = later.Enabled = skip.Enabled = true;
                install.Text = "Update now";
                bar.Visible = false;
            }
        };
        buttons.Controls.AddRange(new Control[] { later, install, skip, page });
        Controls.Add(notes);
        Controls.Add(title);
        Controls.Add(bar);
        Controls.Add(buttons);
        AcceptButton = install;
        CancelButton = later;
        Shown += (_, _) => { notes.SelectionLength = 0; install.Focus(); };
    }
}
