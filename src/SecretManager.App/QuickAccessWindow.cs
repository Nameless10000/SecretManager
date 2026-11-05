using Microsoft.UI.Windowing;
using SecretManager.App.Controls;
using SecretManager.App.Services;
using SecretManager.App.ViewModels;
using SecretManager.Core.Models;
using Windows.System;

namespace SecretManager.App;

public sealed class QuickAccessWindow : Window
{
    private readonly MainViewModel model;
    private readonly Grid root = new() { Padding = new Thickness(20) };
    private readonly PasswordBox password = new() { Header = "Мастер-пароль" };
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button unlock = new() { Content = "Разблокировать" };
    private readonly StackPanel login = new() { Spacing = 14 };
    private QuickAccessPanel? panel;
    private bool closed;
    private bool unlocking;
    private bool wasUnlocked;
    public event Action<SecretField>? CopyRequested;
    public event Action<bool>? FocusChanged;
    public bool IsActive { get; private set; }

    public QuickAccessWindow(MainViewModel model)
    {
        this.model = model;
        Title = "SecretManager — быстрый доступ";
        root.Resources = new ThemeResources();
        Content = root;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(520, 480));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.PreferredMinimumWidth = 480;
            presenter.PreferredMinimumHeight = 320;
        }

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        model.SessionChanged += SessionChanged;
        login.Children.Add(new TextBlock { Text = model.HasVault ? "Хранилище заблокировано" : "Сначала создайте хранилище в главном окне", FontSize = 20, TextWrapping = TextWrapping.Wrap });
        login.Children.Add(password);
        login.Children.Add(unlock);
        login.Children.Add(error);
        Closed += WindowClosed;
        Activated += WindowActivated;
        root.Loaded += RootLoaded;
        unlock.Click += Unlock;
        password.KeyDown += PasswordKeyDown;
        var escape = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, args) => { Close(); args.Handled = true; };
        root.KeyboardAccelerators.Add(escape);
        Render();
        if (root.IsLoaded)
        {
            ThemeService.Apply(root, model.Settings);
        }
    }

    private void RootLoaded(object sender, RoutedEventArgs args)
    {
        ThemeService.Apply(root, model.Settings);
        FocusInput();
    }

    private void WindowActivated(object sender, WindowActivatedEventArgs args)
    {
        IsActive = args.WindowActivationState != WindowActivationState.Deactivated;
        FocusChanged?.Invoke(IsActive);
    }

    public void FocusInput()
    {
        if (panel is not null)
        {
            panel.FocusSearch();
        }
        else
        {
            password.Focus(FocusState.Programmatic);
        }
    }

    private void PasswordKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter)
        {
            Unlock(sender, new RoutedEventArgs());
            args.Handled = true;
        }
    }

    private async void Unlock(object sender, RoutedEventArgs args)
    {
        if (unlocking || closed || !model.HasVault)
        {
            return;
        }

        unlocking = true;
        unlock.IsEnabled = false;
        var entered = password.Password;
        password.Password = "";
        try
        {
            await model.UnlockAsync(entered, "");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or InvalidDataException or System.Security.Cryptography.CryptographicException)
        {
            if (!closed)
            {
                error.Text = "Не удалось открыть хранилище. Проверьте мастер-пароль.";
            }
        }
        finally
        {
            unlocking = false;
            if (!closed)
            {
                unlock.IsEnabled = true;
                FocusInput();
            }
        }
    }

    private void SessionChanged()
    {
        if (closed)
        {
            return;
        }

        if (wasUnlocked && !model.IsUnlocked)
        {
            Close();
            return;
        }

        Render();
    }

    private void Render()
    {
        panel?.Dispose();
        panel = null;
        root.Children.Clear();
        wasUnlocked = model.IsUnlocked;
        if (model.IsUnlocked)
        {
            panel = new QuickAccessPanel(model, () => !closed && model.IsUnlocked);
            panel.CopyRequested += field => CopyRequested?.Invoke(field);
            root.Children.Add(panel);
        }
        else
        {
            unlock.IsEnabled = model.HasVault;
            root.Children.Add(login);
        }

        FocusInput();
        if (root.IsLoaded)
        {
            ThemeService.Apply(root, model.Settings);
        }
    }

    private void WindowClosed(object sender, WindowEventArgs args)
    {
        closed = true;
        IsActive = false;
        FocusChanged?.Invoke(false);
        model.SessionChanged -= SessionChanged;
        password.Password = "";
        panel?.Dispose();
        root.Children.Clear();
        if (unlocking)
        {
            model.Lock();
        }
    }
}
