using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using DeskPlan.Models;
using DeskPlan.Services;

namespace DeskPlan;

public partial class UnscheduledTasksWindow : Window
{
    private readonly TaskRepository _repository;
    private readonly ObservableCollection<TaskItem> _items = [];
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Today);

    public UnscheduledTasksWindow(TaskRepository repository)
    {
        InitializeComponent();
        DialogSizing.Attach(this);
        _repository = repository;
        TaskList.ItemsSource = _items;
        Reload();
    }

    private TaskItem? Selected => TaskList.SelectedItem as TaskItem;

    private void Reload(long? preferredId = null)
    {
        var selectedId = preferredId ?? Selected?.Id;
        _items.Clear();
        foreach (var item in _repository.GetLaterTasks()) _items.Add(item);
        TaskList.SelectedItem = _items.FirstOrDefault(x => x.Id == selectedId) ?? _items.FirstOrDefault();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var item = new TaskItem
        {
            DisplayDate = _today,
            Status = WorkStatus.Pending,
            SortOrder = _items.Count
        };
        var editor = new TaskEditorWindow(item, _today) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result is null) return;
        var id = _repository.Add(editor.Result);
        Reload(id);
    }

    private void EditSelected()
    {
        if (Selected is not { } item) return;
        var editor = new TaskEditorWindow(item.Clone(), _today) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result is null) return;
        _repository.UpdateTask(editor.Result);
        Reload(item.Id);
    }

    private void Edit_Click(object sender, RoutedEventArgs e) => EditSelected();

    private void TaskList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => EditSelected();

    private void MoveToday_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        _repository.MoveToToday(item, _today);
        Reload();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        _repository.Delete(item);
        Reload();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
