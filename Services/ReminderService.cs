using Microsoft.UI.Dispatching;
using TrayNudge.Core;

namespace TrayNudge.Services;

/// <summary>
/// The app's one piece of background work, and the reason it exists: nudges have to arrive while
/// the flyout is closed. It costs one timer, set for the next due nudge (no polling). The timer is
/// capped at <see cref="MaxSleep"/> so the day rollover, clock changes, and resume from sleep are
/// picked up; <see cref="ReminderPlanner.StaleAfter"/> keeps a long sleep from producing a burst of
/// old nudges. It also keeps the tray tooltip's "2 of 4 today" current.
/// </summary>
internal sealed class ReminderService : IDisposable
{
    private static readonly TimeSpan MaxSleep = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SnoozeLength = TimeSpan.FromMinutes(30);

    private readonly DispatcherQueueTimer _timer;
    private readonly Dictionary<string, DateTime> _snoozes = [];
    private DateTime _lastCheck = DateTime.Now;
    private DateOnly _lastDay = DateOnly.FromDateTime(DateTime.Now);

    public ReminderService(DispatcherQueue dispatcher)
    {
        _timer = dispatcher.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Check();
        HabitStore.Changed += OnDataChanged;
        SettingsService.Changed += OnDataChanged;
    }

    /// <summary>The next nudge to show in the flyout footer, or null.</summary>
    public (Habit Habit, DateTime At)? NextNudge
    {
        get
        {
            if (!IsActive)
            {
                return null;
            }

            DateTime now = DateTime.Now;
            (Habit, DateTime)? next = null;
            foreach (Habit habit in HabitStore.Habits)
            {
                DateTime? at = ReminderPlanner.NextReminder(habit, now);
                if (_snoozes.TryGetValue(habit.Id, out DateTime snooze) && snooze > now && (at is null || snooze < at))
                {
                    at = snooze;
                }

                if (at is { } t && (next is null || t < next.Value.Item2))
                {
                    next = (habit, t);
                }
            }

            return next;
        }
    }

    public bool IsPaused => SettingsService.RemindersPausedUntil is { } until && until > DateTime.Now;

    private static bool IsActive => SettingsService.RemindersEnabled;

    public void Start() => Check();

    public void Snooze(string habitId)
    {
        _snoozes[habitId] = DateTime.Now + SnoozeLength;
        NotificationService.Remove(habitId);
        Schedule();
    }

    public void PauseFor(TimeSpan duration)
    {
        SettingsService.RemindersPausedUntil = DateTime.Now + duration;
        SettingsService.RaiseChanged();
    }

    public void Resume()
    {
        SettingsService.RemindersPausedUntil = null;
        SettingsService.RaiseChanged();
    }

    public void Dispose()
    {
        _timer.Stop();
        HabitStore.Changed -= OnDataChanged;
        SettingsService.Changed -= OnDataChanged;
    }

    private void OnDataChanged(object? sender, EventArgs e)
    {
        UpdateTooltip();
        Schedule();
    }

    private void Check()
    {
        DateTime now = DateTime.Now;
        DateOnly today = DateOnly.FromDateTime(now);
        if (today != _lastDay)
        {
            _lastDay = today;
            _snoozes.Clear();
        }

        if (IsActive && !IsPaused)
        {
            foreach (Habit habit in ReminderPlanner.DueBetween(HabitStore.Habits, _lastCheck, now, _snoozes))
            {
                StreakInfo streak = HabitStore.StreakFor(habit);
                (string title, string message) = Encouragement.Nudge(habit, habit.CountOn(today), streak.Current, Random.Shared.Next());
                NotificationService.ShowNudge(habit, title, message);
            }
        }

        foreach (string id in _snoozes.Where(s => s.Value <= now).Select(s => s.Key).ToList())
        {
            _snoozes.Remove(id);
        }

        _lastCheck = now;
        UpdateTooltip();
        Schedule();
    }

    private void Schedule()
    {
        _timer.Stop();
        DateTime now = DateTime.Now;
        TimeSpan wait = MaxSleep;
        if (IsActive && ReminderPlanner.NextWake(HabitStore.Habits, now, _snoozes) is { } next)
        {
            wait = next - now;
        }

        if (SettingsService.RemindersPausedUntil is { } until && until > now)
        {
            wait = TimeSpan.FromTicks(Math.Max(wait.Ticks, (until - now).Ticks));
        }

        // Wake just after the slot so DueBetween sees it as due, never later than the cap or midnight.
        TimeSpan untilMidnight = DateTime.Today.AddDays(1) - now;
        wait = new[] { wait + TimeSpan.FromSeconds(1), MaxSleep, untilMidnight + TimeSpan.FromSeconds(1) }.Min();
        _timer.Interval = wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait;
        _timer.Start();
    }

    private static void UpdateTooltip()
    {
        (int done, int scheduled) = HabitStore.TodayTotals();
        App.Current.SetTrayStatus(scheduled switch
        {
            0 when HabitStore.Habits.Count == 0 => "Add a habit to get started",
            0 => "Nothing scheduled today",
            _ when done == scheduled => $"All {scheduled} done today 🎉",
            _ => $"{done} of {scheduled} done today",
        });
    }
}
