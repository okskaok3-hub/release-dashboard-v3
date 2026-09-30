# Zoom Clipboard 4.0.0 — Windows and Linux

Transfer files through a private Zoom Team Chat channel. Both versions offer guided setup, browser-based Zoom OAuth, channel creation/selection, recent-file browsing, upload, download links, and confirmed deletion.

Download the archives from [GitHub Releases](https://github.com/okskaok3-hub/zoom-clipboard/releases/latest). The repository contains source and documentation; installable packages are attached to releases.

## Choose your download

| Platform | Archive | Requirements |
| --- | --- | --- |
| Windows x64 | `ZoomClipboard-4.0.0-windows-x64.zip` | Windows 10/11 x64. .NET is bundled. |
| Linux | `ZoomClipboard-4.0.0-linux.tar.gz` | Python 3.10+, a desktop web browser, an active graphical session. Works on x64 and ARM64 with the distribution's Python. |

The Linux interface runs in your browser on `http://127.0.0.1:8765`. It is a local application, not a hosted website. The Windows version uses the native WinForms dashboard. No Zoom password or OAuth client secret is included in either archive.

## Windows installation

1. Download the Windows ZIP and extract **the entire archive** into a permanent folder, for example `%LOCALAPPDATA%\Programs\ZoomClipboard`.
2. If a previous version is running, right-click its notification-area icon and choose **Exit** first. The old process must exit before launching a new build; clicking its X only hides it.
3. Open `ZoomClipboardUI.exe`.
4. On a fresh installation, follow **Welcome → Sign in with Zoom → Create/select channel → Dashboard**.
5. Complete authorization in your browser. Return to the application when finished.
6. Create a dedicated private channel, or select an existing Team Chat channel.

Your encrypted login and selected channel are reused on subsequent launches. Previous Windows versions use the same configuration location and migrate without copying tokens manually.

### Windows background and Explorer integration

- Clicking the X hides the dashboard and keeps the process running.
- Double-click the notification-area icon to reopen it; right-click and select **Exit** to stop it.
- Windows decides whether the icon appears in the taskbar or the hidden-icons overflow. Adjust taskbar settings to keep it visible.
- Launching the GUI again restores the existing instance.
- The dashboard can install the Explorer right-click action. On Windows 11 it may appear under **Show more options**.
- Keep the install folder in place after enabling Explorer integration.
- No automatic startup at Windows login is configured by this release.

### Windows commands

Run these from the extracted folder in PowerShell:

```powershell
.\ZoomClipboard.exe login
.\ZoomClipboard.exe channels
.\ZoomClipboard.exe configure-channel 'CHANNEL_ID'
.\ZoomClipboard.exe list-files
.\ZoomClipboard.exe upload 'C:\path\document.pdf'
.\ZoomClipboard.exe upload-link 'C:\path\document.pdf'
.\ZoomClipboard.exe download-latest 'C:\Downloads'
.\ZoomClipboard.exe install-context
.\ZoomClipboard.exe uninstall-context
.\ZoomClipboard.exe logout
```

## Linux installation

### Install dependencies

Ubuntu / Debian:

```bash
sudo apt update
sudo apt install python3 libsecret-tools gnome-keyring python3-gi gir1.2-gtk-3.0 gir1.2-ayatanaappindicator3-0
```

Fedora:

```bash
sudo dnf install python3 libsecret gnome-keyring python3-gobject gtk3 libayatana-appindicator-gtk3
```

On other distributions, install Python 3.10+, the `secret-tool` utility, a Secret Service provider such as GNOME Keyring, and optionally GTK 3, Python GObject introspection, and Ayatana AppIndicator bindings. Use your distribution's system Python for GTK/tray support. No pip packages are required.

### Extract and launch

```bash
tar -xzf ZoomClipboard-4.0.0-linux.tar.gz
cd ZoomClipboard-4.0.0-linux
chmod +x zoom-clipboard install.sh
./zoom-clipboard
```

The launcher opens your browser. Follow Welcome → Zoom sign-in → create/select a private channel → dashboard. After Zoom authorization, return to the original dashboard tab. If your browser blocks the authorization tab, allow pop-ups for `http://127.0.0.1:8765` and click sign-in again.

### Add an application-menu launcher

```bash
./install.sh
```

This copies the app into `${XDG_DATA_HOME:-$HOME/.local/share}/zoom-clipboard` and installs a desktop entry in your user application menu. Start **Zoom Clipboard** from that menu afterwards. It installs only for your current Linux user and does not require root.

### Linux background, tray, and login persistence

- Closing the browser tab leaves the local app running.
- Re-run `./zoom-clipboard`, or use the application-menu launcher, to reopen the same instance.
- A tray menu provides **Open Zoom Clipboard** and **Exit** when GTK 3 and Ayatana AppIndicator are available.
- GNOME Shell may require your distribution's AppIndicator extension to show tray icons. If no tray is visible, the dashboard's **Exit app** button still works.
- Without tray dependencies, the browser dashboard remains fully usable.
- A terminal-launched process may stop when its terminal closes. To launch detached:

```bash
nohup ./zoom-clipboard > /tmp/zoom-clipboard.log 2>&1 &
```

- The desktop keyring stores OAuth tokens using `secret-tool`. Keep your desktop keyring unlocked. Without it, tokens stay in memory for the running session and you must sign in after exiting the app.
- Channel and public-client configuration live in `${XDG_CONFIG_HOME:-$HOME/.config}/zoom-clipboard/config.json`; tokens are never written there.
- No automatic startup at desktop login is configured. The Linux version does not install Windows Explorer menus.
- Avoid running the launcher as root. Linux and Windows keep separate logins and channel selections.

## Upload, copy, download, and delete

1. Select files or drag them onto the upload area. The current send-file endpoint accepts up to **20 MB per file**.
2. Files upload sequentially. The newest download link is copied to the clipboard when browser permission permits it. If clipboard access is denied, Linux shows the link for manual copying.
3. Refresh the file list to browse recent channel attachments. Up to five pages of recent message history are inspected; this is not a complete long-term storage index.
4. Use **Copy link** to get a fresh download URL. Links can expire or require a Zoom browser login.
5. On Windows, **Download** saves into a folder you choose. On Linux, **Download** opens the Zoom link and your browser handles the download and destination.
6. **Delete** requires a confirmation. It deletes the chat file on Zoom; existing links may stop working. Zoom still enforces account and ownership permissions.
7. Deleted attachments that remain as nameless references in message history are excluded from the dashboard. Genuine named zero-byte files remain visible.

A private channel's members can access its files. Use a dedicated channel and keep its membership appropriate for the files you transfer.

## Zoom OAuth app configuration — required for the app owner

The public client ID is prefilled. Whether ordinary users can authorize it depends on your Zoom Marketplace app's publication/allow-list and the account's administrator policies. Source code cannot grant missing Marketplace scopes.

In Zoom Marketplace, enable **Public Client OAuth**, configure the redirect and OAuth allow-list URL as:

```text
http://127.0.0.1:8765/callback
```

Enable these user-level granular scopes:

```text
team_chat:write:message_files
team_chat:read:list_user_messages
team_chat:read:user_message
team_chat:read:file
team_chat:read:list_user_channels
team_chat:write:user_channel
team_chat:delete:file
```

Windows optionally uses `user:read:user` to show the user's profile name and email. After adding scopes, existing users must **sign out and sign in again** to approve the updated permissions. The owner must complete these Marketplace changes separately; the release does not change Marketplace settings.

See [Zoom's Chat API reference](https://developers.zoom.us/docs/api/chat/) for the endpoint contracts. No client secret should be distributed with a public-client app.

## Troubleshooting

| Symptom | Action |
| --- | --- |
| New Windows build seems unchanged | Exit the old tray process before starting the new EXE. |
| Callback port 8765 is occupied | Finish/close another Zoom Clipboard login or stop the other service using that port, then retry. Do not change the redirect independently of the Marketplace app. |
| Channel creation or deletion denied | Check the corresponding scope, sign out/in again, and check Zoom account permissions. |
| Linux login is not remembered | Install `libsecret-tools`/`libsecret`, run in the desktop session, and unlock the Secret Service keyring. |
| Linux tray is missing | Install GTK/Ayatana dependencies and enable desktop AppIndicator support. Reopen from the application launcher. |
| Linux says to open from the launcher | Re-run the launcher. Dashboard authorization is a per-process session kept in that browser tab; opening the bare URL in a new tab cannot bootstrap a new session. |
| Zoom link fails in Citrix/browser | Sign in to Zoom in that browser or access the file's Team Chat message through Zoom Web. |
| File appears only after a delay | Zoom indexes uploaded files asynchronously. Wait briefly and Refresh. |

`ZOOM_CLIPBOARD_TOKEN` is an optional environment override on both platforms. It takes precedence over saved login; signing out does not remove that environment variable. Signing out removes local stored tokens but does not revoke the Zoom authorization grant. Revoke the grant through your Zoom account's authorized-app settings if needed.

## Verify downloads

The release includes `SHA256SUMS`:

```bash
sha256sum -c SHA256SUMS --ignore-missing
```

Windows PowerShell:

```powershell
Get-FileHash .\ZoomClipboard-4.0.0-windows-x64.zip -Algorithm SHA256
```

Compare the result against the matching line in `SHA256SUMS`. Windows binaries are not Authenticode-signed; Windows may show the publisher as unknown.

## Build and verify from source

Source is in `src/windows` and `src/linux` in this repository.

Windows requires the .NET 10 SDK:

```powershell
cd src\windows
dotnet publish ZoomClipboard.csproj -c Release -r win-x64 --self-contained true -o out
dotnet publish ZoomClipboardUI.csproj -c Release -r win-x64 --self-contained true -o out
```

Linux runs directly from Python source:

```bash
cd src/linux
python3 test_zoom_clipboard.py
./zoom-clipboard
```

### Validation of this release

Windows CLI and GUI packages were compiled on Windows. Six offline Linux regression checks passed, covering attachment filtering, saved configuration, private-channel creation payloads, OAuth state validation, trusted redirects, and local dashboard session/origin checks. The browser script was syntax-checked. No real Zoom files were deleted by these checks.

A Linux graphical desktop was not available on the build machine. The Linux release is a preview: actual distribution tray/keyring integration and a complete Zoom authorization/upload/download session still require a Linux-desktop smoke test. Source and offline tests are included for reproducibility.
