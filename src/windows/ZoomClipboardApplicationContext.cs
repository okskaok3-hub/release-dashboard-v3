namespace ZoomClipboard;

internal sealed class ZoomClipboardApplicationContext : ApplicationContext
{
    private Form? activeForm;

    internal ZoomClipboardApplicationContext()
    {
        if (Program.IsOnboardingComplete) ShowDashboard();
        else ShowOnboarding();
    }

    private void ShowOnboarding()
    {
        var form = new OnboardingForm();
        activeForm = form;
        form.JourneyCompleted += (_, _) =>
        {
            form.Hide();
            ShowDashboard();
            form.Dispose();
        };
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(activeForm, form)) ExitThread();
        };
        form.Show();
    }

    private void ShowDashboard()
    {
        var form = new DashboardForm();
        activeForm = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(activeForm, form)) ExitThread();
        };
        form.Show();
    }
}
