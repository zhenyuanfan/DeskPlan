using System.Windows;
using System.Windows.Controls;
using DeskPlan.Models;

namespace DeskPlan;

public partial class TaskEditorWindow : Window
{
    private readonly TaskItem _item;
    private readonly DateOnly _defaultDate;

    public TaskEditorWindow(TaskItem item, DateOnly defaultDate)
    {
        _item = item;
        _defaultDate = defaultDate;
        InitializeComponent();
        Populate();
        Loaded += (_, _) => TitleBox.Focus();
    }

    public TaskItem? Result { get; private set; }

    private void Populate()
    {
        TitleBox.Text = _item.Title;
        PlanDateCheck.IsChecked = _item.ScheduledDate.HasValue || _item.RepeatStartDate.HasValue;
        PlanDatePicker.SelectedDate = (_item.ScheduledDate ?? _item.RepeatStartDate ?? _defaultDate).ToDateTime(TimeOnly.MinValue);
        TimeCheck.IsChecked = _item.StartMinutes.HasValue;
        StartTimeBox.Text = _item.StartMinutes.HasValue ? TaskItem.FormatMinutes(_item.StartMinutes.Value) : "09:00";
        EndTimeBox.Text = _item.EndMinutes.HasValue ? TaskItem.FormatMinutes(_item.EndMinutes.Value) : "09:30";
        DueCheck.IsChecked = _item.DueDate.HasValue;
        DueDatePicker.SelectedDate = (_item.DueDate ?? _defaultDate).ToDateTime(TimeOnly.MinValue);
        RepeatCombo.SelectedIndex = (int)_item.RepeatKind;
        StatusCombo.SelectedIndex = (int)_item.Status;
        NotesBox.Text = _item.Notes;

        var selectedDays = _item.RepeatDays.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        foreach (var check in DayChecks()) check.IsChecked = selectedDays.Contains(check.Tag?.ToString() ?? string.Empty);

        if (_item.IsOccurrence)
        {
            OccurrenceHint.Visibility = Visibility.Visible;
            RepeatCombo.IsEnabled = false;
            PlanDateCheck.IsEnabled = false;
            PlanDatePicker.IsEnabled = false;
        }
        UpdateEnabledStates();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            MessageBox.Show(this, "请输入任务名称。", "还差一点", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int? start = null;
        int? end = null;
        if (TimeCheck.IsChecked == true)
        {
            if (!TryParseTime(StartTimeBox.Text, out var startValue) || !TryParseTime(EndTimeBox.Text, out var endValue))
            {
                MessageBox.Show(this, "时间请使用 HH:mm 格式，例如 09:30。", "时间格式不正确",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (endValue <= startValue)
            {
                MessageBox.Show(this, "结束时间需要晚于开始时间。", "时间范围不正确",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            start = startValue;
            end = endValue;
        }

        var repeat = (RepeatKind)RepeatCombo.SelectedIndex;
        var planDate = PlanDateCheck.IsChecked == true && PlanDatePicker.SelectedDate.HasValue
            ? DateOnly.FromDateTime(PlanDatePicker.SelectedDate.Value)
            : (DateOnly?)null;

        var dueDate = DueCheck.IsChecked == true && DueDatePicker.SelectedDate.HasValue
            ? DateOnly.FromDateTime(DueDatePicker.SelectedDate.Value) : (DateOnly?)null;
        if (repeat == RepeatKind.None && planDate.HasValue && dueDate < planDate)
        {
            MessageBox.Show(this, "截止日期不能早于开始日期。", "检查日期", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _item.Title = TitleBox.Text.Trim();
        _item.StartMinutes = start;
        _item.EndMinutes = end;
        _item.DueDate = DueCheck.IsChecked == true && DueDatePicker.SelectedDate.HasValue
            ? DateOnly.FromDateTime(DueDatePicker.SelectedDate.Value)
            : null;
        _item.Notes = NotesBox.Text.Trim();
        _item.Status = (WorkStatus)Math.Max(0, StatusCombo.SelectedIndex);

        if (!_item.IsOccurrence)
        {
            _item.RepeatKind = repeat;
            _item.RepeatDays = repeat == RepeatKind.SelectedDays
                ? string.Join(',', DayChecks().Where(x => x.IsChecked == true).Select(x => x.Tag))
                : string.Empty;
            if (repeat == RepeatKind.SelectedDays && string.IsNullOrEmpty(_item.RepeatDays))
            {
                MessageBox.Show(this, "请至少选择一个重复日期。", "还差一点",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _item.ScheduledDate = repeat == RepeatKind.None ? planDate : null;
            _item.RepeatStartDate = repeat == RepeatKind.None ? null : planDate ?? _defaultDate;
        }

        Result = _item;
        DialogResult = true;
    }

    private static bool TryParseTime(string value, out int minutes)
    {
        minutes = 0;
        if (!TimeOnly.TryParseExact(value.Trim(), "HH:mm", out var time)) return false;
        minutes = time.Hour * 60 + time.Minute;
        return true;
    }

    private IEnumerable<System.Windows.Controls.CheckBox> DayChecks()
    {
        yield return MondayCheck; yield return TuesdayCheck; yield return WednesdayCheck;
        yield return ThursdayCheck; yield return FridayCheck; yield return SaturdayCheck; yield return SundayCheck;
    }

    private void UpdateEnabledStates()
    {
        if (PlanDatePicker is null || StartTimeBox is null || EndTimeBox is null || DueDatePicker is null || WeekDaysPanel is null) return;
        PlanDatePicker.IsEnabled = PlanDateCheck.IsChecked == true && !_item.IsOccurrence;
        StartTimeBox.IsEnabled = TimeCheck.IsChecked == true;
        EndTimeBox.IsEnabled = TimeCheck.IsChecked == true;
        DueDatePicker.IsEnabled = DueCheck.IsChecked == true;
        WeekDaysPanel.Visibility = RepeatCombo.SelectedIndex == (int)RepeatKind.SelectedDays
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PlanDateCheck_Changed(object sender, RoutedEventArgs e) => UpdateEnabledStates();
    private void TimeCheck_Changed(object sender, RoutedEventArgs e) => UpdateEnabledStates();
    private void DueCheck_Changed(object sender, RoutedEventArgs e) => UpdateEnabledStates();
    private void RepeatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) UpdateEnabledStates();
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
