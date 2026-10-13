using System.Text.RegularExpressions;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

/// <summary>
/// One clock (CODING-RULES-OVS.md N14): outside the composition roots (<c>Program.cs</c>, <c>App.axaml.cs</c>) the client reads
/// time only through the injected <see cref="TimeProvider"/>, except on a line marked with one of the kinds N14 names,
/// and only the roots read their hand-over statics. Comments never count: the patterns are looked for in a line's code,
/// the marker in its comment. A scanner of lines, no C# parser; the theories pin the forms it knows.
/// </summary>
public sealed class OneClockTests
{
    static readonly string[] Roots = ["Program.cs", "App.axaml.cs"];

    static readonly Regex Marker = new(@"^// clock: (frame timing|real time|debug)\b");

    /// <summary>Clocks, waits and timers used directly instead of through the injected clock.</summary>
    static readonly Regex DirectClockUse = new(string.Join("|",
        @"\bDateTime(Offset)?\.(Now|UtcNow|Today)\b",
        @"\bEnvironment\.TickCount",
        @"\bStopwatch\b",
        @"\bThread\.Sleep\(",
        @"\.CancelAfter\(",
        @"\bPeriodicTimer\b",
        @"(?<!\bnuint\s)\bSetTimer\(",
        @"\bSystem\.Timers\b",
        @"\bnew (System\.Threading\.)?Timer\(",
        @"(?<![\w.])(System\.Threading\.)?Timer\??\s+\w+\s*[;=,)]",
        @"\busing static System\.(DateTime|DateTimeOffset|Environment|Threading\.Thread|Threading\.Tasks\.Task|Diagnostics\.Stopwatch)\s*;",
        @"\busing \w+\s*=\s*System\.(Threading\.Timer|Diagnostics\.Stopwatch)\b",
        @"\bTimeProvider\.System\b",
        @"\bProgram\.Time\b"));

    /// <summary>
    /// Calls that may take a time: they pass only with no time at all or with the clock as their last argument (before a
    /// cancellation token). A thread's <c>Join</c> and a handle's <c>WaitOne</c> take no clock, so any argument fails them.
    /// </summary>
    static readonly Regex TimedCall = new(string.Join("|",
        @"\bTask\.Delay\(",
        @"\.Wait\(",
        @"\.WaitAsync\(",
        @"\.WaitOne\(",
        @"(?<!\b(?:string|String|Path))\.Join\(",
        @"\bnew CancellationTokenSource\(",
        @"\bCancellationTokenSource\??\s+\w+\s*=\s*new\("));

    static readonly Regex Clock = new(@"(^|\.)[Tt]ime$");
    static readonly Regex CancellationToken = new(@"(^|\.)(\w*[Tt]oken|ct|cancel)$");

    /// <summary>The hand-over statics of the roots other than the clock.</summary>
    static readonly Regex RootStatic = new(@"\bProgram\.(Log|Options|Settings)");

    [Fact]
    public void ClientSource_OutsideTheRoots_ReadsTimeOnlyThroughTheInjectedClock() => Assert.Empty(ClientLinesWhere(Offends));

    [Fact]
    public void ClientSource_OutsideTheRoots_DoesNotReadTheRootsStatics() => Assert.Empty(ClientLinesWhere(ReadsRootStatic));

    [Theory]
    [InlineData("var shown = DateTime.Now;", true)]
    [InlineData("connectedAt = DateTime.UtcNow;", true)]
    [InlineData("var sent = DateTimeOffset.UtcNow;", true)]
    [InlineData("var day = DateTime.Today;", true)]
    [InlineData("long now = Environment.TickCount64;", true)]
    [InlineData("int now = Environment.TickCount;", true)]
    [InlineData("var clock = Stopwatch.StartNew();", true)]
    [InlineData("Thread.Sleep(10);", true)]
    [InlineData("timeout.CancelAfter(TimeSpan.FromSeconds(10));", true)]
    [InlineData("using var ticks = new PeriodicTimer(period);", true)]
    [InlineData("SetTimer(hwnd, 1, 16, 0);", true)]
    [InlineData("internal static partial nuint SetTimer(nint hwnd, nuint id, uint ms, nint callback);", false)]
    [InlineData("var timer = new System.Timers.Timer(1000);", true)]
    [InlineData("timer = new Timer(_ => OnSecond(), null, period, period);", true)]
    [InlineData("timer = new System.Threading.Timer(_ => OnSecond());", true)]
    [InlineData("readonly Timer timer;", true)]
    [InlineData("Timer timer = new(_ => OnSecond(), null, period, period);", true)]
    [InlineData("ITimer? pingTimer;", false)]
    [InlineData("/// <summary>The <see cref=\"Timer\"/> of the log.</summary>", false)]
    [InlineData("using static System.DateTime;", true)]
    [InlineData("using static System.Threading.Tasks.Task;", true)]
    [InlineData("using Ticker = System.Threading.Timer;", true)]
    [InlineData("using Watch = System.Diagnostics.Stopwatch;", true)]
    [InlineData("this.time = time ?? TimeProvider.System;", true)]
    [InlineData("var time = Program.Time;", true)]
    [InlineData("await Task.Delay(50);", true)]
    [InlineData("await Task.Delay(PingInterval, pingCts.Token);", true)]
    [InlineData("await Task.Delay(wait, lifetime);", true)]
    [InlineData("await Task.Delay(Delay, time);", false)]
    [InlineData("await Task.Delay(RetryDelay, time);", false)]
    [InlineData("await Task.Delay(PingInterval, time, pingCts.Token);", false)]
    [InlineData("using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));", true)]
    [InlineData("using var timeout = new CancellationTokenSource(PendingActionViewModel.Timeout, time);", false)]
    [InlineData("var cancel = new CancellationTokenSource();", false)]
    [InlineData("receiveLoop?.Wait(TimeSpan.FromSeconds(1));", true)]
    [InlineData("await ssl.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(1));", true)]
    [InlineData("var answer = await awaited.Task.WaitAsync(AnswerTimeout, time);", false)]
    [InlineData("await turn.WaitAsync();", false)]
    [InlineData("await reply.WaitAsync(token);", false)]
    [InlineData("thread?.Join(500);", true)]
    [InlineData("thread.Join();", false)]
    [InlineData("var text = string.Join(\", \", names);", false)]
    [InlineData("var path = Path.Join(dir, name);", false)]
    [InlineData("handle.WaitOne(100);", true)]
    [InlineData("wake.WaitOne();", false)]
    [InlineData("CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));", true)]
    [InlineData("readonly CancellationTokenSource cts = new();", false)]
    [InlineData("await Task.Delay(", true)]
    [InlineData("var now = time.GetUtcNow();", false)]
    [InlineData("var url = \"https://example.org\"; var shown = DateTime.Now;", true)]
    [InlineData("count++; // not DateTime.Now", false)]
    [InlineData("var clock = Stopwatch.StartNew(); // clock: frame timing", false)]
    [InlineData("Thread.Sleep(5); // clock: real time", false)]
    [InlineData("await Task.Delay(50); // clock: debug, a few polls of the key thread", false)]
    [InlineData("var clock = Stopwatch.StartNew(); // clock: pacing", true)]
    public void Offends_AnyClientLine_TrueUnlessOnTheClockOrMarked(string line, bool offends) => Assert.Equal(offends, Offends(line));

