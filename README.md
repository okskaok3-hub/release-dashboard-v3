# ZoomClipboard

ZoomClipboard is a Windows utility for sending files to a private Zoom Team Chat channel and downloading recent channel attachments. It includes a graphical dashboard, command-line tools, and optional Windows Explorer context-menu integration.

## Requirements

- Windows 10 or Windows 11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- A Zoom user-level OAuth app with Public Client OAuth enabled

## Install and run

1. Download `ZoomClipboard-v3.0.0.zip` from the [latest release](https://github.com/okskaok3-hub/release-dashboard-v3/releases/latest).
2. Extract the entire archive to a permanent folder.
3. Run `ZoomClipboardUI.exe`.
4. Select **Sign in** and approve access in Zoom.
5. Choose the private Team Chat channel to use for file transfers.

Keep the extracted folder in place if you enable Explorer integration, because the context-menu command runs the executable from that location.

## Features

- Drag and drop or select multiple files for upload.
- Browse recent attachments from a selected Zoom Team Chat channel.
- Copy a fresh attachment download link or download a file locally.
- Add **Copy to Zoom Clipboard** to the Windows Explorer context menu.
- Add a **Send to → Zoom Clipboard** shortcut.
- Use the same saved sign-in and channel configuration from the GUI or CLI.

On Windows 11, the Explorer command may appear under **Show more options**. Zoom's send-file endpoint limits regular files to 20 MB.

## Zoom OAuth setup

In Zoom Marketplace, create a user-level OAuth app and:

1. Enable **Use Public Client OAuth**.
2. Add `http://127.0.0.1:8765/callback` as the development redirect URL and OAuth allow-list URL.
3. Configure these scopes:

   - `team_chat:write:message_files`
   - `team_chat:read:list_user_messages`
   - `team_chat:read:user_message`
   - `team_chat:read:file`
   - `team_chat:read:list_user_channels`

The optional `user:read:user` scope allows the app to display the signed-in user's name and email. Use the app's **Public Client ID**, never its Client Secret.

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
