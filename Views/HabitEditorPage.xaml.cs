using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TrayNudge.Controls;
using TrayNudge.Core;
using TrayNudge.Services;

namespace TrayNudge.Views;

/// <summary>
/// Creates or edits a habit inside the flyout. Works on a copy, so Back or Cancel changes nothing.
/// New habits start with a row of ideas that fill in every field.
/// </summary>
public sealed partial class HabitEditorPage : Page
{
    private const int MaxTimes = 6;

    private static readonly (int Minutes, string Label)[] Intervals =
    [
        (30, "Every 30 minutes"),
        (45, "Every 45 minutes"),
        (60, "Every hour"),
        (90, "Every 90 minutes"),
        (120, "Every 2 hours"),
        (180, "Every 3 hours"),
        (240, "Every 4 hours"),
    ];

    private static readonly (int Hours, string Label)[] HourSteps =
    [
        (1, "Every hour"),
        (2, "Every 2 hours"),
        (3, "Every 3 hours"),
        (4, "Every 4 hours"),
    ];

    // ReminderBox order, which reads better than the enum's (stored) order.
    private static readonly ReminderMode[] ReminderModes =
        [ReminderMode.Off, ReminderMode.Interval, ReminderMode.Hourly, ReminderMode.Times];

    private static readonly string[] ColorNames = ["Blue", "Teal", "Green", "Amber", "Orange", "Pink", "Violet", "Indigo"];

    private readonly TrayFlyoutWindow _host;
    private readonly Habit _habit;
    private readonly bool _isNew;
    private readonly List<(DayOfWeek Day, ToggleButton Button)> _dayButtons = [];
    private readonly List<Button> _swatches = [];
    private bool _loading;

    public HabitEditorPage(TrayFlyoutWindow host, Habit? habit)
    {
        _host = host;
        _isNew = habit is null;
        _habit = habit ?? new Habit { ColorIndex = HabitStore.Habits.Count % HabitPalette.Colors.Count };
        InitializeComponent();

        TitleText.Text = _isNew ? "New habit" : "Edit habit";
        SaveButton.Content = _isNew ? "Start this habit" : "Save";
        IdeasSection.Visibility = _isNew ? Visibility.Visible : Visibility.Collapsed;
        DeleteButton.Visibility = _isNew ? Visibility.Collapsed : Visibility.Visible;

        foreach ((_, string label) in Intervals)
        {
            IntervalBox.Items.Add(label);
        }

        foreach ((_, string label) in HourSteps)
        {
            EveryHoursBox.Items.Add(label);
        }

        BuildIdeas();
        BuildEmojiGrid();
        BuildColors();
        BuildDays();
        LoadFromHabit();

        Loaded += (_, _) => (_isNew ? (Control)BackButton : NameBox).Focus(FocusState.Programmatic);
    }

