using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DeskPlan.Models;
using DeskPlan.Services;

namespace DeskPlan;

public partial class MainWindow : Window
{
    private readonly TaskRepository _repository;
    private readonly ReminderService _reminderService;
    private readonly AppSettings _settings;
    private readonly bool _forceExpanded;
    private readonly ObservableCollection<TaskItem> _todayTasks = [];
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _notificationTimer;
    private readonly DispatcherTimer _settingsSaveTimer;
    private readonly DispatcherTimer _undoTimer;
    private DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly _displayMonth;
    private DateOnly _loadedToday;
    private bool _expanded;
    private bool _loaded;
    private bool _positioning;
    private bool _fitQueued;
    private System.Windows.Point _dragStart;
    private Action? _undoAction;

    public MainWindow(TaskRepository repository, AppSettings settings, bool forceExpanded = false)
    {
        InitializeComponent();
        InitializeDayPreview();
        _repository = repository;
        _reminderService = new ReminderService(repository);
        _settings = settings;
        _forceExpanded = forceExpanded;
        _displayMonth = new DateOnly(_selectedDate.Year, _selectedDate.Month, 1);
        CompactTaskList.ItemsSource = _todayTasks;

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateCompactHeader();
        _clockTimer.Start();

        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _notificationTimer.Tick += (_, _) => CheckNotifications();
        _notificationTimer.Start();

        _settingsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _settingsSaveTimer.Tick += (_, _) =>
        {
            _settingsSaveTimer.Stop();
            SettingsService.Save(_settings);
        };

        _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _undoTimer.Tick += (_, _) => ClearUndo();

        Loaded += (_, _) =>
        {
            _loaded = true;
            MigrateSavedPosition();
            SetExpanded(_forceExpanded || _settings.StartExpanded, false);
            ReloadAll();
            CheckNotifications();
        };
    }

    public TrayService? Tray { get; set; }
    public bool AllowRealClose { get; set; }

