using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using TrayNudge.Services;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace TrayNudge.Controls;

/// <summary>
/// The app's playful motion: pops, wiggles, number bumps and confetti, all on the composition
/// thread so they stay smooth. Celebrations respect both the app's setting and Windows' "Animation
/// effects"; functional motion (progress filling) only shortens.
/// </summary>
internal static class Motion
{
    private static readonly UISettings UiSettings = new();

    /// <summary>True when bounces and confetti should play.</summary>
    public static bool Playful => SettingsService.Celebrations && UiSettings.AnimationsEnabled;

    /// <summary>A squash-and-stretch pop, for a button that was just pressed.</summary>
    public static void Pop(UIElement element, float peak = 1.18f)
    {
        if (!Playful)
        {
            return;
        }

        Visual visual = Prepare(element);
        Compositor compositor = visual.Compositor;
        CubicBezierEasingFunction ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.3f, 0f), new Vector2(0.2f, 1f));
        Vector3KeyFrameAnimation scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, Vector3.One);
        scale.InsertKeyFrame(0.2f, new Vector3(0.86f, 0.86f, 1f), ease);
        scale.InsertKeyFrame(0.55f, new Vector3(peak, peak, 1f), ease);
        scale.InsertKeyFrame(0.8f, new Vector3(0.97f, 0.97f, 1f), ease);
        scale.InsertKeyFrame(1f, Vector3.One, ease);
        scale.Duration = TimeSpan.FromMilliseconds(460);
        visual.StartAnimation("Scale", scale);
    }

    /// <summary>A happy little shake, for a streak badge that just grew.</summary>
    public static void Wiggle(UIElement element)
    {
        if (!Playful)
        {
            return;
        }

        Visual visual = Prepare(element);
        ScalarKeyFrameAnimation rotate = visual.Compositor.CreateScalarKeyFrameAnimation();
        float[] angles = [0, -14, 11, -7, 4, 0];
        for (int i = 0; i < angles.Length; i++)
        {
            rotate.InsertKeyFrame(i / (float)(angles.Length - 1), angles[i]);
        }

        rotate.Duration = TimeSpan.FromMilliseconds(650);
        visual.StartAnimation("RotationAngleInDegrees", rotate);
        Pop(element, 1.25f);
    }

    /// <summary>Slides content up a few pixels while fading it in, for a value that just changed.</summary>
    public static void BumpIn(UIElement element, float distance = 8)
    {
        if (!UiSettings.AnimationsEnabled)
        {
            return;
        }

        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        Compositor compositor = visual.Compositor;
        CubicBezierEasingFunction ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

        Vector3KeyFrameAnimation slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, new Vector3(0, distance, 0));
        slide.InsertKeyFrame(1f, Vector3.Zero, ease);
        slide.Duration = TimeSpan.FromMilliseconds(320);

        ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f);
        fade.Duration = TimeSpan.FromMilliseconds(220);

        visual.StartAnimation("Translation", slide);
        visual.StartAnimation("Opacity", fade);
    }

    /// <summary>Animates a width with a slight overshoot, for progress bars filling up.</summary>
    public static void AnimateWidth(FrameworkElement element, double to)
    {
        // A storyboard on an element outside the live tree (a page being navigated back to) throws.
        if (!UiSettings.AnimationsEnabled || double.IsNaN(element.Width) || !element.IsLoaded)
        {
            element.Width = to;
            return;
        }

        DoubleAnimation animation = new()
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(Playful ? 520 : 200),
            EnableDependentAnimation = true,
            EasingFunction = Playful
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
                : new ExponentialEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, nameof(FrameworkElement.Width));
        Storyboard storyboard = new();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) => element.Width = to;
        try
        {
            storyboard.Begin();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Motion is decoration; never let it take the app down.
            element.Width = to;
        }
    }

    /// <summary>
    /// A burst of confetti from <paramref name="origin"/> (in <paramref name="layer"/>'s
    /// coordinates). Some pieces are the habit's own emoji, so water rains drops.
    /// </summary>
    public static void Burst(Canvas layer, Point origin, IReadOnlyList<Color> colors, string? emoji, int count = 28, double power = 1, double lifetime = 1)
    {
        if (!Playful)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            // Mostly upward, fanned out to both sides.
            double angle = (-90 + ((Random.Shared.NextDouble() - 0.5) * 150)) * Math.PI / 180;
            double speed = (220 + (Random.Shared.NextDouble() * 260)) * power;
            Vector2 velocity = new((float)(Math.Cos(angle) * speed), (float)(Math.Sin(angle) * speed));
            bool useEmoji = emoji is not null && i % 5 == 0;
            Launch(layer, origin, velocity, colors[i % colors.Count], useEmoji ? emoji : null, 0, lifetime);
        }
    }

    /// <summary>Confetti falling across the whole layer, for finishing everything today.</summary>
    public static void Rain(Canvas layer, IReadOnlyList<Color> colors, int count = 60)
    {
        if (!Playful)
        {
            return;
        }

        string[] party = ["🎉", "⭐", "✨"];
        for (int i = 0; i < count; i++)
        {
            Point origin = new(Random.Shared.NextDouble() * layer.ActualWidth, -20);
            Vector2 velocity = new((float)((Random.Shared.NextDouble() - 0.5) * 120), (float)(Random.Shared.NextDouble() * 120));
            string? emoji = i % 8 == 0 ? party[i / 8 % party.Length] : null;
            Launch(layer, origin, velocity, colors[i % colors.Count], emoji, Random.Shared.NextDouble() * 0.6, 1);
        }
    }

    private static void Launch(Canvas layer, Point origin, Vector2 velocity, Color color, string? emoji, double delaySeconds, double lifetime)
    {
        FrameworkElement piece = emoji is not null
            ? new TextBlock { Text = emoji, FontSize = 16, IsHitTestVisible = false }
            : Random.Shared.Next(3) == 0
                ? new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(color) }
                : new Rectangle { Width = 6, Height = 11, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(color) };
        piece.IsHitTestVisible = false;
        Canvas.SetLeft(piece, origin.X - 4);
        Canvas.SetTop(piece, origin.Y - 6);
        layer.Children.Add(piece);

        ElementCompositionPreview.SetIsTranslationEnabled(piece, true);
        Visual visual = ElementCompositionPreview.GetElementVisual(piece);
        visual.CenterPoint = new Vector3(4, 6, 0);
        visual.Opacity = 0;
        Compositor compositor = visual.Compositor;

        const float Gravity = 900f;
        float duration = (float)((1.2 + (Random.Shared.NextDouble() * 0.5)) * lifetime);
        TimeSpan delay = TimeSpan.FromSeconds(delaySeconds);

        // Sample a ballistic arc with a little air drag into keyframes.
        Vector3KeyFrameAnimation path = compositor.CreateVector3KeyFrameAnimation();
        const int Steps = 12;
        for (int s = 0; s <= Steps; s++)
        {
            float t = duration * s / Steps;
            float drag = 1f / (1f + (0.9f * t));
            path.InsertKeyFrame(s / (float)Steps, new Vector3(
                velocity.X * t * drag,
                (velocity.Y * t * drag) + (0.5f * Gravity * t * t),
                0), compositor.CreateLinearEasingFunction());
        }

        path.Duration = TimeSpan.FromSeconds(duration);
        path.DelayTime = delay;

        ScalarKeyFrameAnimation spin = compositor.CreateScalarKeyFrameAnimation();
        float spins = (float)((Random.Shared.NextDouble() - 0.5) * 1080);
        spin.InsertKeyFrame(0f, 0f);
        spin.InsertKeyFrame(1f, spins, compositor.CreateLinearEasingFunction());
        spin.Duration = path.Duration;
        spin.DelayTime = delay;

        ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 1f);
        fade.InsertKeyFrame(0.7f, 1f);
        fade.InsertKeyFrame(1f, 0f);
        fade.Duration = path.Duration;
        fade.DelayTime = delay;
        fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

        CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation("Translation", path);
        visual.StartAnimation("RotationAngleInDegrees", spin);
        visual.StartAnimation("Opacity", fade);
        batch.End();
        batch.Completed += (_, _) => layer.DispatcherQueue.TryEnqueue(() => layer.Children.Remove(piece));
    }

    private static Visual Prepare(UIElement element)
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        if (element is FrameworkElement fe)
        {
            visual.CenterPoint = new Vector3((float)fe.ActualWidth / 2, (float)fe.ActualHeight / 2, 0);
        }

        return visual;
    }
}
