namespace TrayNudge.Core;

/// <summary>A starting point for a new habit; every field stays editable.</summary>
public sealed record HabitTemplate(
    string Name,
    string Emoji,
    int DailyTarget,
    string Unit,
    int ColorIndex,
    ReminderMode Reminder,
    int IntervalMinutes = 60,
    int WindowStartMinutes = 9 * 60,
    int WindowEndMinutes = 18 * 60,
    int[]? Times = null,
    ScheduleDays Days = ScheduleDays.Every)
{
    public Habit Create(DateOnly today) => new()
    {
        Name = Name,
        Emoji = Emoji,
        DailyTarget = DailyTarget,
        Unit = Unit,
        ColorIndex = ColorIndex,
        Reminder = Reminder,
        IntervalMinutes = IntervalMinutes,
        WindowStartMinutes = WindowStartMinutes,
        WindowEndMinutes = WindowEndMinutes,
        ReminderTimes = Times?.ToList() ?? [],
        Days = Days,
        CreatedOn = today,
    };
}

public static class HabitTemplates
{
    public static readonly IReadOnlyList<HabitTemplate> All =
    [
        new("Drink water", "💧", 8, "glasses", 0, ReminderMode.Interval, IntervalMinutes: 90),
        new("Stand up", "🧍", 8, "times", 1, ReminderMode.Interval, IntervalMinutes: 60, WindowEndMinutes: 17 * 60, Days: ScheduleDays.Weekdays),
        new("Step goal", "👟", 1, "", 2, ReminderMode.Times, Times: [17 * 60]),
        new("Journal", "📓", 1, "", 6, ReminderMode.Times, Times: [21 * 60]),
        new("Stretch", "🤸", 2, "times", 4, ReminderMode.Times, Times: [10 * 60, 15 * 60]),
        new("Eye breaks", "👀", 6, "breaks", 3, ReminderMode.Interval, IntervalMinutes: 60, Days: ScheduleDays.Weekdays),
        new("Meditate", "🧘", 1, "", 5, ReminderMode.Times, Times: [8 * 60]),
        new("Read", "📚", 1, "", 7, ReminderMode.Times, Times: [20 * 60 + 30]),
    ];

    /// <summary>Emoji offered in the editor, roughly grouped: body, mind, home, fun.</summary>
    public static readonly IReadOnlyList<string> Emoji =
    [
        "💧", "🧍", "👟", "🏃", "🚴", "🤸", "🧘", "💪",
        "🥗", "🍎", "🥦", "☕", "💊", "😴", "🛏️", "🦷",
        "📓", "📚", "✍️", "🎸", "🎨", "🧠", "🌱", "🌞",
        "👀", "🧹", "🪴", "🐕", "📵", "💬", "🙏", "✨",
    ];
}

/// <summary>
/// Habit accent colors as ARGB. Mid-tone hues that read on both light and dark acrylic, used only
/// for fills (progress, dots, check buttons), never for text.
/// </summary>
public static class HabitPalette
{
    public static readonly IReadOnlyList<uint> Colors =
    [
        0xFF3B8EEA, // blue
        0xFF14A39A, // teal
        0xFF2FB463, // green
        0xFFE5A50A, // amber
        0xFFF2753A, // orange
        0xFFE0559B, // pink
        0xFF8B5CF6, // violet
        0xFF5C6BE6, // indigo
    ];

    public static uint ColorFor(int index) => Colors[((index % Colors.Count) + Colors.Count) % Colors.Count];
}
