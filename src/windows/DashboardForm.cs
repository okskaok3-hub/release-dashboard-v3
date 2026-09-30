using System.Drawing;

namespace ZoomClipboard;

internal sealed class DashboardForm : Form
{
    private readonly NotifyIcon trayIcon;
    private bool allowExit;
    private bool trayHintShown;
    private readonly Panel side = new() { BackColor = Color.White, Dock = DockStyle.Left, Width = 230 };
    private readonly Panel body = new() { BackColor = UiTheme.Canvas, Dock = DockStyle.Fill };
    private readonly Panel home = new() { BackColor = UiTheme.Canvas };
    private readonly Panel files = new() { BackColor = UiTheme.Canvas };
    private readonly Panel settings = new() { BackColor = UiTheme.Canvas };
    private readonly Label identity = L("Not connected", 10, true);
    private readonly Label email = L("Sign in with Zoom", 8.5f);
    private readonly Label state = L("●  Not connected", 9, true);
    private readonly Label status = L("Ready", 9);
    private readonly Label homeSummary = L("Sign in to see your files", 9);
    private readonly Label filesSummary = L("Sign in to see your files", 9);
    private readonly StatusPill connectionPill = new();
    private readonly PremiumButton signInShortcut = Secondary("Sign in");
    private readonly ComboBox channels = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
    private readonly TextBox clientId = new() { PlaceholderText = "Public Client ID" };
    private readonly ListView homeList = MakeList();
    private readonly ListView fullList = MakeList();
    private readonly ListView recentList = MakeList();
    private readonly PremiumButton upload = Primary("Select files");
    private readonly PremiumButton homeRefresh = Secondary("↻  Refresh");
    private readonly PremiumButton fullRefresh = Secondary("↻  Refresh");
    private readonly PremiumButton homeCopy = Secondary("Copy link");
    private readonly PremiumButton fullCopy = Secondary("Copy link");
    private readonly PremiumButton homeDownload = Secondary("Download");
    private readonly PremiumButton fullDownload = Secondary("Download");
    private readonly PremiumButton homeDelete = Danger("Delete");
    private readonly PremiumButton fullDelete = Danger("Delete");
    private readonly PremiumButton login = Primary("Sign in with Zoom");
    private readonly PremiumButton logout = Secondary("Sign out");
    private readonly PremiumButton channelRefresh = Secondary("Refresh channels");
    private readonly PremiumButton install = Secondary("Install right-click action");
    private readonly PremiumProgressBar progress = new();
    private readonly Panel drop = new() { BackColor = Color.FromArgb(246, 249, 255), AllowDrop = true };
    private readonly CardPanel transferCard = new();
    private readonly CardPanel recentCard = new();
    private readonly CardPanel explorerCard = new();
    private readonly CardPanel fullCard = new();
    private readonly CardPanel accountCard = new();
    private readonly CardPanel channelCard = new();
    private readonly CardPanel integrationCard = new();
    private readonly PremiumButton[] nav = [Nav("⌂   Home"), Nav("▱   File Explorer"), Nav("↑   Transfers"), Nav("⚙   Settings")];
    private IReadOnlyList<Program.UploadedFile> entries = [];
    private bool busy;
    private bool loadingChannels;
    private sealed record Channel(string Id, string Name) { public override string ToString() => Name; }

    internal DashboardForm()
    {
        Text = "Zoom Clipboard";
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(1050, 700);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Canvas;
        Font = new Font("Segoe UI", 10);

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Open Zoom Clipboard", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitApplication());
        trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Zoom Clipboard",
            Visible = true,
            ContextMenuStrip = trayMenu
        };
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        Controls.Add(body);
        Controls.Add(side);
        BuildSidebar();
        BuildHome();
        BuildFiles();
        BuildSettings();
        body.Controls.AddRange([home, files, settings]);
        status.Dock = DockStyle.Bottom;
        status.Padding = new Padding(25, 0, 0, 0);
        status.Height = 28;
        body.Controls.Add(status);
        Wire();
        ShowPage(0);
        Resize += (_, _) => Place();
        Shown += async (_, _) => { Place(); if (Program.IsSignedIn) await Run(LoadAccount); else SetSignedOut(); };
        FormClosing += OnFormClosing;
        FormClosed += (_, _) => trayIcon.Dispose();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (allowExit || e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
            return;

        e.Cancel = true;
        Hide();
        if (trayHintShown) return;
        trayHintShown = true;
        trayIcon.ShowBalloonTip(3000, "Zoom Clipboard is still running",
            "Double-click the tray icon to open it, or right-click the icon to exit.", ToolTipIcon.Info);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        allowExit = true;
        trayIcon.Visible = false;
        Close();
    }

