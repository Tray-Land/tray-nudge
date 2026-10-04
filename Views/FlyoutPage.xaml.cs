using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TrayNudge.Controls;
using TrayNudge.Core;
using TrayNudge.Services;
using Windows.Foundation;

namespace TrayNudge.Views;

/// <summary>Pages the host should refresh when the user comes back to them.</summary>
public interface IRefreshable
{
    void Refresh();
}

/// <summary>
/// Today: a greeting, the day's progress, a card per habit, and when the next nudge comes. Nothing
/// here polls; it redraws on data changes while visible and on a once-a-minute tick for the footer
/// and the date rollover.
/// </summary>
public sealed partial class FlyoutPage : Page, IDisposable
{
    private static readonly TimeSpan BannerDuration = TimeSpan.FromSeconds(5);

    private readonly TrayFlyoutWindow _host;
    private readonly Dictionary<string, HabitCard> _cards = [];
    private readonly DispatcherQueueTimer _minuteTimer;
    private readonly DispatcherQueueTimer _bannerTimer;
    private DateOnly _shownDay;
    private bool _isShown;

    public FlyoutPage(TrayFlyoutWindow host)
    {
        _host = host;
        InitializeComponent();

        _minuteTimer = DispatcherQueue.CreateTimer();
        _minuteTimer.Interval = TimeSpan.FromMinutes(1);
        _minuteTimer.Tick += (_, _) => OnMinute();

        _bannerTimer = DispatcherQueue.CreateTimer();
        _bannerTimer.Interval = BannerDuration;
        _bannerTimer.IsRepeating = false;
        _bannerTimer.Tick += (_, _) => Banner.Visibility = Visibility.Collapsed;
    }

    /// <summary>The flyout opened: redraw and follow changes until it hides.</summary>
    public void OnShown()
    {
        if (!_isShown)
        {
            _isShown = true;
            HabitStore.Changed += OnDataChanged;
            SettingsService.Changed += OnDataChanged;
        }

        Refresh();
        _minuteTimer.Start();
        if (EmptyState.Visibility == Visibility.Visible)
        {
            Motion.Pop(EmptySprout, 1.3f);
        }
    }

    /// <summary>The flyout hid: stop listening and ticking.</summary>
    public void OnHidden()
    {
        if (_isShown)
        {
            _isShown = false;
            HabitStore.Changed -= OnDataChanged;
            SettingsService.Changed -= OnDataChanged;
        }

        _minuteTimer.Stop();
        _bannerTimer.Stop();
        Banner.Visibility = Visibility.Collapsed;
    }

    public void Dispose()
    {
        OnHidden();
        foreach (HabitCard card in _cards.Values)
        {
            card.ActionRequested -= Card_ActionRequested;
        }

        _cards.Clear();
        HabitList.Children.Clear();
    }

    private void OnDataChanged(object? sender, EventArgs e) => Refresh();

    private void OnMinute()
    {
        if (DateOnly.FromDateTime(DateTime.Now) != _shownDay)
        {
            Refresh();
        }
        else
        {
            UpdateFooter();
        }
    }

    private void Refresh()
    {
        DateTime now = DateTime.Now;
        DateOnly today = DateOnly.FromDateTime(now);
        _shownDay = today;
        IReadOnlyList<Habit> habits = HabitStore.Habits;

        GreetingText.Text = Encouragement.Greeting(now.Hour);
        DateText.Text = now.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);

        (int done, int scheduled) = HabitStore.TodayTotals();
        SummaryCard.Visibility = habits.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        DayRing.Value = scheduled == 0 ? 100 : done * 100.0 / scheduled;
        DayRingText.Text = scheduled == 0 ? "🌿" : $"{done}/{scheduled}";
        SummaryText.Text = Encouragement.TodaySummary(done, scheduled);
        TipText.Text = Encouragement.TipOfTheDay(today);

