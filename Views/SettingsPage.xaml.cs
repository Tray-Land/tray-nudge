using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TrayNudge.Services;
using Windows.ApplicationModel;
using Windows.UI.ViewManagement;

namespace TrayNudge.Views;

/// <summary>A handful of settings, so they live in the flyout rather than a window.</summary>
public sealed partial class SettingsPage : Page, IRefreshable
{
    private static readonly TimeSpan PauseLength = TimeSpan.FromHours(1);

    private readonly TrayFlyoutWindow _host;
    private bool _loading = true;

    public SettingsPage(TrayFlyoutWindow host)
    {
        _host = host;
        InitializeComponent();
        VersionText.Text = $"{App.DisplayName} {GetVersion()} · Made to help you be a little kinder to yourself, one small step at a time.";
        Loaded += (_, _) => BackButton.Focus(FocusState.Programmatic);
        Refresh();
    }

    public void Refresh()
    {
        _loading = true;
        RemindersToggle.IsOn = SettingsService.RemindersEnabled;
        GentleToggle.IsOn = SettingsService.GentleStreaks;
        CelebrationsToggle.IsOn = SettingsService.Celebrations;
        if (!new UISettings().AnimationsEnabled)
        {
            CelebrationsDescription.Text = "Animation effects are off in Windows, so these stay still.";
        }

        UpdatePause();
        _loading = false;
        _ = LoadStartupStateAsync();
    }

    private void UpdatePause()
    {
        bool paused = App.Current.Reminders?.IsPaused == true;
        PauseCard.Visibility = SettingsService.RemindersEnabled ? Visibility.Visible : Visibility.Collapsed;
        PauseButton.Content = paused ? "Resume" : "Pause 1 hour";
        PauseDescription.Text = paused && SettingsService.RemindersPausedUntil is { } until
            ? $"Paused until {until.ToString("t", CultureInfo.CurrentCulture)}."
            : "Quiet all nudges for an hour, for a meeting or some focus time.";
    }

    private static string GetVersion()
    {
        try
        {
            PackageVersion v = Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            return "(unpackaged)";
        }
    }

    private async Task LoadStartupStateAsync() => ShowStartupState(await StartupService.GetStateAsync());

    private void ShowStartupState(StartupTaskState? state)
    {
        _loading = true;
        StartupToggle.IsOn = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

        // The user (Task Manager, Settings > Apps > Startup) or policy has the final say; the app
        // can't override it, so say where to change it instead of offering a dead toggle.
        StartupToggle.IsEnabled = state is StartupTaskState.Enabled or StartupTaskState.Disabled;
        StartupDescription.Text = state switch
        {
            StartupTaskState.DisabledByUser => "Turned off in Settings > Apps > Startup. Turn it on there.",
            StartupTaskState.DisabledByPolicy or StartupTaskState.EnabledByPolicy => "Managed by your organization.",
            null => "Only available when the app is installed.",
            _ => "So your nudges are ready after you sign in.",
        };
        _loading = false;
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            ShowStartupState(await StartupService.SetEnabledAsync(StartupToggle.IsOn));
        }
    }

    private void RemindersToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        SettingsService.RemindersEnabled = RemindersToggle.IsOn;
        SettingsService.RaiseChanged();
        UpdatePause();
    }

    private void GentleToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            SettingsService.GentleStreaks = GentleToggle.IsOn;
            SettingsService.RaiseChanged();
        }
    }

    private void CelebrationsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            SettingsService.Celebrations = CelebrationsToggle.IsOn;
        }
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.Current.Reminders is not { } reminders)
        {
            return;
        }

        if (reminders.IsPaused)
        {
            reminders.Resume();
        }
        else
        {
            reminders.PauseFor(PauseLength);
        }

        UpdatePause();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => _host.GoBack();
}
