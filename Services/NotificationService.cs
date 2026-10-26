using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using TrayNudge.Core;

namespace TrayNudge.Services;

/// <summary>What a toast click asked for.</summary>
public enum NudgeAction
{
    Open,
    Log,
    Snooze,
}

/// <summary>
/// Reminder and celebration toasts. Each habit has its own tag, so a new nudge for it replaces the
/// last one instead of stacking up in Action Center, and logging the habit clears it.
/// </summary>
internal static class NotificationService
{
    private const string ActionKey = "action";
    private const string HabitKey = "habit";
    private const string Group = "nudges";
    private const string DigestTag = "digest";

    private static bool _registered;

    /// <summary>Raised on a background thread when a toast or one of its buttons is clicked.</summary>
    public static event EventHandler<(NudgeAction Action, string? HabitId)>? Invoked;

    public static void Register()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            // Nudges are the heart of the app, but the flyout still works without them.
            System.Diagnostics.Debug.WriteLine($"App notifications unavailable: {ex.Message}");
        }
    }

    public static void Unregister()
    {
        if (_registered)
        {
            AppNotificationManager.Default.Unregister();
            _registered = false;
        }
    }

    /// <summary>Reads the action from a toast that launched the app (it wasn't running).</summary>
    public static (NudgeAction Action, string? HabitId) Parse(IDictionary<string, string> arguments)
    {
        arguments.TryGetValue(HabitKey, out string? habitId);
        NudgeAction action = arguments.TryGetValue(ActionKey, out string? value) && Enum.TryParse(value, out NudgeAction parsed)
            ? parsed
            : NudgeAction.Open;
        return (action, habitId);
    }

    public static void ShowNudge(Habit habit, string title, string message)
    {
        if (!_registered)
        {
            return;
        }

        string logLabel = habit.IsCounted ? "Log one" : "Done";
        AppNotification notification = new AppNotificationBuilder()
            .AddArgument(ActionKey, nameof(NudgeAction.Open))
            .AddArgument(HabitKey, habit.Id)
            .AddText(title)
            .AddText(message)
            .AddButton(new AppNotificationButton(logLabel)
                .AddArgument(ActionKey, nameof(NudgeAction.Log))
                .AddArgument(HabitKey, habit.Id))
            .AddButton(new AppNotificationButton("Later")
                .AddArgument(ActionKey, nameof(NudgeAction.Snooze))
                .AddArgument(HabitKey, habit.Id))
            .BuildNotification();
        Show(notification, habit.Id);
    }

    /// <summary>
    /// One toast standing in for several habits due together. It replaces the per-habit toasts, and
    /// tapping it opens the flyout where each can be logged.
    /// </summary>
    public static void ShowDigest(IReadOnlyList<Habit> habits, string title, string message)
    {
        if (!_registered)
        {
            return;
        }

        foreach (Habit habit in habits)
        {
            Remove(habit.Id);
        }

        AppNotification notification = new AppNotificationBuilder()
            .AddArgument(ActionKey, nameof(NudgeAction.Open))
            .AddText(title)
            .AddText(message)
            .BuildNotification();
        Show(notification, DigestTag);
    }

    /// <summary>A quiet confirmation after logging from a toast, replacing the nudge.</summary>
    public static void ShowLogged(Habit habit, string title, string message)
    {
        if (!_registered)
        {
            return;
        }

        AppNotification notification = new AppNotificationBuilder()
            .AddArgument(ActionKey, nameof(NudgeAction.Open))
            .AddArgument(HabitKey, habit.Id)
            .AddText(title)
            .AddText(message)
            .MuteAudio()
            .BuildNotification();
        Show(notification, habit.Id);
    }

    public static void Remove(string habitId)
    {
        if (_registered)
        {
            _ = AppNotificationManager.Default.RemoveByTagAndGroupAsync(habitId, Group);
        }
    }

    private static void Show(AppNotification notification, string habitId)
    {
        try
        {
            notification.Tag = habitId;
            notification.Group = Group;
            notification.ExpiresOnReboot = true;
            notification.Expiration = DateTimeOffset.Now.AddHours(3);
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Toast failed: {ex.Message}");
        }
    }

    private static void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) =>
        Invoked?.Invoke(null, Parse(args.Arguments));
}