    [Theory]
    [InlineData("var log = Program.Log;", true)]
    [InlineData("var options = Program.Options;", true)]
    [InlineData("var settings = Program.Settings;", true)]
    [InlineData("var warning = Program.SettingsWarning;", true)]
    [InlineData("var builder = Program.BuildAvaloniaApp();", false)]
    [InlineData("count++; // not Program.Log", false)]
    public void ReadsRootStatic_AnyClientLine_TrueForTheHandOverStatics(string line, bool reads) => Assert.Equal(reads, ReadsRootStatic(line));

    /// <summary>"file:line: text" of every line where <paramref name="check"/> holds, in the client's sources outside the roots.</summary>
    static List<string> ClientLinesWhere(Func<string, bool> check)
    {
        var files = RepoPaths.SourceFiles(RepoPaths.ClientSource, "*.cs")
            .Select(file => Path.GetRelativePath(RepoPaths.ClientSource, file))
            .Where(file => !Roots.Contains(file))
            .ToList();
        Assert.NotEmpty(files);
        var found = new List<string>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(Path.Combine(RepoPaths.ClientSource, file));
            for (int index = 0; index < lines.Length; index++)
                if (check(lines[index])) found.Add($"{file}:{index + 1}: {lines[index].Trim()}");
        }
        return found;
    }

    static bool Offends(string line)
    {
        var (code, comment) = Split(line);
        return !Marker.IsMatch(comment) && (DirectClockUse.IsMatch(code) || TimedCallWithoutClock(code));
    }

    static bool ReadsRootStatic(string line) => RootStatic.IsMatch(Split(line).Code);

    /// <summary>
    /// A timed call whose arguments, a trailing cancellation token aside, do not end with the clock (<c>time</c>,
    /// <c>Time</c>, <c>vm.Time</c>), or whose argument list does not close on its line (it fails closed).
    /// </summary>
    static bool TimedCallWithoutClock(string code)
    {
        foreach (Match call in TimedCall.Matches(code))
        {
            if (Arguments(code, call.Index + call.Length) is not { } arguments) return true;
            if (arguments.Count > 0 && CancellationToken.IsMatch(arguments[^1])) arguments.RemoveAt(arguments.Count - 1);
            if (arguments.Count > 0 && !Clock.IsMatch(arguments[^1])) return true;
        }
        return false;
    }

    /// <summary>The top-level arguments of the call whose list starts at <paramref name="start"/>, or null when the list does not close on the line.</summary>
    static List<string>? Arguments(string code, int start)
    {
        var arguments = new List<string>();
        int depth = 0;
        for (int index = start; index < code.Length; index++)
        {
            if (code[index] is '(' or '[' or '{') depth++;
            else if (code[index] is ')' or ']' or '}') depth--;
            if ((code[index] == ',' && depth == 0) || depth < 0)
            {
                arguments.Add(code[start..index].Trim());
                start = index + 1;
            }
            if (depth < 0) return arguments.Where(argument => argument.Length > 0).ToList();
        }
        return null;
    }

    /// <summary>A line's code and its comment, which starts at the first <c>//</c> outside a string or character literal.</summary>
    static (string Code, string Comment) Split(string line)
    {
        bool inString = false;
        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (inString)
            {
                if (character == '\\') index++; // the escaped character
                else if (character == '"') inString = false;
            }
            else if (character == '"') inString = true;
            else if (character == '\'' && index + 1 < line.Length) index += line[index + 1] == '\\' ? 3 : 2; // to the closing quote of 'x' or '\x'
            else if (character == '/' && index + 1 < line.Length && line[index + 1] == '/') return (line[..index], line[index..]);
        }
        return (line, "");
    }
}
