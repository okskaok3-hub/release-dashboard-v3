using System.Drawing;

namespace ZoomClipboard;

internal sealed class OnboardingForm : Form
{
    private readonly Panel content = new() { BackColor = UiTheme.Canvas };
    private readonly Label stepLabel = MakeLabel("STEP 1 OF 3", 9, true, UiTheme.Primary);
    private readonly Label status = MakeLabel("", 9.5f, false, UiTheme.Muted);
    private readonly TextBox clientId = new() { PlaceholderText = "Zoom Public Client ID" };
    private readonly TextBox channelName = new() { PlaceholderText = "Channel name", Text = "Zoom Clipboard" };
    private readonly ComboBox channels = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
    private readonly PremiumButton primary = new() { Width = 230, Height = 46 };
    private readonly PremiumButton secondary = new()
    {
        Width = 180, Height = 42, FillColor = UiTheme.PrimarySoft,
        HoverColor = Color.FromArgb(216, 230, 255), TextColor = UiTheme.PrimaryDark
    };
    private bool busy;
    private int step;

    private sealed record Channel(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    internal event EventHandler? JourneyCompleted;

    internal OnboardingForm()
    {
        Text = "Welcome to Zoom Clipboard";
        ClientSize = new Size(820, 590);
        MinimumSize = MaximumSize = new Size(836, 629);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = UiTheme.Canvas;
        Font = new Font("Segoe UI", 10);
        Icon = SystemIcons.Application;

        var header = new GradientHeader { Dock = DockStyle.Top, Height = 105 };
        var brand = MakeLabel("ZOOM CLIPBOARD", 10, true, Color.FromArgb(225, 233, 255));
        brand.SetBounds(34, 20, 300, 24);
        var heading = MakeLabel("A simpler path between desktops", 22, true, Color.White);
        heading.SetBounds(34, 44, 650, 42);
        header.Controls.AddRange([brand, heading]);

        content.SetBounds(0, 105, 820, 445);
        stepLabel.SetBounds(54, 30, 220, 24);
        status.SetBounds(54, 385, 700, 28);
        content.Controls.AddRange([stepLabel, status]);
        Controls.AddRange([content, header]);

        ShowStep(0);
        Shown += async (_, _) =>
        {
            if (!Program.IsSignedIn) return;
            await Run(async () =>
            {
                await LoadChannels();
                ShowStep(2);
            });
        };
    }

    private void ShowStep(int nextStep)
    {
        step = nextStep;
        primary.Click -= WelcomeNext;
        primary.Click -= SignIn;
        primary.Click -= CreateChannel;
        secondary.Click -= ChooseChannel;
        content.SuspendLayout();
        foreach (Control control in content.Controls.Cast<Control>().ToArray())
            if (!ReferenceEquals(control, stepLabel) && !ReferenceEquals(control, status)) content.Controls.Remove(control);
        status.Text = "";
        stepLabel.Text = $"STEP {step + 1} OF 3";

        if (step == 0) BuildWelcome();
        else if (step == 1) BuildSignIn();
        else BuildChannel();

        content.ResumeLayout(true);
        UpdateButtons();
    }

    private void BuildWelcome()
    {
        var title = MakeLabel("Welcome to Zoom Clipboard", 28, true, UiTheme.Text);
        title.SetBounds(54, 72, 650, 55);
        var copy = MakeLabel("Move files through a private Zoom Team Chat channel—without breaking your flow.", 12, false, UiTheme.Muted);
        copy.SetBounds(56, 135, 680, 55);
        var card = new CardPanel { Location = new Point(54, 212), Size = new Size(710, 112) };
        var journey = MakeLabel("1  Connect Zoom       2  Create a private channel       3  Start transferring", 11, true, UiTheme.PrimaryDark);
        journey.SetBounds(28, 24, 650, 34);
        var note = MakeLabel("You only do this setup once. Your sign-in and channel choice are securely remembered.", 9.5f, false, UiTheme.Muted);
        note.SetBounds(28, 63, 650, 26);
        card.Controls.AddRange([journey, note]);
        primary.Text = "Get started";
        primary.SetBounds(54, 340, 230, 46);
        primary.Click += WelcomeNext;
        content.Controls.AddRange([title, copy, card, primary]);
    }

    private void WelcomeNext(object? sender, EventArgs e)
    {
        ShowStep(1);
    }

    private void BuildSignIn()
    {
        var title = MakeLabel("Connect your Zoom account", 26, true, UiTheme.Text);
        title.SetBounds(54, 72, 650, 52);
        var copy = MakeLabel("Your browser will open for approval. Zoom Clipboard never stores your password.", 11, false, UiTheme.Muted);
        copy.SetBounds(56, 128, 680, 38);
        var card = new CardPanel { Location = new Point(54, 190), Size = new Size(710, 145) };
        card.Controls.Add(LabelAt("Public Client ID", 10, true, UiTheme.Text, 24, 20, 630, 25));
        clientId.Text = Program.SavedClientId;
        clientId.SetBounds(24, 54, 650, 34);
        card.Controls.Add(clientId);
        card.Controls.Add(LabelAt("The included public ID is ready to use; advanced users can replace it.", 8.8f, false, UiTheme.Muted, 24, 96, 650, 24));
        primary.Text = "Sign in with Zoom";
        primary.SetBounds(54, 350, 230, 46);
        primary.Click += SignIn;
        content.Controls.AddRange([title, copy, card, primary]);
    }

    private async void SignIn(object? sender, EventArgs e)
    {
        await Run(async () =>
        {
            status.Text = "Complete sign-in in the browser…";
            await Program.SignInForUi(clientId.Text.Trim());
            await LoadChannels();
            ShowStep(2);
        });
    }

    private void BuildChannel()
    {
        var title = MakeLabel("Set up your private channel", 26, true, UiTheme.Text);
        title.SetBounds(54, 65, 650, 52);
        var copy = MakeLabel("Create a dedicated private channel, or choose one you already use.", 11, false, UiTheme.Muted);
        copy.SetBounds(56, 120, 680, 35);

        var createCard = new CardPanel { Location = new Point(54, 172), Size = new Size(338, 165) };
        createCard.Controls.Add(LabelAt("Create new", 13, true, UiTheme.Text, 22, 17, 280, 30));
        channelName.SetBounds(22, 56, 294, 34);
        createCard.Controls.Add(channelName);
        primary.Text = "Create private channel";
        primary.SetBounds(22, 106, 294, 42);
        primary.Click += CreateChannel;
        createCard.Controls.Add(primary);

        var chooseCard = new CardPanel { Location = new Point(410, 172), Size = new Size(354, 165) };
        chooseCard.Controls.Add(LabelAt("Use existing", 13, true, UiTheme.Text, 22, 17, 290, 30));
        channels.SetBounds(22, 56, 310, 34);
        chooseCard.Controls.Add(channels);
        secondary.Text = "Use selected channel";
        secondary.SetBounds(22, 106, 310, 42);
        secondary.Click += ChooseChannel;
        chooseCard.Controls.Add(secondary);

        content.Controls.AddRange([title, copy, createCard, chooseCard]);
    }

    private async void CreateChannel(object? sender, EventArgs e)
    {
        await Run(async () =>
        {
            status.Text = "Creating your private Zoom channel…";
            await Program.CreateChannelForUi(channelName.Text);
            CompleteJourney();
        });
    }

    private void ChooseChannel(object? sender, EventArgs e)
    {
        if (channels.SelectedItem is not Channel channel)
        {
            status.ForeColor = UiTheme.Danger;
            status.Text = "Choose a channel first.";
            return;
        }
        Program.SaveChannelForUi(channel.Id);
        CompleteJourney();
    }

    private async Task LoadChannels()
    {
        var available = (await Program.GetChannels())
            .Where(c => !c.Id.StartsWith("web_ins_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new Channel(c.Id, c.Name)).ToArray();
        channels.Items.Clear();
        channels.Items.AddRange(available);
        if (available.Length > 0) channels.SelectedIndex = 0;
    }

    private void CompleteJourney()
    {
        status.ForeColor = UiTheme.Success;
        status.Text = "Setup complete. Opening your dashboard…";
        JourneyCompleted?.Invoke(this, EventArgs.Empty);
    }

    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        status.ForeColor = UiTheme.Primary;
        UpdateButtons();
        try { await action(); }
        catch (Exception ex)
        {
            status.ForeColor = UiTheme.Danger;
            status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Zoom Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { busy = false; UpdateButtons(); }
    }

    private void UpdateButtons()
    {
        primary.Enabled = !busy;
        secondary.Enabled = !busy && channels.SelectedItem is Channel;
        clientId.Enabled = channelName.Enabled = channels.Enabled = !busy;
    }

    private static Label MakeLabel(string text, float size, bool bold, Color color) => new()
    {
        Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = color, BackColor = Color.Transparent, AutoSize = false
    };

    private static Label LabelAt(string text, float size, bool bold, Color color, int x, int y, int width, int height)
    {
        var label = MakeLabel(text, size, bold, color);
        label.SetBounds(x, y, width, height);
        return label;
    }
}