    private void BuildSidebar()
    {
        var logo = L("ZC", 17, true, Color.White);
        logo.BackColor = UiTheme.Primary;
        logo.TextAlign = ContentAlignment.MiddleCenter;
        logo.SetBounds(20, 25, 54, 48);
        side.Controls.Add(logo);
        side.Controls.Add(At(L("Zoom Clipboard", 10.5f, true), 84, 27, 140, 26));
        side.Controls.Add(At(L("Upload and share files", 8.2f), 84, 52, 140, 20));
        identity.AutoEllipsis = email.AutoEllipsis = true;
        for (var i = 0; i < nav.Length; i++) { nav[i].SetBounds(16, i < 3 ? 118 + 52 * i : 302, 198, 44); side.Controls.Add(nav[i]); }
        side.Controls.Add(new Panel { BackColor = UiTheme.Border, Bounds = new Rectangle(20, 282, 190, 1) });
        side.Controls.Add(state);
        side.Controls.Add(identity);
        side.Controls.Add(email);
    }

    private void BuildHome()
    {
        home.Controls.Add(At(L("Welcome to Zoom Clipboard", 25, true), 28, 24, 760, 48));
        home.Controls.Add(At(L("Upload a file, copy its link, and browse files in your Zoom channel.", 10.5f), 30, 72, 760, 29));
        connectionPill.SetBounds(850, 36, 140, 34);
        signInShortcut.SetBounds(850, 36, 125, 36);
        home.Controls.AddRange([connectionPill, signInShortcut]);
        home.Controls.AddRange([transferCard, recentCard, explorerCard]);
        transferCard.Controls.Add(At(L("Transfer files", 17, true), 22, 18, 400, 34));
        transferCard.Controls.Add(At(L("Upload to Zoom, then paste the copied link in Citrix.", 9.3f), 22, 55, 430, 38));
        drop.SetBounds(22, 110, 435, 245);
        transferCard.Controls.Add(drop);
        var cloud = At(L("☁", 37, true, UiTheme.Primary), 0, 20, 435, 66);
        cloud.TextAlign = ContentAlignment.MiddleCenter;
        drop.Controls.Add(cloud);
        var prompt = At(L("Drop files here or click to upload", 12, true), 0, 98, 435, 30);
        prompt.TextAlign = ContentAlignment.MiddleCenter;
        drop.Controls.Add(prompt);
        var note = At(L("Multiple files supported · 20 MB per file", 9), 0, 130, 435, 28);
        note.TextAlign = ContentAlignment.MiddleCenter;
        drop.Controls.Add(note);
        upload.SetBounds(98, 174, 240, 46);
        drop.Controls.Add(upload);
        progress.SetBounds(22, 372, 435, 9);
        transferCard.Controls.Add(progress);
        recentCard.Controls.Add(At(L("Recent transfers", 14, true), 20, 16, 300, 29));
        recentList.SetBounds(18, 51, 440, 150);
        recentCard.Controls.Add(recentList);
        explorerCard.Controls.Add(At(L("File Explorer", 17, true), 20, 19, 280, 34));
        homeRefresh.SetBounds(320, 19, 125, 38);
        explorerCard.Controls.Add(homeRefresh);
        homeSummary.SetBounds(20, 61, 460, 25);
        explorerCard.Controls.Add(homeSummary);
        homeList.SetBounds(20, 95, 440, 450);
        explorerCard.Controls.Add(homeList);
        homeCopy.SetBounds(20, 560, 105, 40);
        homeDownload.SetBounds(135, 560, 105, 40);
        homeDelete.SetBounds(250, 560, 88, 40);
        explorerCard.Controls.AddRange([homeCopy, homeDownload, homeDelete]);
    }

