// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;

namespace Kimi.Lsp;

/// <summary>Reads <c>Content-Length</c> frames (SPEC 23.4.1) with bounded headers and payloads.</summary>
internal sealed class LspFrameReader
{
    /// <summary>The largest accepted header section.</summary>
    public const int MaxHeaderBytes = 8 * 1024;

    /// <summary>The largest accepted payload.</summary>
    public const int MaxPayloadBytes = 64 * 1024 * 1024;

    private static ReadOnlySpan<byte> ContentLength => "content-length:"u8;

    private readonly Stream input;
    private readonly byte[] header = new byte[MaxHeaderBytes];
    private int start;
    private int end;

    /// <summary>Initializes a new instance of the <see cref="LspFrameReader"/> class.</summary>
    /// <param name="input">The input stream.</param>
    public LspFrameReader(Stream input)
    {
        this.input = input;
    }

    /// <summary>Reads the next frame into a rented buffer, which the caller returns to <see cref="ArrayPool{T}.Shared"/>.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The payload buffer and length, or null at the end of the input.</returns>
    /// <exception cref="InvalidDataException">The header is malformed or a bound is exceeded.</exception>
    public async ValueTask<(byte[] Buffer, int Length)?> ReadAsync(CancellationToken cancellationToken)
    {
        var contentLength = -1;
        var headerBytes = 0;
        var hasHeader = false;
        while (true)
        {
            var (offset, line, consumedBytes) = await this.ReadLineAsync(MaxHeaderBytes - headerBytes, cancellationToken).ConfigureAwait(false);
            headerBytes += consumedBytes;
            if (line < 0)
            {
                if (!hasHeader)
                {
                    return null;
                }

                throw new InvalidDataException("The input ended inside a frame header.");
            }

            if (line == 0)
            {
                if (contentLength >= 0)
                {
                    break;
                }

                if (hasHeader)
                {
                    throw new InvalidDataException("Missing Content-Length header.");
                }

                continue; // Tolerate blank lines between frames.
            }

            hasHeader = true;
            var text = this.header.AsSpan(offset, line);
            if (StartsWithIgnoreCase(text, ContentLength))
            {
                var value = text[ContentLength.Length..].Trim((byte)' ');
                if (contentLength >= 0 || value.IndexOfAnyExceptInRange((byte)'0', (byte)'9') >= 0 ||
                    !Utf8Parser.TryParse(value, out int parsed, out var consumed) || consumed != value.Length || parsed < 0 || parsed > MaxPayloadBytes)
                {
                    throw new InvalidDataException("Invalid Content-Length header.");
                }

                contentLength = parsed;
            }
        }

        var payload = ArrayPool<byte>.Shared.Rent(Math.Max(contentLength, 1));
        try
        {
            var buffered = Math.Min(this.end - this.start, contentLength);
            this.header.AsSpan(this.start, buffered).CopyTo(payload);
            this.start += buffered;
            if (buffered < contentLength)
            {
                await this.input.ReadExactlyAsync(payload.AsMemory(buffered, contentLength - buffered), cancellationToken).ConfigureAwait(false);
            }

            return (payload, contentLength);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(payload);
            throw;
        }
    }

    private static bool StartsWithIgnoreCase(ReadOnlySpan<byte> text, ReadOnlySpan<byte> prefix)
    {
        if (text.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            var c = text[i];
            if ((uint)(c - 'A') <= 'Z' - 'A')
            {
                c = (byte)(c + 0x20);
            }

            if (c != prefix[i])
            {
                return false;
            }
        }

        return true;
    }

    // Returns the offset and length of the next header line without its terminator, advancing past it.
    // The line stays valid until the next call; the length is -1 at the end of the input.
    private async ValueTask<(int Offset, int Length, int Consumed)> ReadLineAsync(int remaining, CancellationToken cancellationToken)
    {
        while (true)
        {
            var index = this.header.AsSpan(this.start, this.end - this.start).IndexOf((byte)'\n');
            if (index >= 0)
            {
                if (index + 1 > remaining)
                {
                    throw new InvalidDataException("A frame header exceeds its bound.");
                }

                var offset = this.start;
                var length = index > 0 && this.header[offset + index - 1] == (byte)'\r' ? index - 1 : index;
                this.start += index + 1;
                return (offset, length, index + 1);
            }

            if (this.start > 0)
            {
                this.header.AsSpan(this.start, this.end - this.start).CopyTo(this.header);
                this.end -= this.start;
                this.start = 0;
            }

            if (this.end >= remaining)
            {
                throw new InvalidDataException("A frame header exceeds its bound.");
            }

            var read = await this.input.ReadAsync(this.header.AsMemory(this.end), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (this.end != 0)
                {
                    throw new InvalidDataException("The input ended inside a frame header.");
                }

                return (0, -1, 0);
            }

            this.end += read;
        }
    }
}

