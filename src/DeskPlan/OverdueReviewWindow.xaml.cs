using System.Collections.ObjectModel;
using System.Windows;
using DeskPlan.Models;
using DeskPlan.Services;

namespace DeskPlan;

public partial class OverdueReviewWindow : Window
{
    private readonly TaskRepository _repository;
    private readonly ObservableCollection<TaskItem> _items;
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Today);

    public OverdueReviewWindow(TaskRepository repository, IEnumerable<TaskItem> items)
    {
        InitializeComponent();
        DialogSizing.Attach(this);
        _repository = repository;
        _items = new ObservableCollection<TaskItem>(items);
        TaskList.ItemsSource = _items;
        if (_items.Count > 0) TaskList.SelectedIndex = 0;
    }

    private TaskItem? Selected => TaskList.SelectedItem as TaskItem;

    private void MoveToday_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        _repository.MoveToToday(item, _today);
        _items.Remove(item);
    }

    private void Keep_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        _repository.MarkReviewed(item);
        _items.Remove(item);
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        _repository.UpdateStatus(item, WorkStatus.Skipped);
        _items.Remove(item);
    }

    private void MoveAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items.ToList()) _repository.MoveToToday(item, _today);
        _items.Clear();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
