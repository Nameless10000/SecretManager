using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using SecretManager.Core.Models;
using Windows.UI.ViewManagement;

namespace SecretManager.App.Services;

public sealed class MotionService(Func<AppSettings> settings)
{
    private readonly ConditionalWeakTable<FrameworkElement, object> wired = new();
    private readonly ConditionalWeakTable<FrameworkElement, object> translated = new();
    private readonly UISettings system = new();
    private readonly Func<bool>? systemAnimations;
    internal MotionService(Func<AppSettings> settings, Func<bool> systemAnimations) : this(settings) => this.systemAnimations = systemAnimations;
    public bool Enabled => settings().AnimationLevel != AnimationLevel.Off && (systemAnimations?.Invoke() ?? system.AnimationsEnabled);
    public void Wire(DependencyObject root)
    {
        var transition = Enabled ? new BrushTransition { Duration = TimeSpan.FromMilliseconds(
            (settings().AnimationLevel == AnimationLevel.Maximum ? 180 : 120) / settings().MotionSpeed) } : null;
        if (root is ContentPresenter presenter) presenter.BackgroundTransition = transition;
        if (root is Panel panel) panel.BackgroundTransition = transition;
        if (root is Border border) border.BackgroundTransition = transition;
        // WinUI visual-state storyboards also apply labels, knob positions and
        // visibility. Keep their native timing intact, including when effects are
        // off; rewriting a running state timeline can leave stale UI state.
        if (root is FrameworkElement element && (root is Button || root is Expander) && !wired.TryGetValue(element, out _))
        {
            wired.Add(element, new object());
            element.PointerEntered += (_, _) => Scale(element, 1.012f, true);
            element.PointerExited += (_, _) => Scale(element, 1, false);
            element.PointerPressed += (_, _) => Scale(element, .975f, false);
            element.PointerReleased += (_, _) => Scale(element, 1, false);
            element.PointerCanceled += (_, _) => Scale(element, 1, false);
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) Wire(VisualTreeHelper.GetChild(root, i));
    }
    private void Scale(FrameworkElement element, float value, bool hover)
    {
        if (!element.IsLoaded || element.XamlRoot is null || !Enabled || settings().AnimationLevel != AnimationLevel.Maximum) return;
        if (element is Expander && value > 1) value = MotionGeometry.CardHoverScale(element.ActualWidth);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        var spring = visual.Compositor.CreateSpringVector3Animation();
        spring.FinalValue = new Vector3(value, value, 1);
        spring.DampingRatio = hover ? .85f : .6f;
        spring.Period = TimeSpan.FromMilliseconds(150 / settings().MotionSpeed);
        visual.StartAnimation("Scale", spring);
    }
    public void Enter(FrameworkElement element, int stagger = 0)
    {
        if (element.XamlRoot is null || !Enabled) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0); fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(220);
        fade.DelayTime = TimeSpan.FromMilliseconds(Math.Min(stagger, 6) * 35);
        visual.StartAnimation("Opacity", fade);
        if (settings().AnimationLevel == AnimationLevel.Maximum)
        {
            var movement = visual.Compositor.CreateVector3KeyFrameAnimation();
            movement.InsertKeyFrame(0, new Vector3(0, 10, 0)); movement.InsertKeyFrame(1, Vector3.Zero);
            movement.Duration = TimeSpan.FromMilliseconds(320);
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            if (!translated.TryGetValue(element, out _)) translated.Add(element, new object());
            visual.StartAnimation("Translation", movement);
        }
    }
    public void Reset(DependencyObject root)
    {
        if (root is FrameworkElement element && element.XamlRoot is not null)
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation("Scale"); visual.StopAnimation("Opacity");
            if (translated.TryGetValue(element, out _))
            {
                visual.StopAnimation("Translation");
                visual.Properties.InsertVector3("Translation", Vector3.Zero);
            }
            visual.Scale = Vector3.One; visual.Opacity = 1;
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) Reset(VisualTreeHelper.GetChild(root, i));
    }
}
