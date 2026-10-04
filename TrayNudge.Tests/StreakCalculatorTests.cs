using TrayNudge.Core;

namespace TrayNudge.Tests;

[TestClass]
public sealed class StreakCalculatorTests
{
    // A Wednesday.
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static Habit DailyHabit(int target = 1) => new()
    {
        Name = "Water",
        DailyTarget = target,
        CreatedOn = Today.AddDays(-60),
    };

    private static void Meet(Habit habit, params int[] daysAgo)
    {
        foreach (int ago in daysAgo)
        {
            habit.AddProgress(Today.AddDays(-ago), habit.DailyTarget);
        }
    }

    [TestMethod]
    public void UnfinishedTodayDoesNotBreakStreak()
    {
        Habit habit = DailyHabit();
        Meet(habit, 1, 2, 3);

        Assert.AreEqual(3, StreakCalculator.CurrentStreak(habit, Today, gentle: false).Streak);
    }

    [TestMethod]
    public void MetTodayExtendsStreak()
    {
        Habit habit = DailyHabit();
        Meet(habit, 0, 1, 2);

        Assert.AreEqual(3, StreakCalculator.CurrentStreak(habit, Today, gentle: false).Streak);
    }

    [TestMethod]
    public void StrictModeBreaksOnOneMiss()
    {
        Habit habit = DailyHabit();
        Meet(habit, 0, 2, 3, 4);

        Assert.AreEqual(1, StreakCalculator.CurrentStreak(habit, Today, gentle: false).Streak);
    }

    [TestMethod]
    public void GentleModeForgivesOneMiss()
    {
        Habit habit = DailyHabit();
        Meet(habit, 0, 2, 3, 4);

        Assert.AreEqual(4, StreakCalculator.CurrentStreak(habit, Today, gentle: true).Streak);
    }

    [TestMethod]
    public void GentleModeBreaksOnTwoMissesInARow()
    {
        Habit habit = DailyHabit();
        Meet(habit, 0, 3, 4);

        Assert.AreEqual(1, StreakCalculator.CurrentStreak(habit, Today, gentle: true).Streak);
    }

    [TestMethod]
    public void GraceAtHeadIsReported()
    {
        Habit habit = DailyHabit();
        Meet(habit, 2, 3);

        (int streak, bool grace) = StreakCalculator.CurrentStreak(habit, Today, gentle: true);

        Assert.AreEqual(2, streak);
        Assert.IsTrue(grace);
    }

    [TestMethod]
    public void UnscheduledDaysAreSkipped()
    {
        Habit habit = DailyHabit();
        habit.Days = ScheduleDays.Weekdays;

        // Wed (today, open), Tue, Mon met; Sun and Sat are off; Fri met.
        Meet(habit, 1, 2, 5);

        Assert.AreEqual(3, StreakCalculator.CurrentStreak(habit, Today, gentle: false).Streak);
    }

    [TestMethod]
    public void RestDaysProtectStreak()
    {
        Habit habit = DailyHabit();
        Meet(habit, 1, 3);
        habit.RestDays.Add(Habit.DayKey(Today.AddDays(-2)));

        Assert.AreEqual(2, StreakCalculator.CurrentStreak(habit, Today, gentle: false).Streak);
        Assert.AreEqual(DayStatus.Rest, StreakCalculator.StatusOn(habit, Today.AddDays(-2), Today));
    }

    [TestMethod]
    public void PartialProgressIsNotMet()
    {
        Habit habit = DailyHabit(target: 8);
        habit.AddProgress(Today.AddDays(-1), 5);

        Assert.AreEqual(0, StreakCalculator.CurrentStreak(habit, Today, gentle: false).Streak);
        Assert.AreEqual(DayStatus.Partial, StreakCalculator.StatusOn(habit, Today.AddDays(-1), Today));
    }

    [TestMethod]
    public void TodayIsOpenNotMissed()
    {
        Habit habit = DailyHabit();

        Assert.AreEqual(DayStatus.Open, StreakCalculator.StatusOn(habit, Today, Today));
    }

    [TestMethod]
    public void StreakStopsAtCreation()
    {
        Habit habit = DailyHabit();
        habit.CreatedOn = Today.AddDays(-2);
        Meet(habit, 0, 1, 2, 3, 4);

        Assert.AreEqual(3, StreakCalculator.CurrentStreak(habit, Today, gentle: true).Streak);
    }

    [TestMethod]
    public void BestStreakFindsLongestRun()
    {
        Habit habit = DailyHabit();
        Meet(habit, 20, 19, 18, 17, 16, 10, 9, 1, 0);

        Assert.AreEqual(5, StreakCalculator.BestStreak(habit, Today, gentle: false));
    }

    [TestMethod]
    public void BestStreakGentleBridgesSingleMisses()
    {
        Habit habit = DailyHabit();
        Meet(habit, 10, 9, 7, 6);

        Assert.AreEqual(2, StreakCalculator.BestStreak(habit, Today, gentle: false));
        Assert.AreEqual(4, StreakCalculator.BestStreak(habit, Today, gentle: true));
    }

    [TestMethod]
    public void ConsistencyIgnoresUnfinishedToday()
    {
        Habit habit = DailyHabit();
        Meet(habit, 1, 2, 3);

        Consistency c = StreakCalculator.ConsistencyOver(habit, Today, days: 7);

        Assert.AreEqual(3, c.Met);
        Assert.AreEqual(6, c.Scheduled);
        Assert.AreEqual(50, c.Percent);
    }

    [TestMethod]
    public void ConsistencyIsNullBeforeAnythingScheduled()
    {
        Habit habit = DailyHabit();
        habit.CreatedOn = Today;

        Assert.IsNull(StreakCalculator.ConsistencyOver(habit, Today).Ratio);
    }
}
