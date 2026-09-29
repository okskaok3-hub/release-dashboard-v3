# ZoomClipboard

ZoomClipboard is a Windows utility for sending files to a private Zoom Team Chat channel and downloading recent channel attachments. It includes a graphical dashboard, command-line tools, and optional Windows Explorer context-menu integration.

## Requirements

- Windows 10 or Windows 11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- A Zoom account

## Install and run

1. Download `ZoomClipboard-v3.0.0.zip` from the [latest release](https://github.com/okskaok3-hub/release-dashboard-v3/releases/latest).
2. Extract the entire archive to a permanent folder.
3. Run `ZoomClipboardUI.exe`.
4. Select **Sign in** and approve access in Zoom.
5. Choose the private Team Chat channel to use for file transfers.

The Zoom app configuration and Public Client ID are already included. Users do not need to create or configure their own OAuth app.

Keep the extracted folder in place if you enable Explorer integration, because the context-menu command runs the executable from that location.

## Features

- Drag and drop or select multiple files for upload.
- Browse recent attachments from a selected Zoom Team Chat channel.
- Copy a fresh attachment download link or download a file locally.
- Add **Copy to Zoom Clipboard** to the Windows Explorer context menu.
- Add a **Send to → Zoom Clipboard** shortcut.
- Use the same saved sign-in and channel configuration from the GUI or CLI.

On Windows 11, the Explorer command may appear under **Show more options**. Zoom's send-file endpoint limits regular files to 20 MB.

## Sign in

Select **Sign in** in the app and complete Zoom's authorization page. The app uses Public Client OAuth, so no client secret is distributed with the application. The included Zoom app requests only the permissions needed for Team Chat file transfers and recent-file browsing.

## Command-line usage

```powershell
ZoomClipboard.exe login
ZoomClipboard.exe channels
ZoomClipboard.exe configure-channel '<channel ID>'
ZoomClipboard.exe list-files
ZoomClipboard.exe upload 'C:\path\file.txt'
ZoomClipboard.exe upload-link 'C:\path\file.txt'
ZoomClipboard.exe download-latest 'C:\destination'
ZoomClipboard.exe install-context
ZoomClipboard.exe uninstall-context
```

OAuth tokens are stored with Windows DPAPI and are bound to the current Windows user. Signing out deletes the locally saved tokens but does not revoke the app grant in Zoom. To revoke access completely, remove the app from the authorized apps in your Zoom account.

Use a dedicated private channel. Channel members can access files uploaded to that channel, and copied download links may expire or require a Zoom browser login.