        EmptyState.Visibility = habits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListScroller.Visibility = habits.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        SyncCards(habits, today);
        UpdateFooter();
    }

    private void SyncCards(IReadOnlyList<Habit> habits, DateOnly today)
    {
        // Same habits in the same order: update in place so only changes animate.
        bool sameOrder = HabitList.Children.Count == habits.Count
            && habits.Select(h => h.Id).SequenceEqual(HabitList.Children.Cast<HabitCard>().Select(c => c.HabitId));

        foreach (string removed in _cards.Keys.Except(habits.Select(h => h.Id)).ToList())
        {
            _cards[removed].ActionRequested -= Card_ActionRequested;
            _cards.Remove(removed);
        }

        if (!sameOrder)
        {
            HabitList.Children.Clear();
        }

        for (int i = 0; i < habits.Count; i++)
        {
            Habit habit = habits[i];
            if (!_cards.TryGetValue(habit.Id, out HabitCard? card))
            {
                card = new HabitCard();
                card.ActionRequested += Card_ActionRequested;
                _cards[habit.Id] = card;
            }

            card.IsFirst = i == 0;
            card.IsLast = i == habits.Count - 1;
            card.Bind(habit, today, HabitStore.StreakFor(habit), StreakCalculator.HasHistory(habit, today));
            if (!sameOrder)
            {
                HabitList.Children.Add(card);
            }
        }
    }

    private void UpdateFooter()
    {
        ReminderService? reminders = App.Current.Reminders;
        if (HabitStore.Habits.Count == 0 || reminders is null)
        {
            FooterIcon.Glyph = "";
            FooterText.Text = "Your gentle nudges will show up here.";
            return;
        }

        if (!SettingsService.RemindersEnabled)
        {
            FooterIcon.Glyph = "";
            FooterText.Text = "Nudges are off. Turn them on in Settings.";
            return;
        }

        if (reminders.IsPaused && SettingsService.RemindersPausedUntil is { } until)
        {
            FooterIcon.Glyph = "";
            FooterText.Text = $"Nudges paused until {FormatWhen(until)}";
            return;
        }

        FooterIcon.Glyph = "";
        FooterText.Text = reminders.NextNudge is { } next
            ? $"Next nudge: {next.Habit.Emoji} {next.Habit.Name}, {FormatWhen(next.At)}"
            : "No nudges coming up. Enjoy the quiet.";
    }

    private static string FormatWhen(DateTime at)
    {
        string time = at.ToString("t", CultureInfo.CurrentCulture);
        int days = DateOnly.FromDateTime(at).DayNumber - DateOnly.FromDateTime(DateTime.Now).DayNumber;
        return days switch
        {
            0 => time,
            1 => $"tomorrow {time}",
            _ => $"{at.ToString("dddd", CultureInfo.CurrentCulture)} {time}",
        };
    }

    private void Card_ActionRequested(object? sender, CardAction action)
    {
        if (sender is not HabitCard card || HabitStore.Find(card.HabitId) is not { } habit)
        {
            return;
        }

        switch (action)
        {
            case CardAction.Log:
                Log(card, habit);
                break;
            case CardAction.Undo:
                HabitStore.Log(habit.Id, -1);
                break;
            case CardAction.Details:
                _host.Navigate(new HabitDetailPage(_host, habit.Id));
                break;
            case CardAction.Edit:
                _host.Navigate(new HabitEditorPage(_host, HabitStore.Clone(habit)));
                break;
            case CardAction.Rest:
                HabitStore.ToggleRestToday(habit.Id);
                break;
            case CardAction.MoveUp:
                HabitStore.Move(habit.Id, -1);
                break;
            case CardAction.MoveDown:
                HabitStore.Move(habit.Id, 1);
                break;
        }
    }

    private void Log(HabitCard card, Habit habit)
    {
        // A yes/no habit that's already done stays done; "Not done yet" in the menu undoes it.
        if (!habit.IsCounted && habit.IsMetOn(HabitStore.Today))
        {
            return;
        }

        if (HabitStore.Log(habit.Id, 1) is not { } result)
        {
            return;
        }

        Point origin = CenterOf(card.LogButton);
        if (!result.BecameMet)
        {
            // A little sparkle for every step along the way: brief, so it stays around the button.
            Motion.Burst(_host.Celebrations, origin, HabitColors.ConfettiFor(habit.ColorIndex), null, count: 10, power: 0.6, lifetime: 0.45);
            return;
        }

        NotificationService.Remove(habit.Id);
        bool milestone = Encouragement.IsMilestone(result.Streak);
        Motion.Burst(_host.Celebrations, origin, HabitColors.ConfettiFor(habit.ColorIndex), habit.Emoji,
            count: milestone ? 48 : 30, power: milestone ? 1.3 : 1);

        if (result.AllDoneToday)
        {
            Motion.Rain(_host.Celebrations, HabitColors.All());
            Motion.Pop(DayRing, 1.15f);
            ShowBanner("🎉", "All done for today", Encouragement.AllDoneCelebration(Random.Shared.Next()));
        }
        else
        {
            (string title, string message) = Encouragement.GoalMet(habit, result.Streak, Random.Shared.Next());
            ShowBanner(milestone ? "🏆" : habit.Emoji, title, message);
        }
    }

    /// <summary>A warm hello for a habit that was just created.</summary>
    public void Welcome(Habit habit)
    {
        ShowBanner("🌱", $"Day one: {habit.Name}", "Every lasting habit starts with a single day. Nice choice.");
        if (_cards.TryGetValue(habit.Id, out HabitCard? card))
        {
            card.StartBringIntoView();
        }
    }

    private void ShowBanner(string emoji, string title, string message)
    {
        BannerEmoji.Text = emoji;
        BannerTitle.Text = title;
        BannerMessage.Text = message;
        Banner.Visibility = Visibility.Visible;
        Motion.BumpIn(Banner, -14);
        Motion.Pop(BannerEmoji, 1.4f);
        _bannerTimer.Stop();
        _bannerTimer.Start();
    }

    private Point CenterOf(FrameworkElement element)
    {
        try
        {
            return element.TransformToVisual(_host.Celebrations)
                .TransformPoint(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        }
        catch (ArgumentException)
        {
            return new Point(_host.Celebrations.ActualWidth / 2, _host.Celebrations.ActualHeight / 2);
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e) => _host.Navigate(new HabitEditorPage(_host, null));

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => _host.Navigate(new SettingsPage(_host));
}
