using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Windows.Storage;

namespace TrayNudge.Services;

/// <summary>
/// Typed wrapper over <see cref="ApplicationData.LocalSettings"/>. Every read falls back to a
/// default so a missing or corrupt value never crashes the app. Habits and their history live in
/// <see cref="HabitStore"/>, not here.
/// </summary>
/// <remarks>
/// LocalSettings values must be small (8 KB each) WinRT primitives. Store enums as int, and lists
/// or records as JSON through a source-generated <c>JsonSerializerContext</c> (trim-safe) with
/// <see cref="GetJson"/> / <see cref="SetJson"/>. Anything larger belongs in a file under
/// <c>ApplicationData.Current.LocalFolder</c>.
/// </remarks>
internal static class SettingsService
{
    /// <summary>False until the first launch has shown the flyout.</summary>
    public static bool HasLaunchedBefore
    {
        get => Get(nameof(HasLaunchedBefore), false);
        set => Set(nameof(HasLaunchedBefore), value);
    }

    /// <summary>Master switch for reminder toasts.</summary>
    public static bool RemindersEnabled
    {
        get => Get(nameof(RemindersEnabled), true);
        set => Set(nameof(RemindersEnabled), value);
    }

    /// <summary>Nudges stay quiet until this time ("Pause for an hour"); null when not paused.</summary>
    public static DateTime? RemindersPausedUntil
    {
        get
        {
            long ticks = Get(nameof(RemindersPausedUntil), 0L);
            return ticks > 0 && ticks <= DateTime.MaxValue.Ticks ? new DateTime(ticks, DateTimeKind.Local) : null;
        }

        set => Set(nameof(RemindersPausedUntil), value?.Ticks);
    }

    /// <summary>"Never miss twice": one missed day doesn't reset a streak.</summary>
    public static bool GentleStreaks
    {
        get => Get(nameof(GentleStreaks), true);
        set => Set(nameof(GentleStreaks), value);
    }

    /// <summary>Confetti and bounces. Also off when Windows animation effects are off.</summary>
    public static bool Celebrations
    {
        get => Get(nameof(Celebrations), true);
        set => Set(nameof(Celebrations), value);
    }

    /// <summary>Raised on the UI thread when a setting that affects display or reminders changes.</summary>
    public static event EventHandler? Changed;

    public static void RaiseChanged() => Changed?.Invoke(null, EventArgs.Empty);

    public static T? GetJson<T>(string key, JsonTypeInfo<T> typeInfo)
    {
        if (Get<string?>(key, null) is not { } json)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public static void SetJson<T>(string key, T? value, JsonTypeInfo<T> typeInfo) =>
        Set(key, value is null ? null : JsonSerializer.Serialize(value, typeInfo));

    private static T Get<T>(string key, T defaultValue)
    {
        try
        {
            return ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out object? value) && value is T typed
                ? typed
                : defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    private static void Set(string key, object? value)
    {
        try
        {
            if (value is null)
            {
                ApplicationData.Current.LocalSettings.Values.Remove(key);
            }
            else
            {
                ApplicationData.Current.LocalSettings.Values[key] = value;
            }
        }
        catch
        {
            // Settings are best-effort; ignore storage failures.
        }
    }
}
