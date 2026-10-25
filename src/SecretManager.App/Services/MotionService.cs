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
    public bool Enabled => settings().AnimationLevel != AnimationLevel.Off && system.AnimationsEnabled;
    public void Wire(DependencyObject root)
    {
        var transition = Enabled ? new BrushTransition { Duration = TimeSpan.FromMilliseconds(
            (settings().AnimationLevel == AnimationLevel.Maximum ? 180 : 120) / settings().MotionSpeed) } : null;
        if (root is ContentPresenter presenter) presenter.BackgroundTransition = transition;
        if (root is Panel panel) panel.BackgroundTransition = transition;
        if (root is Border border) border.BackgroundTransition = transition;
        if (root is FrameworkElement stateRoot)
        {
            foreach (var group in VisualStateManager.GetVisualStateGroups(stateRoot))
            {
                foreach (var state in group.States)
                    if (state.Storyboard is { } storyboard)
                        storyboard.SpeedRatio = Enabled ? settings().MotionSpeed : 100_000;
            }
        }
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
        if (!Enabled || settings().AnimationLevel != AnimationLevel.Maximum) return;
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
        if (!Enabled) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0); fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(220 / settings().MotionSpeed);
        fade.DelayTime = TimeSpan.FromMilliseconds(Math.Min(stagger, 6) * 35 / settings().MotionSpeed);
        visual.StartAnimation("Opacity", fade);
        if (settings().AnimationLevel == AnimationLevel.Maximum)
        {
            var movement = visual.Compositor.CreateVector3KeyFrameAnimation();
            movement.InsertKeyFrame(0, new Vector3(0, 10, 0)); movement.InsertKeyFrame(1, Vector3.Zero);
            movement.Duration = TimeSpan.FromMilliseconds(320 / settings().MotionSpeed);
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            if (!translated.TryGetValue(element, out _)) translated.Add(element, new object());
            visual.StartAnimation("Translation", movement);
        }
    }
    public void Reset(DependencyObject root)
    {
        if (root is FrameworkElement element)
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
