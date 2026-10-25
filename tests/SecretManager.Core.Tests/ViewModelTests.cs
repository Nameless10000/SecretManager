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
            AutoLockMinutes = 7, ClipboardClearSeconds = 45, MotionSpeed = 1.5, CloseToTray = false };
        vm.SaveSettings(settings); vm.Lock(); await vm.UnlockAsync(Password, "");
        Assert.Equal(settings, vm.Settings);
    }

    [Fact]
    public async Task BadPasswordCannotRevealViewModelItems()
    {
        using var vm = new MainViewModel(Database); await vm.UnlockAsync(Password, Password);
        vm.SaveAssociation(new() { Title = "Закрытая тема" }); vm.Lock();
        await Assert.ThrowsAnyAsync<System.Security.Cryptography.CryptographicException>(() => vm.UnlockAsync("bad", ""));
        Assert.False(vm.IsUnlocked); Assert.Empty(vm.Associations);
    }

    [Theory]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    public void MalformedCustomThemeIsRejected(string color) => Assert.Throws<InvalidDataException>(() =>
        VaultValidation.Validate(new() { Settings = new() { AccentColor = color } }));

    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}
