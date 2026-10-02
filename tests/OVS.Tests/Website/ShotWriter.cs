using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Views;
using SkiaSharp;

namespace OVS.Tests.Website;

/// <summary>
/// Package 116 (A124, A125): writes what a headless window shows as the website's images (WebP) and clips (PNG frames at
/// a steady 30 fps, encoded later by website/scripts/encode-clips.mjs).
/// </summary>
public static class ShotWriter
{
    public const int Quality = 82, Fps = 30;

    /// <summary>Where the files go; unset, the scenes are skipped (they never run in the normal test run or in CI).</summary>
    public static string? Folder => Environment.GetEnvironmentVariable("OVS_SHOTS") is { Length: > 0 } dir ? dir : null;

    /// <summary>Lets the clock run: animations run on real time in the headless tests.</summary>
    public static void Settle(int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        while (watch.ElapsedMilliseconds < milliseconds);
        // one frame after the time is up: under load the last one in the loop may have started long before
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Nothing in the window moves or waits to move: no animation started from code runs, no page switch is pending, no
    /// row enters or flashes (the flash waits on real time).
    /// </summary>
    public static bool Quiet(Visual root) => Motion.Running == 0 && !root.GetVisualDescendants().OfType<StyledElement>()
        .Any(e => e.Classes.Contains("entering") || e.Classes.Contains("fresh") || e.Classes.Contains(PageMotion.PageGhostClass));

    /// <summary>
    /// Lets the clock run until the window is <see cref="Quiet"/> (at most <paramref name="cap"/> ms), then a little more
    /// for the style transitions the last change started. A fixed time is not enough under load: every step of a chain
    /// (a row that enters, then flashes, then fades back) needs its own frames.
    /// </summary>
    public static void SettleUntilQuiet(Visual root, int cap = 10_000)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        while (!Quiet(root) && watch.ElapsedMilliseconds < cap);
        Settle(300); // the transitions are at most 220 ms
    }

    /// <summary>The window as it is drawn now.</summary>
    public static SKBitmap Capture(TopLevel window)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame rendered");
        using var png = new MemoryStream();
        frame.Save(png);
        png.Position = 0;
        return SKBitmap.Decode(png);
    }

    public static byte[] WebP(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, Quality);
        return data.ToArray();
    }

    public static void SaveWebP(TopLevel window, string path)
    {
        using var bitmap = Capture(window);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, WebP(bitmap));
    }

    /// <summary>
    /// Records <paramref name="milliseconds"/> of the window into <c>frame0000.png</c>... at a steady 30 fps, plus
    /// <c>poster.webp</c> (the frame at <paramref name="posterAt"/>: what shows before it plays, so clips that start
    /// alike can still look different), doing each step at its time. Capturing takes its own time, so each slot of
    /// 1/30 s gets the frame taken closest to it.
    /// </summary>
    public static void Record(TopLevel window, string folder, int milliseconds, int posterAt, params (int At, Action Do)[] steps)
    {
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.GetFiles(folder)) File.Delete(old);
        var taken = new List<(long At, byte[] Png)>();
        var due = new Queue<(int At, Action Do)>(steps.OrderBy(s => s.At));
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds <= milliseconds)
        {
            while (due.Count > 0 && due.Peek().At <= watch.ElapsedMilliseconds) due.Dequeue().Do();
            using var bitmap = Capture(window);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            taken.Add((watch.ElapsedMilliseconds, data.ToArray()));
        }
        int slots = milliseconds * Fps / 1000;
        for (int i = 0; i < slots; i++)
        {
            long at = i * 1000L / Fps;
            var best = taken.MinBy(t => Math.Abs(t.At - at));
            File.WriteAllBytes(Path.Combine(folder, $"frame{i:D4}.png"), best.Png);
        }
        using var poster = SKBitmap.Decode(taken.MinBy(t => Math.Abs(t.At - posterAt)).Png);
        File.WriteAllBytes(Path.Combine(folder, "poster.webp"), WebP(poster));
    }
}
