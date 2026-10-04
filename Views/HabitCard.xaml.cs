using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TrayNudge.Controls;
using TrayNudge.Core;

namespace TrayNudge.Views;

/// <summary>What a card asks its page to do.</summary>
public enum CardAction
{
    Log,
    Undo,
    Details,
    Edit,
    Rest,
    MoveUp,
    MoveDown,
}

/// <summary>
/// One habit on the Today page: emoji, name, streak, today's progress, a one-tap log button, and
/// the last seven days as dots. Updates in place, animating only what changed.
/// </summary>
public sealed partial class HabitCard : UserControl
{
    private const int StripDays = 7;

    private readonly List<(FrameworkElement Column, Ellipse Dot, TextBlock Letter)> _strip = [];
    private Habit? _habit;
    private int _count = -1;
    private int _streak = -1;
    private double _fraction;

    public HabitCard()
    {
        InitializeComponent();
        for (int i = 0; i < StripDays; i++)
        {
            Ellipse dot = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            TextBlock letter = new()
            {
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            StackPanel column = new() { Spacing = 2, Width = 16 };
            column.Children.Add(new Grid { Width = 14, Height = 14, Children = { dot } });
            column.Children.Add(letter);
            WeekStrip.Children.Add(column);
            _strip.Add((column, dot, letter));
        }
    }

    public event EventHandler<CardAction>? ActionRequested;

    public string HabitId => _habit?.Id ?? "";

    /// <summary>The log button, so the page can launch confetti from it.</summary>
    public FrameworkElement LogButton => CheckButton;

    public bool IsFirst { get; set; }

    public bool IsLast { get; set; }

    public void Bind(Habit habit, DateOnly today, StreakInfo streak, bool hasHistory)
    {
        bool sameHabit = _habit?.Id == habit.Id;
        _habit = habit;
        int count = habit.CountOn(today);
        int target = Math.Max(1, habit.DailyTarget);
        bool met = count >= target;

        Brush color = HabitColors.Brush(habit.ColorIndex);
        EmojiText.Text = habit.Emoji;
        EmojiBadge.Background = HabitColors.Brush(habit.ColorIndex, 0.22);
        NameText.Text = habit.Name;
        ProgressText.Text = Encouragement.ProgressText(habit, count);

        // Check button: hollow ring with "+" (or a faint check) until met, then a filled check.
        CheckRing.Stroke = color;
        CheckRing.Fill = met ? color : null;
        CheckGlyph.Glyph = met || !habit.IsCounted ? "" : "";
        CheckGlyph.Foreground = met
            ? new SolidColorBrush(Microsoft.UI.Colors.White)
            : habit.IsCounted ? color : FaintProbe.Foreground;
        string label = met
            ? habit.IsCounted ? $"Log another {habit.Name}, goal already met" : $"{habit.Name} is done for today"
            : habit.IsCounted ? $"Log one {habit.Name}" : $"Mark {habit.Name} done";
        AutomationProperties.SetName(CheckButton, label);
        ToolTipService.SetToolTip(CheckButton, met && !habit.IsCounted ? "Done for today" : habit.IsCounted ? "Log one" : "Mark done");

        string? streakLabel = Encouragement.StreakLabel(streak);
        StreakPill.Visibility = streakLabel is null ? Visibility.Collapsed : Visibility.Visible;
        StreakText.Text = streakLabel is null ? "" : $"🔥 {streakLabel}";

        string? hint = Encouragement.CardHint(habit, today, streak, hasHistory);
        HintText.Text = hint ?? "";
        HintText.Visibility = hint is null ? Visibility.Collapsed : Visibility.Visible;

        BarHost.Visibility = habit.IsCounted ? Visibility.Visible : Visibility.Collapsed;
        BarFill.Background = color;
        _fraction = Math.Min(1, count / (double)target);
        UpdateBar(animate: sameHabit);

        BindStrip(habit, today);

        // Only animate real changes to the same habit, not the first bind.
        if (sameHabit && count != _count)
        {
            Motion.BumpIn(ProgressText);
        }

        if (sameHabit && streak.Current > _streak && _streak >= 0)
        {
            Motion.Wiggle(StreakPill);
        }

        _count = count;
        _streak = streak.Current;
    }

    private void BindStrip(Habit habit, DateOnly today)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        Brush color = HabitColors.Brush(habit.ColorIndex);
        Brush faint = FaintProbe.Foreground;
        Brush partial = HabitColors.Brush(habit.ColorIndex, 0.4);

        for (int i = 0; i < StripDays; i++)
        {
            DateOnly day = today.AddDays(i - (StripDays - 1));
            (FrameworkElement column, Ellipse dot, TextBlock letter) = _strip[i];
            DayStatus status = StreakCalculator.StatusOn(habit, day, today);

            dot.StrokeDashArray = null;
            dot.StrokeThickness = 0;
            dot.Stroke = null;
            dot.Fill = null;
            double size = 12;
            switch (status)
            {
                case DayStatus.Met:
                    dot.Fill = color;
                    break;
                case DayStatus.Partial:
                    dot.Fill = partial;
                    break;
                case DayStatus.Open:
                    dot.Stroke = color;
                    dot.StrokeThickness = 2;
                    break;
                case DayStatus.Missed:
                    // A quiet outline, never red: a missed day is just a day.
                    dot.Stroke = faint;
                    dot.StrokeThickness = 1.5;
                    size = 10;
                    break;
                case DayStatus.Rest:
                    dot.Stroke = faint;
                    dot.StrokeThickness = 1.5;
                    dot.StrokeDashArray = [1.5, 1.5];
                    size = 10;
                    break;
                default:
                    dot.Fill = faint;
                    size = 4;
                    break;
            }

            dot.Width = size;
            dot.Height = size;

            string dayName = culture.DateTimeFormat.GetAbbreviatedDayName(day.DayOfWeek);
            letter.Text = dayName[..1];
            letter.Foreground = faint;
            letter.FontWeight = day == today ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;

            string description = $"{(day == today ? "Today" : culture.DateTimeFormat.GetDayName(day.DayOfWeek))}: {Describe(status, habit, day)}";
            ToolTipService.SetToolTip(column, description);
            AutomationProperties.SetName(column, description);
        }
    }

