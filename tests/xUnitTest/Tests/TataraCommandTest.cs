// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text;
using Arc.Unit;
using Kimi;
using Kimi.Command;
using Kimi.Compiler;
using Kimi.Unit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace XunitTest;

// kimi tatara's session without a native build (Tatara design A3 §3): the redirected reader through the command, the gate, the exit
// codes, the scope per line and the Ctrl+C action. The loop reads and resets Environment.ExitCode, so the class runs alone
// (ConsoleIsolationCollection). Each session runs in a unit of its own, built as the CLI builds it, with the host's console replaced.
[Collection<ConsoleIsolationCollection>]
public sealed class TataraCommandTest : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-tatara-command-" + Guid.NewGuid().ToString("N"));

    public TataraCommandTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, true);

    // The gate's rows (§2.1): an empty line does nothing; an unknown first word, exit with an argument and an unenclosed '|' are that
    // line's error and the session continues; build in another case is build; a quoted path with a space is one argument; exit ends.
    [Theory]
    [InlineData("\n\n", 0, null)]
    [InlineData("frob x\n", 1, "Unknown command 'frob'. A kimi tatara line is build, run or exit.")]
    [InlineData("exit now\n", 1, "Use exit without arguments.")]
    [InlineData("build Lib/Lib.kimiproj | build Lib/Lib.kimiproj\n", 1, "A line takes no unenclosed '|'")]
    [InlineData("BUILD Lib/Lib.kimiproj\n", 0, "Project: Lib (Lib.kimiproj)")]
    [InlineData("build \"With space/Lib.kimiproj\"\n", 0, "Project: Lib (Lib.kimiproj)")]
    public async Task ALineIsGatedByItsFirstWord(string lines, int exit, string? expected)
    {
        this.Library("Lib");
        this.Library("With space");
        var (code, text, _) = await this.Session(lines + "build Lib/Lib.kimiproj\nexit\n");
        Assert.Equal(exit, code);
        if (expected is not null)
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }

        Assert.Equal(exit != 0, text.Contains("Exit code: 1", StringComparison.Ordinal));
        Assert.Equal(lines.Contains("BUILD", StringComparison.Ordinal) || lines.Contains("With space", StringComparison.Ordinal) ? 2 : 1, Count(text, "Project: Lib (Lib.kimiproj)"));
    }

    // A line above the limit (32,767 code units, a command line's) is refused before every other rule, not cut, exit padded with
    // spaces past the limit included; a line at the limit is a command line or exit.
    [Theory]
    [InlineData("build ", 'a', 2)]
    [InlineData("exit", ' ', 1)]
    public async Task ALineAboveTheLimitIsRefused(string word, char padding, int failures)
    {
        var limit = word + new string(padding, TataraCommand.LineLimit - word.Length);
        var (code, text, _) = await this.Session(limit + padding + "\n" + limit + "\n");
        Assert.Equal(1, code);
        Assert.Equal(failures, Count(text, "Exit code: 1"));
        Assert.Equal(1, Count(text, $"The line is longer than {TataraCommand.LineLimit} characters."));
    }

    // exit and the end of the input end the session; the reader reads up to the line's end and no further (O-454 (b): a run program
    // reads the input's later lines); a carriage return before the line feed and a byte order mark at the start are dropped.
    [Theory]
    [InlineData("exit\nrest", 5)]
    [InlineData("\uFEFFexit\r\nrest", 9)]
    [InlineData("\n", 1)]
    [InlineData("", 0)]
    public async Task TheSessionEndsAtExitOrTheEndAndReadsNoFurther(string input, int position)
    {
        var (code, _, stream) = await this.Session(input);
        Assert.Equal(0, code);
        Assert.Equal(position, stream.Position);
    }

    // An unknown option is reported as single-shot reports it (the same parser and options), and a Library's build gives the files,
    // records and console text single-shot's build gives (the folder replaced).
    [Theory]
    [InlineData("build Lib/Lib.kimiproj --NoSuchOption x")]
    [InlineData("build Lib/Lib.kimiproj")]
    public async Task ALineRunsAsSingleShotRunsIt(string line)
    {
        var single = Path.Combine(this.directory, "single");
        var session = Path.Combine(this.directory, "session");
        foreach (var root in new[] { single, session })
        {
            WriteLibrary(Path.Combine(root, "Lib"));
        }

        var console = new SessionConsole();
        var product = Unit(console);
        var current = Environment.CurrentDirectory;
        Environment.CurrentDirectory = single;
        try
        {
            Environment.ExitCode = 0;
            await product.Execute(line, product.Context.ServiceProvider, TestContext.Current.CancellationToken);
        }
        finally
        {
            Environment.CurrentDirectory = current;
        }

        var singleExit = Environment.ExitCode;
        Environment.ExitCode = 0;
        var (code, text, _) = await this.Session(line + "\n", session);
        Assert.Equal(singleExit == 0 ? 0 : 1, code);
        var expected = console.Text.Replace(single, "<W>", StringComparison.Ordinal) + (singleExit == 0 ? string.Empty : $"Exit code: {singleExit}\n");
        Assert.Equal(expected, text.Replace(session, "<W>", StringComparison.Ordinal));
        Assert.Equal(Files(single), Files(session));
    }

    // Each line gets a new scope, so a new command and a new Solution (§2.2); the session's exit code is 0 when every line succeeded
    // and 1 when one failed.
    [Fact]
    public async Task EachLineGetsItsOwnSolutionAndTheSessionCountsFailures()
    {
        this.Library("Lib");
        var console = new SessionConsole();
        var (code, _, _) = await Session("build Lib/Lib.kimiproj\nbuild Lib/Lib.kimiproj\n", this.directory, console, TestContext.Current.CancellationToken);
        Assert.Equal(0, code);
        Assert.Equal(2, console.Solutions);
        (code, _, _) = await this.Session("build Lib/Lib.kimiproj\nbuild Missing.kimiproj\nbuild Lib/Lib.kimiproj\n");
        Assert.Equal(1, code);
    }

    // A removed option fails its line alone (Tatara B6b4, design §5.2 row 3): its text on standard error and the line's exit code 1;
    // the next line builds, and the session returns 1.
    [Fact]
    public async Task ARemovedOptionFailsItsLineAlone()
    {
        this.Library("Lib");
        var error = Console.Error;
        using var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            var (code, text, _) = await this.Session("build Lib/Lib.kimiproj --ToolchainRoot x\nbuild Lib/Lib.kimiproj\n");
            Assert.Equal(1, code);
            Assert.Equal(1, Count(text, "Exit code: 1"));
            Assert.Equal(1, Count(text, "Project: Lib (Lib.kimiproj)"));
        }
        finally
        {
            Console.SetError(error);
        }

        Assert.Equal("--ToolchainRoot was removed: Kimi carries its toolchain.", stderr.ToString().TrimEnd());
    }

    // The Ctrl+C action (§2.4): the session replaces Program.CancelKey and restores it at its end; a press during a line cancels that
    // line's token ("Command cancelled.", 130, a failed line) and the next line runs.
    [Fact]
    public async Task APressDuringALineCancelsOnlyThatLine()
    {
        this.Library("Lib");
        var previous = Program.CancelKey;
        var pressed = 0;
        var console = new SessionConsole
        {
            OnProject = () =>
            {
                if (pressed++ == 0)
                {
                    Program.CancelKey();
                }
            },
        };
        var (code, text, _) = await Session("build Lib/Lib.kimiproj\nbuild Lib/Lib.kimiproj\n", this.directory, console, TestContext.Current.CancellationToken);
        Assert.Same(previous, Program.CancelKey);
        Assert.Equal(1, code);
        Assert.Equal(1, Count(text, "Command cancelled."));
        Assert.Contains("Exit code: 130", text, StringComparison.Ordinal);
        Assert.Equal(2, Count(text, "Project: Lib (Lib.kimiproj)"));
    }

    // The root's termination (the window's close, logoff, a press with nothing pending: the token Run takes) cancels the running line
    // and ends the session cancelled (IMPL 20.3: "Cancellation returns 130"): the line's "Command cancelled." and the session's, 130; the
    // second line never runs.
    [Fact]
    public async Task TheRootsTerminationEndsTheSession()
    {
        this.Library("Lib");
        var previous = Program.CancelKey;
        using var root = new CancellationTokenSource();
        var console = new SessionConsole { OnProject = root.Cancel };
        var (code, text, _) = await Session("build Lib/Lib.kimiproj\nbuild Lib/Lib.kimiproj\n", this.directory, console, root.Token);
        Assert.Same(previous, Program.CancelKey);
        Assert.Equal(130, code);
        Assert.Equal(2, Count(text, "Command cancelled."));
        Assert.Equal(1, Count(text, "Project: Lib (Lib.kimiproj)"));
    }

    // F1 (the Coordinator's IMPL review of A4; IMPL 20.3: "Cancellation returns 130"): a press while the redirected reader waits requests
    // the root's termination, which cannot interrupt a console read already started; when that read returns a whole line or the end of
    // input, the session ends cancelled ("Command cancelled.", 130) and the line does not run. The row's action cancels the root's
    // token, standing in for the default, which no in-process test calls (r2).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task APressWhileTheReaderWaitsEndsTheSessionWith130(bool line)
    {
        this.Library("Lib");
        var previous = Program.CancelKey;
        using var root = new CancellationTokenSource();
        Program.CancelKey = root.Cancel;
        try
        {
            Action press = () => Program.CancelKey();
            var input = line ? new LineStream([("build Lib/Lib.kimiproj", press)]) : new LineStream([], press);
            var (code, text) = await Session(input, this.directory, new SessionConsole(), root.Token);
            Assert.Equal(130, code);
            Assert.Equal(1, Count(text, "Command cancelled."));
            Assert.Equal(0, Count(text, "Project: Lib (Lib.kimiproj)"));
        }
        finally
        {
            Program.CancelKey = previous;
        }
    }

    // A second press during a line already cancelled ends the process at once with 130 by Program.EndProcess: a child test process runs
    // a session whose line's build presses twice.
    [Fact]
    public async Task ASecondPressDuringALineEndsTheProcessWith130()
    {
        this.Library("Lib");
        await this.RunChildEndingWith130(nameof(Child.PressesTwiceDuringALine));
    }

    // The default action (Tatara design A4 §2.2, the owner's O-460 (a)): its first press requests the root's termination and leaves the
    // process running; its second ends the process at once with 130. Its flag is process-wide and never resets, so a child test process
    // presses (r2); it marks the folder between the presses.
    [Fact]
    public async Task TheDefaultActionsSecondPressEndsTheProcessWith130()
    {
        await this.RunChildEndingWith130(nameof(Child.PressesTheDefaultTwice));
        Assert.True(File.Exists(Path.Combine(this.directory, Child.FirstPress)));
    }

    // With no read or line pending, the redirected reader waiting for input, a press is the action the session replaced (design A4
    // §2.2, C-427 N6): the row's own action stands for the default, which no in-process test calls (r2).
    [Fact]
    public async Task APressWhileTheReaderWaitsCallsTheReplacedAction()
    {
        var previous = Program.CancelKey;
        var pressed = 0;
        Action action = () => pressed++;
        Program.CancelKey = action;
        try
        {
            var command = Unit(new SessionConsole()).Context.ServiceProvider.GetRequiredService<TataraCommand>();
            Assert.Equal(0, await command.Run(new LineStream([("exit", () => Program.CancelKey())]), TestContext.Current.CancellationToken));
            Assert.Equal(1, pressed);
            Assert.Same(action, Program.CancelKey);
        }
        finally
        {
            Program.CancelKey = previous;
        }
    }

    // A unit built as the CLI builds it, with the host's console replaced; the console counts the Solution objects it creates.
    internal static CommandUnit.Product Unit(SessionConsole console)
    {
        var builder = new CommandUnit.Builder();
        builder.Configure(context =>
        {
            context.Services.AddSingleton<IConsoleService>(console);
            context.Services.AddTransient(x =>
            {
                console.Solutions++;
                return new Solution(x.GetRequiredService<Kimigayo>());
            });
        });
        return (CommandUnit.Product)builder.Build();
    }

    internal static int Count(string text, string part)
    {
        var count = 0;
        for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    // Every file under a folder (text files: a Library build writes its record only), by its relative path and its text with the folder
    // replaced, as written and JSON-escaped.
    internal static string Files(string root)
        => string.Join('\n', Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(x => Path.GetRelativePath(root, x) + "\n" + File.ReadAllText(x).Replace(root.Replace("\\", "\\\\", StringComparison.Ordinal), "<W>", StringComparison.Ordinal)
                .Replace(root, "<W>", StringComparison.Ordinal)));

    // A session over the input in the folder, with the root's token.
    internal static async Task<(int Code, string Text, MemoryStream Input)> Session(string input, string folder, SessionConsole console, CancellationToken cancellationToken)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        var (code, text) = await Session(stream, folder, console, cancellationToken);
        return (code, text, stream);
    }

    // A session over the input in the folder, with the root's token, run as the command runs it: through CommandExecution, which ends a
    // cancelled session with "Command cancelled." and 130.
    internal static async Task<(int Code, string Text)> Session(Stream input, string folder, SessionConsole console, CancellationToken cancellationToken)
    {
        var provider = Unit(console).Context.ServiceProvider;
        var command = provider.GetRequiredService<TataraCommand>();
        var current = Environment.CurrentDirectory;
        Environment.CurrentDirectory = folder;
        try
        {
            Environment.ExitCode = 0;
            var code = await CommandExecution.Execute(provider.GetRequiredService<Kimigayo>(), [], () => command.Run(input, cancellationToken));
            Assert.Equal(0, Environment.ExitCode);
            return (code, console.Text);
        }
        finally
        {
            Environment.CurrentDirectory = current;
        }
    }

    // A Library project in the folder: one function, no native build.
    internal static void WriteLibrary(string path)
    {
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "Lib.kimiproj"), $"OutputKind=\"Library\" Targets={{\"{WindowsProfile.Target}\"}}");
        File.WriteAllText(Path.Combine(path, "lib.kimi"), "func one() -> i32 => 1\n");
    }

    private Task<(int Code, string Text, MemoryStream Input)> Session(string input, string? folder = null)
        => Session(input, folder ?? this.directory, new SessionConsole(), TestContext.Current.CancellationToken);

    private void Library(string folder) => WriteLibrary(Path.Combine(this.directory, folder));

    // A Child row in a child test process with this row's folder: it ends the process with 130, so it never reports a result.
    private async Task RunChildEndingWith130(string method)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        start.Environment[Child.Folder] = this.directory;
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(typeof(TataraCommandTest).Assembly.Location);
        }

        foreach (var argument in new[] { "-method", $"{typeof(Child).FullName}.{method}", "-explicit", "only" })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        _ = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.True(process.ExitCode == 130, await output);
    }

    // The child rows, each run by its parent row in a child test process: each ends the process with 130, so it never reports a result.
    // Its folder is the parent's (named by Folder, removed by the parent).
    public sealed class Child
    {
        internal const string Folder = "KIMI_TATARA_PRESS_FOLDER";

        internal const string FirstPress = "first press";

        // The default action twice, the folder marked between the presses.
        [Fact(Explicit = true)]
        public void PressesTheDefaultTwice()
        {
            Program.CancelKey();
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Folder)!, FirstPress), string.Empty);
            Program.CancelKey();
            Assert.Fail("The default action's second press did not end the process.");
        }

        // The session's line presses twice.
        [Fact(Explicit = true)]
        public async Task PressesTwiceDuringALine()
        {
            var console = new SessionConsole
            {
                OnProject = () =>
                {
                    Program.CancelKey();
                    Program.CancelKey();
                },
            };
            await Session("build Lib/Lib.kimiproj\n", Environment.GetEnvironmentVariable(Folder)!, console, TestContext.Current.CancellationToken);
            Assert.Fail("The second press did not end the process.");
        }
    }

    // The session's input: each line's bytes and a line feed, then its end. A line's action runs in the read that returns its line feed,
    // and the end's in the read that returns the end: while the reader waits (no read or line pending), before the session sees the
    // line or the end.
    internal sealed class LineStream((string Line, Action? Before)[] lines, Action? atEnd = null) : Stream
    {
        private int line = -1;
        private byte[] current = [];
        private int position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (this.position == this.current.Length)
            {
                if (this.line + 1 == lines.Length)
                {
                    atEnd?.Invoke();
                    return 0;
                }

                this.line++;
                this.current = Encoding.UTF8.GetBytes(lines[this.line].Line + "\n");
                this.position = 0;
            }

            if (this.position == this.current.Length - 1)
            {
                lines[this.line].Before?.Invoke();
            }

            buffer[offset] = this.current[this.position++];
            return 1;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // The host's console: its text, with a hook on a build's project line (the moment a line's build runs).
    internal sealed class SessionConsole : IConsoleService
    {
        private readonly StringBuilder text = new();

        public string Text => this.text.ToString();

        public bool KeyAvailable => false;

        public bool EnableColor { get; set; }

        public Action? OnProject { get; init; }

        public int Solutions { get; set; }

        public void Write(string? message, ConsoleColor color = ConsoleColor.Gray) => this.text.Append(message);

        public void Write(ReadOnlySpan<char> message, ConsoleColor color = ConsoleColor.Gray) => this.text.Append(message);

        public void WriteLine(string? message, ConsoleColor color = ConsoleColor.Gray) => this.WriteLine(message.AsSpan(), color);

        public void WriteLine(ReadOnlySpan<char> message, ConsoleColor color = ConsoleColor.Gray)
        {
            this.text.Append(message).Append('\n');
            if (message.StartsWith("Project: ", StringComparison.Ordinal))
            {
                this.OnProject?.Invoke();
            }
        }

        public Task<InputResult> ReadLineAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public ConsoleKeyInfo ReadKey(bool intercept) => throw new NotSupportedException();
    }
}
