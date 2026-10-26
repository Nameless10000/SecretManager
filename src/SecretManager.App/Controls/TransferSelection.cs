using SecretManager.Core.Models;

namespace SecretManager.App.Controls;

public sealed class TransferSelection : UserControl, IDisposable
{
    internal TreeView Tree { get; } = new() { SelectionMode = TreeViewSelectionMode.Multiple, CanDragItems = false, CanReorderItems = false, MaxHeight = 390 };
    private readonly Dictionary<TreeViewNode, Guid> ids = [];
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap };
    public bool HasSelection => Tree.SelectedNodes.Count != 0 || Tree.RootNodes.Count == 0;
    public event Action? SelectionChanged;

    public TransferSelection(VaultDocument document)
    {
        var panel = new StackPanel { Spacing = 12, MinWidth = 360, MaxWidth = 600 };
        panel.Children.Add(new TextBlock { Text = "Отметьте ассоциации целиком или раскройте их и выберите отдельные поля. Значения полей здесь не показываются.", TextWrapping = TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var all = new Button { Content = "Выбрать всё" }; var none = new Button { Content = "Снять выбор" };
        all.Click += (_, _) => { Tree.SelectAll(); UpdateSummary(); }; none.Click += (_, _) => ClearSelection();
        actions.Children.Add(all); actions.Children.Add(none); panel.Children.Add(actions);
        foreach (var topic in document.Associations)
        {
            var node = new TreeViewNode { Content = topic.Title }; ids[node] = topic.Id;
            foreach (var field in topic.Fields)
            {
                var child = new TreeViewNode { Content = field.Description }; ids[child] = field.Id; node.Children.Add(child);
            }
            Tree.RootNodes.Add(node);
        }
        Tree.SelectionChanged += (_, _) => UpdateSummary();
        panel.Children.Add(Tree); panel.Children.Add(summary); Content = panel;
        Loaded += SelectInitially;
    }

    private void SelectInitially(object sender, RoutedEventArgs e)
    { Loaded -= SelectInitially; Tree.SelectAll(); UpdateSummary(); }

    public Dictionary<Guid, IReadOnlySet<Guid>> Capture()
    {
        var selected = Tree.SelectedNodes.ToHashSet();
        var result = new Dictionary<Guid, IReadOnlySet<Guid>>();
        foreach (var node in Tree.RootNodes)
        {
            var fields = node.Children.Where(selected.Contains).Select(n => ids[n]).ToHashSet();
            if (selected.Contains(node) || fields.Count > 0) result[ids[node]] = fields;
        }
        return result;
    }
    internal void ClearSelection() { Tree.SelectedNodes.Clear(); UpdateSummary(); }
    internal void UpdateSummary()
    {
        var selection = Capture();
        summary.Text = $"Выбрано ассоциаций: {selection.Count}; полей: {selection.Values.Sum(f => f.Count)}";
        SelectionChanged?.Invoke();
    }
    public void Dispose()
    { Loaded -= SelectInitially; Tree.SelectedNodes.Clear(); Tree.RootNodes.Clear(); ids.Clear(); }
}