    private static string Describe(DayStatus status, Habit habit, DateOnly day) => status switch
    {
        DayStatus.Met => "done",
        DayStatus.Partial => $"{habit.CountOn(day)} of {habit.DailyTarget}",
        DayStatus.Open => habit.CountOn(day) > 0 ? $"{habit.CountOn(day)} of {habit.DailyTarget} so far" : "still to come",
        DayStatus.Missed => "a pause",
        DayStatus.Rest => "rest day",
        _ => "day off",
    };

    private void UpdateBar(bool animate)
    {
        double width = BarHost.ActualWidth * _fraction;
        if (animate && Math.Abs(width - BarFill.Width) > 0.5)
        {
            Motion.AnimateWidth(BarFill, width);
        }
        else
        {
            BarFill.Width = width;
        }
    }

    private void BarHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateBar(animate: false);

    private void CheckButton_Click(object sender, RoutedEventArgs e)
    {
        Motion.Pop(CheckButton);
        ActionRequested?.Invoke(this, CardAction.Log);
    }

    private void Details_Tapped(object sender, TappedRoutedEventArgs e) => ActionRequested?.Invoke(this, CardAction.Details);

    private void MoreMenu_Opening(object? sender, object e)
    {
        if (_habit is null)
        {
            return;
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        MoreMenu.Items.Clear();
        AddItem("See progress", "", CardAction.Details);
        if (_habit.CountOn(today) > 0)
        {
            AddItem(_habit.IsCounted ? "Take one back" : "Not done yet", "", CardAction.Undo);
        }

        AddItem(_habit.IsRestDay(today) ? "Not resting today" : "Rest today", "", CardAction.Rest);
        AddItem("Edit", "", CardAction.Edit);
        if (!IsFirst || !IsLast)
        {
            MoreMenu.Items.Add(new MenuFlyoutSeparator());
        }

        if (!IsFirst)
        {
            AddItem("Move up", "", CardAction.MoveUp);
        }

        if (!IsLast)
        {
            AddItem("Move down", "", CardAction.MoveDown);
        }
    }

    private void AddItem(string text, string glyph, CardAction action)
    {
        MenuFlyoutItem item = new() { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => ActionRequested?.Invoke(this, action);
        MoreMenu.Items.Add(item);
    }
}
