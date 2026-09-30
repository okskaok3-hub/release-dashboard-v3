using System.Drawing;

namespace ZoomClipboard;

internal sealed class MainForm : Form
{
    private readonly StatusPill connectionPill = new();
    private readonly Label profileName = MakeLabel("Not connected", 14, FontStyle.Bold, UiTheme.Text);
    private readonly Label profileEmail = MakeLabel("Sign in to connect your Zoom account", 9.5f, FontStyle.Regular, UiTheme.Muted);
    private readonly Label avatar = MakeLabel("?", 20, FontStyle.Bold, Color.White);
    private readonly TextBox clientId = new();
    private readonly LinkLabel advanced = new();
    private readonly PremiumButton login = PrimaryButton("Sign in with Zoom");
    private readonly PremiumButton logout = SecondaryButton("Sign out");
    private readonly ComboBox channels = new();
    private readonly PremiumButton refresh = SecondaryButton("Refresh");
    private readonly PremiumButton upload = PrimaryButton("Upload file & copy link");
    private readonly PremiumButton download = SecondaryButton("Download latest file");
    private readonly PremiumButton install = SecondaryButton("Enable right-click action");
    private readonly Label explorerState = MakeLabel("Checking Explorer integration...", 9.5f, FontStyle.Regular, UiTheme.Muted);
    private readonly Label status = MakeLabel("Ready", 9.5f, FontStyle.Regular, UiTheme.Muted);
    private readonly PremiumProgressBar progressBar = new();
    private readonly Panel scopeBanner = new();
    private readonly Label scopeText = MakeLabel("Profile permission updated. Sign out and sign in once to show your name and email.", 9f, FontStyle.Regular, UiTheme.Warning);
    private bool busy;

