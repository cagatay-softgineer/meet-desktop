# Meet Desktop

A tiny Windows app for Google Meet, so you don't need to keep Chrome open just for meetings.

- **Light:** a ~4 MB installer. It uses the WebView2 engine built into Windows and runs no tabs or extensions. Everything is freed when you close it.
- **Global shortcuts:** they work even when the window is in the background, and you can change them.
- **Stays signed in**, with camera and microphone automatically allowed for `meet.google.com` only.
- **Other links** (from meeting chat etc.) open in your normal browser.
- **Auto-updates** from GitHub Releases.

> Not affiliated with Google. This is a dedicated window for the Meet website.

## Install

Download **GoogleMeetSetup.exe** from the [latest release](https://github.com/cagatay-softgineer/meet-desktop/releases/latest) and run it.

The installer isn't code-signed, so Windows SmartScreen may warn you. Click **More info → Run anyway**.

Requirements: Windows 10/11 with .NET Framework 4.8 (built in) and the WebView2 runtime (installed automatically if missing).

## Keyboard shortcuts

| Action | Default |
|---|---|
| Mute / unmute microphone | `Ctrl+Alt+Shift+M` |
| Camera on / off | `Ctrl+Alt+Shift+V` |
| Raise / lower hand | `Ctrl+Alt+Shift+H` |
| Leave call | `Ctrl+Alt+Shift+L` |
| Show / hide window | `Ctrl+Alt+Shift+G` |

To change them, press `Ctrl+,` in the app, or right-click the tray icon and choose **Keyboard shortcuts…**. When you're the host, **Leave call** always picks "just leave" and never ends the meeting for everyone.

## Updates

The app checks GitHub Releases at most once a day and shows a tray notification when a new version is out. You can also check yourself from the tray menu with **Check for updates…**, or by running `MeetApp.exe --check-updates`. Updating downloads the new installer, installs it silently and reopens the app.

## Building

```powershell
dotnet build -c Release
# installer (needs Inno Setup 6 and installer\MicrosoftEdgeWebview2Setup.exe):
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\MeetApp.iss
```

## Releasing

Bump `<Version>` in `MeetApp.csproj` (and the default in `installer/MeetApp.iss`), commit, then:

```powershell
git tag v1.2.0
git push origin v1.2.0
```

GitHub Actions builds the installer and publishes the release. Installed apps will pick it up on their next check.
