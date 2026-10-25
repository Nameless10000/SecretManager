using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
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
    private string status = "";
    private int generation;
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
    public string Status { get => status; set => SetProperty(ref status, value); }
    public string Query { get => query; set { if (SetProperty(ref query, value) && IsUnlocked) Refresh(); } }
    public AppSettings Settings { get => settings; private set => SetProperty(ref settings, value); }
    public ObservableCollection<AssociationViewModel> Associations { get; } = [];
    public int TotalCount => IsUnlocked ? session.Document.Associations.Count : 0;
    public string CountLabel => $"{Associations.Count} из {TotalCount} ассоциаций";
    public IRelayCommand LockCommand { get; }

    public async Task UnlockAsync(string password, string confirmation)
    {
        if (IsBusy) return;
        IsBusy = true;
        var attempt = ++generation;
        try
        {
            if (!HasVault && password != confirmation) throw new ArgumentException("Пароли не совпадают.");
            await Task.Run(() => { if (HasVault) session.Unlock(password); else session.Create(password); });
            if (attempt != generation) { session.Lock(); return; }
            HasVault = true; Settings = session.Document.Settings;
            IsUnlocked = true; Query = ""; Refresh(); Status = "Хранилище открыто";
        }
        finally { IsBusy = false; }
    }

    public void Lock()
    {
        generation++;
        session.Lock(); Associations.Clear(); query = "";
        OnPropertyChanged(nameof(Query)); OnPropertyChanged(nameof(TotalCount)); OnPropertyChanged(nameof(CountLabel));
        IsUnlocked = false; Status = "Хранилище заблокировано";
    }

    public void Refresh()
    {
        var expanded = Associations.Where(a => a.IsExpanded).Select(a => a.Model.Id).ToHashSet();
        Associations.Clear();
        foreach (var a in session.Search(Query)) Associations.Add(new(a) { IsExpanded = expanded.Contains(a.Id) });
        OnPropertyChanged(nameof(TotalCount)); OnPropertyChanged(nameof(CountLabel));
    }

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
    { session.Save(session.Document with { Settings = next }); Settings = next; }
    public Task ChangePasswordAsync(string oldPassword, string newPassword) => Task.Run(() => session.ChangePassword(oldPassword, newPassword));
    public Task<string> ExportAsync(string password) => Task.Run(() => session.Export(password));
    public IReadOnlyList<ImportConflict> FindConflicts(VaultDocument imported) => VaultMerge.FindConflicts(session.Document, imported);
    public void Import(VaultDocument imported, IReadOnlyDictionary<Guid, ConflictResolution> choices)
    { session.Save(VaultMerge.Merge(session.Document, imported, choices)); Refresh(); }
    public void Dispose() => session.Dispose();
}
