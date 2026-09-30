using System.Diagnostics;
using System.Drawing;

namespace ZoomClipboard;

internal sealed class UploadForm : Form
{
    private readonly string filePath;
    private readonly Label title = MakeLabel("Preparing upload", 18, FontStyle.Bold, UiTheme.Text);
    private readonly Label fileName = MakeLabel("", 10.5f, FontStyle.Bold, UiTheme.Text);
    private readonly Label fileDetails = MakeLabel("", 9f, FontStyle.Regular, UiTheme.Muted);
    private readonly Label detail = MakeLabel("Connecting securely to Zoom...", 9.5f, FontStyle.Regular, UiTheme.Muted);
    private readonly Label percent = MakeLabel("0%", 9.5f, FontStyle.Bold, UiTheme.Primary);
    private readonly PremiumProgressBar bar = new();
    private readonly PremiumButton retry = new() { Text = "Try again", Width = 110, Visible = false };
    private readonly PremiumButton settings = new()
    {
        Text = "Open settings", Width = 130, FillColor = UiTheme.PrimarySoft,
        HoverColor = Color.FromArgb(216, 227, 255), TextColor = UiTheme.PrimaryDark
    };
    private bool running;

    internal UploadForm(string path)
    {
        filePath = path;
        Text = "Copy to Zoom Clipboard";
        ClientSize = new Size(560, 350);
        MinimumSize = new Size(576, 389);
        MaximumSize = new Size(576, 389);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiTheme.Canvas;
        Font = new Font("Segoe UI", 10);

        var header = new GradientHeader { Dock = DockStyle.Top, Height = 86 };
        var brand = MakeLabel("ZOOM CLIPBOARD", 9f, FontStyle.Bold, Color.FromArgb(224, 232, 255));
        brand.SetBounds(26, 18, 300, 22);
        var heading = MakeLabel("Secure file transfer", 19, FontStyle.Bold, Color.White);
        heading.SetBounds(26, 39, 420, 36);
        header.Controls.AddRange([brand, heading]);
        Controls.Add(header);

        var card = new CardPanel { Location = new Point(24, 106), Size = new Size(512, 220) };
        title.SetBounds(24, 19, 360, 34);
        var icon = MakeLabel("↑", 22, FontStyle.Bold, UiTheme.Primary);
        icon.TextAlign = ContentAlignment.MiddleCenter;
        icon.BackColor = UiTheme.PrimarySoft;
        icon.SetBounds(24, 61, 48, 48);
        fileName.Text = Path.GetFileName(path);
        fileName.SetBounds(88, 62, 385, 26);
        fileDetails.Text = FormatSize(path);
        fileDetails.SetBounds(88, 88, 385, 22);
        detail.SetBounds(24, 124, 395, 26);
        percent.TextAlign = ContentAlignment.MiddleRight;
        percent.SetBounds(420, 124, 65, 26);
        bar.SetBounds(24, 155, 464, 10);
        retry.SetBounds(24, 180, 112, 34);
        settings.SetBounds(356, 180, 132, 34);
        card.Controls.AddRange([title, icon, fileName, fileDetails, detail, percent, bar, retry, settings]);
        Controls.Add(card);

        Shown += async (_, _) => await Upload();
        retry.Click += async (_, _) => await Upload();
        settings.Click += (_, _) =>
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find the application.");
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        };
    }

    private async Task Upload()
    {
        if (running) return;
        running = true;
        retry.Visible = false;
        detail.ForeColor = UiTheme.Muted;
        bar.Value = 0;
        percent.Text = "0%";
        title.Text = "Uploading your file";
        title.ForeColor = UiTheme.Text;
        try
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException("The selected file no longer exists.", filePath);
            if (!Program.IsSignedIn) throw new InvalidOperationException("Sign in from the main Zoom Clipboard window first.");
            if (Program.SavedChannelId is null) throw new InvalidOperationException("Select a Team Chat channel in Zoom Clipboard first.");
            detail.Text = "Sending securely to Zoom...";
            await Program.UploadLinkForUi(filePath, new Progress<int>(value =>
            {
                bar.Value = value;
                percent.Text = $"{value}%";
                detail.Text = value < 100 ? "Sending securely to Zoom..." : "Creating your download link...";
            }));
            bar.Value = 100;
            percent.Text = "100%";
            title.Text = "File copied successfully";
            title.ForeColor = UiTheme.Success;
            detail.Text = "The download link is now on your clipboard.";
            await Task.Delay(1500);
            Close();
        }
        catch (Exception ex)
        {
            title.Text = "Upload needs attention";
            title.ForeColor = UiTheme.Danger;
            detail.Text = ex.Message;
            detail.ForeColor = UiTheme.Danger;
            retry.Visible = true;
        }
        finally { running = false; }
    }

    private static string FormatSize(string path)
    {
        if (!File.Exists(path)) return "File unavailable";
        var bytes = new FileInfo(path).Length;
        return bytes switch
        {
            < 1024 => $"{bytes} bytes",
            < 1024 * 1024 => $"{bytes / 1024d:0.0} KB",
            _ => $"{bytes / 1024d / 1024d:0.0} MB"
        };
    }

    private static Label MakeLabel(string text, float size, FontStyle style, Color color) => new()
    {
        Text = text, Font = new Font("Segoe UI", size, style), ForeColor = color,
        BackColor = Color.Transparent, AutoSize = false
    };
}
