using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Moonrise.Services;

public sealed class MotionController
{
    public const int FastMilliseconds = 140;
    public const int NormalMilliseconds = 180;
    public const int PageMilliseconds = 210;
    public const int DrawerMilliseconds = 225;
    public const int ModalMilliseconds = 185;
    public const int ToastMilliseconds = 200;

    public MotionController(bool? reducedMotion = null)
    {
        ReducedMotion = reducedMotion ??
            (!SystemParameters.ClientAreaAnimation ||
             string.Equals(Environment.GetEnvironmentVariable("MOONRISE_REDUCED_MOTION"), "1", StringComparison.Ordinal));
    }

    public bool ReducedMotion { get; }
    public bool AllowsTranslation => !ReducedMotion;

    public void EnterPage(FrameworkElement page)
    {
        page.BeginAnimation(UIElement.OpacityProperty, null);
        page.Opacity = 1;
        var transform = EnsureTranslateTransform(page);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = 0;
        if (ReducedMotion)
            return;

        page.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(PageMilliseconds)) { EasingFunction = EaseOut() },
            HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(PageMilliseconds)) { EasingFunction = EaseOut() },
            HandoffBehavior.SnapshotAndReplace);
    }

    public void ShowOverlay(FrameworkElement element, double offset, int durationMilliseconds, Action? completed = null)
    {
        element.Visibility = Visibility.Visible;
        element.IsHitTestVisible = true;
        var transform = EnsureTranslateTransform(element);
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(ReducedMotion ? 0.45 : 0, 1, TimeSpan.FromMilliseconds(ReducedMotion ? FastMilliseconds : durationMilliseconds)) { EasingFunction = EaseOut() },
            HandoffBehavior.SnapshotAndReplace);
        if (AllowsTranslation)
            transform.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(offset, 0, TimeSpan.FromMilliseconds(durationMilliseconds)) { EasingFunction = EaseOut() },
                HandoffBehavior.SnapshotAndReplace);
        if (completed is not null)
            element.Dispatcher.InvokeAsync(completed, System.Windows.Threading.DispatcherPriority.Background);
    }

    public void HideOverlay(FrameworkElement element, double offset, int durationMilliseconds, Action completed)
    {
        element.IsHitTestVisible = false;
        var fade = new DoubleAnimation(element.Opacity, 0, TimeSpan.FromMilliseconds(ReducedMotion ? FastMilliseconds : durationMilliseconds))
        {
            EasingFunction = EaseOut()
        };
        fade.Completed += (_, _) =>
        {
            element.Visibility = Visibility.Collapsed;
            element.Opacity = 1;
            completed();
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        if (AllowsTranslation)
            EnsureTranslateTransform(element).BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, offset, TimeSpan.FromMilliseconds(durationMilliseconds)) { EasingFunction = EaseOut() },
                HandoffBehavior.SnapshotAndReplace);
    }

    private static TranslateTransform EnsureTranslateTransform(FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform translate)
            return translate;
        translate = new TranslateTransform();
        element.RenderTransform = translate;
        return translate;
    }

    private static IEasingFunction EaseOut() => new CubicEase { EasingMode = EasingMode.EaseOut };
}
