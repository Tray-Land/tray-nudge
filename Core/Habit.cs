using System.Globalization;
using System.Text.Json.Serialization;

namespace TrayNudge.Core;

/// <summary>Days of the week a habit is scheduled on.</summary>
[Flags]
public enum ScheduleDays
{
    None = 0,
    Sunday = 1 << 0,
    Monday = 1 << 1,
    Tuesday = 1 << 2,
    Wednesday = 1 << 3,
    Thursday = 1 << 4,
    Friday = 1 << 5,
    Saturday = 1 << 6,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekends = Saturday | Sunday,
    Every = Weekdays | Weekends,
}

public enum ReminderMode
{
    /// <summary>No nudges; the habit only lives in the flyout.</summary>
    Off = 0,

    /// <summary>Every <see cref="Habit.IntervalMinutes"/> between the window's start and end.</summary>
    Interval = 1,

    /// <summary>At each time in <see cref="Habit.ReminderTimes"/>.</summary>
    Times = 2,

    /// <summary>
    /// At <see cref="Habit.MinutePastHour"/> past every <see cref="Habit.EveryHours"/> hours within
    /// the window (9:50, 10:50, 11:50…).
    /// </summary>
    Hourly = 3,
}

/// <summary>
/// A recurring habit: something done a target number of times on each scheduled day, with
/// optional reminders. Progress is a count per calendar day.
/// </summary>
public sealed class Habit
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    public string Emoji { get; set; } = "✨";

    /// <summary>How many times a day counts as done (8 glasses, 1 journal entry).</summary>
    public int DailyTarget { get; set; } = 1;

    /// <summary>Plural noun for the count ("glasses"); blank for simple yes/no habits.</summary>
    public string Unit { get; set; } = "";

    public int ColorIndex { get; set; }

    public ScheduleDays Days { get; set; } = ScheduleDays.Every;

    public ReminderMode Reminder { get; set; } = ReminderMode.Off;

    public int IntervalMinutes { get; set; } = 60;

    /// <summary>Minute of the hour to nudge at, for <see cref="ReminderMode.Hourly"/>.</summary>
    public int MinutePastHour { get; set; }

    /// <summary>Hours between nudges, for <see cref="ReminderMode.Hourly"/>.</summary>
    public int EveryHours { get; set; } = 1;

    /// <summary>Start of the interval or hourly window, in minutes after midnight.</summary>
    public int WindowStartMinutes { get; set; } = 9 * 60;

    /// <summary>End of the interval or hourly window (inclusive), in minutes after midnight.</summary>
    public int WindowEndMinutes { get; set; } = 18 * 60;

    /// <summary>Reminder times in minutes after midnight, for <see cref="ReminderMode.Times"/>.</summary>
    public List<int> ReminderTimes { get; set; } = [];

    public DateOnly CreatedOn { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>When progress was last logged; interval nudges skip a slot right after a log.</summary>
    public DateTime? LastLoggedAt { get; set; }

    /// <summary>Count per day, keyed by <see cref="DayKey"/>.</summary>
    public Dictionary<string, int> Progress { get; set; } = [];

    /// <summary>Days the person chose to rest; they neither count nor break a streak.</summary>
    public HashSet<string> RestDays { get; set; } = [];

    [JsonIgnore]
    public bool IsCounted => DailyTarget > 1;

    public static string DayKey(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public int CountOn(DateOnly day) => Progress.TryGetValue(DayKey(day), out int count) ? count : 0;

    public bool IsMetOn(DateOnly day) => CountOn(day) >= Math.Max(1, DailyTarget);

    public bool IsRestDay(DateOnly day) => RestDays.Contains(DayKey(day));

    /// <summary>True when the habit asks for something on this day (scheduled, started, not resting).</summary>
    public bool IsScheduledOn(DateOnly day) =>
        day >= CreatedOn && Days.Includes(day.DayOfWeek) && !IsRestDay(day);

    /// <summary>Adds <paramref name="delta"/> to a day's count, clamped to 0..99.</summary>
    public int AddProgress(DateOnly day, int delta)
    {
        int count = Math.Clamp(CountOn(day) + delta, 0, 99);
        if (count == 0)
        {
            Progress.Remove(DayKey(day));
        }
        else
        {
            Progress[DayKey(day)] = count;
        }

        return count;
    }

    /// <summary>Drops progress and rest days older than <paramref name="keepFrom"/> so the file stays small.</summary>
    public void Prune(DateOnly keepFrom)
    {
        string cutoff = DayKey(keepFrom);
        foreach (string key in Progress.Keys.Where(k => string.CompareOrdinal(k, cutoff) < 0).ToList())
        {
            Progress.Remove(key);
        }

        RestDays.RemoveWhere(k => string.CompareOrdinal(k, cutoff) < 0);
    }
}

public static class ScheduleDaysExtensions
{
    public static ScheduleDays ToFlag(this DayOfWeek day) => (ScheduleDays)(1 << (int)day);

    public static bool Includes(this ScheduleDays days, DayOfWeek day) => (days & day.ToFlag()) != 0;

    /// <summary>"Every day", "Weekdays", "Weekends", or "Mon, Wed, Fri".</summary>
    public static string Describe(this ScheduleDays days, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return days switch
        {
            ScheduleDays.Every => "Every day",
            ScheduleDays.Weekdays => "Weekdays",
            ScheduleDays.Weekends => "Weekends",
            ScheduleDays.None => "No days",
            _ => string.Join(", ", WeekOrder(culture)
                .Where(d => days.Includes(d))
                .Select(d => culture.DateTimeFormat.GetAbbreviatedDayName(d))),
        };
    }

    /// <summary>The days of the week starting from the culture's first day.</summary>
    public static IEnumerable<DayOfWeek> WeekOrder(CultureInfo? culture = null)
    {
        DayOfWeek first = (culture ?? CultureInfo.CurrentCulture).DateTimeFormat.FirstDayOfWeek;
        for (int i = 0; i < 7; i++)
        {
            yield return (DayOfWeek)(((int)first + i) % 7);
        }
    }
}
