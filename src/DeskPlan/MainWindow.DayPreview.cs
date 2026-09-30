using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DeskPlan.Models;
using DeskPlan.Services;

namespace DeskPlan;

public partial class MainWindow
{
    private readonly DispatcherTimer _dayOpenTimer = new() { Interval = TimeSpan.FromMilliseconds(380) };
    private readonly DispatcherTimer _dayCloseTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private Border? _hoverDay;
    private Border? _previewDay;
    private DateOnly _previewDate;

    private void InitializeDayPreview()
    {
        _dayOpenTimer.Tick += (_, _) =>
        {
            _dayOpenTimer.Stop();
            if (_expanded && _hoverDay is { IsMouseOver: true } day
                && Mouse.LeftButton == MouseButtonState.Released)
                ShowDayPreview(day);
        };
        _dayCloseTimer.Tick += (_, _) =>
        {
            _dayCloseTimer.Stop();
            // Moving to another date starts a new open delay. Do not cancel it with
            // the earlier date's shorter close delay.
            if (!DayPreviewCard.IsMouseOver && _previewDay?.IsMouseOver != true
                && _hoverDay?.IsMouseOver != true) HideDayPreview();
        };
        DayPreviewCard.MouseEnter += (_, _) =>
        {
            _dayCloseTimer.Stop();
            _dayOpenTimer.Stop();
        };
        DayPreviewCard.MouseLeave += (_, _) => ScheduleDayPreviewClose();
        Deactivated += (_, _) => HideDayPreview();
        SizeChanged += (_, _) => HideDayPreview();
        IsVisibleChanged += (_, _) => { if (!IsVisible) HideDayPreview(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DayPreviewLayer.Visibility == Visibility.Visible)
            {
                HideDayPreview();
                e.Handled = true;
            }
        };
        PreviewMouseDown += (_, _) =>
        {
            if (DayPreviewLayer.Visibility == Visibility.Visible && !DayPreviewCard.IsMouseOver)
                HideDayPreview();
        };
    }

    private void DayCell_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hoverDay = (Border)sender;
        if (_previewDay == _hoverDay && DayPreviewLayer.Visibility == Visibility.Visible)
        {
            _dayCloseTimer.Stop();
            return;
        }
        _dayOpenTimer.Stop();
        _dayOpenTimer.Start();
    }

    private void DayCell_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_hoverDay == sender)
        {
            _hoverDay = null;
            _dayOpenTimer.Stop();
        }
        ScheduleDayPreviewClose();
    }

    private void ScheduleDayPreviewClose()
    {
        _dayCloseTimer.Stop();
        _dayCloseTimer.Start();
    }

    private void ShowDayPreview(Border day)
    {
        if (day.Tag is not DateOnly date || ExpandedPanel.ActualWidth < 1 || ExpandedPanel.ActualHeight < 1) return;
        _dayCloseTimer.Stop();
        _previewDay = day;
        _previewDate = date;
        var tasks = _repository.GetTasksForDate(date).OrderByDescending(x => x.IsMultiDay).ToList();
        DayPreviewTitle.Text = date.ToString("M月d日 · dddd");
        DayPreviewCount.Text = $"共 {tasks.Count} 项 · 未完成 {tasks.Count(x => !x.IsDone)} 项";
        DayPreviewList.ItemsSource = tasks;
        DayPreviewEmpty.Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DayPreviewList.Visibility = tasks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var origin = day.TranslatePoint(new System.Windows.Point(), DayPreviewLayer);
        var anchor = new System.Drawing.RectangleF((float)origin.X, (float)origin.Y, (float)day.ActualWidth, (float)day.ActualHeight);
        var viewport = new System.Drawing.SizeF((float)ExpandedPanel.ActualWidth, (float)ExpandedPanel.ActualHeight);
        // A newly visible WPF ListBox is measured again after binding creates its rows.
        // Fix the card's outer size before positioning so that second layout cannot push it off-screen.
        var desiredHeight = tasks.Count == 0 ? 190 : Math.Min(540, 180 + tasks.Count * 120);
        var placement = DayPreviewLayout.Place(anchor, viewport, new System.Drawing.SizeF(420, desiredHeight));
        DayPreviewCard.Width = placement.Width;
        DayPreviewCard.Height = placement.Height;
        DayPreviewCard.MaxHeight = placement.Height;
        DayPreviewLayer.Visibility = Visibility.Visible;
        Canvas.SetLeft(DayPreviewCard, placement.Left);
        Canvas.SetTop(DayPreviewCard, placement.Top);
        // Each date starts at its first task, even after scrolling through another day.
        if (tasks.Count > 0) DayPreviewList.ScrollIntoView(tasks[0]);
    }

    private void HideDayPreview()
    {
        _dayOpenTimer.Stop();
        _dayCloseTimer.Stop();
        _hoverDay = _previewDay = null;
        DayPreviewLayer.Visibility = Visibility.Collapsed;
        DayPreviewList.ItemsSource = null;
    }

    private void DayPreviewClose_Click(object sender, RoutedEventArgs e) => HideDayPreview();

    private void DayPreviewEdit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TaskItem task) return;
        HideDayPreview();
        EditTask(task);
    }

    private void DayPreviewAdd_Click(object sender, RoutedEventArgs e)
    {
        _selectedDate = _previewDate;
        HideDayPreview();
        RenderMonth();
        AddDetailed();
    }
}
