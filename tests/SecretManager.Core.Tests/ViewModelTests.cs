using SecretManager.App.ViewModels;
using SecretManager.Core.Models;
using Xunit;

namespace SecretManager.Core.Tests;

public sealed class ViewModelTests : IDisposable
{
    private const string Password = "viewmodel test password 123";
    private readonly string folder = Path.Combine(Path.GetTempPath(), "SecretManager.ViewModel.Tests", Guid.NewGuid().ToString());
    private string Database => Path.Combine(folder, "vault.db");

    [Fact]
    public async Task CrudSurvivesReopenAndCardsStartCollapsed()
    {
        var topic = new SecretAssociation { Title = "Тестовый сайт" };
        var field = new SecretField { Description = "Логин", Value = "demo@example.test" };
        using (var vm = new MainViewModel(Database))
        {
            Assert.False(vm.IsUnlocked); await vm.UnlockAsync(Password, Password);
            vm.SaveAssociation(topic); vm.SaveField(topic.Id, field);
            Assert.False(vm.Associations[0].IsExpanded);
            vm.Associations[0].IsExpanded = true;
            vm.SaveField(topic.Id, field with { Description = "Новый логин", Value = "edited@example.test" });
            Assert.True(vm.Associations[0].IsExpanded);
            vm.Lock(); Assert.Empty(vm.Associations); Assert.Equal("", vm.Query);
        }
        using var reopened = new MainViewModel(Database);
        await reopened.UnlockAsync(Password, "");
        Assert.False(reopened.Associations[0].IsExpanded);
        Assert.Equal("edited@example.test", reopened.Associations[0].Fields[0].Model.Value);
        reopened.DeleteField(topic.Id, field.Id); Assert.Empty(reopened.Associations[0].Fields);
        reopened.DeleteAssociation(topic.Id); Assert.Empty(reopened.Associations);
    }

    [Fact]
    public async Task SearchAndDuplicateNameValidationWorkThroughViewModel()
    {
        using var vm = new MainViewModel(Database); await vm.UnlockAsync(Password, Password);
        vm.SaveAssociation(new() { Title = "Личная почта" });
        Assert.Throws<ArgumentException>(() => vm.SaveAssociation(new() { Title = "личная ПОЧТА" }));
        vm.Query = "ПОЧ"; Assert.Single(vm.Associations);
        vm.Query = "несуществующее"; Assert.Empty(vm.Associations);
        vm.Lock(); vm.Query = "любая строка"; Assert.Empty(vm.Associations);
    }

    [Theory]
    [InlineData(AppTheme.Light, AnimationLevel.Off)]
    [InlineData(AppTheme.Dark, AnimationLevel.Medium)]
    [InlineData(AppTheme.Midnight, AnimationLevel.Maximum)]
    [InlineData(AppTheme.Forest, AnimationLevel.Medium)]
    [InlineData(AppTheme.Custom, AnimationLevel.Maximum)]
    public async Task AppearanceAndSecuritySettingsPersist(AppTheme theme, AnimationLevel animation)
    {
        using var vm = new MainViewModel(Database); await vm.UnlockAsync(Password, Password);
        var settings = new AppSettings { Theme = theme, AnimationLevel = animation, FontFamily = "Consolas", FontSize = 17,
            CornerRadius = 22, AccentColor = "#12ab34", BackgroundColor = "#111A22", CompactLayout = true,
            AutoLockMinutes = 7, ClipboardClearSeconds = 45, MotionSpeed = 1.5, CloseToTray = false, SingleExpandedAssociation = false };
        vm.SaveSettings(settings); vm.Lock(); await vm.UnlockAsync(Password, "");
        Assert.Equal(settings, vm.Settings);
    }

    [Fact]
    public async Task AccordionCanBeDisabledAndEnablingItKeepsTheLastOpenedCard()
    {
        using var vm = new MainViewModel(Database); await vm.UnlockAsync(Password, Password);
        vm.SaveAssociation(new() { Title = "А" }); vm.SaveAssociation(new() { Title = "Б" });
        Assert.True(vm.Settings.SingleExpandedAssociation);
        vm.Associations[0].IsExpanded = true; vm.Associations[1].IsExpanded = true;
        Assert.False(vm.Associations[0].IsExpanded); Assert.True(vm.Associations[1].IsExpanded);
        vm.SaveSettings(vm.Settings with { SingleExpandedAssociation = false });
        vm.Associations[0].IsExpanded = true; Assert.All(vm.Associations, a => Assert.True(a.IsExpanded));
        vm.SaveSettings(vm.Settings with { SingleExpandedAssociation = true });
        Assert.True(vm.Associations[0].IsExpanded); Assert.False(vm.Associations[1].IsExpanded);
        vm.SaveAssociation(vm.Associations[0].Model with { Title = "А новая" });
        Assert.Single(vm.Associations, a => a.IsExpanded);
        vm.Lock(); await vm.UnlockAsync(Password, ""); Assert.All(vm.Associations, a => Assert.False(a.IsExpanded));
    }

