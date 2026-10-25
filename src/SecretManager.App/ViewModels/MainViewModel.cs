using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using SecretManager.Core.Models;
using SecretManager.Core.Services;
using SecretManager.Core.Storage;

namespace SecretManager.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly VaultSession session;
    private string query = "";
    private bool unlocked;
    private bool busy;
    private bool disposed;
    private string status = "";
    private int generation;
    private Guid? lastExpanded;
    private AppSettings settings = new();
    public MainViewModel(string path)
    {
        session = new VaultSession(new VaultRepository(path));
        HasVault = session.Exists;
        LockCommand = new RelayCommand(Lock);
    }
    public event Action? SessionChanged;
    public bool HasVault { get; private set; }
    public bool IsUnlocked { get => unlocked; private set { SetProperty(ref unlocked, value); SessionChanged?.Invoke(); } }
    public bool IsBusy { get => busy; private set => SetProperty(ref busy, value); }
    public string Status { get => status; set { if (!disposed) SetProperty(ref status, value); } }
    public string Query { get => query; set { if (!disposed && SetProperty(ref query, value) && IsUnlocked) Refresh(); } }
    public AppSettings Settings { get => settings; private set => SetProperty(ref settings, value); }
    public ObservableCollection<AssociationViewModel> Associations { get; } = [];
    public int TotalCount => IsUnlocked ? session.Document.Associations.Count : 0;
    public string CountLabel => $"{Associations.Count} из {TotalCount} ассоциаций";
    public IRelayCommand LockCommand { get; }

    public async Task UnlockAsync(string password, string confirmation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsBusy) return;
        IsBusy = true;
        var attempt = ++generation;
        try
        {
            if (!HasVault && password != confirmation) throw new ArgumentException("Пароли не совпадают.");
            await Task.Run(() => { if (HasVault) session.Unlock(password); else session.Create(password); });
            if (disposed) { session.Lock(); return; }
            // Creation may have committed while a lock request invalidated this
            // attempt. The next login must open the existing vault, not create it.
            HasVault = true; OnPropertyChanged(nameof(HasVault));
            if (attempt != generation) { session.Lock(); SessionChanged?.Invoke(); return; }
            Settings = session.Document.Settings;
            IsUnlocked = true; Query = ""; Refresh(); Status = "Хранилище открыто";
        }
        finally { if (disposed) busy = false; else IsBusy = false; }
    }

    public void Lock()
    {
        if (disposed) return;
        generation++;
        session.Lock();
        // Collection/property bindings read CountLabel synchronously. Publish a consistent
        // locked state before clearing the collection; the session is already closed.
        unlocked = false;
        DetachAssociations(); Associations.Clear(); query = ""; lastExpanded = null;
        OnPropertyChanged(nameof(IsUnlocked));
        OnPropertyChanged(nameof(Query)); OnPropertyChanged(nameof(TotalCount)); OnPropertyChanged(nameof(CountLabel));
        Status = "Хранилище заблокировано";
        SessionChanged?.Invoke();
    }

    public void Refresh()
    {
        var expanded = Associations.Where(a => a.IsExpanded).Select(a => a.Model.Id).ToHashSet();
        DetachAssociations();
        Associations.Clear();
        foreach (var a in session.Search(Query))
        {
            var item = new AssociationViewModel(a);
            item.PropertyChanged += AssociationChanged;
            Associations.Add(item); item.IsExpanded = expanded.Contains(a.Id);
        }
        OnPropertyChanged(nameof(TotalCount)); OnPropertyChanged(nameof(CountLabel));
    }

    private void AssociationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AssociationViewModel.IsExpanded) || sender is not AssociationViewModel { IsExpanded: true } opened) return;
        lastExpanded = opened.Model.Id;
        if (Settings.SingleExpandedAssociation)
            foreach (var item in Associations) if (!ReferenceEquals(item, opened)) item.IsExpanded = false;
    }
    private void DetachAssociations()
    { foreach (var item in Associations) item.PropertyChanged -= AssociationChanged; }

    public void SaveAssociation(SecretAssociation association)
    {
        if (session.Document.Associations.Any(a => a.Id != association.Id && string.Equals(a.Title.Trim(), association.Title.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Ассоциация с таким названием уже существует.");
        var items = session.Document.Associations.Where(a => a.Id != association.Id).Append(association).ToList();
        session.Save(session.Document with { Associations = items }); Refresh();
    }

    public void DeleteAssociation(Guid id)
    { session.Save(session.Document with { Associations = session.Document.Associations.Where(a => a.Id != id).ToList() }); Refresh(); }

    public void SaveField(Guid topicId, SecretField field)
    {
        var topic = session.Document.Associations.Single(a => a.Id == topicId);
        SaveAssociation(topic with { UpdatedAt = DateTimeOffset.UtcNow,
            Fields = topic.Fields.Where(f => f.Id != field.Id).Append(field).ToList() });
    }

    public void DeleteField(Guid topicId, Guid fieldId)
    {
        var topic = session.Document.Associations.Single(a => a.Id == topicId);
        SaveAssociation(topic with { UpdatedAt = DateTimeOffset.UtcNow, Fields = topic.Fields.Where(f => f.Id != fieldId).ToList() });
    }

    public void SaveSettings(AppSettings next)
    {
        session.Save(session.Document with { Settings = next }); Settings = next;
        if (next.SingleExpandedAssociation)
        {
            var keep = Associations.FirstOrDefault(a => a.IsExpanded && a.Model.Id == lastExpanded) ?? Associations.FirstOrDefault(a => a.IsExpanded);
            foreach (var item in Associations) if (!ReferenceEquals(item, keep)) item.IsExpanded = false;
        }
    }
    public Task ChangePasswordAsync(string oldPassword, string newPassword) => Task.Run(() => session.ChangePassword(oldPassword, newPassword));
    public Task<string> ExportAsync(string password) => Task.Run(() => session.Export(password));
    public IReadOnlyList<ImportConflict> FindConflicts(VaultDocument imported) => VaultMerge.FindConflicts(session.Document, imported);
    public void Import(VaultDocument imported, IReadOnlyDictionary<Guid, ConflictResolution> choices)
    { session.Save(VaultMerge.Merge(session.Document, imported, choices)); Refresh(); }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; generation++; unlocked = false; busy = false;
        session.Dispose(); DetachAssociations(); Associations.Clear(); query = ""; lastExpanded = null;
    }
}
