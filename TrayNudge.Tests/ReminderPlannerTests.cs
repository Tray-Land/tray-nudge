using TrayNudge.Core;

namespace TrayNudge.Tests;

[TestClass]
public sealed class ReminderPlannerTests
{
    // Wednesday, Oct 7 2026.
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static DateTime At(int hour, int minute = 0, int dayOffset = 0) =>
        Today.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, minute));

    private static Habit Water() => new()
    {
        Name = "Water",
        DailyTarget = 8,
        Reminder = ReminderMode.Interval,
        IntervalMinutes = 90,
        WindowStartMinutes = 9 * 60,
        WindowEndMinutes = 18 * 60,
        CreatedOn = Today.AddDays(-10),
    };

    [TestMethod]
    public void IntervalSlotsSpanWindow()
    {
        CollectionAssert.AreEqual(
            new[] { 540, 630, 720, 810, 900, 990, 1080 },
            ReminderPlanner.SlotsFor(Water()).ToArray());
    }

    [TestMethod]
    public void NextIntervalSlotIsStrictlyAfter()
    {
        Assert.AreEqual(At(10, 30), ReminderPlanner.NextReminder(Water(), At(9)));
    }

    [TestMethod]
    public void AfterWindowMovesToTomorrow()
    {
        Assert.AreEqual(At(9, 0, 1), ReminderPlanner.NextReminder(Water(), At(18, 30)));
    }

    [TestMethod]
    public void MetTodaySkipsToTomorrow()
    {
        Habit habit = Water();
        habit.AddProgress(Today, 8);

        Assert.AreEqual(At(9, 0, 1), ReminderPlanner.NextReminder(habit, At(10)));
    }

    [TestMethod]
    public void RecentLogSkipsTheNextIntervalSlot()
    {
        Habit habit = Water();
        habit.LastLoggedAt = At(10, 15);

        Assert.AreEqual(At(12), ReminderPlanner.NextReminder(habit, At(10, 20)));
    }

    [TestMethod]
    public void TimesModeUsesSortedTimes()
    {
        Habit habit = new()
        {
            Reminder = ReminderMode.Times,
            ReminderTimes = [21 * 60, 8 * 60],
            CreatedOn = Today,
        };

        Assert.AreEqual(At(8), ReminderPlanner.NextReminder(habit, At(7)));
        Assert.AreEqual(At(21), ReminderPlanner.NextReminder(habit, At(8)));
    }

    [TestMethod]
    public void UnscheduledAndRestDaysAreSkipped()
    {
        Habit habit = new()
        {
            Reminder = ReminderMode.Times,
            ReminderTimes = [8 * 60],
            Days = ScheduleDays.Weekdays,
            CreatedOn = Today,
        };
        habit.RestDays.Add(Habit.DayKey(Today.AddDays(1))); // Thursday

        // Wed after 8 → Thu is resting → Fri.
        Assert.AreEqual(At(8, 0, 2), ReminderPlanner.NextReminder(habit, At(9)));

        // Fri after 8 → weekend → Mon.
        Assert.AreEqual(At(8, 0, 5), ReminderPlanner.NextReminder(habit, At(9, 0, 2)));
    }

    [TestMethod]
    public void OffHasNoReminder()
    {
        Assert.IsNull(ReminderPlanner.NextReminder(new Habit { CreatedOn = Today }, At(9)));
    }

    [TestMethod]
    public void DueBetweenSkipsStaleSlots()
    {
        Habit habit = Water();

        Assert.AreEqual(1, ReminderPlanner.DueBetween([habit], At(10, 25), At(10, 31)).Count);

        // Slept from 9:05 to 13:00: the 12:00 slot is an hour old, so stay quiet.
        Assert.AreEqual(0, ReminderPlanner.DueBetween([habit], At(9, 5), At(13)).Count);
    }

    [TestMethod]
    public void SnoozeComesDue()
    {
        Habit habit = Water();
        Dictionary<string, DateTime> snoozes = new() { [habit.Id] = At(11) };

        Assert.AreEqual(1, ReminderPlanner.DueBetween([habit], At(10, 45), At(11), snoozes).Count);
        Assert.AreEqual(At(11), ReminderPlanner.NextWake([habit], At(10, 45), snoozes));
    }

    [TestMethod]
    public void NextWakeIsEarliestAcrossHabits()
    {
        Habit journal = new()
        {
            Reminder = ReminderMode.Times,
            ReminderTimes = [10 * 60],
            CreatedOn = Today,
        };

        Assert.AreEqual(At(10), ReminderPlanner.NextWake([Water(), journal], At(9, 30)));
    }
}