    public void ShowAndLocate()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (!IsVisible) Show();
        QueueFitToScreen();
        Activate();
    }

    public void OpenSettings()
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        SettingsService.Save(_settings);
    }

    private void MigrateSavedPosition()
    {
        if (_settings.PositionSchema >= 5) return;
        if (!double.IsNaN(_settings.Left) && !double.IsNaN(_settings.Top))
        {
            _settings.PixelLeft = (int)Math.Round(_settings.Left);
            _settings.PixelTop = (int)Math.Round(_settings.Top);
        }
        _settings.PositionSchema = 5;
        SettingsService.Save(_settings);
    }

    private void ReloadAll()
    {
        ReloadToday();
        RenderMonth();
        UpdateReviewBanner();
    }

    private void ReloadToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        _loadedToday = today;
        _todayTasks.Clear();
        foreach (var task in _repository.GetTasksForDate(today)) _todayTasks.Add(task);
        TodayEmptyText.Visibility = _todayTasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCompactHeader();
    }

    private void UpdateCompactHeader()
    {
        var now = DateTime.Now;
        if (_loaded && _loadedToday != DateOnly.FromDateTime(now))
        {
            ReloadAll();
            return;
        }
        ClockText.Text = now.ToString("HH:mm");
        TodayDateText.Text = now.ToString("M月d日 dddd");
        RemainingText.Text = $"剩余 {_todayTasks.Count(x => !x.IsDone)} 项";
        if (!ChineseHolidayCalendar.HasYear(now.Year)) RemainingText.Text += " · 节假日待更新";
        RemainingText.ToolTip = ChineseHolidayCalendar.CoverageText(now.Year);
    }

    private void RenderMonth()
    {
        if (!_loaded) return;
        HideDayPreview();
        MonthHost.Children.Clear();
        MonthTitleText.Text = _displayMonth.ToString("yyyy年 M月");
        SelectedDateText.Text = $"已选择 {_selectedDate:M月d日}";
        CalendarStatusText.Text = ChineseHolidayCalendar.CoverageText(_displayMonth.Year);
        CalendarStatusText.Foreground = (Brush)FindResource(ChineseHolidayCalendar.HasYear(_displayMonth.Year)
            ? "MutedTextBrush" : "WarningBrush");

        var first = _displayMonth;
        var offset = (int)first.DayOfWeek;
        var start = first.AddDays(-offset);
        var cells = (int)Math.Ceiling((offset + DateTime.DaysInMonth(first.Year, first.Month)) / 7d) * 7;
        cells = Math.Clamp(cells, 35, 42);

        // On a short/high-DPI screen retain room for a date and at least one task.
        // Scroll the calendar instead of squeezing six weeks into unreadable slivers.
        var minimumCalendarHeight = cells / 7 * 84;
        var monthGrid = new UniformGrid
        {
            Columns = 7, Rows = cells / 7,
            Height = Math.Max(minimumCalendarHeight, MonthHost.ActualHeight)
        };
        for (var index = 0; index < cells; index++)
        {
            var date = start.AddDays(index);
            monthGrid.Children.Add(CreateDateCell(date));
        }
        var monthScroll = new ScrollViewer
        {
            Content = monthGrid,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        monthScroll.SizeChanged += (_, e) => monthGrid.Height = Math.Max(minimumCalendarHeight, e.NewSize.Height);
        monthScroll.ScrollChanged += (_, e) =>
        {
            if (e.VerticalChange != 0) HideDayPreview();
        };
        MonthHost.Children.Add(monthScroll);
    }

    private Border CreateDateCell(DateOnly date)
    {
        var isCurrentMonth = date.Month == _displayMonth.Month && date.Year == _displayMonth.Year;
        var isToday = date == DateOnly.FromDateTime(DateTime.Today);
        var tasks = _repository.GetTasksForDate(date);

        var cell = new Border
        {
            Tag = date,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(2),
            Padding = new Thickness(7, 6, 6, 4),
            Cursor = Cursors.Hand,
            Opacity = isCurrentMonth ? 1 : 0.6,
            ClipToBounds = true
        };
        ApplyDateCellAppearance(cell, date);
        cell.MouseLeftButtonDown += DateCell_MouseLeftButtonDown;
        cell.MouseEnter += DayCell_MouseEnter;
        cell.MouseLeave += DayCell_MouseLeave;

        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var dayNumber = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = isToday ? (Brush)FindResource("TodayBrush") : System.Windows.Media.Brushes.Transparent,
            MinWidth = 28,
            Padding = new Thickness(5, 3, 5, 3),
            Child = new TextBlock
            {
                Text = date.Day.ToString(),
                FontSize = 14,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                Foreground = isToday ? System.Windows.Media.Brushes.White : (Brush)FindResource("TextBrush")
            }
        };
        header.Children.Add(dayNumber);
        if (ChineseHolidayCalendar.GetAdjustment(date) is { } holiday)
            header.Children.Add(new TextBlock
            {
                Text = holiday.IsWorkingDay ? "班" : "休", FontSize = 11, Margin = new Thickness(2, 5, 0, 0),
                Foreground = (Brush)FindResource(holiday.IsWorkingDay ? "WarningBrush" : "SuccessBrush"),
                ToolTip = holiday.Name + (holiday.IsWorkingDay ? "调休补班" : "放假，自动重复任务跳过；可手动新建")
            });
        if (tasks.Count > 0)
            header.Children.Add(new TextBlock
            {
                Text = $"{tasks.Count} 项", FontSize = 11, Margin = new Thickness(5, 5, 0, 0),
                Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush")
            });
        Grid.SetRow(header, 0);
        panel.Children.Add(header);

        var taskPanel = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };
        foreach (var task in tasks.OrderByDescending(x => x.IsMultiDay)) taskPanel.Children.Add(CreateMonthTaskLine(task));
        var taskScroll = new ScrollViewer
        {
            Content = taskPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(taskScroll, 1);
        panel.Children.Add(taskScroll);
        cell.Child = panel;
        return cell;
    }

    private void ApplyDateCellAppearance(Border cell, DateOnly date)
    {
        var selected = date == _selectedDate;
        var currentMonth = date.Month == _displayMonth.Month && date.Year == _displayMonth.Year;
        var background = selected ? "AccentSoftBrush" : !currentMonth ? "WindowBackgroundBrush"
            : date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "HeaderBrush" : "CardBrush";
        cell.Background = (Brush)FindResource(background);
        cell.BorderBrush = (Brush)FindResource(selected ? "AccentBrush" : "SoftBorderBrush");
    }

    private FrameworkElement CreateMonthTaskLine(TaskItem task)
    {
        var row = new Grid { Height = 26, Tag = task };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dotColor = task.IsDone
            ? (Brush)FindResource("BorderBrush")
            : task.IsRecurring
                ? (Brush)FindResource("SuccessBrush")
                : task.HasTime
                    ? (Brush)FindResource("AccentBrush")
                    : (Brush)FindResource("WarningBrush");
        var done = new System.Windows.Controls.Button
        {
            Tag = task, Width = 14, Height = 22, Padding = new Thickness(0),
            Content = new Ellipse { Width = 7, Height = 7, Fill = dotColor },
            BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent,
            ToolTip = task.IsDone ? "恢复为待办" : "完成"
        };
        done.Click += CompleteTask_Click;
        Grid.SetColumn(done, 0);
        row.Children.Add(done);

        var time = new TextBlock
        {
            Text = task.CalendarTimeText,
            FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("MutedTextBrush"), Margin = new Thickness(0, 0, 5, 0)
        };
        Grid.SetColumn(time, 1);
        row.Children.Add(time);

        var title = new TextBlock
        {
            Text = task.Title, Tag = task, FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = task.IsDone ? (Brush)FindResource("MutedTextBrush") : (Brush)FindResource("TextBrush")
        };
        if (task.IsDone) title.TextDecorations = TextDecorations.Strikethrough;
        title.MouseLeftButtonDown += MonthTask_MouseLeftButtonDown;
        Grid.SetColumn(title, 2);
        row.Children.Add(title);

        var menu = new System.Windows.Controls.ContextMenu();
        var editItem = new System.Windows.Controls.MenuItem { Header = "编辑" };
        editItem.Click += (_, _) => EditTask(task);
        var deleteItem = new System.Windows.Controls.MenuItem { Header = "删除" };
        deleteItem.Click += (_, _) => DeleteTask(task);
        menu.Items.Add(editItem);
        menu.Items.Add(deleteItem);
        row.ContextMenu = menu;
        return new Border
        {
            Child = row, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 1, 0, 1),
            Background = task.IsMultiDay ? (Brush)FindResource("AccentSoftBrush") : System.Windows.Media.Brushes.Transparent,
            BorderThickness = task.IsMultiDay ? new Thickness(3, 0, 0, 0) : new Thickness(0),
            BorderBrush = (Brush)FindResource("AccentBrush"), Opacity = task.IsDone ? 0.6 : 1
        };
    }

    private void UpdateReviewBanner()
    {
        var overdue = _repository.GetOverdue(DateOnly.FromDateTime(DateTime.Today));
        ReviewBanner.Visibility = overdue.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ReviewText.Text = $"有 {overdue.Count} 项之前的工作还没整理，点击右上角“待整理”处理";
        ReviewButton.Content = overdue.Count == 0 ? "待整理" : $"待整理 {overdue.Count}";
    }

    private void CheckNotifications()
    {
        if (Tray is not { } tray) return;
        try
        {
            _reminderService.CheckAndNotify(DateTime.Now, tray.Notify);
        }
        catch (Exception error)
        {
            // Reminders are a background convenience: a transient system/database
            // failure must not close the planner. The next timer tick can retry.
            // Do not log exception messages, which may contain personal data.
            System.Diagnostics.Trace.TraceWarning("Reminder check failed ({0}); retry on the next tick.",
                error.GetType().Name);
        }
    }

    private void SetExpanded(bool expanded, bool save = true)
    {
        HideDayPreview();
        if (save) RememberPosition();
        _positioning = true;
        WindowState = WindowState.Normal;
        var handle = new WindowInteropHelper(this).Handle;
        GetWindowRect(handle, out var previous);
        var x = save ? previous.Left : _settings.PixelLeft;
        var y = save ? previous.Top : _settings.PixelTop;
        if (!expanded && _settings.CompactLeft is int compactX && _settings.CompactTop is int compactY)
            (x, y) = (compactX, compactY);
        else if (!save && expanded && _settings.ExpandedLeft is int expandedX && _settings.ExpandedTop is int expandedY)
            (x, y) = (expandedX, expandedY);

        var screen = x == int.MinValue || y == int.MinValue
            ? System.Windows.Forms.Screen.PrimaryScreen!
            : System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x, y));
        var area = screen.WorkingArea;
        if (x == int.MinValue || y == int.MinValue)
            (x, y) = (area.Right - 404, area.Top + 24);
        // Move onto the destination monitor before measuring its WPF/physical pixel scale.
        var anchor = WindowPlacement.Fit(new System.Drawing.Rectangle(x, y, 1, 1), area);
        SetWindowPos(handle, IntPtr.Zero, anchor.X, anchor.Y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        _expanded = expanded;
        CompactPanel.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        ExpandedPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ResizeMode = ResizeMode.NoResize;
        ApplyScreenLimits(screen);
        // A calendar is a panel, not a remembered maximized/snap rectangle.
        Width = Math.Clamp(expanded ? 1080 : 380, MinWidth, MaxWidth);
        Height = Math.Clamp(expanded ? 700 : 360, MinHeight, MaxHeight);
        FitToScreen(screen, x, y);
        Dispatcher.BeginInvoke(() =>
        {
            // A monitor DPI transition can finish after the size/layout change.
            FitToScreen(screen, x, y);
            _positioning = false;
            RememberPosition();
        }, DispatcherPriority.Loaded);
        if (save)
        {
            _settings.StartExpanded = expanded;
            QueueSettingsSave();
        }
    }

    private void ApplyScreenLimits(System.Windows.Forms.Screen screen)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var area = screen.WorkingArea;
        MinWidth = MinHeight = 1;
        MaxWidth = Math.Max(1, (area.Width - 16) / dpi.DpiScaleX * (_expanded ? 0.88 : 1));
        MaxHeight = Math.Max(1, (area.Height - 16) / dpi.DpiScaleY * (_expanded ? 0.88 : 1));
        MinWidth = Math.Min(_expanded ? 760 : 370, MaxWidth);
        MinHeight = Math.Min(_expanded ? 480 : 330, MaxHeight);
    }

    private void FitToScreen(System.Windows.Forms.Screen screen, int? x = null, int? y = null)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect)) return;
        ApplyScreenLimits(screen);
        Width = Math.Clamp(Width, MinWidth, MaxWidth);
        Height = Math.Clamp(Height, MinHeight, MaxHeight);
        UpdateLayout();
        GetWindowRect(handle, out rect);
        var fit = WindowPlacement.Fit(new System.Drawing.Rectangle(x ?? rect.Left, y ?? rect.Top,
            rect.Right - rect.Left, rect.Bottom - rect.Top), screen.WorkingArea);
        SetWindowPos(handle, IntPtr.Zero, fit.X, fit.Y, fit.Width, fit.Height, SwpNoZOrder | SwpNoActivate);
    }

    private void QueueFitToScreen()
    {
        if (!_loaded || _positioning || _fitQueued) return;
        _fitQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _fitQueued = false;
            if (_positioning) return;
            _positioning = true;
            FitToScreen(System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle));
            _positioning = false;
            RememberPosition();
        }, DispatcherPriority.ApplicationIdle);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Finished dragging/resizing, monitor layout, work area or DPI changed.
        if (message is 0x0232 or 0x007E or 0x001A or 0x02E0) QueueFitToScreen();
        return IntPtr.Zero;
    }

    private void EditTask(TaskItem task)
    {
        var editor = new TaskEditorWindow(task.Clone(), task.DisplayDate) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result is null) return;
        _repository.UpdateTask(editor.Result);
        ReloadAll();
    }

    private void AddDetailed()
    {
        var item = new TaskItem
        {
            ScheduledDate = _selectedDate, DisplayDate = _selectedDate,
            Status = WorkStatus.Pending,
            SortOrder = _repository.GetTasksForDate(_selectedDate).Count
        };
        var editor = new TaskEditorWindow(item, _selectedDate) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result is null) return;
        _repository.Add(editor.Result);
        ReloadAll();
    }

    private void AddQuick(string title, DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        _repository.AddQuick(title, date);
        ReloadAll();
    }

    private void ShowUndo(string message, Action action)
    {
        _undoAction = action;
        UndoText.Text = message;
        UndoBar.Visibility = Visibility.Visible;
        _undoTimer.Stop();
        _undoTimer.Start();
    }

    private void ClearUndo()
    {
        _undoTimer.Stop();
        _undoAction = null;
        UndoBar.Visibility = Visibility.Collapsed;
    }

    private void QueueSettingsSave()
    {
        if (!_loaded) return;
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void Expand_Click(object sender, RoutedEventArgs e) => SetExpanded(true);
    private void Collapse_Click(object sender, RoutedEventArgs e) => SetExpanded(false);

    private void PreviousMonth_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(-1);
        RenderMonth();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(1);
        RenderMonth();
    }

    private void TodayMonth_Click(object sender, RoutedEventArgs e)
    {
        _selectedDate = DateOnly.FromDateTime(DateTime.Today);
        _displayMonth = new DateOnly(_selectedDate.Year, _selectedDate.Month, 1);
        RenderMonth();
    }

    private void DateCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DateOnly date) return;
        _selectedDate = date;
        if (e.ClickCount >= 2) AddDetailed();
        else
        {
            SelectedDateText.Text = $"已选择 {_selectedDate:M月d日}";
            if (MonthHost.Children.OfType<ScrollViewer>().FirstOrDefault()?.Content is UniformGrid grid)
                foreach (var dayCell in grid.Children.OfType<Border>())
                    if (dayCell.Tag is DateOnly day)
                        ApplyDateCellAppearance(dayCell, day);
        }
        e.Handled = true;
    }

    private void MonthTask_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ClickCount >= 2 && (sender as FrameworkElement)?.Tag is TaskItem task)
        {
            EditTask(task);
            e.Handled = true;
        }
    }

    private void CompleteTask_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TaskItem task) return;
        var old = task.Status;
        var next = task.IsDone ? WorkStatus.Pending : WorkStatus.Completed;
        _repository.UpdateStatus(task, next);
        ReloadAll();
        ShowUndo(next == WorkStatus.Completed ? "任务已完成" : "已恢复为待办", () =>
        {
            _repository.UpdateStatus(task, old);
            ReloadAll();
        });
        e.Handled = true;
    }

    private void DeleteTask(TaskItem task)
    {
        _repository.Delete(task);
        ReloadAll();
        ShowUndo("任务已删除", () =>
        {
            _repository.Restore(task);
            ReloadAll();
        });
    }

    private void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TaskItem task) DeleteTask(task);
        e.Handled = true;
    }

    private void CompactTaskList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CompactTaskList.SelectedItem is TaskItem task) EditTask(task);
    }

    private void CompactTaskList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _dragStart = e.GetPosition(this);

    private void CompactTaskList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || (e.GetPosition(this) - _dragStart).Length < 8) return;
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as TaskItem;
        if (item is not null) DragDrop.DoDragDrop(CompactTaskList, item, DragDropEffects.Move);
    }

    private void CompactTaskList_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(typeof(TaskItem)) is not TaskItem moved) return;
        var target = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as TaskItem;
        var list = _todayTasks.ToList();
        list.RemoveAll(x => x.Id == moved.Id && x.DisplayDate == moved.DisplayDate);
        var index = target is null ? list.Count : Math.Max(0, list.FindIndex(x => x.Id == target.Id));
        list.Insert(index, moved);
        _repository.Reorder(list);
        ReloadAll();
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void CompactAddBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AddQuick(CompactAddBox.Text, DateOnly.FromDateTime(DateTime.Today));
        CompactAddBox.Clear();
        e.Handled = true;
    }

    private void MonthAddBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AddQuick(MonthAddBox.Text, _selectedDate);
        MonthAddBox.Clear();
        e.Handled = true;
    }

    private void AddDetailed_Click(object sender, RoutedEventArgs e) => AddDetailed();
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void Review_Click(object sender, RoutedEventArgs e)
    {
        var overdue = _repository.GetOverdue(DateOnly.FromDateTime(DateTime.Today));
        new OverdueReviewWindow(_repository, overdue) { Owner = this }.ShowDialog();
        ReloadAll();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        var action = _undoAction;
        ClearUndo();
        action?.Invoke();
    }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(e.OriginalSource as DependencyObject) is not null
            || FindAncestor<System.Windows.Controls.TextBox>(e.OriginalSource as DependencyObject) is not null) return;
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
            QueueFitToScreen();
        }
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
        => RememberPosition();

    private void RememberPosition()
    {
        if (!_loaded || _positioning) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            _settings.PreferredScreen = System.Windows.Forms.Screen.FromHandle(handle).DeviceName;
            if (GetWindowRect(handle, out var rect))
            {
                _settings.PixelLeft = rect.Left;
                _settings.PixelTop = rect.Top;
                if (_expanded)
                    (_settings.ExpandedLeft, _settings.ExpandedTop) = (rect.Left, rect.Top);
                else
                    (_settings.CompactLeft, _settings.CompactTop) = (rect.Left, rect.Top);
            }
        }
        _settings.Left = Left;
        _settings.Top = Top;
        QueueSettingsSave();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (_loaded && !_positioning && WindowState == WindowState.Maximized)
            SetExpanded(_expanded, false);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_loaded)
        {
            RememberPosition();
            SettingsService.Save(_settings);
        }
        if (AllowRealClose) return;
        e.Cancel = true;
        Hide();
    }

    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
