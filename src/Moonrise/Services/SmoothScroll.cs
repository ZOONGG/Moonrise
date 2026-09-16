using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Moonrise.Services;

/// <summary>Pixel wheel scrolling with a bounded, interruptible animation.</summary>
public static class SmoothScroll
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, EnabledChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(ScrollState), typeof(SmoothScroll));

    public static bool GetIsEnabled(DependencyObject value) => (bool)value.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject value, bool enabled) => value.SetValue(IsEnabledProperty, enabled);

    internal static double WheelTarget(double offset, double extent, double viewport, int delta, int lines) =>
        Math.Clamp(offset - delta / 120d * (lines < 0 ? viewport : lines * 22d), 0, Math.Max(0, extent - viewport));

    private static void EnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not ScrollViewer viewer) return;
        if (viewer.GetValue(StateProperty) is ScrollState previous) previous.Dispose();
        viewer.SetValue(StateProperty, (bool)args.NewValue ? new ScrollState(viewer) : null);
    }

    private sealed class ScrollState : IDisposable
    {
        private readonly ScrollViewer _viewer;
        private readonly Stopwatch _clock = new();
        private double _from;
        private double _target;
        private int _direction;
        private bool _animating;

        public ScrollState(ScrollViewer viewer)
        {
            _viewer = viewer;
            viewer.PreviewMouseWheel += Wheel;
            viewer.PreviewMouseDown += CancelMouse;
            viewer.PreviewKeyDown += CancelKey;
            viewer.Unloaded += Unloaded;
            viewer.IsVisibleChanged += VisibilityChanged;
        }

        private void Wheel(object sender, MouseWheelEventArgs args)
        {
            // The innermost viewer owns the gesture. Bubble at its boundaries.
            var source = args.OriginalSource as DependencyObject;
            while (source is not null && source is not ScrollViewer)
                source = Parent(source);
            if (source != _viewer || args.Handled || SystemParameters.WheelScrollLines == 0) return;
            var viewer = _viewer;
            while (viewer is not null)
            {
                if (viewer.GetValue(StateProperty) is ScrollState state && state.Move(args.Delta))
                {
                    args.Handled = true;
                    return;
                }
                source = Parent(viewer);
                while (source is not null && source is not ScrollViewer) source = Parent(source);
                viewer = source as ScrollViewer;
            }
        }

        private bool Move(int delta)
        {
            if (_viewer.ScrollableHeight <= 0) return false;
            var direction = Math.Sign(delta);
            var origin = _animating && direction == _direction ? _target : _viewer.VerticalOffset;
            var target = WheelTarget(origin, _viewer.ExtentHeight, _viewer.ViewportHeight,
                delta, SystemParameters.WheelScrollLines);
            if (Math.Abs(target - origin) < 0.01) return _animating;
            Stop();
            _from = _viewer.VerticalOffset;
            _target = target;
            _direction = direction;
            if (!SystemParameters.ClientAreaAnimation || Environment.GetEnvironmentVariable("MOONRISE_REDUCED_MOTION") == "1")
                _viewer.ScrollToVerticalOffset(target);
            else
            {
                _animating = true;
                _clock.Restart();
                CompositionTarget.Rendering += Render;
            }
            return true;
        }

        private void Render(object? sender, EventArgs args)
        {
            var progress = Math.Min(1, _clock.Elapsed.TotalMilliseconds / 180);
            var eased = 1 - Math.Pow(1 - progress, 3);
            _target = Math.Clamp(_target, 0, _viewer.ScrollableHeight);
            _viewer.ScrollToVerticalOffset(_from + (_target - _from) * eased);
            if (progress >= 1) Stop();
        }

        private void Stop()
        {
            CompositionTarget.Rendering -= Render;
            _animating = false;
            _clock.Reset();
        }

        private void CancelMouse(object sender, MouseButtonEventArgs args) => Stop();
        private void CancelKey(object sender, System.Windows.Input.KeyEventArgs args) => Stop();
        private void Unloaded(object sender, RoutedEventArgs args) => Stop();
        private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) { if (!(bool)args.NewValue) Stop(); }

        public void Dispose()
        {
            Stop();
            _viewer.PreviewMouseWheel -= Wheel;
            _viewer.PreviewMouseDown -= CancelMouse;
            _viewer.PreviewKeyDown -= CancelKey;
            _viewer.Unloaded -= Unloaded;
            _viewer.IsVisibleChanged -= VisibilityChanged;
        }
    }

    private static DependencyObject? Parent(DependencyObject child) => child is Visual or System.Windows.Media.Media3D.Visual3D
        ? VisualTreeHelper.GetParent(child) : LogicalTreeHelper.GetParent(child);
}