    private void BuildFiles()
    {
        files.Controls.Add(At(L("Files in your Zoom channel", 25, true), 28, 24, 700, 48));
        files.Controls.Add(At(L("Files in your selected Zoom Team Chat channel. Select one to copy its link or download it.", 10.5f), 30, 72, 900, 29));
        files.Controls.Add(fullCard);
        fullCard.Controls.Add(At(L("Files", 17, true), 20, 18, 350, 34));
        fullRefresh.SetBounds(760, 19, 130, 38);
        fullCard.Controls.Add(fullRefresh);
        filesSummary.SetBounds(20, 61, 700, 25);
        fullCard.Controls.Add(filesSummary);
        fullList.SetBounds(20, 95, 870, 470);
        fullCard.Controls.Add(fullList);
        fullCopy.SetBounds(20, 575, 140, 42);
        fullDownload.SetBounds(170, 575, 140, 42);
        fullDelete.SetBounds(320, 575, 120, 42);
        fullCard.Controls.AddRange([fullCopy, fullDownload, fullDelete]);
    }

    private void BuildSettings()
    {
        settings.Controls.Add(At(L("Settings", 25, true), 28, 24, 700, 48));
        settings.Controls.AddRange([accountCard, channelCard, integrationCard]);
        accountCard.SetBounds(28, 112, 455, 228);
        accountCard.Controls.Add(At(L("Zoom account", 16, true), 22, 18, 400, 35));
        accountCard.Controls.Add(At(L("Sign in to access your channels and files.", 9), 22, 57, 390, 30));
        login.SetBounds(22, 103, 205, 42);
        logout.SetBounds(237, 103, 170, 42);
        accountCard.Controls.AddRange([login, logout]);
        clientId.Text = Program.SavedClientId;
        clientId.SetBounds(22, 168, 385, 30);
        accountCard.Controls.Add(clientId);
        channelCard.SetBounds(500, 112, 480, 228);
        channelCard.Controls.Add(At(L("Destination channel", 16, true), 22, 18, 400, 35));
        channelCard.Controls.Add(At(L("Channel members can access its uploaded files.", 9), 22, 57, 430, 30));
        channels.SetBounds(22, 104, 430, 33);
        channelRefresh.SetBounds(22, 163, 176, 40);
        channelCard.Controls.AddRange([channels, channelRefresh]);
        integrationCard.SetBounds(28, 356, 952, 166);
        integrationCard.Controls.Add(At(L("Windows integration", 16, true), 22, 18, 500, 35));
        integrationCard.Controls.Add(At(L("Add a Copy to Zoom Clipboard command to the file right-click menu.", 9), 22, 58, 700, 28));
        install.SetBounds(22, 106, 245, 42);
        integrationCard.Controls.Add(install);
    }