/// <summary>Writes frames in order from one queue, so no two frames interleave and the state owner never waits on output.</summary>
internal sealed class LspSender
{
    private readonly Stream output;
    private readonly Channel<Action<Utf8JsonWriter>> queue = Channel.CreateUnbounded<Action<Utf8JsonWriter>>(new() { SingleReader = true });
    private readonly ArrayBufferWriter<byte> body = new(4096);
    private readonly byte[] headerBytes = new byte[64];
    private readonly Task pump;

    /// <summary>Initializes a new instance of the <see cref="LspSender"/> class.</summary>
    /// <param name="output">The output stream; nothing else may write to it.</param>
    public LspSender(Stream output)
    {
        this.output = output;
        this.pump = Task.Run(this.PumpAsync);
    }

    /// <summary>Queues a success response; a null result is written as <c>"result":null</c>.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="id">The request ID.</param>
    /// <param name="result">The result.</param>
    /// <param name="typeInfo">The result's serializer metadata.</param>
    public void Result<T>(JsonElement? id, T? result, JsonTypeInfo<T>? typeInfo)
        => this.queue.Writer.TryWrite(writer =>
        {
            Begin(writer, id);
            writer.WritePropertyName("result");
            if (result is null || typeInfo is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                JsonSerializer.Serialize(writer, result, typeInfo);
            }

            writer.WriteEndObject();
        });

    /// <summary>Queues an error response; it carries <c>error</c> and no <c>result</c>.</summary>
    /// <param name="id">The request ID, or null when it is unknown.</param>
    /// <param name="code">The JSON-RPC error code.</param>
    /// <param name="message">The error message.</param>
    public void Error(JsonElement? id, int code, string message)
        => this.queue.Writer.TryWrite(writer =>
        {
            Begin(writer, id);
            writer.WritePropertyName("error");
            JsonSerializer.Serialize(writer, new JsonRpcError { Code = code, Message = message }, LspJsonContext.Default.JsonRpcError);
            writer.WriteEndObject();
        });

    /// <summary>Queues a notification or request serialized from one immutable value.</summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="message">The message.</param>
    /// <param name="typeInfo">The message's serializer metadata.</param>
    public void Send<T>(T message, JsonTypeInfo<T> typeInfo)
        => this.queue.Writer.TryWrite(writer => JsonSerializer.Serialize(writer, message, typeInfo));

    /// <summary>Waits until every frame queued so far is written, or the output has closed.</summary>
    /// <returns>A task that completes when the earlier frames are written or the sender stops.</returns>
    public Task FlushAsync()
    {
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!this.queue.Writer.TryWrite(_ => written.TrySetResult()))
        {
            written.TrySetResult();
        }

        return Task.WhenAny(written.Task, this.pump);
    }

    /// <summary>Stops accepting messages and waits until every queued frame is written.</summary>
    /// <returns>A task that completes when the output is drained.</returns>
    public async Task DrainAsync()
    {
        this.queue.Writer.TryComplete();
        await this.pump.ConfigureAwait(false);
    }

    private static void Begin(Utf8JsonWriter writer, JsonElement? id)
    {
        writer.WriteStartObject();
        writer.WriteString("jsonrpc", "2.0");
        writer.WritePropertyName("id");
        if (id is { } value && value.ValueKind is JsonValueKind.Number or JsonValueKind.String)
        {
            value.WriteTo(writer);
        }
        else
        {
            writer.WriteNullValue();
        }
    }

    private static int WriteHeader(Span<byte> destination, int contentLength)
    {
        "Content-Length: "u8.CopyTo(destination);
        var position = 16;
        Utf8Formatter.TryFormat(contentLength, destination[position..], out var written);
        position += written;
        "\r\n\r\n"u8.CopyTo(destination[position..]);
        return position + 4;
    }

    private async Task PumpAsync()
    {
        using var writer = new Utf8JsonWriter(this.body);
        try
        {
            await foreach (var write in this.queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                this.body.ResetWrittenCount();
                writer.Reset(this.body);
                write(writer);
                writer.Flush();
                if (this.body.WrittenCount == 0)
                {
                    continue; // A flush marker.
                }

                var length = WriteHeader(this.headerBytes, this.body.WrittenCount);
                await this.output.WriteAsync(this.headerBytes.AsMemory(0, length)).ConfigureAwait(false);
                await this.output.WriteAsync(this.body.WrittenMemory).ConfigureAwait(false);
                await this.output.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // The client went away; there is nobody left to write to.
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            this.queue.Writer.TryComplete();
            while (this.queue.Reader.TryRead(out _))
            {
                // Release messages that can no longer be sent.
            }
        }
    }
}
