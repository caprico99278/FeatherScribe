using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FeatherScribe.App;

internal static class UiMotion
{
    private const string MotionEaseOutKey = "MotionEaseOut";
    private const string MotionFastDurationKey = "MotionFastDuration";
    private const string MotionNormalDurationKey = "MotionNormalDuration";
    private const string MotionSlowDurationKey = "MotionSlowDuration";
    private const string MotionRevealOffsetKey = "MotionRevealOffset";
    private const string MotionSubtleOffsetKey = "MotionSubtleOffset";
    private const string MotionMutedOpacityKey = "MotionMutedOpacity";

    // Presentation-only token: bumped on every Stop so a superseded animation's
    // Completed handler never clears or resets the clocks of a newer one.
    private static readonly DependencyProperty MotionVersionProperty =
        DependencyProperty.RegisterAttached(
            "MotionVersion",
            typeof(int),
            typeof(UiMotion),
            new PropertyMetadata(0));

    public static void Reveal(FrameworkElement element)
    {
        RunReveal(
            element,
            fromOpacity: 0,
            offsetKey: MotionRevealOffsetKey,
            durationKey: MotionSlowDurationKey);
    }

    public static void RevealResult(FrameworkElement element)
    {
        RunReveal(
            element,
            fromOpacity: 0.75,
            offsetKey: MotionRevealOffsetKey,
            durationKey: MotionNormalDurationKey);
    }

    public static void SubtleUpdate(FrameworkElement element)
    {
        RunReveal(
            element,
            fromOpacity: GetDoubleResource(element, MotionMutedOpacityKey, 0.65),
            offsetKey: MotionSubtleOffsetKey,
            durationKey: MotionFastDurationKey);
    }

    public static void Stop(FrameworkElement element)
    {
        element.SetValue(MotionVersionProperty, (int)element.GetValue(MotionVersionProperty) + 1);
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (TryGetTranslateTransform(element.RenderTransform, out var translate))
        {
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            translate.Y = 0;
        }

        element.Opacity = 1;
    }

    private static void RunReveal(
        FrameworkElement element,
        double fromOpacity,
        string offsetKey,
        string durationKey)
    {
        var translate = EnsureTranslateTransform(element);
        var offset = GetDoubleResource(element, offsetKey, 0);
        var duration = GetDurationResource(element, durationKey, TimeSpan.FromMilliseconds(180));
        var easing = element.TryFindResource(MotionEaseOutKey) as IEasingFunction;

        Stop(element);
        var version = (int)element.GetValue(MotionVersionProperty);
        element.Opacity = fromOpacity;
        translate.Y = offset;

        var opacity = new DoubleAnimation(1, duration)
        {
            EasingFunction = easing,
        };
        var slide = new DoubleAnimation(0, duration)
        {
            EasingFunction = easing,
        };

        opacity.Completed += (_, _) =>
        {
            if ((int)element.GetValue(MotionVersionProperty) != version)
            {
                return;
            }

            element.Opacity = 1;
            element.BeginAnimation(UIElement.OpacityProperty, null);
        };
        slide.Completed += (_, _) =>
        {
            if ((int)element.GetValue(MotionVersionProperty) != version)
            {
                return;
            }

            translate.Y = 0;
            translate.BeginAnimation(TranslateTransform.YProperty, null);
        };

        element.BeginAnimation(UIElement.OpacityProperty, opacity);
        translate.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private static TranslateTransform EnsureTranslateTransform(FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform translate)
        {
            return translate;
        }

        if (element.RenderTransform is TransformGroup group)
        {
            var existingTranslate = group.Children.OfType<TranslateTransform>().FirstOrDefault();
            if (existingTranslate is not null)
            {
                return existingTranslate;
            }

            translate = new TranslateTransform();
            group.Children.Add(translate);
            return translate;
        }

        if (element.RenderTransform is not null && element.RenderTransform != Transform.Identity)
        {
            group = new TransformGroup();
            group.Children.Add(element.RenderTransform);
            translate = new TranslateTransform();
            group.Children.Add(translate);
            element.RenderTransform = group;
            return translate;
        }

        translate = new TranslateTransform();
        element.RenderTransform = translate;
        return translate;
    }

    private static bool TryGetTranslateTransform(Transform transform, out TranslateTransform translate)
    {
        if (transform is TranslateTransform direct)
        {
            translate = direct;
            return true;
        }

        if (transform is TransformGroup group)
        {
            var child = group.Children.OfType<TranslateTransform>().FirstOrDefault();
            if (child is not null)
            {
                translate = child;
                return true;
            }
        }

        translate = null!;
        return false;
    }

    private static Duration GetDurationResource(FrameworkElement element, string key, TimeSpan fallback)
        => element.TryFindResource(key) is Duration duration ? duration : new Duration(fallback);

    private static double GetDoubleResource(FrameworkElement element, string key, double fallback)
        => element.TryFindResource(key) is double value ? value : fallback;
}