    [Fact]
    public async Task BadPasswordCannotRevealViewModelItems()
    {
        using var vm = new MainViewModel(Database); await vm.UnlockAsync(Password, Password);
        vm.SaveAssociation(new() { Title = "Закрытая тема" }); vm.Lock();
        await Assert.ThrowsAnyAsync<System.Security.Cryptography.CryptographicException>(() => vm.UnlockAsync("bad", ""));
        Assert.False(vm.IsUnlocked); Assert.Empty(vm.Associations);
    }

    [Fact]
    public async Task LockDuringFirstCreationKeepsVaultClosedAndAllowsTheNextLogin()
    {
        using var vm = new MainViewModel(Database);
        var pending = vm.UnlockAsync(Password, Password);
        vm.Lock();
        await pending;
        Assert.False(vm.IsUnlocked); Assert.Empty(vm.Associations);
        Assert.True(vm.HasVault);
        await vm.UnlockAsync(Password, "");
        Assert.True(vm.IsUnlocked);
    }

    [Fact]
    public async Task RepeatedLockDuringPasswordVerificationDoesNotRevealOldItems()
    {
        using var vm = new MainViewModel(Database); await vm.UnlockAsync(Password, Password);
        vm.SaveAssociation(new() { Title = "Синтетическая запись" }); vm.Lock();
        var pending = vm.UnlockAsync(Password, "");
        vm.Lock(); vm.Lock();
        await pending;
        Assert.False(vm.IsUnlocked); Assert.Empty(vm.Associations); Assert.Equal(0, vm.TotalCount);
        await vm.UnlockAsync(Password, "");
        Assert.Single(vm.Associations);
    }

    [Fact]
    public async Task ClosingDuringFirstUnlockCannotReopenOrNotifyDisposedViewModel()
    {
        using var vm = new MainViewModel(Database);
        var pending = vm.UnlockAsync(Password, Password);
        vm.Dispose();
        var notifications = 0;
        vm.PropertyChanged += (_, _) => notifications++;
        vm.SessionChanged += () => notifications++;
        await pending;
        Assert.False(vm.IsUnlocked); Assert.False(vm.IsBusy); Assert.Empty(vm.Associations);
        vm.Query = "late binding update"; vm.Status = "late notification";
        Assert.Equal(0, notifications);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LockNotifiesBindingsOnlyAfterTheLockedStateIsConsistent(bool keyboardPath)
    {
        using var vm = new MainViewModel(Database); await vm.UnlockAsync(Password, Password);
        var topic = new SecretAssociation { Title = "Сохраняется после блокировки", Fields = [new() { Description = "Логин", Value = "demo@example.invalid" }] };
        vm.SaveAssociation(topic);
        var observingLock = true;
        var reads = 0;
        void ReadLikeBinding()
        {
            if (!observingLock) return;
            // XAML reads these immediately when PropertyChanged / CollectionChanged fires.
            var label = vm.CountLabel;
            var total = vm.TotalCount;
            Assert.False(vm.IsUnlocked); Assert.Equal(0, total); Assert.Contains("из 0", label); reads++;
        }
        vm.PropertyChanged += (_, _) => ReadLikeBinding();
        vm.Associations.CollectionChanged += (_, _) => ReadLikeBinding();
        if (keyboardPath) vm.Lock(); else vm.LockCommand.Execute(null);
        vm.Lock(); // Repeated tray / timer requests must also be safe.
        Assert.True(reads > 0); observingLock = false;
        Assert.Empty(vm.Associations);
        await vm.UnlockAsync(Password, "");
        Assert.Equal(topic.Id, Assert.Single(vm.Associations).Model.Id);
        Assert.Equal("demo@example.invalid", vm.Associations[0].Fields[0].Model.Value);
    }

    [Theory]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    public void MalformedCustomThemeIsRejected(string color) => Assert.Throws<InvalidDataException>(() =>
        VaultValidation.Validate(new() { Settings = new() { AccentColor = color } }));

    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}
