using TrayNudge.Core;
using Windows.Storage;

namespace TrayNudge.Services;

/// <summary>The outcome of logging progress, so the UI knows what to celebrate.</summary>
public readonly record struct LogResult(Habit Habit, int Count, bool BecameMet, int Streak, bool AllDoneToday);

/// <summary>
/// Owns the habit list and its history: loaded once at startup (a few KB), changed only on the UI
/// thread, and written to <c>habits.json</c> in LocalFolder after every change. Writes go to a temp
/// file first and are serialized, so a crash mid-write never loses the file.
/// </summary>
internal static class HabitStore
{
    private const string FileName = "habits.json";

    private static readonly SemaphoreSlim SaveLock = new(1, 1);
    private static HabitData _data = new();
    private static string? _path;

    /// <summary>Raised on the UI thread after any change.</summary>
    public static event EventHandler? Changed;

    public static IReadOnlyList<Habit> Habits => _data.Habits;

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public static Habit? Find(string id) => _data.Habits.FirstOrDefault(h => h.Id == id);

    public static void Load()
    {
        _path = ResolvePath();
        try
        {
            _data = File.Exists(_path) ? HabitData.Parse(File.ReadAllText(_path)) : new HabitData();
        }
        catch (IOException)
        {
            _data = new HabitData();
        }
    }

    /// <summary>(done, scheduled) for today across all habits.</summary>
    public static (int Done, int Scheduled) TodayTotals()
    {
        DateOnly today = Today;
        int scheduled = 0;
        int done = 0;
        foreach (Habit habit in _data.Habits)
        {
            if (habit.IsScheduledOn(today))
            {
                scheduled++;
                if (habit.IsMetOn(today))
                {
                    done++;
                }
            }
        }

        return (done, scheduled);
    }

    public static StreakInfo StreakFor(Habit habit) =>
        StreakCalculator.Calculate(habit, Today, SettingsService.GentleStreaks);

    public static LogResult? Log(string habitId, int delta)
    {
        if (Find(habitId) is not { } habit)
        {
            return null;
        }

        DateOnly today = Today;
        bool wasMet = habit.IsMetOn(today);
        (int doneBefore, _) = TodayTotals();
        int count = habit.AddProgress(today, delta);
        if (delta > 0)
        {
            habit.LastLoggedAt = DateTime.Now;
        }

        bool isMet = habit.IsMetOn(today);
        (int done, int scheduled) = TodayTotals();
        Commit();

        return new LogResult(
            habit,
            count,
            BecameMet: isMet && !wasMet,
            Streak: StreakFor(habit).Current,
            AllDoneToday: scheduled > 0 && done == scheduled && doneBefore < done);
    }

    public static void ToggleRestToday(string habitId)
    {
        if (Find(habitId) is not { } habit)
        {
            return;
        }

        string key = Habit.DayKey(Today);
        if (!habit.RestDays.Remove(key))
        {
            habit.RestDays.Add(key);
        }

        Commit();
    }

    public static void Upsert(Habit habit)
    {
        int index = _data.Habits.FindIndex(h => h.Id == habit.Id);
        if (index >= 0)
        {
            _data.Habits[index] = habit;
        }
        else
        {
            _data.Habits.Add(habit);
        }

        Commit();
    }

    public static void Delete(string habitId)
    {
        if (_data.Habits.RemoveAll(h => h.Id == habitId) > 0)
        {
            Commit();
        }
    }

    public static void Move(string habitId, int direction)
    {
        int index = _data.Habits.FindIndex(h => h.Id == habitId);
        int target = index + direction;
        if (index < 0 || target < 0 || target >= _data.Habits.Count)
        {
            return;
        }

        (_data.Habits[index], _data.Habits[target]) = (_data.Habits[target], _data.Habits[index]);
        Commit();
    }

    /// <summary>A deep copy for the editor, so Cancel leaves the stored habit untouched.</summary>
    public static Habit Clone(Habit habit)
    {
        HabitData single = HabitData.Parse(new HabitData { Habits = [habit] }.Serialize(Today));
        return single.Habits[0];
    }

    private static void Commit()
    {
        string json = _data.Serialize(Today);
        _ = SaveAsync(json);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static async Task SaveAsync(string json)
    {
        if (_path is null)
        {
            return;
        }

        await SaveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            string temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, json).ConfigureAwait(false);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The in-memory copy is still right; the next change tries again.
        }
        finally
        {
            SaveLock.Release();
        }
    }

    private static string ResolvePath()
    {
        try
        {
            return Path.Combine(ApplicationData.Current.LocalFolder.Path, FileName);
        }
        catch
        {
            // Unpackaged runs have no ApplicationData.
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrayNudge");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, FileName);
        }
    }
}
