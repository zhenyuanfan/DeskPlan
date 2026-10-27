using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskPlan;
using DeskPlan.Models;
using DeskPlan.Services;

// In-process, offscreen WPF rendering: no Window.Show, input injection, power
// changes, tray icon, startup registration or access to the personal database.
internal static class Program
{
    private static readonly List<object> Results = [];
    private static readonly List<string> Failures = [];
    private static string _output = "";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static int Main(string[] args)
    {
        _output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/visual-checks");
        Directory.CreateDirectory(_output);
        var qa = Path.Combine(_output, "DeskPlan-QA-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("DESKPLAN_DATA_DIR", qa);
        var app = new App();
        app.InitializeComponent(); // Do not Run: production OnStartup must never execute.
        var repository = new TaskRepository();
        repository.Initialize();
        var today = DateOnly.FromDateTime(DateTime.Today);
        Seed(repository, today);

        foreach (var light in new[] { true, false })
        {
            ThemeService.ApplyTheme(app.Resources, light);
            var theme = light ? "light" : "dark";
            var main = new MainWindow(repository, new AppSettings { StartWithWindows = false });
            StopTimers(main);
            Field(main, "_loaded", true);
            Field(main, "_expanded", false);
            Call(main, "ReloadAll");
            Capture(main, $"{theme}-compact", 380, 360, 1.5, () =>
            {
                Inside(main, "CompactAddBox");
                TextFits(main, "TodayDateText");
                TextFits(main, "RemainingText");
                ButtonsFit(main);
            });
            Capture(main, $"{theme}-compact-small", 370, 330, 1, () => ButtonsFit(main));
            Capture(main, $"{theme}-compact-2x", 380, 360, 2, () => ButtonsFit(main));

            Field(main, "_expanded", true);
            Named<FrameworkElement>(main, "CompactPanel").Visibility = Visibility.Collapsed;
            Named<FrameworkElement>(main, "ExpandedPanel").Visibility = Visibility.Visible;
            Call(main, "RenderMonth");
            Capture(main, $"{theme}-month", 1080, 700, 1, () =>
            {
                ButtonsFit(main);
                TextFits(main, "MonthTitleText");
                Inside(main, "MonthAddBox");
                var calendar = Named<Grid>(main, "MonthHost").Children.OfType<ScrollViewer>().Single();
                if (((UniformGrid)calendar.Content).Rows == 5)
                    Expect(calendar.ScrollableHeight < 2, "Normal-size five-week month should fit without outer scrolling.");
            });
            var todayCell = Descendants((Visual)main.Content).OfType<Border>()
                .Single(x => x.Tag is DateOnly date && date == today);
            Call(main, "ShowDayPreview", todayCell);
            Capture(main, $"{theme}-day-detail", 1080, 700, 1.25, () =>
            {
                Inside(main, "DayPreviewCard");
                ButtonsFit(main);
                var list = Named<ListBox>(main, "DayPreviewList");
                Expect(list.Items.Count >= 6, "Detail did not bind all sample tasks.");
                var scroll = Descendants(list).OfType<ScrollViewer>().First();
                Expect(scroll.ScrollableHeight > 0, "Long task list is not scrollable.");
            });
            Call(main, "HideDayPreview");
            Field(main, "_displayMonth", new DateOnly(2026, 8, 1)); // six-week month
            Call(main, "RenderMonth");
            Named<Button>(main, "ReviewButton").Content = "待整理 999";
            Named<Border>(main, "ReviewBanner").Visibility = Visibility.Visible;
            Named<TextBlock>(main, "ReviewText").Text = "有 999 项之前的工作还没整理，点击右上角“待整理”处理";
            Capture(main, $"{theme}-month-small-six-weeks", 760, 480, 1.5, () =>
            {
                ButtonsFit(main);
                TextFits(main, "MonthTitleText");
                var cells = Descendants((Visual)main.Content).OfType<Border>().Where(x => x.Tag is DateOnly).ToList();
                Expect(cells.Count == 42, "Six-week month did not generate 42 dates.");
                Expect(cells.All(x => x.ActualHeight >= 76), "Short calendar crushes date/task rows.");
            });
            var shortCalendar = Named<Grid>(main, "MonthHost").Children.OfType<ScrollViewer>().Single();
            Expect(shortCalendar.ScrollableHeight > 0, "Short six-week calendar should scroll.");
            shortCalendar.ScrollToEnd();
            Capture(main, $"{theme}-month-small-bottom", 760, 480, 1, () =>
            {
                ButtonsFit(main);
                Expect(shortCalendar.VerticalOffset > 0, "Last week is not reachable.");
                var lastWeek = Descendants((Visual)main.Content).OfType<Border>()
                    .Single(x => x.Tag is DateOnly day && day == new DateOnly(2026, 8, 31));
                Inside(main, lastWeek);
            });
            Named<Border>(main, "ReviewBanner").Visibility = Visibility.Collapsed;
            Capture(main, $"{theme}-month-six-weeks", 1080, 700, 1, () =>
                Expect(shortCalendar.ScrollableHeight < 2, "Normal six-week month should fit without outer scrolling."));
            StopTimers(main);

            var settings = new SettingsWindow(new AppSettings { StartWithWindows = false });
            Capture(settings, $"{theme}-settings", 444, 481, 1.25, () =>
            {
                ButtonsFit(settings);
                TextFits(settings, "VersionText");
                Expect(Named<TextBlock>(settings, "VersionText").Text.Contains("0.1.3"), "Wrong version badge.");
            });
            var editor = new TaskEditorWindow(new TaskItem
            {
                Title = "示例安排：准备下周项目交付与需求评审材料",
                RepeatKind = RepeatKind.SelectedDays, RepeatStartDate = today,
                RepeatDays = "1,2,3,4,5,6,0", StartMinutes = 540, EndMinutes = 570,
                Notes = "这是虚构的布局验证数据，不包含个人任务。"
            }, today);
            Capture(editor, $"{theme}-editor", 484, 611, 1.25, () => ButtonsFit(editor));
            Capture(editor, $"{theme}-editor-short", 484, 440, 1, () =>
            {
                ButtonsFit(editor);
                var scroll = Descendants((Visual)editor.Content).OfType<ScrollViewer>().First();
                Expect(scroll.ScrollableHeight > 0, "Short editor content should scroll.");
                scroll.ScrollToEnd();
            });
            Capture(editor, $"{theme}-editor-short-bottom", 484, 440, 1, () => ButtonsFit(editor));
            var review = new OverdueReviewWindow(repository, repository.GetTasksForDate(today));
            Capture(review, $"{theme}-review", 624, 421, 1, () => ButtonsFit(review));
            var empty = new OverdueReviewWindow(repository, []);
            Capture(empty, $"{theme}-review-empty", 624, 321, 1, () => ButtonsFit(empty));
        }
        File.WriteAllText(Path.Combine(_output, "report.json"), JsonSerializer.Serialize(new
        {
            mode = "offscreen WPF; fictional data; no desktop input or real sleep",
            scenarios = Results, failures = Failures
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Rendered {Results.Count} scenarios; failures: {Failures.Count}; output: {_output}");
        foreach (var failure in Failures) Console.WriteLine(failure);
        return Failures.Count == 0 ? 0 : 1;
    }

    private static void Seed(TaskRepository repo, DateOnly day)
    {
        for (var i = 0; i < 8; i++) repo.Add(new TaskItem
        {
            Title = i switch { 0 => "整理项目方案", 1 => "设计评审", 2 => "完成本周工作记录",
                3 => "长标题示例：核对下周交付内容并整理反馈意见，确认各环节的责任人与时间",
                _ => $"示例安排 {i + 1} · 跟进事项" },
            ScheduledDate = day, StartMinutes = i == 3 ? null : 540 + i * 30,
            EndMinutes = i == 3 ? null : 565 + i * 30,
            Status = i == 7 ? WorkStatus.Completed : i == 0 ? WorkStatus.InProgress : WorkStatus.Pending,
            DueDate = i == 0 ? day.AddDays(2) : null
        });
        foreach (var date in new[] { new DateOnly(2026, 8, 15), new DateOnly(2026, 8, 31) })
            repo.Add(new TaskItem { Title = "六周月份的样例安排", ScheduledDate = date, StartMinutes = 540 });
    }

    private static void Capture(Window window, string name, double width, double height, double scale, Action assertions)
    {
        var root = (FrameworkElement)window.Content;
        // Inherit the same window text settings without creating a native window.
        TextElement.SetFontFamily(root, window.FontFamily);
        TextElement.SetForeground(root, window.Foreground);
        root.InvalidateMeasure();
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        root.UpdateLayout();
        try
        {
            Expect(root.ActualWidth <= width + 1 && root.ActualHeight <= height + 1,
                $"Root exceeded requested viewport: {root.ActualWidth} x {root.ActualHeight}");
            assertions();
        }
        catch (Exception ex) { Failures.Add(name + ": " + ex.Message); }
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(_output, name + ".png"))) encoder.Save(file);
        Results.Add(new { name, width, height, scale, actualWidth = root.ActualWidth, actualHeight = root.ActualHeight,
            scrolls = Descendants(root).OfType<ScrollViewer>().Select(s => new { s.Name, s.ScrollableHeight, s.ViewportHeight, s.ExtentHeight }).ToList() });
    }

    private static void Inside(Window window, string name) => Inside(window, Named<FrameworkElement>(window, name));
    private static void Inside(Window window, FrameworkElement element)
    {
        var root = (FrameworkElement)window.Content;
        var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        Expect(element.ActualWidth > 0 && element.ActualHeight > 0, $"Empty element: {element.Name}");
        Expect(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= root.ActualWidth + 1
            && bounds.Bottom <= root.ActualHeight + 1, $"Out of bounds: {element.Name} {bounds}");
    }
    private static void TextFits(Window window, string name)
    {
        var text = Named<TextBlock>(window, name);
        Inside(window, text);
        var formatted = new FormattedText(text.Text, CultureInfo.CurrentCulture, text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, text.Foreground, 1);
        Expect(formatted.WidthIncludingTrailingWhitespace <= text.ActualWidth + 2,
            $"Text clipped: {name} needs {formatted.WidthIncludingTrailingWhitespace:F1}, has {text.ActualWidth:F1}");
    }
    private static void ButtonsFit(Window window)
    {
        var root = (FrameworkElement)window.Content;
        foreach (var button in Descendants(root).OfType<Button>())
        {
            var visible = true;
            var scrollable = false;
            for (DependencyObject? p = button; p is not null && p != root; p = VisualTreeHelper.GetParent(p))
            {
                if (p is UIElement e && e.Visibility != Visibility.Visible) visible = false;
                if (p is ScrollViewer) scrollable = true;
            }
            if (visible && !scrollable) Inside(window, button);
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static T Named<T>(Window window, string name) where T : class => (T)window.FindName(name);
    private static void Field(object instance, string name, object value) => instance.GetType().GetField(name, Private)!.SetValue(instance, value);
    private static void Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, Private)!.Invoke(instance, args);
    private static void StopTimers(MainWindow main)
    {
        foreach (var field in typeof(MainWindow).GetFields(Private))
            if (field.GetValue(main) is DispatcherTimer timer) timer.Stop();
    }
    private static void Expect(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
