namespace TrayNudge.Core;

/// <summary>
/// Every word the app says about progress. The voice is warm and plain: it celebrates showing up,
/// treats a miss as a pause rather than a failure, and never scolds. Callers pass a
/// <c>pick</c> number (random in the app, fixed in tests) to vary the wording.
/// </summary>
public static class Encouragement
{
    private static readonly string[] Tips =
    [
        "Consistency beats intensity. Small and regular wins.",
        "Missed a day? Never miss twice, and you're right on track.",
        "Tie a new habit to something you already do every day.",
        "Make it easy to start. Two minutes is plenty.",
        "Progress, not perfection.",
        "Every check-in is a vote for the person you're becoming.",
        "Rest days are part of the plan, not a break from it.",
        "Habits take around two months to feel automatic. Be patient with yourself.",
        "Celebrate the small wins. They're the ones that add up.",
        "You don't have to be motivated. You just have to show up.",
    ];

    private static readonly string[] Celebrations =
    [
        "Nicely done.",
        "That's one more step.",
        "You showed up today.",
        "Look at you go.",
        "Another one in the books.",
        "Future you says thanks.",
        "Small step, real change.",
    ];

    private static readonly string[] NudgeOpeners =
    [
        "A gentle nudge",
        "Quick check-in",
        "Whenever you're ready",
        "A little reminder",
        "Your future self called",
    ];

    public static string Greeting(int hour) => hour switch
    {
        < 5 => "Hello, night owl",
        < 12 => "Good morning",
        < 17 => "Good afternoon",
        _ => "Good evening",
    };

    public static string TipOfTheDay(DateOnly today) => Tips[today.DayOfYear % Tips.Length];

    /// <summary>The line under the greeting.</summary>
    public static string TodaySummary(int done, int scheduled)
    {
        if (scheduled == 0)
        {
            return "Nothing scheduled today. Enjoy the breather.";
        }

        if (done == 0)
        {
            return scheduled == 1 ? "One small thing today. You've got this." : $"{scheduled} small things today. Start with any one.";
        }

        if (done >= scheduled)
        {
            return "All done for today. You showed up for yourself.";
        }

        return $"{done} of {scheduled} done. You're on your way.";
    }

    public static string ProgressText(Habit habit, int count)
    {
        int target = Math.Max(1, habit.DailyTarget);
        if (!habit.IsCounted)
        {
            return count >= target ? "Done for today" : "Not yet today";
        }

        string unit = string.IsNullOrWhiteSpace(habit.Unit) ? "" : $" {habit.Unit}";
        return count >= target ? $"{count} of {target}{unit} · goal met" : $"{count} of {target}{unit}";
    }

    /// <summary>Short streak label for a card, or null when there's nothing to show yet.</summary>
    public static string? StreakLabel(StreakInfo streak) => streak.Current switch
    {
        0 => null,
        1 => "1 day",
        _ => $"{streak.Current} in a row",
    };

    /// <summary>
    /// A one-line hint under a habit, chosen for where the person is: a first day, a fresh start,
    /// a streak resting on a forgiven miss, or a rest day.
    /// </summary>
    public static string? CardHint(Habit habit, DateOnly today, StreakInfo streak, bool hasHistory)
    {
        if (habit.IsRestDay(today))
        {
            return "Resting today. Your streak is safe.";
        }

        if (!habit.Days.Includes(today.DayOfWeek))
        {
            return "Day off today.";
        }

        if (habit.IsMetOn(today))
        {
            return null;
        }

        if (streak.IsCarriedByGrace)
        {
            return $"Yesterday was a pause. Today keeps your {streak.Current} going.";
        }

        if (streak.Current == 0 && hasHistory)
        {
            return "Fresh start. Picking it back up is the habit.";
        }

        if (streak.Current == 0 && habit.CreatedOn == today)
        {
            return "Day one. Every streak starts here.";
        }

        return null;
    }

    /// <summary>Title and message for the moment a day's target is reached.</summary>
    public static (string Title, string Message) GoalMet(Habit habit, int streak, int pick)
    {
        string title = $"{habit.Name}: done";
        string message = Milestone(streak) ?? Celebrations[Mod(pick, Celebrations.Length)];
        return (title, message);
    }

    /// <summary>Words for a streak worth pausing on, or null for an ordinary day.</summary>
    public static string? Milestone(int streak) => streak switch
    {
        3 => "Three in a row. A pattern is forming.",
        7 => "A full week. That's real momentum.",
        14 => "Two weeks strong. This is sticking.",
        21 => "Three weeks. It's becoming part of you.",
        30 => "Thirty in a row. Look how far you've come.",
        50 => "Fifty. Steady and strong.",
        66 => "66 in a row: about how long a habit takes to feel automatic.",
        100 => "One hundred. That's who you are now.",
        365 => "A whole year. Remarkable.",
        _ when streak > 100 && streak % 50 == 0 => $"{streak} in a row. Still showing up.",
        _ => null,
    };

    public static bool IsMilestone(int streak) => Milestone(streak) is not null;

    /// <summary>Toast text for a reminder.</summary>
    public static (string Title, string Message) Nudge(Habit habit, int count, int streak, int pick)
    {
        string title = $"{habit.Emoji} {NudgeOpeners[Mod(pick, NudgeOpeners.Length)]}: {habit.Name}";
        string message;
        if (habit.IsCounted && count > 0)
        {
            int left = Math.Max(1, habit.DailyTarget) - count;
            message = left == 1
                ? $"{count} of {habit.DailyTarget} so far. Just one more to go."
                : $"{count} of {habit.DailyTarget} so far. Nice pace.";
        }
        else if (streak > 1)
        {
            message = $"Your {streak}-day streak is waiting for you.";
        }
        else
        {
            message = "A small step now is all it takes.";
        }

        return (title, message);
    }

    public static string AllDoneCelebration(int pick) => Mod(pick, 3) switch
    {
        0 => "Everything's done today. Take a moment to feel good about that.",
        1 => "Full house today. You kept every promise to yourself.",
        _ => "All habits done. That's what consistency looks like.",
    };

    public static string ConsistencyText(Consistency c) => c.Percent switch
    {
        null => "Your story starts today.",
        >= 90 => $"{c.Met} of the last {c.Scheduled} days. Wonderfully steady.",
        >= 70 => $"{c.Met} of the last {c.Scheduled} days. Strong and steady.",
        >= 40 => $"{c.Met} of the last {c.Scheduled} days. Building nicely.",
        _ => $"{c.Met} of the last {c.Scheduled} days. Every one counts.",
    };

    private static int Mod(int value, int length) => ((value % length) + length) % length;
}
