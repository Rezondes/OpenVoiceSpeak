using OVS.Tools;

// Package 95: regenerates src/OVS.Client/Assets/ovs.ico and docs/logo.png from src/OVS.Client/Assets/logo.svg.
// Run from anywhere inside the repository: dotnet run --project tools/render-icon
var dir = new DirectoryInfo(Environment.CurrentDirectory);
while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "OVS.Client", "Assets", "logo.svg"))) dir = dir.Parent;
if (dir is null)
{
    Console.Error.WriteLine("Run this inside the OpenVoiceSpeak repository.");
    return 1;
}

var assets = Path.Combine(dir.FullName, "src", "OVS.Client", "Assets");
var svg = File.ReadAllText(Path.Combine(assets, "logo.svg"));
File.WriteAllBytes(Path.Combine(assets, "ovs.ico"), LogoRenderer.RenderIco(svg));
File.WriteAllBytes(Path.Combine(dir.FullName, "docs", "logo.png"), LogoRenderer.RenderPng(svg, 256));
Console.WriteLine($"Wrote {Path.Combine(assets, "ovs.ico")} and docs/logo.png");
return 0;