    private void BuildIdeas()
    {
        foreach (HabitTemplate template in HabitTemplates.All)
        {
            Button button = new()
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 4, 4),
                Padding = new Thickness(8, 4, 8, 4),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = template.Emoji, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = template.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
                    },
                },
            };
            AutomationProperties.SetName(button, $"Use idea: {template.Name}");
            button.Click += (_, _) =>
            {
                ApplyTemplate(template);
                Motion.Pop(button);
                Motion.Pop(EmojiButton, 1.3f);
            };
            IdeasGrid.Children.Add(button);
        }
    }

    private void BuildEmojiGrid()
    {
        foreach (string emoji in HabitTemplates.Emoji)
        {
            Button button = new()
            {
                Width = 36,
                Height = 36,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content = new TextBlock { Text = emoji, FontSize = 18 },
            };
            AutomationProperties.SetName(button, emoji);
            button.Click += (_, _) =>
            {
                _habit.Emoji = emoji;
                EmojiText.Text = emoji;
                EmojiFlyout.Hide();
                Motion.Pop(EmojiButton, 1.3f);
            };
            EmojiGrid.Children.Add(button);
        }
    }

    private void BuildColors()
    {
        for (int i = 0; i < HabitPalette.Colors.Count; i++)
        {
            int index = i;
            Button swatch = new()
            {
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(15),
                Background = HabitColors.Brush(index),
                BorderThickness = new Thickness(0),
                Content = new FontIcon { Glyph = "", FontSize = 12, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) },
            };
            AutomationProperties.SetName(swatch, ColorNames[index]);
            ToolTipService.SetToolTip(swatch, ColorNames[index]);
            swatch.Click += (_, _) =>
            {
                _habit.ColorIndex = index;
                UpdateColors();
                Motion.Pop(swatch, 1.25f);
            };
            _swatches.Add(swatch);
            ColorRow.Children.Add(swatch);
        }
    }

    private void BuildDays()
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        foreach (DayOfWeek day in ScheduleDaysExtensions.WeekOrder(culture))
        {
            ToggleButton button = new()
            {
                Width = 38,
                Height = 32,
                Padding = new Thickness(0),
                Content = culture.DateTimeFormat.GetShortestDayName(day),
            };
            AutomationProperties.SetName(button, culture.DateTimeFormat.GetDayName(day));
            button.Click += (_, _) => UpdateDaysHint();
            _dayButtons.Add((day, button));
            DaysRow.Children.Add(button);
        }
    }

    private void ApplyTemplate(HabitTemplate template)
    {
        Habit fresh = template.Create(HabitStore.Today);
        _habit.Name = fresh.Name;
        _habit.Emoji = fresh.Emoji;
        _habit.DailyTarget = fresh.DailyTarget;
        _habit.Unit = fresh.Unit;
        _habit.ColorIndex = fresh.ColorIndex;
        _habit.Days = fresh.Days;
        _habit.Reminder = fresh.Reminder;
        _habit.IntervalMinutes = fresh.IntervalMinutes;
        _habit.MinutePastHour = fresh.MinutePastHour;
        _habit.EveryHours = fresh.EveryHours;
        _habit.WindowStartMinutes = fresh.WindowStartMinutes;
        _habit.WindowEndMinutes = fresh.WindowEndMinutes;
        _habit.ReminderTimes = fresh.ReminderTimes;
        LoadFromHabit();
    }

    private void LoadFromHabit()
    {
        _loading = true;
        EmojiText.Text = _habit.Emoji;
        NameBox.Text = _habit.Name;
        TargetBox.Value = Math.Max(1, _habit.DailyTarget);
        UnitBox.Text = _habit.Unit;
        UpdateColors();

        foreach ((DayOfWeek day, ToggleButton button) in _dayButtons)
        {
            button.IsChecked = _habit.Days.Includes(day);
        }

        ReminderBox.SelectedIndex = Math.Max(0, Array.IndexOf(ReminderModes, _habit.Reminder));
        int interval = Array.FindIndex(Intervals, i => i.Minutes >= _habit.IntervalMinutes);
        IntervalBox.SelectedIndex = interval < 0 ? Intervals.Length - 1 : interval;
        int hourStep = Array.FindIndex(HourSteps, h => h.Hours >= _habit.EveryHours);
        EveryHoursBox.SelectedIndex = hourStep < 0 ? HourSteps.Length - 1 : hourStep;
        MinuteBox.Value = Math.Clamp(_habit.MinutePastHour, 0, 59);
        FromPicker.Time = TimeSpan.FromMinutes(_habit.WindowStartMinutes);
        UntilPicker.Time = TimeSpan.FromMinutes(_habit.WindowEndMinutes);

        TimesList.Children.Clear();
        foreach (int minutes in _habit.ReminderTimes.Order())
        {
            AddTimeRow(TimeSpan.FromMinutes(minutes));
        }

        _loading = false;
        UpdateTargetHint();
        UpdateDaysHint();
        UpdateReminderPanels();
        UpdateSaveEnabled();
    }

    private void UpdateColors()
    {
        for (int i = 0; i < _swatches.Count; i++)
        {
            bool selected = i == _habit.ColorIndex;
            if (_swatches[i].Content is FontIcon icon)
            {
                icon.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            }

            _swatches[i].BorderThickness = new Thickness(selected ? 2 : 0);
            _swatches[i].BorderBrush = (Brush)NameBox.Foreground;
        }

        EmojiButton.Background = HabitColors.Brush(_habit.ColorIndex, 0.22);
    }

    private void UpdateTargetHint()
    {
        int target = (int)Math.Max(1, double.IsNaN(TargetBox.Value) ? 1 : TargetBox.Value);
        UnitBox.Visibility = target > 1 ? Visibility.Visible : Visibility.Collapsed;
        TargetHint.Text = target > 1
            ? $"Tap once for each one. The day counts when you reach {target}."
            : "A simple yes for the day. One tap and it's done.";
    }

    private void UpdateDaysHint()
    {
        ScheduleDays days = SelectedDays();
        DaysHint.Text = days == ScheduleDays.None
            ? "Pick at least one day."
            : $"{days.Describe()}. Days off never count against a streak.";
        UpdateSaveEnabled();
    }

    private void UpdateReminderPanels()
    {
        ReminderMode mode = SelectedMode();
        IntervalBox.Visibility = mode == ReminderMode.Interval ? Visibility.Visible : Visibility.Collapsed;
        HourlyPanel.Visibility = mode == ReminderMode.Hourly ? Visibility.Visible : Visibility.Collapsed;
        WindowPanel.Visibility = mode is ReminderMode.Interval or ReminderMode.Hourly ? Visibility.Visible : Visibility.Collapsed;
        TimesPanel.Visibility = mode == ReminderMode.Times ? Visibility.Visible : Visibility.Collapsed;
        if (mode == ReminderMode.Times && TimesList.Children.Count == 0 && !_loading)
        {
            AddTimeRow(TimeSpan.FromHours(9));
        }

        AddTimeButton.IsEnabled = TimesList.Children.Count < MaxTimes;
        UpdateHourlyHint();
    }

    // Spells out the result ("9:50, 10:50 … 5:50 PM") so the minute field can't be misread.
    private void UpdateHourlyHint()
    {
        // Inputs can fire while InitializeComponent is still building the page.
        if (_loading || UntilPicker is null || HourlyPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        Habit preview = new()
        {
            Reminder = ReminderMode.Hourly,
            MinutePastHour = SelectedMinute(),
            EveryHours = HourSteps[Math.Max(0, EveryHoursBox.SelectedIndex)].Hours,
            WindowStartMinutes = (int)FromPicker.Time.TotalMinutes,
            WindowEndMinutes = (int)UntilPicker.Time.TotalMinutes,
        };
        List<string> times = ReminderPlanner.SlotsFor(preview).Select(FormatTime).ToList();
        HourlyHint.Text = times.Count switch
        {
            0 => "No nudges fit between those times yet. Try a wider window.",
            <= 4 => $"Nudges at {string.Join(", ", times)}.",
            _ => $"Nudges at {times[0]}, {times[1]} … {times[^1]}.",
        };
    }

    private static string FormatTime(int minutes) =>
        DateTime.Today.AddMinutes(minutes).ToString("t", CultureInfo.CurrentCulture);

    private ReminderMode SelectedMode() => ReminderModes[Math.Max(0, ReminderBox.SelectedIndex)];

    private int SelectedMinute() => (int)Math.Clamp(double.IsNaN(MinuteBox.Value) ? 0 : MinuteBox.Value, 0, 59);

    private void UpdateSaveEnabled() =>
        SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text) && SelectedDays() != ScheduleDays.None;

    private ScheduleDays SelectedDays() => _dayButtons
        .Where(d => d.Button.IsChecked == true)
        .Aggregate(ScheduleDays.None, (days, d) => days | d.Day.ToFlag());

    private void AddTimeRow(TimeSpan time)
    {
        Grid row = new() { ColumnSpacing = 4 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TimePicker picker = new() { Time = time, MinuteIncrement = 5, HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
        AutomationProperties.SetName(picker, "Nudge time");
        Button remove = new()
        {
            Style = (Style)Application.Current.Resources["FlyoutIconButtonStyle"],
            Content = new FontIcon { Glyph = "", FontSize = 12 },
        };
        AutomationProperties.SetName(remove, "Remove this time");
        ToolTipService.SetToolTip(remove, "Remove this time");
        remove.Click += (_, _) =>
        {
            TimesList.Children.Remove(row);
            AddTimeButton.IsEnabled = TimesList.Children.Count < MaxTimes;
        };
        Grid.SetColumn(remove, 1);
        row.Children.Add(picker);
        row.Children.Add(remove);
        TimesList.Children.Add(row);
        AddTimeButton.IsEnabled = TimesList.Children.Count < MaxTimes;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _habit.Name = NameBox.Text.Trim();
        _habit.DailyTarget = (int)Math.Clamp(double.IsNaN(TargetBox.Value) ? 1 : TargetBox.Value, 1, 99);
        _habit.Unit = _habit.DailyTarget > 1 ? UnitBox.Text.Trim() : "";
        _habit.Days = SelectedDays();
        _habit.Reminder = SelectedMode();
        _habit.IntervalMinutes = Intervals[Math.Max(0, IntervalBox.SelectedIndex)].Minutes;
        _habit.EveryHours = HourSteps[Math.Max(0, EveryHoursBox.SelectedIndex)].Hours;
        _habit.MinutePastHour = SelectedMinute();
        _habit.WindowStartMinutes = (int)FromPicker.Time.TotalMinutes;
        _habit.WindowEndMinutes = Math.Max(_habit.WindowStartMinutes, (int)UntilPicker.Time.TotalMinutes);
        _habit.ReminderTimes = TimesList.Children
            .OfType<Grid>()
            .Select(row => (int)row.Children.OfType<TimePicker>().First().Time.TotalMinutes)
            .Distinct()
            .Order()
            .ToList();
        if (_habit.Reminder == ReminderMode.Times && _habit.ReminderTimes.Count == 0)
        {
            _habit.Reminder = ReminderMode.Off;
        }

        if (_isNew)
        {
            _habit.CreatedOn = HabitStore.Today;
        }

        HabitStore.Upsert(_habit);
        _host.GoBack();
        if (_isNew)
        {
            _host.MainPage.Welcome(_habit);
        }
    }

    private void ConfirmDelete_Click(object sender, RoutedEventArgs e)
    {
        DeleteFlyout.Hide();
        NotificationService.Remove(_habit.Id);
        HabitStore.Delete(_habit.Id);
        _host.ShowMainPage();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => _host.GoBack();

    // Three ideas per row whatever the text scale or scrollbar takes.
    private void IdeasGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double width = Math.Floor(((FrameworkElement)IdeasGrid.Parent).ActualWidth / 3);
        if (width > 0 && Math.Abs(width - IdeasGrid.ItemWidth) > 0.5)
        {
            IdeasGrid.ItemWidth = width;
        }
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        DeletePrompt.Text = $"Delete {NameBox.Text.Trim()}?";
        UpdateSaveEnabled();
    }

    // Both can fire while InitializeComponent is still building the page.
    private void TargetBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (UnitBox is not null)
        {
            UpdateTargetHint();
        }
    }

    private void ReminderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AddTimeButton is not null)
        {
            UpdateReminderPanels();
        }
    }

    private void EveryHoursBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateHourlyHint();

    private void MinuteBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => UpdateHourlyHint();

    private void WindowPicker_SelectedTimeChanged(TimePicker sender, TimePickerSelectedValueChangedEventArgs args) => UpdateHourlyHint();

    private void AddTimeButton_Click(object sender, RoutedEventArgs e)
    {
        TimeSpan last = TimesList.Children.OfType<Grid>().LastOrDefault()?.Children.OfType<TimePicker>().First().Time ?? TimeSpan.FromHours(9);
        AddTimeRow(TimeSpan.FromMinutes(Math.Min(23 * 60 + 55, last.TotalMinutes + 180)));
    }
}
