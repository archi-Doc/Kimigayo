// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using System.Text;
using Kimi.Build;
using Kimi.Diagnostics;
using Kimi.Unit;
using SimpleCommandLine;
using SimplePrompt;

namespace Kimi.Command;

// kimi tatara (Tatara design A3 §2): reads lines and runs each as `kimi build ...` or `kimi run ...` would run, in one process, the
// native build done by Tatara. Inputs: standard input, a terminal's (SimplePrompt; the only path that touches SimpleConsole) or a
// redirected one (read one line at a time without read-ahead). Outputs: each line's command output, its exit code when it is not 0,
// and the session's exit code (0 when every line succeeded; 1 when any failed, a cancelled line included, or when Tatara's refusal
// ended the session). Invariants: a line is a command line, parsed and executed by CommandUnit.Product.Execute with a new DI scope's
// provider and the line's own token, so nothing is reused between lines but the process's library per target, Tatara's loaded DLL
// per path and its refusal, Kimigayo, the current directory and the runtime; the session owns Ctrl+C while it runs (Program.CancelKey)
// and restores it at its end. Prohibitions: no build rule of its own; the loop catches nothing (CommandExecution classifies a
// command's failures); a refusal is read from Tatara, never from a message's text. Temporary (Tatara A3 to A4): the session sets
// Kimigayo.NativeExecutor to Tatara's factory; unit A4 makes Tatara's executor the build's.
[SimpleCommand("tatara")]
public sealed partial class TataraCommand(Kimigayo kimigayo, UnitContext unitContext, IServiceScopeFactory scopes) : ISimpleCommand
{
    /// <summary>The longest line, in UTF-16 code units: a command line's limit (CreateProcess).</summary>
    internal const int LineLimit = 32_767;

    private static readonly ReadLineOptions PromptOptions = ReadLineOptions.SingleLine with
    {
        Prompt = "tatara> ",
        MultilineDelimiter = null,
        AllowEmptyInput = true,
        MaxInputLength = LineLimit + 1, // One above the limit, so a longer line is refused, not cut.
    };

    private readonly CommandUnit.Product product = new(unitContext);
    private Pending? pending; // What a Ctrl+C cancels: the prompt's read or the running line; replaced as a whole.
    private bool started; // Whether the redirected input's first line was read (its byte order mark is skipped).