    private void Wire()
    {
        nav[0].Click += (_, _) => ShowPage(0);
        nav[1].Click += (_, _) => ShowPage(1);
        nav[2].Click += (_, _) => ShowPage(2);
        nav[3].Click += (_, _) => ShowPage(3);
        signInShortcut.Click += (_, _) => ShowPage(3);
        upload.Click += async (_, _) =>
        {
            using var picker = new OpenFileDialog { Title = "Select files to upload", Multiselect = true };
            if (picker.ShowDialog(this) == DialogResult.OK) await UploadFiles(picker.FileNames);
        };
        drop.DragEnter += (_, e) => { var ok = !busy && channels.SelectedItem is Channel && e.Data?.GetDataPresent(DataFormats.FileDrop) == true; e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None; if (ok) drop.BackColor = Color.FromArgb(224, 235, 255); };
        drop.DragLeave += (_, _) => drop.BackColor = Color.FromArgb(246, 249, 255);
        drop.DragDrop += async (_, e) => { drop.BackColor = Color.FromArgb(246, 249, 255); if (!busy && channels.SelectedItem is Channel && e.Data?.GetData(DataFormats.FileDrop) is string[] paths) await UploadFiles(paths.Where(File.Exists).ToArray()); };
        homeRefresh.Click += async (_, _) => await Run(LoadFiles);
        fullRefresh.Click += async (_, _) => await Run(LoadFiles);
        channelRefresh.Click += async (_, _) => await Run(LoadAccount);
        login.Click += async (_, _) => await Run(async () => { SetStatus("Complete Zoom sign-in in your browser.", UiTheme.Primary); await Program.SignInForUi(clientId.Text.Trim()); await LoadAccount(); });
        logout.Click += (_, _) => { Program.SignOutForUi(); channels.Items.Clear(); entries = []; SetSignedOut(); FillLists(); SetStatus("Signed out. Saved login removed.", UiTheme.Muted); };
        install.Click += (_, _) => { try { Program.InstallContextForUi(); SetStatus("Right-click action installed.", UiTheme.Success); } catch (Exception ex) { Error(ex.Message); } };
        channels.SelectedIndexChanged += async (_, _) => { if (loadingChannels || channels.SelectedItem is not Channel c) return; Program.SaveChannelForUi(c.Id); await Run(LoadFiles); };
        homeCopy.Click += async (_, _) => await CopyLink(homeList);
        fullCopy.Click += async (_, _) => await CopyLink(fullList);
        homeDownload.Click += async (_, _) => await Download(homeList);
        fullDownload.Click += async (_, _) => await Download(fullList);
        homeDelete.Click += async (_, _) => await DeleteFile(homeList);
        fullDelete.Click += async (_, _) => await DeleteFile(fullList);
        homeList.DoubleClick += async (_, _) => await CopyLink(homeList);
        fullList.DoubleClick += async (_, _) => await CopyLink(fullList);
        homeList.SelectedIndexChanged += (_, _) => UpdateButtons();
        fullList.SelectedIndexChanged += (_, _) => UpdateButtons();
        foreach (var list in new[] { homeList, fullList, recentList })
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Copy download link", null, async (_, _) => await CopyLink(list));
            menu.Items.Add("Download file", null, async (_, _) => await Download(list));
            menu.Items.Add(new ToolStripSeparator());
            var deleteItem = menu.Items.Add("Delete from Zoom", null, async (_, _) => await DeleteFile(list));
            deleteItem.ForeColor = UiTheme.Danger;
            list.ContextMenuStrip = menu;
        }
    }

    private async Task LoadAccount()
    {
        SetStatus("Connecting to Zoom...", UiTheme.Primary);
        var saved = Program.SavedChannelId;
        var available = (await Program.GetChannels()).Where(c => !c.Id.StartsWith("web_ins_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).Select(c => new Channel(c.Id, c.Name)).ToArray();
        loadingChannels = true;
        try
        {
            channels.Items.Clear();
            channels.Items.AddRange(available);
            var index = Array.FindIndex(available, c => c.Id == saved);
            if (index >= 0) channels.SelectedIndex = index;
            else if (available.Length == 1) { channels.SelectedIndex = 0; Program.SaveChannelForUi(available[0].Id); }
        }
        finally { loadingChannels = false; }
        try
        {
            var profile = await Program.GetUserProfile();
            identity.Text = profile?.Name ?? "Connected to Zoom";
            email.Text = profile?.Email ?? "Profile permission unavailable";
        }
        catch { identity.Text = "Connected to Zoom"; email.Text = "Profile unavailable"; }
        state.Text = "●  Connected to Zoom";
        state.ForeColor = UiTheme.Success;
        connectionPill.SetState("Connected", UiTheme.Success, UiTheme.SuccessSoft);
        signInShortcut.Visible = false;
        await LoadFiles();

        if (!Program.IsContextInstalled)
        {
            try { Program.InstallContextForUi(); SetStatus($"Showing {entries.Count} files. Right-click action auto-enabled.", UiTheme.Success); }
            catch { /* non-critical */ }
        }
    }

    private async Task LoadFiles()
    {
        if (!Program.IsSignedIn || channels.SelectedItem is not Channel)
        {
            entries = [];
            FillLists();
            SetStatus("Choose a destination channel in Settings.", UiTheme.Muted);
            return;
        }
        SetStatus("Loading uploaded files...", UiTheme.Primary);
        entries = await Program.GetUploadedFilesForUi();
        FillLists();
        SetStatus($"Showing {entries.Count} files from {channels.SelectedItem}.", UiTheme.Success);
    }

    private void FillLists()
    {
        Fill(homeList, entries);
        Fill(fullList, entries);
        Fill(recentList, entries.Take(3));
        var text = !Program.IsSignedIn ? "Sign in to see your files" : channels.SelectedItem is null ? "Choose a channel in Settings"
            : entries.Count == 0 ? "No files found in this channel" : $"{entries.Count} files in {channels.SelectedItem}";
        homeSummary.Text = filesSummary.Text = text;
        UpdateButtons();
    }

    private static void Fill(ListView list, IEnumerable<Program.UploadedFile> records)
    {
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            foreach (var file in records)
            {
                var item = new ListViewItem(file.Name) { Tag = file };
                item.SubItems.Add(file.Size >= 1048576 ? $"{file.Size / 1048576d:0.0} MB" :
                    file.Size >= 1024 ? $"{file.Size / 1024d:0.0} KB" : $"{file.Size} B");
                item.SubItems.Add(file.UploadedAt == DateTimeOffset.MinValue ? "—" : file.UploadedAt.ToLocalTime().ToString("d MMM yyyy"));
                list.Items.Add(item);
            }
        }
        finally { list.EndUpdate(); }
    }

    private async Task UploadFiles(string[] paths)
    {
        if (paths.Length == 0) return;
        await Run(async () =>
        {
            var count = 0;
            foreach (var path in paths)
            {
                progress.Value = 0;
                SetStatus($"Uploading {Path.GetFileName(path)} ({count + 1}/{paths.Length})...", UiTheme.Primary);
                await Program.UploadLinkForUi(path, new Progress<int>(value => progress.Value = value));
                count++;
            }
            progress.Value = 100;
            await LoadFiles();
            SetStatus($"Uploaded {count} file(s). Latest link copied to clipboard.", UiTheme.Success);
        });
    }

    private async Task CopyLink(ListView list)
    {
        if (list.SelectedItems.Count == 0 || list.SelectedItems[0].Tag is not Program.UploadedFile file) return;
        await Run(async () =>
        {
            Clipboard.SetText(await Program.GetFileLinkForUi(file.FileId));
            SetStatus($"Copied link for {file.Name}. Zoom links may expire or require sign-in.", UiTheme.Success);
        });
    }

    private async Task Download(ListView list)
    {
        if (list.SelectedItems.Count == 0 || list.SelectedItems[0].Tag is not Program.UploadedFile file) return;
        using var picker = new FolderBrowserDialog { Description = $"Download {file.Name} to" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        await Run(async () => SetStatus($"Downloaded to {await Program.DownloadFileForUi(file, picker.SelectedPath)}", UiTheme.Success));
    }

    private async Task DeleteFile(ListView list)
    {
        if (list.SelectedItems.Count == 0 || list.SelectedItems[0].Tag is not Program.UploadedFile file) return;
        var confirmed = MessageBox.Show(this,
            $"Delete '{file.Name}' from Zoom?\n\nThis cannot be undone and the existing download link will stop working.",
            "Delete file", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (confirmed != DialogResult.Yes) return;

        await Run(async () =>
        {
            SetStatus($"Deleting {file.Name}…", UiTheme.Warning);
            await Program.DeleteFileForUi(file.FileId);
            entries = entries.Where(entry => entry.FileId != file.FileId).ToArray();
            FillLists();
            try { await LoadFiles(); }
            catch (Exception)
            {
                SetStatus($"Deleted {file.Name}. Could not refresh Zoom history; try Refresh later.", UiTheme.Warning);
                return;
            }
            SetStatus($"Deleted {file.Name} from Zoom.", UiTheme.Success);
        });
    }

    private void ShowPage(int index)
    {
        home.Visible = index is 0 or 2;
        files.Visible = index == 1;
        settings.Visible = index == 3;
        for (var i = 0; i < nav.Length; i++) { nav[i].FillColor = i == index ? UiTheme.PrimarySoft : Color.White; nav[i].Invalidate(); }
        home.Controls[0].Text = index == 2 ? "Recent transfers" : "Welcome to Zoom Clipboard";
    }

    private void Place()
    {
        var w = body.ClientSize.Width;
        var h = body.ClientSize.Height - status.Height;
        foreach (var page in new[] { home, files, settings }) page.SetBounds(0, 0, w, h);
        state.SetBounds(22, side.Height - 100, 195, 25);
        identity.SetBounds(22, side.Height - 65, 190, 23);
        email.SetBounds(22, side.Height - 43, 190, 20);
        var left = Math.Max(390, (w - 74) / 2);
        var right = w - left - 72;
        transferCard.SetBounds(28, 115, left, 412);
        recentCard.SetBounds(28, 539, left, Math.Max(135, h - 554));
        explorerCard.SetBounds(44 + left, 115, right, h - 132);
        drop.Width = left - 44;
        foreach (Control c in drop.Controls) if (c is Label) c.Width = drop.Width;
        upload.Left = (drop.Width - upload.Width) / 2;
        progress.Width = left - 44;
        recentList.Width = left - 40;
        recentList.Height = recentCard.Height - 64;
        homeRefresh.Left = right - 145;
        homeList.Width = right - 40;
        homeList.Height = explorerCard.Height - 160;
        homeList.Columns[0].Width = Math.Max(170, homeList.Width - 224);
        recentList.Columns[0].Width = Math.Max(170, recentList.Width - 224);
        homeCopy.Top = homeDownload.Top = homeDelete.Top = explorerCard.Height - 55;
        fullCard.SetBounds(28, 115, w - 56, h - 132);
        fullRefresh.Left = fullCard.Width - 150;
        fullList.Width = fullCard.Width - 40;
        fullList.Height = fullCard.Height - 162;
        fullList.Columns[0].Width = Math.Max(230, fullList.Width - 224);
        fullCopy.Top = fullDownload.Top = fullDelete.Top = fullCard.Height - 57;
        accountCard.Width = (w - 72) / 2;
        channelCard.Left = accountCard.Right + 16;
        channelCard.Width = w - channelCard.Left - 28;
        integrationCard.Width = w - 56;
        connectionPill.Left = signInShortcut.Left = w - 175;
    }

    private void SetSignedOut()
    {
        identity.Text = "Not connected";
        email.Text = "Sign in with Zoom";
        state.Text = "●  Not connected";
        state.ForeColor = UiTheme.Muted;
        connectionPill.SetState("Signed out", UiTheme.Muted, Color.FromArgb(238, 242, 247));
        signInShortcut.Visible = true;
        FillLists();
    }

    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        UpdateButtons();
        try { await action(); }
        catch (Exception ex) { SetStatus(ex.Message, UiTheme.Danger); Error(ex.Message); }
        finally { busy = false; UpdateButtons(); }
    }

    private void UpdateButtons()
    {
        var ready = !busy && Program.IsSignedIn && channels.SelectedItem is Channel;
        upload.Enabled = homeRefresh.Enabled = fullRefresh.Enabled = ready;
        homeCopy.Enabled = homeDownload.Enabled = homeDelete.Enabled = ready && homeList.SelectedItems.Count > 0;
        fullCopy.Enabled = fullDownload.Enabled = fullDelete.Enabled = ready && fullList.SelectedItems.Count > 0;
        login.Enabled = install.Enabled = !busy;
        logout.Enabled = channelRefresh.Enabled = channels.Enabled = !busy && Program.IsSignedIn;
    }

    private void SetStatus(string message, Color color) { status.Text = message; status.ForeColor = color; }
    private void Error(string message) => MessageBox.Show(this, message, "Zoom Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
    private static T At<T>(T c, int x, int y, int w, int h) where T : Control { c.SetBounds(x, y, w, h); return c; }
    private static Label L(string text, float size, bool bold = false, Color? color = null) => new()
    {
        Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = color ?? (bold ? UiTheme.Text : UiTheme.Muted), BackColor = Color.Transparent, AutoSize = false
    };
    private static PremiumButton Primary(string text) => new() { Text = text };
    private static PremiumButton Secondary(string text) => new() { Text = text, FillColor = UiTheme.PrimarySoft, HoverColor = Color.FromArgb(216, 230, 255), TextColor = UiTheme.PrimaryDark };
    private static PremiumButton Danger(string text) => new() { Text = text, FillColor = Color.FromArgb(254, 226, 226), HoverColor = Color.FromArgb(254, 202, 202), TextColor = UiTheme.Danger };
    private static PremiumButton Nav(string text) => new() { Text = text, FillColor = Color.White, HoverColor = UiTheme.PrimarySoft, TextColor = UiTheme.PrimaryDark, AlignLeft = true, Radius = 14 };
    private static ListView MakeList()
    {
        var list = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false,
            BorderStyle = BorderStyle.None, BackColor = Color.White, ForeColor = UiTheme.Text,
            Font = new Font("Segoe UI", 9.5f), HideSelection = false,
            SmallImageList = new ImageList { ImageSize = new Size(1, 30) } };
        list.Columns.Add("Name", 230);
        list.Columns.Add("Size", 85);
        list.Columns.Add("Uploaded", 120);
        return list;
    }
}
