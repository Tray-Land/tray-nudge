namespace TrayNudge.Core;

/// <summary>How a single day looks for a habit, for week strips and the history grid.</summary>
public enum DayStatus
{
    /// <summary>Before the habit existed, or a day it isn't scheduled on.</summary>
    Off,

    /// <summary>A day the person chose to rest.</summary>
    Rest,

    /// <summary>Scheduled, nothing logged, and the day is over.</summary>
    Missed,

    /// <summary>Some progress, short of the target.</summary>
    Partial,

    /// <summary>Target reached.</summary>
    Met,

    /// <summary>Today, scheduled, not yet met: still in play, never a miss.</summary>
    Open,
}

public readonly record struct StreakInfo(int Current, int Best, bool IsCarriedByGrace);

public readonly record struct Consistency(int Met, int Scheduled)
{
    /// <summary>0..1, or null when nothing has been scheduled yet.</summary>
    public double? Ratio => Scheduled == 0 ? null : (double)Met / Scheduled;

    public int? Percent => Ratio is { } r ? (int)Math.Round(r * 100) : null;
}

/// <summary>
/// Streaks count scheduled days in a row where the target was met. Unscheduled days and rest days
/// are skipped, never counted against anyone. Today only adds to a streak; until midnight it can't
/// break one. With <c>gentle</c> on ("never miss twice"), one missed day is forgiven as long as the
/// next scheduled day is met: the streak carries over it without counting it.
/// </summary>
public static class StreakCalculator
{
    private const int MaxLookbackDays = 3660;

    public static DayStatus StatusOn(Habit habit, DateOnly day, DateOnly today)
    {
        if (day < habit.CreatedOn || day > today)
        {
            return DayStatus.Off;
        }

        if (habit.IsRestDay(day))
        {
            return DayStatus.Rest;
        }

        if (!habit.Days.Includes(day.DayOfWeek))
        {
            // Doing it on a day off still shows, it just isn't asked for.
            return habit.IsMetOn(day) ? DayStatus.Met : DayStatus.Off;
        }

        int count = habit.CountOn(day);
        if (count >= Math.Max(1, habit.DailyTarget))
        {
            return DayStatus.Met;
        }

        if (day == today)
        {
            return DayStatus.Open;
        }

        return count > 0 ? DayStatus.Partial : DayStatus.Missed;
    }

    public static StreakInfo Calculate(Habit habit, DateOnly today, bool gentle)
    {
        (int current, bool grace) = CurrentStreak(habit, today, gentle);
        int best = Math.Max(current, BestStreak(habit, today, gentle));
        return new StreakInfo(current, best, grace);
    }

    /// <summary>
    /// Walks back from today. Returns the streak and whether it's currently resting on a forgiven
    /// miss (so the UI can say "today keeps it going").
    /// </summary>
    public static (int Streak, bool CarriedByGrace) CurrentStreak(Habit habit, DateOnly today, bool gentle)
    {
        int streak = 0;
        int pendingMisses = 0;
        bool graceUsedAtHead = false;
        bool seenMet = false;

        DateOnly stop = Max(habit.CreatedOn, today.AddDays(-MaxLookbackDays));
        for (DateOnly day = today; day >= stop; day = day.AddDays(-1))
        {
            if (!habit.IsScheduledOn(day))
            {
                continue;
            }

            if (habit.IsMetOn(day))
            {
                streak++;
                pendingMisses = 0;
                seenMet = true;
                continue;
            }

            if (day == today)
            {
                continue; // Still in play.
            }

            pendingMisses++;
            if (!gentle || pendingMisses >= 2)
            {
                break;
            }

            if (!seenMet)
            {
                graceUsedAtHead = true;
            }
        }

        return (streak, graceUsedAtHead && streak > 0);
    }

    /// <summary>The longest streak ever, by the same rules, through yesterday plus today if met.</summary>
    public static int BestStreak(Habit habit, DateOnly today, bool gentle)
    {
        int best = 0;
        int run = 0;
        int misses = 0;

        DateOnly start = Max(habit.CreatedOn, today.AddDays(-MaxLookbackDays));
        for (DateOnly day = start; day <= today; day = day.AddDays(1))
        {
            if (!habit.IsScheduledOn(day))
            {
                continue;
            }

            if (habit.IsMetOn(day))
            {
                run++;
                misses = 0;
                best = Math.Max(best, run);
            }
            else if (day != today)
            {
                misses++;
                if (!gentle || misses >= 2)
                {
                    run = 0;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Met days out of scheduled days over the last <paramref name="days"/> days. Today counts only
    /// once it's met, so an unfinished morning never drags the number down.
    /// </summary>
    public static Consistency ConsistencyOver(Habit habit, DateOnly today, int days = 30)
    {
        int met = 0;
        int scheduled = 0;
        for (DateOnly day = today.AddDays(-(days - 1)); day <= today; day = day.AddDays(1))
        {
            if (!habit.IsScheduledOn(day))
            {
                continue;
            }

            bool isMet = habit.IsMetOn(day);
            if (day == today && !isMet)
            {
                continue;
            }

            scheduled++;
            if (isMet)
            {
                met++;
            }
        }

        return new Consistency(met, scheduled);
    }

    /// <summary>True if the habit was ever met before today (to tell a fresh start from a first day).</summary>
    public static bool HasHistory(Habit habit, DateOnly today) =>
        habit.Progress.Keys.Any(k => string.CompareOrdinal(k, Habit.DayKey(today)) < 0);

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}
