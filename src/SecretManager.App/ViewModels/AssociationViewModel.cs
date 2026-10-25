using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using SecretManager.Core.Models;

namespace SecretManager.App.ViewModels;

public sealed class FieldViewModel(SecretField model)
{
    public SecretField Model { get; } = model;
    public string Description => Model.Description;
    public string DisplayValue => Model.Kind switch
    {
        FieldKind.Secret => "••••••••••••",
        FieldKind.Image => "Изображение · " + Model.FileName,
        FieldKind.File => "Файл · " + Model.FileName,
        _ => Model.Value.Length > 150 ? Model.Value[..150] + "…" : Model.Value
    };
    public bool IsImage => Model.Kind == FieldKind.Image;
    public string Icon => Model.Kind switch { FieldKind.Secret => "\uE72E", FieldKind.Image => "\uEB9F", FieldKind.File => "\uE8A5", _ => "\uE8D2" };
}

public sealed class AssociationViewModel(SecretAssociation model) : ObservableObject
{
    private bool expanded;
    public SecretAssociation Model { get; } = model;
    public string Title => Model.Title;
    public string Initial => Title[..1].ToUpperInvariant();
    public string Subtitle => $"{Model.Fields.Count} полей · изменено {Model.UpdatedAt.ToLocalTime():dd.MM.yyyy}";
    public ObservableCollection<FieldViewModel> Fields { get; } = new(model.Fields.Select(f => new FieldViewModel(f)));
    public bool IsExpanded { get => expanded; set => SetProperty(ref expanded, value); }
}