    public async Task Execute(string[] args, CancellationToken cancellationToken)
    {
        Environment.ExitCode = await CommandExecution.Execute(kimigayo, async () =>
        {
            if (args.Length != 0)
            {
                throw new CommandInputException("Use kimi tatara without arguments; it reads build and run lines from standard input.");
            }

            return await this.Run(Console.IsInputRedirected ? Console.OpenStandardInput() : null, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    // Ends this process at once with 130 (a second Ctrl+C during a cancelled line): TerminateProcess, not Environment.Exit, which would
    // run tatara.dll's detach, and that can wait on a lock the stuck thread holds.
    internal static void EndProcess() => TerminateProcess(GetCurrentProcess(), 130);

    /// <summary>Runs a session over a redirected input, or over the terminal's prompt when the input is null.</summary>
    /// <param name="input">The redirected input, or null for the terminal.</param>
    /// <param name="cancellationToken">The root's token: its cancellation ends the session.</param>
    /// <returns>The session's exit code.</returns>
    internal async Task<int> Run(Stream? input, CancellationToken cancellationToken)
    {
        kimigayo.NativeExecutor = NativeToolchain.TataraExecutor.Create;
        var previous = Program.CancelKey;
        Program.CancelKey = this.CancelKey;
        using var registration = cancellationToken.Register(() => Volatile.Read(ref this.pending)?.Source.Cancel());
        var failed = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested && await this.ReadLine(input, cancellationToken).ConfigureAwait(false) is { } line)
            {
                var words = SimpleParserHelper.SplitArguments(line, default);
                if (words.Length == 0)
                {
                    continue; // An empty line.
                }
                else if (words is [var word] && word.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var code = Refusal(line, words) is { } error ? this.Fail(error) : await this.RunLine(line, cancellationToken).ConfigureAwait(false);
                if (code != 0)
                {
                    failed = true;
                    kimigayo.WriteLine(DiagnosticSeverity.Information, $"Exit code: {code}");
                }

                if (Tatara.FirstRefusal() is { } cause)
                {
                    kimigayo.WriteLine(DiagnosticSeverity.Error, $"Tatara cannot run again in this process ({cause.Trim()}); a new kimi tatara starts clean.");
                    return 1;
                }
            }
        }
        finally
        {
            Volatile.Write(ref this.pending, null);
            Program.CancelKey = previous;
        }

        return failed ? 1 : 0;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(nint process, uint exitCode);

    // The gate (§2.1), on SimpleCommandLine's own split of a nonempty line other than exit: build and run (case-insensitive, as command
    // names compare) are command lines; anything else is that line's error.
    private static string? Refusal(string line, string[] words)
    {
        if (line.Length > LineLimit)
        {
            return $"The line is longer than {LineLimit} characters.";
        }
        else if (Array.IndexOf(words, "|") >= 0)
        {
            return "A line takes no unenclosed '|': kimi tatara runs one command per line.";
        }
        else if (words[0].Equals("exit", StringComparison.OrdinalIgnoreCase))
        {
            return "Use exit without arguments.";
        }

        return words[0].Equals("build", StringComparison.OrdinalIgnoreCase) || words[0].Equals("run", StringComparison.OrdinalIgnoreCase) ?
            null : $"Unknown command '{words[0]}'. A kimi tatara line is build, run or exit.";
    }

    private int Fail(string message)
    {
        kimigayo.WriteLine(DiagnosticSeverity.Error, message);
        return 1;
    }

    // One line: a new scope (a new command and Solution) and the line's own token; the command's code read from Environment.ExitCode,
    // which is reset for the next line.
    private async Task<int> RunLine(string line, CancellationToken cancellationToken)
    {
        var source = new CancellationTokenSource();
        Volatile.Write(ref this.pending, new Pending(source, true));
        if (cancellationToken.IsCancellationRequested)
        {
            source.Cancel();
        }

        Environment.ExitCode = 0;
        using (var scope = scopes.CreateScope())
        {
            await this.product.Execute(line, scope.ServiceProvider, source.Token).ConfigureAwait(false);
        }

        Volatile.Write(ref this.pending, null);
        var code = Environment.ExitCode;
        Environment.ExitCode = 0;
        return code;
    }

    // A Ctrl+C in a session (§2.4): at the prompt it cancels the read (the prompt is written again); during a line it cancels the line's
    // token; during a line already cancelled it ends the process at once with 130.
    private void CancelKey()
    {
        if (Volatile.Read(ref this.pending) is not { } state)
        {
            return;
        }
        else if (state.IsLine && state.Source.IsCancellationRequested)
        {
            EndProcess();
        }

        state.Source.Cancel();
    }

    private Task<string?> ReadLine(Stream? input, CancellationToken cancellationToken)
        => input is null ? this.ReadPrompt(cancellationToken) : this.ReadRedirected(input, cancellationToken);

    // The terminal's line: SimplePrompt, single-line; a cancelled read writes the prompt again, a terminated one ends the input.
    private async Task<string?> ReadPrompt(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var state = new Pending(new CancellationTokenSource(), false);
            Volatile.Write(ref this.pending, state);
            var result = await SimpleConsole.Instance.ReadLineAsync(PromptOptions, state.Source.Token).ConfigureAwait(false);
            Volatile.Write(ref this.pending, null);
            if (result.IsSuccess)
            {
                return result.Text;
            }
            else if (result.IsTerminated)
            {
                return null;
            }
        }

        return null;
    }

    // The redirected input's line: bytes up to the line feed, one read each, so nothing after the line is consumed (a run program shares
    // the input, O-454 (b)); UTF-8, a carriage return before the line feed dropped, a byte order mark at the start of the input skipped;
    // null at the end of the input. A line is kept up to four bytes per allowed code unit, so a longer one decodes above the limit.
    private async Task<string?> ReadRedirected(Stream input, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        var read = 0;
        while (true)
        {
            if (await input.ReadAsync(one, cancellationToken).ConfigureAwait(false) == 0)
            {
                if (read == 0)
                {
                    return null;
                }

                break;
            }
            else if (one[0] == (byte)'\n')
            {
                break;
            }

            read++;
            if (bytes.Count <= 4 * LineLimit)
            {
                bytes.Add(one[0]);
            }
        }

        var line = CollectionsMarshal.AsSpan(bytes);
        if (!this.started && line.StartsWith("﻿"u8))
        {
            line = line[3..];
        }

        this.started = true;
        return Encoding.UTF8.GetString(line.EndsWith("\r"u8) ? line[..^1] : line);
    }

    // The state a Ctrl+C reads, replaced as a whole: the source it cancels and whether it is a line's (else the prompt's read).
    private sealed record Pending(CancellationTokenSource Source, bool IsLine);
}
