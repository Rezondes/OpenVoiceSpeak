using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using OVS.Tests.Client.Localization;
using OVS.Tests.TestSupport;
using SkiaSharp;

namespace OVS.Tests.Website;

/// <summary>Package 116: the website's image generator itself, in the normal test run.</summary>
[Collection(WebsiteShots.Collection)]
public sealed class WebsiteShotsTests() : TempDirectoryTest("ovs-shots-test-", quiet: true)
{
    [AvaloniaFact]
    public void Writer_EncodesWebP_RightSizeAndDecodable()
    {
        var window = new Window { Width = 320, Height = 200, Content = new TextBlock { Text = "OpenVoiceSpeak" } };
        window.Show();
        var path = Path.Combine(dir, "probe.webp");
        ShotWriter.SaveWebP(window, path);
        window.Close();
        using var codec = SKCodec.Create(path);
        Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
        Assert.Equal((320, 200), (codec.Info.Width, codec.Info.Height));
    }

    [Fact]
    public void Scenes_SkippedWithoutEnvironment()
    {
        var before = Environment.GetEnvironmentVariable("OVS_SHOTS");
        Environment.SetEnvironmentVariable("OVS_SHOTS", null);
        try
        {
            Assert.Null(ShotWriter.Folder);
            Assert.NotNull(new ShotVariantsAttribute().Skip);
            Assert.Equal(4, new ShotVariantsAttribute().GetData(null!).Count());
        }
        finally
        {
            Environment.SetEnvironmentVariable("OVS_SHOTS", before);
        }
    }

    /// <summary>
    /// Fixed data and times: the same code gives the same image. A looping animation (the breathing speaking ring) runs on
    /// real time and Avalonia's animation clock cannot be driven from outside, so the check renders the hero without it.
    /// </summary>
    [AvaloniaFact]
    public void SameScene_TwiceIdentical()
    {
        var first = File.ReadAllBytes(WebsiteShots.Hero(Directory.CreateDirectory(Path.Combine(dir, "a")).FullName, "de", "light", speaking: false));
        var second = File.ReadAllBytes(WebsiteShots.Hero(Directory.CreateDirectory(Path.Combine(dir, "b")).FullName, "de", "light", speaking: false));
        Assert.Equal(first, second);
    }

    [AvaloniaFact]
    public void ShowcaseServer_EnglishHasNoGermanText()
    {
        using var scene = new WebsiteScene("en", "light");
        var showcase = scene.Connect();
        showcase.FillRaidChat();
        scene.Vm.Chat!.Selected = scene.Vm.Chat.ChannelTab;
        ShotWriter.Settle(300);
        var seen = scene.Window.GetVisualDescendants().OfType<TextBlock>().Where(textBlock => textBlock.IsEffectivelyVisible).Select(textBlock => textBlock.Text).OfType<string>().ToList();
        Assert.Contains("Thursday raid", seen);
        var german = LocalizationTests.GermanOnly();
        var left = seen.Where(german.Contains).Distinct().ToList();
        Assert.True(left.Count == 0, "Noch deutsch: " + string.Join(" | ", left));
        Assert.DoesNotContain(seen, text => text.Contains("Strategie") || text.Contains("Donnerstag"));
    }

    /// <summary>Package 117: the images show a made-up server only: no profile or debug title, example addresses, invented people.</summary>
    [AvaloniaFact]
    public void ShowcaseServer_NoRealData()
    {
        using var scene = new WebsiteScene("de", "light");
        var showcase = scene.Connect();
        Assert.DoesNotContain("[", scene.Window.Title);
        Assert.DoesNotContain("Debug", scene.Window.Title);
        Assert.All(showcase.Server.Mirror.Users.Values, user => Assert.Matches("^fp[0-9]{2}$", user.Fingerprint));
        showcase.AnswerAdministration();
        var bans = (OVS.Shared.Protocol.Messages.Moderation.BanList)showcase.Reply!(new OVS.Shared.Protocol.Messages.Moderation.ListBans { RequestId = "b" })!;
        // documentation addresses only (RFC 5737)
        Assert.All(bans.Bans.Select(ban => ban.Ip).Concat(bans.Bans.Select(ban => ban.LastAttemptIp)).OfType<string>(), ip => Assert.StartsWith("203.0.113.", ip));
    }
}