    private sealed record ChannelItem(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    internal MainForm()
    {
        SuspendLayout();
        Text = "Zoom Clipboard";
        ClientSize = new Size(900, 680);
        MinimumSize = new Size(916, 719);
        MaximumSize = new Size(916, 719);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = UiTheme.Canvas;
        Font = new Font("Segoe UI", 10f);
        Icon = SystemIcons.Application;

        BuildHeader();
        BuildAccountCard();
        BuildChannelCard();
        BuildActionsCard();
        BuildIntegrationCard();
        BuildFooter();
        WireEvents();
        ResumeLayout(false);

        Shown += async (_, _) =>
        {
            UpdateButtons();
            UpdateExplorerState();
            if (Program.IsSignedIn) await Run(RefreshAccountAndChannels);
            else SetSignedOut();
        };
    }

    private void BuildHeader()
    {
        var header = new GradientHeader { Dock = DockStyle.Top, Height = 132 };
        var mark = new Label
        {
            Text = "ZC", ForeColor = UiTheme.Primary, BackColor = Color.White,
            Font = new Font("Segoe UI", 16, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter
        };
        mark.SetBounds(34, 32, 54, 54);
        var title = MakeLabel("Zoom Clipboard", 24, FontStyle.Bold, Color.White);
        title.SetBounds(108, 27, 430, 42);
        var subtitle = MakeLabel("A secure bridge for moving files into restricted desktops", 10.5f, FontStyle.Regular, Color.FromArgb(224, 232, 255));
        subtitle.SetBounds(110, 70, 520, 28);
        connectionPill.SetBounds(730, 42, 136, 36);
        connectionPill.SetState("Signed out", Color.FromArgb(238, 242, 255), Color.FromArgb(35, Color.White));
        header.Controls.AddRange([mark, title, subtitle, connectionPill]);
        Controls.Add(header);
    }

    private void BuildAccountCard()
    {
        var card = new CardPanel { Location = new Point(28, 156), Size = new Size(412, 212) };
        var eyebrow = MakeLabel("ZOOM ACCOUNT", 8.5f, FontStyle.Bold, UiTheme.Muted);
        eyebrow.SetBounds(24, 19, 190, 20);
        avatar.TextAlign = ContentAlignment.MiddleCenter;
        avatar.BackColor = UiTheme.Primary;
        avatar.SetBounds(24, 52, 54, 54);
        profileName.SetBounds(94, 49, 280, 28);
        profileEmail.SetBounds(94, 78, 282, 24);

        login.SetBounds(24, 126, 166, 40);
        logout.SetBounds(200, 126, 96, 40);
        advanced.Text = "App ID settings";
        advanced.LinkColor = UiTheme.Primary;
        advanced.ActiveLinkColor = UiTheme.PrimaryDark;
        advanced.SetBounds(305, 136, 84, 24);

        clientId.Text = Program.SavedClientId;
        clientId.PlaceholderText = "Public Client ID";
        clientId.BorderStyle = BorderStyle.FixedSingle;
        clientId.Visible = false;
        clientId.SetBounds(24, 174, 365, 26);
        card.Controls.AddRange([eyebrow, avatar, profileName, profileEmail, login, logout, advanced, clientId]);
        Controls.Add(card);
    }

    private void BuildChannelCard()
    {
        var card = new CardPanel { Location = new Point(460, 156), Size = new Size(412, 212) };
        var eyebrow = MakeLabel("DESTINATION", 8.5f, FontStyle.Bold, UiTheme.Muted);
        eyebrow.SetBounds(24, 19, 180, 20);
        var title = MakeLabel("Private Team Chat channel", 14, FontStyle.Bold, UiTheme.Text);
        title.SetBounds(24, 48, 330, 30);
        var hint = MakeLabel("Files are shared only with members of this channel.", 9.25f, FontStyle.Regular, UiTheme.Muted);
        hint.SetBounds(24, 78, 360, 24);
        channels.DropDownStyle = ComboBoxStyle.DropDownList;
        channels.FlatStyle = FlatStyle.Flat;
        channels.Font = new Font("Segoe UI", 10f);
        channels.SetBounds(24, 119, 262, 34);
        refresh.SetBounds(296, 117, 92, 38);
        var privacy = MakeLabel("Encrypted sign-in  •  Private destination recommended", 8.75f, FontStyle.Regular, UiTheme.Success);
        privacy.SetBounds(24, 170, 360, 22);
        card.Controls.AddRange([eyebrow, title, hint, channels, refresh, privacy]);
        Controls.Add(card);
    }

    private void BuildActionsCard()
    {
        var card = new CardPanel { Location = new Point(28, 388), Size = new Size(548, 174) };
        var title = MakeLabel("Move a file", 15, FontStyle.Bold, UiTheme.Text);
        title.SetBounds(24, 20, 260, 30);
        var hint = MakeLabel("Upload to Zoom and paste the generated link inside Citrix.", 9.5f, FontStyle.Regular, UiTheme.Muted);
        hint.SetBounds(24, 51, 475, 24);
        upload.SetBounds(24, 91, 238, 48);
        download.SetBounds(274, 91, 238, 48);
        progressBar.SetBounds(24, 151, 488, 9);
        card.Controls.AddRange([title, hint, upload, download, progressBar]);
        Controls.Add(card);
    }

    private void BuildIntegrationCard()
    {
        var card = new CardPanel { Location = new Point(596, 388), Size = new Size(276, 174) };
        var title = MakeLabel("File Explorer", 15, FontStyle.Bold, UiTheme.Text);
        title.SetBounds(22, 20, 220, 30);
        explorerState.SetBounds(22, 55, 230, 42);
        install.SetBounds(22, 106, 230, 42);
        card.Controls.AddRange([title, explorerState, install]);
        Controls.Add(card);
    }

    private void BuildFooter()
    {
        scopeBanner.BackColor = Color.FromArgb(255, 247, 230);
        scopeBanner.Visible = false;
        scopeBanner.SetBounds(28, 578, 844, 38);
        scopeText.SetBounds(14, 8, 816, 22);
        scopeBanner.Controls.Add(scopeText);
        Controls.Add(scopeBanner);
        status.SetBounds(38, 628, 824, 28);
        Controls.Add(status);
    }

    private void WireEvents()
    {
        advanced.Click += (_, _) =>
        {
            clientId.Visible = !clientId.Visible;
            advanced.Text = clientId.Visible ? "Hide settings" : "App ID settings";
        };
        login.Click += async (_, _) => await Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(clientId.Text)) throw new InvalidOperationException("Enter the Public Client ID first.");
            SetStatus("Complete Zoom authorization in your browser...", UiTheme.Primary);
            await Program.SignInForUi(clientId.Text.Trim());
            await RefreshAccountAndChannels();
        });
        logout.Click += (_, _) =>
        {
            Program.SignOutForUi();
            channels.Items.Clear();
            SetSignedOut();
            SetStatus(Program.HasTokenOverride
                ? "Saved login removed. The environment token override is still active."
                : "Signed out safely on this computer.", UiTheme.Muted);
            UpdateButtons();
        };
        refresh.Click += async (_, _) => await Run(RefreshAccountAndChannels);
        install.Click += (_, _) =>
        {
            try
            {
                Program.InstallContextForUi();
                UpdateExplorerState();
                SetStatus("Explorer action enabled. Right-click a file and choose Copy to Zoom Clipboard.", UiTheme.Success);
            }
            catch (Exception ex) { ShowError(ex.Message); }
        };
        channels.SelectedIndexChanged += (_, _) =>
        {
            if (channels.SelectedItem is ChannelItem item)
            {
                Program.SaveChannelForUi(item.Id);
                SetStatus($"Files will be sent to {item.Name}.", UiTheme.Muted);
            }
            UpdateButtons();
        };
        upload.Click += async (_, _) =>
        {
            using var picker = new OpenFileDialog { Title = "Choose a file to send to Zoom" };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            await Run(async () =>
            {
                progressBar.Value = 0;
                SetStatus($"Uploading {Path.GetFileName(picker.FileName)}...", UiTheme.Primary);
                await Program.UploadLinkForUi(picker.FileName, new Progress<int>(value => progressBar.Value = value));
                progressBar.Value = 100;
                SetStatus("Upload complete — the download link is on your clipboard.", UiTheme.Success);
            });
        };
        download.Click += async (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = "Choose a folder for the latest Zoom file" };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            await Run(async () =>
            {
                SetStatus("Downloading the latest file...", UiTheme.Primary);
                await Program.DownloadLatestForUi(picker.SelectedPath);
                SetStatus($"Downloaded to {picker.SelectedPath}", UiTheme.Success);
            });
        };
    }

    private async Task RefreshAccountAndChannels()
    {
        SetStatus("Connecting securely to Zoom...", UiTheme.Primary);
        connectionPill.SetState("Connecting", Color.White, Color.FromArgb(38, Color.White));
        var selectedId = Program.SavedChannelId;
        var items = (await Program.GetChannels())
            .Where(c => !c.Id.StartsWith("web_ins_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new ChannelItem(c.Id, c.Name)).ToArray();
        channels.Items.Clear();
        channels.Items.AddRange(items);
        var match = Array.FindIndex(items, c => c.Id == selectedId);
        if (match >= 0) channels.SelectedIndex = match;
        else if (items.Length == 1) channels.SelectedIndex = 0;

        var profile = await Program.GetUserProfile();
        if (profile is null)
        {
            profileName.Text = "Connected to Zoom";
            profileEmail.Text = "Reauthorize once to load your profile";
            avatar.Text = "Z";
            scopeBanner.Visible = true;
            login.Text = "Authorize profile access";
        }
        else
        {
            profileName.Text = profile.Value.Name;
            profileEmail.Text = string.IsNullOrWhiteSpace(profile.Value.Email) ? "Zoom account connected" : profile.Value.Email;
            avatar.Text = Initials(profile.Value.Name);
            scopeBanner.Visible = false;
            login.Text = "Reconnect Zoom";
        }
        connectionPill.SetState("Connected", UiTheme.Success, UiTheme.SuccessSoft);
        SetStatus(items.Length == 0 ? "No private channels found. Create one in Zoom Team Chat." :
            channels.SelectedItem is ChannelItem item ? $"Ready — destination is {item.Name}." : "Choose a destination channel.",
            items.Length == 0 ? UiTheme.Warning : UiTheme.Muted);
    }

    private void SetSignedOut()
    {
        profileName.Text = "Not connected";
        profileEmail.Text = "Sign in to connect your Zoom account";
        avatar.Text = "?";
        scopeBanner.Visible = false;
        login.Text = "Sign in with Zoom";
        connectionPill.SetState("Signed out", Color.FromArgb(238, 242, 255), Color.FromArgb(35, Color.White));
    }

    private void UpdateExplorerState()
    {
        var enabled = Program.IsContextInstalled;
        explorerState.Text = enabled ? "Ready\nRight-click any file to upload" : "Not enabled\nAdd the action to File Explorer";
        explorerState.ForeColor = enabled ? UiTheme.Success : UiTheme.Muted;
        install.Text = enabled ? "Reinstall right-click action" : "Enable right-click action";
    }

    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        UpdateButtons();
        try { await action(); }
        catch (Exception ex)
        {
            connectionPill.SetState("Attention", UiTheme.Danger, Color.FromArgb(254, 235, 235));
            SetStatus(ex.Message, UiTheme.Danger);
            ShowError(ex.Message);
        }
        finally { busy = false; UpdateButtons(); }
    }

    private void UpdateButtons()
    {
        login.Enabled = !busy;
        logout.Enabled = !busy && Program.IsSignedIn;
        refresh.Enabled = !busy && Program.IsSignedIn;
        channels.Enabled = !busy && Program.IsSignedIn;
        upload.Enabled = !busy && Program.IsSignedIn && channels.SelectedItem is ChannelItem;
        download.Enabled = upload.Enabled;
        install.Enabled = !busy;
    }

    private void SetStatus(string text, Color color) { status.Text = text; status.ForeColor = color; }
    private void ShowError(string message) => MessageBox.Show(this, message, "Zoom Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }

    private static Label MakeLabel(string text, float size, FontStyle style, Color color) => new()
    {
        Text = text, Font = new Font("Segoe UI", size, style), ForeColor = color,
        BackColor = Color.Transparent, AutoSize = false
    };

    private static PremiumButton PrimaryButton(string text) => new() { Text = text };
    private static PremiumButton SecondaryButton(string text) => new()
    {
        Text = text, FillColor = UiTheme.PrimarySoft, HoverColor = Color.FromArgb(216, 227, 255), TextColor = UiTheme.PrimaryDark
    };
}
