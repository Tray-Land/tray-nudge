using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TrayNudge.Controls;
using TrayNudge.Core;
using TrayNudge.Services;

namespace TrayNudge.Views;

/// <summary>
/// One habit's story: current and best streak, 30-day consistency, and twelve weeks of days as a
/// heatmap. Misses are drawn as quiet empty squares; there's no red anywhere.
/// </summary>
public sealed partial class HabitDetailPage : Page, IRefreshable
{
    private const int Weeks = 12;
    private const double Cell = 14;
    private const double Gap = 4;

    private readonly TrayFlyoutWindow _host;
    private readonly string _habitId;

    public HabitDetailPage(TrayFlyoutWindow host, string habitId)
    {
        _host = host;
        _habitId = habitId;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Refresh();
            BackButton.Focus(FocusState.Programmatic);
            Motion.Pop(CurrentTile, 1.08f);
        };
    }

    public void Refresh()
    {
        if (HabitStore.Find(_habitId) is not { } habit)
        {
            _host.ShowMainPage();
            return;
        }

        DateOnly today = HabitStore.Today;
        StreakInfo streak = HabitStore.StreakFor(habit);
        Consistency consistency = StreakCalculator.ConsistencyOver(habit, today);

        TitleText.Text = $"{habit.Emoji} {habit.Name}";
        CurrentValue.Text = streak.Current > 0 ? $"🔥 {streak.Current}" : "0";
        BestValue.Text = streak.Best > 0 ? $"🏆 {streak.Best}" : "0";
        ConsistencyValue.Text = consistency.Percent is { } p ? $"{p}%" : "–";
        ConsistencyText.Text = Encouragement.ConsistencyText(consistency);

        ScheduleText.Text = DescribeSchedule(habit);

        bool resting = habit.IsRestDay(today);
        RestLabel.Text = resting ? "Not resting today" : "Rest today";
        int count = habit.CountOn(today);
        UndoButton.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UndoLabel.Text = habit.IsCounted ? "Take one back" : "Not done yet";

        BuildHeatmap(habit, today);
        BuildLegend(habit);
    }

    private static string DescribeSchedule(Habit habit)
    {
        string goal = habit.IsCounted
            ? $"{habit.DailyTarget}{(string.IsNullOrWhiteSpace(habit.Unit) ? " times" : $" {habit.Unit}")} a day"
            : "Once a day";
        string nudges = habit.Reminder switch
        {
            ReminderMode.Interval =>
                $"nudges every {FormatInterval(habit.IntervalMinutes)} from {FormatTime(habit.WindowStartMinutes)} to {FormatTime(habit.WindowEndMinutes)}",
            ReminderMode.Hourly =>
                $"nudges every {FormatInterval(Math.Clamp(habit.EveryHours, 1, ReminderPlanner.MaxEveryHours) * 60)} at :{Math.Clamp(habit.MinutePastHour, 0, 59):D2} from {FormatTime(habit.WindowStartMinutes)} to {FormatTime(habit.WindowEndMinutes)}",
            ReminderMode.Times when habit.ReminderTimes.Count > 0 =>
                $"nudges at {string.Join(", ", habit.ReminderTimes.Order().Select(FormatTime))}",
            _ => "no nudges",
        };
        return $"{goal} · {habit.Days.Describe()} · {nudges}. Started {habit.CreatedOn.ToString("MMMM d", CultureInfo.CurrentCulture)}.";
    }

    private static string FormatInterval(int minutes) => minutes switch
    {
        60 => "hour",
        _ when minutes % 60 == 0 => $"{minutes / 60} hours",
        _ => $"{minutes} minutes",
    };

    private static string FormatTime(int minutes) =>
        DateTime.Today.AddMinutes(minutes).ToString("t", CultureInfo.CurrentCulture);

    private void BuildHeatmap(Habit habit, DateOnly today)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        List<DayOfWeek> order = ScheduleDaysExtensions.WeekOrder(culture).ToList();
        int todayRow = order.IndexOf(today.DayOfWeek);
        DateOnly start = today.AddDays(-todayRow - ((Weeks - 1) * 7));

        HeatGrid.Children.Clear();
        HeatGrid.RowDefinitions.Clear();
        HeatGrid.ColumnDefinitions.Clear();
        HeatGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        for (int c = 0; c < Weeks; c++)
        {
            HeatGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Cell + Gap) });
        }

        HeatGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
        for (int r = 0; r < 7; r++)
        {
            HeatGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Cell + Gap) });
        }

        Brush tertiary = TertiaryProbe.Foreground;
        Brush faint = FaintProbe.Foreground;

        // Day labels on alternate rows keep it airy.
        for (int r = 0; r < 7; r += 2)
        {
            string name = culture.DateTimeFormat.GetAbbreviatedDayName(order[r]);
            AddText(name.Length > 3 ? name[..3] : name, r + 1, 0, tertiary);
        }

        int lastMonth = -1;
        for (int week = 0; week < Weeks; week++)
        {
            DateOnly weekStart = start.AddDays(week * 7);
            if (weekStart.Month != lastMonth)
            {
                lastMonth = weekStart.Month;
                TextBlock month = AddText(culture.DateTimeFormat.GetAbbreviatedMonthName(weekStart.Month), 0, week + 1, tertiary);
                Grid.SetColumnSpan(month, 3);
            }

            for (int row = 0; row < 7; row++)
            {
                DateOnly day = weekStart.AddDays(row);
                if (day > today)
                {
                    continue;
                }

                Rectangle cell = new() { Width = Cell, Height = Cell, RadiusX = 3, RadiusY = 3 };
                DayStatus status = StreakCalculator.StatusOn(habit, day, today);
                switch (status)
                {
                    case DayStatus.Met:
                        cell.Fill = HabitColors.Brush(habit.ColorIndex);
                        break;
                    case DayStatus.Partial:
                    case DayStatus.Open when habit.CountOn(day) > 0:
                        double ratio = habit.CountOn(day) / (double)Math.Max(1, habit.DailyTarget);
                        cell.Fill = HabitColors.Brush(habit.ColorIndex, 0.2 + (0.5 * ratio));
                        break;
                    case DayStatus.Rest:
                        cell.Stroke = faint;
                        cell.StrokeThickness = 1;
                        cell.StrokeDashArray = [2, 2];
                        break;
                    case DayStatus.Missed:
                    case DayStatus.Open:
                        cell.Fill = faint;
                        cell.Opacity = 0.22;
                        break;
                    default:
                        cell.Fill = faint;
                        cell.Opacity = 0.07;
                        break;
                }

                if (day == today)
                {
                    cell.Stroke = StrongProbe.Foreground;
                    cell.StrokeThickness = 1.5;
                    cell.Opacity = 1;
                    if (status is DayStatus.Open or DayStatus.Rest or DayStatus.Off && habit.CountOn(day) == 0)
                    {
                        cell.Fill = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                        cell.StrokeDashArray = status == DayStatus.Rest ? [2, 2] : null;
                    }
                }

                string description = $"{day.ToString("D", culture)}: {Describe(status, habit, day)}";
                ToolTipService.SetToolTip(cell, description);
                AutomationProperties.SetName(cell, description);
                Grid.SetRow(cell, row + 1);
                Grid.SetColumn(cell, week + 1);
                HeatGrid.Children.Add(cell);
            }
        }
    }

    private static string Describe(DayStatus status, Habit habit, DateOnly day) => status switch
    {
        DayStatus.Met => habit.IsCounted ? $"{habit.CountOn(day)} of {habit.DailyTarget}, done" : "done",
        DayStatus.Partial => $"{habit.CountOn(day)} of {habit.DailyTarget}",
        DayStatus.Open => "today, still to come",
        DayStatus.Missed => "a pause",
        DayStatus.Rest => "rest day",
        _ => "day off",
    };

    private void BuildLegend(Habit habit)
    {
        Legend.Children.Clear();
        AddLegend(new Rectangle { Fill = FaintProbe.Foreground, Opacity = 0.22 }, "Pause");
        AddLegend(new Rectangle { Stroke = FaintProbe.Foreground, StrokeThickness = 1, StrokeDashArray = [2, 2] }, "Rest");
        AddLegend(new Rectangle { Fill = HabitColors.Brush(habit.ColorIndex, 0.45) }, "Partway");
        AddLegend(new Rectangle { Fill = HabitColors.Brush(habit.ColorIndex) }, "Done");
    }

    private void AddLegend(Rectangle swatch, string label)
    {
        swatch.Width = 10;
        swatch.Height = 10;
        swatch.RadiusX = 2;
        swatch.RadiusY = 2;
        swatch.VerticalAlignment = VerticalAlignment.Center;
        Legend.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                swatch,
                new TextBlock { Text = label, FontSize = 11, Foreground = TertiaryProbe.Foreground, VerticalAlignment = VerticalAlignment.Center },
            },
        });
    }

    private TextBlock AddText(string text, int row, int column, Brush foreground)
    {
        TextBlock block = new()
        {
            Text = text,
            FontSize = 10,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        HeatGrid.Children.Add(block);
        return block;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => _host.GoBack();

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (HabitStore.Find(_habitId) is { } habit)
        {
            _host.Navigate(new HabitEditorPage(_host, HabitStore.Clone(habit)));
        }
    }

    private void RestButton_Click(object sender, RoutedEventArgs e)
    {
        HabitStore.ToggleRestToday(_habitId);
        Refresh();
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        HabitStore.Log(_habitId, -1);
        Refresh();
    }
}
