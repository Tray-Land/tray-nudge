using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using TrayNudge.Core;
using TrayNudge.Services;
using TrayNudge.Views;
using WinUIEx;

namespace TrayNudge;

/// <summary>
/// Tray-only application: no window at launch, just a notification-area icon that toggles the
/// flyout. Windows exist only while they're in use; the idle app is an icon, the habit list, and
/// one reminder timer.
/// </summary>
public partial class App : Application
{
    public const string DisplayName = "Tray Nudge";

    private const string MutexName = "Local\\TrayNudge_SingleInstance";
    private const string ShowEventName = "Local\\TrayNudge_ShowFlyout";
    private const int MaxTooltipLength = 127; // NOTIFYICONDATA.szTip limit

    // Startup leaves JIT and first-request state behind that the idle app never touches again.
    private static readonly TimeSpan StartupTrimDelay = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan MenuPauseLength = TimeSpan.FromHours(1);

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private TrayIcon? _trayIcon;
    private TrayFlyoutWindow? _flyout;
    private ReminderService? _reminders;
    private DispatcherQueue? _dispatcher;
    private bool _isExiting;

    public App()
    {
        InitializeComponent();

        // A tray app lives on after its windows close; only the Exit menu ends it.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => LogCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
    }

    public static new App Current => (App)Application.Current;

