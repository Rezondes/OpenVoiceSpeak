using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Styling;

namespace OVS.Client.Animations;

/// <summary>Package 105, 106: a refused input or a failed connect shakes its card once (3 swings of <see cref="Swing"/>).</summary>
internal static class ShakeMotion
{
    /// <summary>How far one swing goes, in px.</summary>
    const double Swing = 6;

    internal static void Shake(Animatable target)
    {
        if (!Motion.IsAnimated) return;
        var shake = new Animation { Duration = TimeSpan.FromMilliseconds(360), Easing = Motion.Ease };
        double[] steps = [0, -Swing, Swing, -Swing, Swing, -Swing / 2, 0];
        for (int i = 0; i < steps.Length; i++)
            shake.Children.Add(new KeyFrame { Cue = new Cue((double)i / (steps.Length - 1)), Setters = { new Setter(TranslateTransform.XProperty, steps[i]) } });
        _ = shake.Play(target);
    }
}
