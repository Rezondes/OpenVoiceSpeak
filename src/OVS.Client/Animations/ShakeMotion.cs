using Avalonia;
using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Styling;

namespace OVS.Client.Animations;

/// <summary>Package 105, 106: a refused input or a failed connect shakes its card once (3 swings of <see cref="Swing"/>).</summary>
internal static class ShakeMotion
{
    /// <summary>How far one swing goes, in px.</summary>
    const double Swing = 6;

    /// <summary>Shakes <paramref name="target"/> once; nothing in the simplified display of its window.</summary>
    internal static void Shake(Visual target)
    {
        if (!Motion.IsAnimated(target)) return;
        var shake = new Animation { Duration = TimeSpan.FromMilliseconds(360), Easing = Motion.Ease };
        double[] steps = [0, -Swing, Swing, -Swing, Swing, -Swing / 2, 0];
        for (int index = 0; index < steps.Length; index++)
            shake.Children.Add(new KeyFrame { Cue = new Cue((double)index / (steps.Length - 1)), Setters = { new Setter(TranslateTransform.XProperty, steps[index]) } });
        _ = shake.PlayAsync(target);
    }
}