    /// <summary>Null only before launch finishes.</summary>
    internal ReminderService? Reminders => _reminders;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance owns the tray icon: ask it to open its flyout, then quit.
            try
            {
                // Let the running instance take the foreground when it shows its flyout.
                Windows.Win32.PInvoke.AllowSetForegroundWindow(unchecked((uint)-1)); // ASFW_ANY
                using EventWaitHandle existing = EventWaitHandle.OpenExisting(ShowEventName);
                existing.Set();
            }
            catch
            {
                // The other instance may be shutting down; nothing to do.
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Exit();
            return;
        }

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => _dispatcher.TryEnqueue(ShowFlyout), null, Timeout.Infinite, executeOnlyOnce: false);

        HabitStore.Load();
        InitializeTrayIcon();

        NotificationService.Invoked += (_, e) => _dispatcher.TryEnqueue(() => HandleNudgeAction(e.Action, e.HabitId));
        NotificationService.Register();
        _reminders = new ReminderService(_dispatcher);
        _reminders.Start();

        if (TryGetLaunchNotification() is { } launch)
        {
            // Started by a toast click while the app wasn't running: just do what was asked.
            SettingsService.HasLaunchedBefore = true;
            HandleNudgeAction(launch.Action, launch.HabitId);
            MemoryService.ReleaseIdle(StartupTrimDelay);
        }
        else if (!SettingsService.HasLaunchedBefore)
        {
            // First run: show where the app lives instead of launching into silence.
            SettingsService.HasLaunchedBefore = true;
            ShowFlyout();
        }
        else
        {
            MemoryService.ReleaseIdle(StartupTrimDelay);
        }
    }

    /// <summary>Sets the tray tooltip under the app name, e.g. "2 of 4 done today".</summary>
    public void SetTrayStatus(string? status)
    {
        if (_trayIcon is null)
        {
            return;
        }

        string tooltip = string.IsNullOrWhiteSpace(status) ? DisplayName : $"{DisplayName}\n{status}";
        _trayIcon.Tooltip = tooltip.Length > MaxTooltipLength ? tooltip[..MaxTooltipLength] : tooltip;
    }

    public void ShowFlyout()
    {
        TrayFlyoutWindow flyout = EnsureFlyout();
        flyout.HidePopup();
        flyout.ShowMainPage();
        flyout.ShowPopup();
    }

    /// <summary>Shows the flyout on its settings page.</summary>
    public void ShowSettings() => ShowFlyoutOn(f => f.ShowSettingsPage());

    private void ShowFlyoutOn(Action<TrayFlyoutWindow> navigate)
    {
        TrayFlyoutWindow flyout = EnsureFlyout();
        navigate(flyout);
        if (!flyout.IsPopupVisible)
        {
            flyout.ShowPopup();
        }
    }

    /// <summary>Acts on a toast click: log from the toast, snooze, or open the flyout.</summary>
    private void HandleNudgeAction(NudgeAction action, string? habitId)
    {
        switch (action)
        {
            case NudgeAction.Log when habitId is not null:
                if (HabitStore.Log(habitId, 1) is { } result)
                {
                    ConfirmLoggedFromToast(result);
                }

                break;
            case NudgeAction.Snooze when habitId is not null:
                _reminders?.Snooze(habitId);
                break;
            default:
                ShowFlyout();
                break;
        }
    }

    private static void ConfirmLoggedFromToast(LogResult result)
    {
        Habit habit = result.Habit;
        if (result.BecameMet)
        {
            (string title, string message) = Encouragement.GoalMet(habit, result.Streak, Random.Shared.Next());
            NotificationService.ShowLogged(habit, $"{habit.Emoji} {title} 🎉", message);
        }
        else
        {
            NotificationService.ShowLogged(habit, $"{habit.Emoji} Logged", Encouragement.ProgressText(habit, result.Count) + ". Nice pace.");
        }
    }

    private static (NudgeAction Action, string? HabitId)? TryGetLaunchNotification()
    {
        try
        {
            AppActivationArguments activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activation.Kind == ExtendedActivationKind.AppNotification
                && activation.Data is AppNotificationActivatedEventArgs toast)
            {
                return NotificationService.Parse(toast.Arguments);
            }
        }
        catch
        {
            // Unpackaged or unusual activation: treat as a normal launch.
        }

        return null;
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new TrayIcon(1, TrayIconPath, DisplayName);
        _trayIcon.Selected += (_, _) => EnsureFlyout().Toggle();
        _trayIcon.ContextMenu += TrayIcon_ContextMenu;
        _trayIcon.IsVisible = true;
        WindowPlacementService.SetTrayIcon(_trayIcon);
        SystemThemeService.Changed += (_, _) => _dispatcher?.TryEnqueue(() => _trayIcon?.SetIcon(TrayIconPath));
    }

    // A white glyph disappears on a light taskbar, which follows the Windows theme, not the app theme.
    private static string TrayIconPath => Path.Combine(
        AppContext.BaseDirectory, "Assets", SystemThemeService.IsLight ? "AppIcon-dark.ico" : "AppIcon.ico");

    private void TrayIcon_ContextMenu(TrayIcon sender, TrayIconEventArgs args)
    {
        MenuFlyout menu = new();
        MenuFlyoutItem open = new() { Text = $"Open {DisplayName}", Icon = new FontIcon { Glyph = "" } };
        open.Click += (_, _) => ShowFlyout();
        menu.Items.Add(open);

        MenuFlyoutItem add = new() { Text = "Add a habit", Icon = new FontIcon { Glyph = "" } };
        add.Click += (_, _) => ShowFlyoutOn(f => f.ShowNewHabitPage());
        menu.Items.Add(add);

        if (_reminders is { } reminders && SettingsService.RemindersEnabled && HabitStore.Habits.Count > 0)
        {
            MenuFlyoutItem pause = reminders.IsPaused
                ? new() { Text = "Resume nudges", Icon = new FontIcon { Glyph = "" } }
                : new() { Text = "Pause nudges for an hour", Icon = new FontIcon { Glyph = "" } };
            pause.Click += (_, _) =>
            {
                if (reminders.IsPaused)
                {
                    reminders.Resume();
                }
                else
                {
                    reminders.PauseFor(MenuPauseLength);
                }
            };
            menu.Items.Add(pause);
        }

        MenuFlyoutItem settings = new() { Text = "Settings", Icon = new FontIcon { Glyph = "" } };
        settings.Click += (_, _) => ShowSettings();
        menu.Items.Add(settings);

        MenuFlyoutItem exit = new() { Text = "Exit", Icon = new FontIcon { Glyph = "" } };
        exit.Click += (_, _) => ExitApp();
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(exit);
        args.Flyout = menu;
    }

    private TrayFlyoutWindow EnsureFlyout()
    {
        if (_flyout is null)
        {
            // The flyout closes itself after staying hidden for a while; the next open builds a new one.
            _flyout = new TrayFlyoutWindow();
            _flyout.Closed += (_, _) =>
            {
                _flyout = null;
                ReleaseIdleResourcesIfNoWindows();
            };
        }

        return _flyout;
    }

    private void ReleaseIdleResourcesIfNoWindows()
    {
        if (_flyout is null && !_isExiting)
        {
            MemoryService.ReleaseIdle();
        }
    }

    private void ExitApp()
    {
        _isExiting = true;
        _flyout?.CloseWindow();
        _reminders?.Dispose();
        NotificationService.Unregister();

        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Exit();
    }

    private static void LogCrash(Exception? ex)
    {
        try
        {
            string path = Path.Combine(Path.GetTempPath(), "TrayNudge-crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:O}] {ex}{Environment.NewLine}");
        }
        catch
        {
            // Never throw from the crash logger.
        }
    }
}
