using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using SecretManager.Core.Models;

namespace SecretManager.App.ViewModels;

public sealed class AssociationViewModel(SecretAssociation model) : ObservableObject
{
    private bool expanded;
    public SecretAssociation Model { get; } = model;
    public string Title => (Model.IsFavorite ? "★ " : "") + Model.Title;
    public string Initial => Title[..1].ToUpperInvariant();
    public string Subtitle => $"{Model.Fields.Count} полей · изменено {Model.UpdatedAt.ToLocalTime():dd.MM.yyyy}" +
        (Model.Tags.Count > 0 ? " · " + string.Join(", ", Model.Tags) : "") +
        (Model.ExpiresAt is { } expires ? $" · срок {expires.ToLocalTime():d}" : "");
    public ObservableCollection<FieldViewModel> Fields { get; } = new(model.Fields.Select(f => new FieldViewModel(f)));
    public bool IsExpanded { get => expanded; set => SetProperty(ref expanded, value); }
}
