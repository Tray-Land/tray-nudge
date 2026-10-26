using TrayNudge.Core;

namespace TrayNudge.Tests;

[TestClass]
public sealed class EncouragementAndDataTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    [TestMethod]
    public void SummaryCelebratesShowingUp()
    {
        Assert.AreEqual("2 of 4 done. You're on your way.", Encouragement.TodaySummary(2, 4));
        StringAssert.StartsWith(Encouragement.TodaySummary(4, 4), "All done");
        StringAssert.StartsWith(Encouragement.TodaySummary(0, 0), "Nothing scheduled");
    }

    [TestMethod]
    public void ProgressTextHandlesCountsAndChecks()
    {
        Habit water = new() { DailyTarget = 8, Unit = "glasses" };
        Habit journal = new() { DailyTarget = 1 };

        Assert.AreEqual("3 of 8 glasses", Encouragement.ProgressText(water, 3));
        Assert.AreEqual("8 of 8 glasses · goal met", Encouragement.ProgressText(water, 8));
        Assert.AreEqual("Not yet today", Encouragement.ProgressText(journal, 0));
        Assert.AreEqual("Done for today", Encouragement.ProgressText(journal, 1));
    }

    [TestMethod]
    public void MilestonesOnlyOnSpecialDays()
    {
        Assert.IsNotNull(Encouragement.Milestone(7));
        Assert.IsNotNull(Encouragement.Milestone(66));
        Assert.IsNotNull(Encouragement.Milestone(150));
        Assert.IsNull(Encouragement.Milestone(8));
        Assert.IsNull(Encouragement.Milestone(0));
    }

    [TestMethod]
    public void HintsAreKindAfterAMiss()
    {
        Habit habit = new() { CreatedOn = Today.AddDays(-10) };
        habit.AddProgress(Today.AddDays(-5), 1);

        string? hint = Encouragement.CardHint(habit, Today, new StreakInfo(0, 1, false), hasHistory: true);

        Assert.AreEqual("Fresh start. Picking it back up is the habit.", hint);
    }

    [TestMethod]
    public void DigestNamesEveryHabitGently()
    {
        List<Habit> habits = [new() { Name = "Water", Emoji = "💧" }, new() { Name = "Stretch", Emoji = "🧘" }];
        (string title, string message) = Encouragement.DigestNudge(habits);

        StringAssert.Contains(title, "2");
        StringAssert.Contains(message, "Water");
        StringAssert.Contains(message, "Stretch");
        Assert.IsFalse((title + message).Contains('!'));
    }

    [TestMethod]
    public void NoWordingScolds()
    {
        string[] harsh = ["fail", "broke", "lost", "oops", "should have", "!"];
        Habit habit = new() { Name = "Water", Emoji = "💧", DailyTarget = 8 };
        List<string> lines = [];
        for (int pick = 0; pick < 10; pick++)
        {
            lines.Add(Encouragement.Nudge(habit, pick % 8, pick, pick).Message);
            lines.Add(Encouragement.GoalMet(habit, pick, pick).Message);
            lines.Add(Encouragement.AllDoneCelebration(pick));
        }

        for (int day = 0; day < 20; day++)
        {
            lines.Add(Encouragement.TipOfTheDay(Today.AddDays(day)));
        }

        foreach (string line in lines)
        {
            foreach (string word in harsh)
            {
                Assert.IsFalse(line.Contains(word, StringComparison.OrdinalIgnoreCase), $"\"{line}\" contains \"{word}\"");
            }
        }
    }

    [TestMethod]
    public void DataRoundTripsAndPrunes()
    {
        HabitData data = new();
        Habit habit = HabitTemplates.All[0].Create(Today);
        habit.AddProgress(Today, 3);
        habit.AddProgress(Today.AddDays(-HabitData.KeepDays - 5), 2);
        habit.RestDays.Add(Habit.DayKey(Today.AddDays(-1)));
        data.Habits.Add(habit);

        HabitData copy = HabitData.Parse(data.Serialize(Today));

        Habit restored = copy.Habits.Single();
        Assert.AreEqual(habit.Id, restored.Id);
        Assert.AreEqual(3, restored.CountOn(Today));
        Assert.AreEqual(0, restored.CountOn(Today.AddDays(-HabitData.KeepDays - 5)));
        Assert.IsTrue(restored.IsRestDay(Today.AddDays(-1)));
        Assert.AreEqual(ReminderMode.Interval, restored.Reminder);
        Assert.AreEqual(ScheduleDays.Every, restored.Days);
    }

    [TestMethod]
    public void CorruptDataFallsBackToEmpty()
    {
        Assert.AreEqual(0, HabitData.Parse("{ not json").Habits.Count);
        Assert.AreEqual(0, HabitData.Parse("null").Habits.Count);
    }

    [TestMethod]
    public void ParseRepairsOutOfRangeValues()
    {
        HabitData data = HabitData.Parse("""{"Habits":[{"Id":"a","DailyTarget":0,"Emoji":"","Reminder":"Interval"}]}""");

        Assert.AreEqual(1, data.Habits[0].DailyTarget);
        Assert.AreEqual("✨", data.Habits[0].Emoji);
    }

    [TestMethod]
    public void ProgressClampsAtZero()
    {
        Habit habit = new();
        habit.AddProgress(Today, -3);

        Assert.AreEqual(0, habit.CountOn(Today));
        Assert.AreEqual(0, habit.Progress.Count);
    }
}
