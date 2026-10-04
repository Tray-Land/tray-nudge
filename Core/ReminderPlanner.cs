namespace TrayNudge.Core;

/// <summary>
/// Works out when each habit should nudge next, so the app can sleep on a single timer until then
/// instead of polling. Nudges never fire on unscheduled or rest days, or once today's target is met.
/// Interval nudges also skip a slot when the person logged within the last half interval: they're
/// already on it.
/// </summary>
public static class ReminderPlanner
{
    public const int MinIntervalMinutes = 15;
    public const int MaxIntervalMinutes = 8 * 60;

    /// <summary>A nudge that comes due while the PC slept is only shown if it's at most this old.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    private const int LookaheadDays = 8;

    /// <summary>The habit's reminder slots on any scheduled day, in minutes after midnight, ascending.</summary>
    public static IReadOnlyList<int> SlotsFor(Habit habit)
    {
        switch (habit.Reminder)
        {
            case ReminderMode.Interval:
                int interval = Math.Clamp(habit.IntervalMinutes, MinIntervalMinutes, MaxIntervalMinutes);
                int start = Math.Clamp(habit.WindowStartMinutes, 0, 1439);
                int end = Math.Clamp(habit.WindowEndMinutes, start, 1439);
                List<int> slots = [];
                for (int m = start; m <= end; m += interval)
                {
                    slots.Add(m);
                }

                return slots;

            case ReminderMode.Times:
                return habit.ReminderTimes.Select(t => Math.Clamp(t, 0, 1439)).Distinct().Order().ToList();

            default:
                return [];
        }
    }

    /// <summary>The first nudge strictly after <paramref name="after"/>, or null if there's none soon.</summary>
    public static DateTime? NextReminder(Habit habit, DateTime after)
    {
        IReadOnlyList<int> slots = SlotsFor(habit);
        if (slots.Count == 0)
        {
            return null;
        }

        DateOnly firstDay = DateOnly.FromDateTime(after);
        for (int offset = 0; offset < LookaheadDays; offset++)
        {
            DateOnly day = firstDay.AddDays(offset);
            if (!habit.IsScheduledOn(day) || habit.IsMetOn(day))
            {
                continue;
            }

            foreach (int minutes in slots)
            {
                DateTime slot = day.ToDateTime(TimeOnly.MinValue).AddMinutes(minutes);
                if (slot <= after || LoggedRecently(habit, slot))
                {
                    continue;
                }

                return slot;
            }
        }

        return null;
    }

    /// <summary>
    /// Habits with a nudge in (<paramref name="lastCheck"/>, <paramref name="now"/>] that isn't
    /// stale. Snoozes count as one-off slots.
    /// </summary>
    public static IReadOnlyList<Habit> DueBetween(
        IEnumerable<Habit> habits, DateTime lastCheck, DateTime now, IReadOnlyDictionary<string, DateTime>? snoozes = null)
    {
        List<Habit> due = [];
        foreach (Habit habit in habits)
        {
            DateOnly today = DateOnly.FromDateTime(now);
            if (habit.IsMetOn(today))
            {
                continue;
            }

            DateTime? slot = NextReminder(habit, lastCheck);
            bool slotDue = slot is { } s && s <= now && now - s <= StaleAfter;
            bool snoozeDue = snoozes is not null
                && snoozes.TryGetValue(habit.Id, out DateTime until)
                && until > lastCheck && until <= now && now - until <= StaleAfter;

            if (slotDue || snoozeDue)
            {
                due.Add(habit);
            }
        }

        return due;
    }

    /// <summary>The next moment anything needs attention (a slot or a snooze), or null.</summary>
    public static DateTime? NextWake(
        IEnumerable<Habit> habits, DateTime after, IReadOnlyDictionary<string, DateTime>? snoozes = null)
    {
        DateTime? next = null;
        foreach (Habit habit in habits)
        {
            next = Earliest(next, NextReminder(habit, after));
        }

        if (snoozes is not null)
        {
            foreach (DateTime until in snoozes.Values)
            {
                if (until > after)
                {
                    next = Earliest(next, until);
                }
            }
        }

        return next;
    }

    private static bool LoggedRecently(Habit habit, DateTime slot)
    {
        if (habit.Reminder != ReminderMode.Interval || habit.LastLoggedAt is not { } logged)
        {
            return false;
        }

        double halfInterval = Math.Clamp(habit.IntervalMinutes, MinIntervalMinutes, MaxIntervalMinutes) / 2.0;
        return logged <= slot && slot - logged < TimeSpan.FromMinutes(halfInterval);
    }

    private static DateTime? Earliest(DateTime? a, DateTime? b) =>
        a is null ? b : b is null ? a : a < b ? a : b;
}
