using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrayNudge.Core;

/// <summary>Everything the app persists about habits: the list, in display order, with history.</summary>
public sealed class HabitData
{
    public const int CurrentVersion = 1;

    /// <summary>History older than this is pruned on save; the history grid shows far less.</summary>
    public const int KeepDays = 400;

    public int Version { get; set; } = CurrentVersion;

    public List<Habit> Habits { get; set; } = [];

    public static HabitData Parse(string json)
    {
        try
        {
            HabitData? data = JsonSerializer.Deserialize(json, HabitJsonContext.Default.HabitData);
            if (data is null)
            {
                return new HabitData();
            }

            // A hand-edited or partially written file must not crash anything downstream.
            data.Habits.RemoveAll(h => h is null || string.IsNullOrWhiteSpace(h.Id));
            foreach (Habit habit in data.Habits)
            {
                habit.Name ??= "";
                habit.Emoji = string.IsNullOrWhiteSpace(habit.Emoji) ? "✨" : habit.Emoji;
                habit.Unit ??= "";
                habit.DailyTarget = Math.Clamp(habit.DailyTarget, 1, 99);
                habit.ReminderTimes ??= [];
                habit.Progress ??= [];
                habit.RestDays ??= [];
                if (!Enum.IsDefined(habit.Reminder))
                {
                    habit.Reminder = ReminderMode.Off;
                }
            }

            return data;
        }
        catch (JsonException)
        {
            return new HabitData();
        }
    }

    public string Serialize(DateOnly today)
    {
        DateOnly keepFrom = today.AddDays(-KeepDays);
        foreach (Habit habit in Habits)
        {
            habit.Prune(keepFrom);
        }

        return JsonSerializer.Serialize(this, HabitJsonContext.Default.HabitData);
    }
}

[JsonSourceGenerationOptions(WriteIndented = false, UseStringEnumConverter = true)]
[JsonSerializable(typeof(HabitData))]
public sealed partial class HabitJsonContext : JsonSerializerContext;
