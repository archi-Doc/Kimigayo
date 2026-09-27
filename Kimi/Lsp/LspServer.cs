// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Text.Json;
using System.Threading.Channels;

#pragma warning disable SA1402 // The queue items belong to the host.

namespace Kimi.Lsp;

/// <summary>
/// The language server host (SPEC 23.4): the receive loop, the state owner loop and the sender over injected streams.
/// </summary>
public sealed class LspServer
{
    /// <summary>Runs one session until <c>exit</c> or the end of the input.</summary>
    /// <param name="input">The client's messages.</param>
    /// <param name="output">The protocol output; nothing else may write to it.</param>
    /// <param name="cancellationToken">Ends the session like the end of the input.</param>
    /// <returns>The process exit code: 0 after <c>shutdown</c>, otherwise 1.</returns>
    public Task<int> Run(Stream input, Stream output, CancellationToken cancellationToken)
        => Run(input, output, static () => Environment.TickCount64, null, cancellationToken);

    /// <summary>Runs one session with a replaceable clock and runner, for tests.</summary>
    /// <param name="input">The client's messages.</param>
    /// <param name="output">The protocol output.</param>
    /// <param name="clock">The time in milliseconds.</param>
    /// <param name="configure">Configures the session before the first message.</param>
    /// <param name="cancellationToken">Ends the session like the end of the input.</param>
    /// <returns>The exit code.</returns>
    internal static async Task<int> Run(Stream input, Stream output, Func<long> clock, Action<LspSession>? configure, CancellationToken cancellationToken)
    {
        var queue = Channel.CreateUnbounded<object>(new() { SingleReader = true });
        var writer = queue.Writer;
        var sender = new LspSender(output);
        var session = new LspSession(sender, static check => Task.Run(check.Run), item => writer.TryWrite(item));
        configure?.Invoke(session);
        using var timer = new Timer(static state => ((ChannelWriter<object>)state!).TryWrite(CheckTimer.Instance), writer, Timeout.Infinite, Timeout.Infinite);
        var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var receive = Receive(new(input), writer, stop.Token);
        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var now = clock();
                session.Process(item, now);
                if (session.Exited)
                {
                    break;
                }

                session.Tick(now);
                timer.Change(session.Deadline is { } deadline ? Math.Max(0, deadline - now) : Timeout.Infinite, Timeout.Infinite);
            }
        }
        catch (OperationCanceledException)
        {
        }

        // Exit waits for neither a running check nor the receive loop: a console read ignores cancellation.
        writer.TryComplete();
        await stop.CancelAsync().ConfigureAwait(false);
        _ = receive.ContinueWith(static (_, state) => ((CancellationTokenSource)state!).Dispose(), stop, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        await sender.DrainAsync().ConfigureAwait(false);
        return session.ExitCode;
    }

    // Reads frames and enqueues each parsed message; a malformed body is answered, a malformed header ends the input.
    private static async Task Receive(LspFrameReader reader, ChannelWriter<object> writer, CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false) is { } frame)
            {
                var (buffer, length) = frame;
                try
                {
                    writer.TryWrite(JsonSerializer.Deserialize(buffer.AsSpan(0, length), LspJsonContext.Default.LspMessage) ?? (object)new InvalidFrame(-32600, "Invalid request."));
                }
                catch (JsonException ex)
                {
                    writer.TryWrite(new InvalidFrame(-32700, "Parse error: " + ex.Message));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
        catch (InvalidDataException ex)
        {
            writer.TryWrite(new InvalidFrame(-32700, ex.Message));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        writer.TryWrite(EndOfInput.Instance);
    }
}

/// <summary>A frame that could not be parsed; the state owner answers with a JSON-RPC error.</summary>
/// <param name="Code">The error code.</param>
/// <param name="Message">The message.</param>
internal sealed record InvalidFrame(int Code, string Message);

/// <summary>The input ended.</summary>
internal sealed class EndOfInput
{
    /// <summary>The only instance.</summary>
    public static readonly EndOfInput Instance = new();
}

/// <summary>The check deadline passed.</summary>
internal sealed class CheckTimer
{
    /// <summary>The only instance.</summary>
    public static readonly CheckTimer Instance = new();
}
