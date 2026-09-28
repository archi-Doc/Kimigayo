// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Buffers.Text;
using System.Runtime.InteropServices;
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
/// <remarks>
/// Messages are queued as values and serialized on the pump. Each frame is written once, with its header before the body
/// in one buffer, and the output is flushed when the queue runs empty rather than after every frame.
/// </remarks>
internal sealed class LspSender
{
    // "Content-Length: ", at most ten digits and the blank line fit before the body.
    private const int HeaderSpace = 32;

    private static readonly JsonEncodedText JsonRpcName = JsonEncodedText.Encode("jsonrpc");
    private static readonly JsonEncodedText VersionValue = JsonEncodedText.Encode("2.0");
    private static readonly JsonEncodedText IdName = JsonEncodedText.Encode("id");
    private static readonly JsonEncodedText MethodName = JsonEncodedText.Encode("method");
    private static readonly JsonEncodedText ParamsName = JsonEncodedText.Encode("params");
    private static readonly JsonEncodedText ResultName = JsonEncodedText.Encode("result");
    private static readonly JsonEncodedText ErrorName = JsonEncodedText.Encode("error");
    private static readonly JsonEncodedText CodeName = JsonEncodedText.Encode("code");
    private static readonly JsonEncodedText MessageName = JsonEncodedText.Encode("message");

    private readonly Stream output;
    private readonly Channel<Outgoing> queue = Channel.CreateUnbounded<Outgoing>(new() { SingleReader = true });
    private readonly ArrayBufferWriter<byte> frame = new(4096);
    private readonly Task pump;

    /// <summary>Initializes a new instance of the <see cref="LspSender"/> class.</summary>
    /// <param name="output">The output stream; nothing else may write to it.</param>
    public LspSender(Stream output)
    {
        this.output = output;
        this.pump = Task.Run(this.PumpAsync);
    }

    private enum OutgoingKind : byte
    {
        Result,
        Error,
        Request,
        Notification,
        Flush,
    }

    /// <summary>Queues a success response; a null result is written as <c>"result":null</c>.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="id">The request ID.</param>
    /// <param name="result">The result.</param>
    /// <param name="typeInfo">The result's serializer metadata.</param>
    public void Result<T>(RequestId? id, T? result, JsonTypeInfo<T>? typeInfo)
        => this.queue.Writer.TryWrite(new(OutgoingKind.Result, id, null, result, typeInfo));

    /// <summary>Queues an error response; it carries <c>error</c> and no <c>result</c>.</summary>
    /// <param name="id">The request ID, or null when it is unknown.</param>
    /// <param name="code">The JSON-RPC error code.</param>
    /// <param name="message">The error message.</param>
    public void Error(RequestId? id, int code, string message)
        => this.queue.Writer.TryWrite(new(OutgoingKind.Error, id, message, null, null, code));

    /// <summary>Queues a request to the client.</summary>
    /// <typeparam name="T">The parameter type.</typeparam>
    /// <param name="id">The request ID.</param>
    /// <param name="method">The method.</param>
    /// <param name="parameters">The parameters, which must not change afterwards.</param>
    /// <param name="typeInfo">The parameters' serializer metadata.</param>
    public void Request<T>(int id, string method, T parameters, JsonTypeInfo<T> typeInfo)
        => this.queue.Writer.TryWrite(new(OutgoingKind.Request, new(id, null), method, parameters, typeInfo));

    /// <summary>Queues a notification.</summary>
    /// <typeparam name="T">The parameter type.</typeparam>
    /// <param name="method">The method.</param>
    /// <param name="parameters">The parameters, which must not change afterwards.</param>
    /// <param name="typeInfo">The parameters' serializer metadata.</param>
    public void Notify<T>(string method, T parameters, JsonTypeInfo<T> typeInfo)
        => this.queue.Writer.TryWrite(new(OutgoingKind.Notification, null, method, parameters, typeInfo));

    /// <summary>Waits until every frame queued so far is written and flushed, or the output has closed.</summary>
    /// <returns>A task that completes when the earlier frames are written or the sender stops.</returns>
    public Task FlushAsync()
    {
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!this.queue.Writer.TryWrite(new(OutgoingKind.Flush, null, null, written, null)))
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

    private static void WriteBody(Utf8JsonWriter writer, in Outgoing message)
    {
        writer.WriteStartObject();
        writer.WriteString(JsonRpcName, VersionValue);
        if (message.Kind != OutgoingKind.Notification)
        {
            writer.WritePropertyName(IdName);
            if (message.Id is { } id)
            {
                id.WriteTo(writer);
            }
            else
            {
                writer.WriteNullValue();
            }
        }

        switch (message.Kind)
        {
            case OutgoingKind.Result:
                writer.WritePropertyName(ResultName);
                WriteValue(writer, message);
                break;
            case OutgoingKind.Error:
                writer.WriteStartObject(ErrorName);
                writer.WriteNumber(CodeName, message.Code);
                writer.WriteString(MessageName, message.Text);
                writer.WriteEndObject();
                break;
            default:
                writer.WriteString(MethodName, message.Text);
                writer.WritePropertyName(ParamsName);
                WriteValue(writer, message);
                break;
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, in Outgoing message)
    {
        if (message.Value is null || message.TypeInfo is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            JsonSerializer.Serialize(writer, message.Value, message.TypeInfo);
        }
    }

    // Serializes one message after room for its header, then writes the header just before the body.
    // A message that cannot be serialized is dropped, so one bad value never stops the output.
    private bool TryFrame(Utf8JsonWriter writer, in Outgoing message, out ReadOnlyMemory<byte> bytes)
    {
        bytes = default;
        this.frame.ResetWrittenCount();
        this.frame.GetSpan(HeaderSpace);
        this.frame.Advance(HeaderSpace);
        writer.Reset(this.frame);
        try
        {
            WriteBody(writer, message);
            writer.Flush();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or JsonException)
        {
            return false;
        }

        var written = MemoryMarshal.AsMemory(this.frame.WrittenMemory);
        Span<byte> header = stackalloc byte[HeaderSpace];
        "Content-Length: "u8.CopyTo(header);
        Utf8Formatter.TryFormat(written.Length - HeaderSpace, header[16..], out var digits);
        "\r\n\r\n"u8.CopyTo(header[(16 + digits)..]);
        var start = HeaderSpace - 20 - digits;
        header[..(20 + digits)].CopyTo(written.Span[start..]);
        bytes = written[start..];
        return true;
    }

    private async Task PumpAsync()
    {
        using var writer = new Utf8JsonWriter(this.frame);
        var reader = this.queue.Reader;
        try
        {
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                var unflushed = false;
                while (reader.TryRead(out var message))
                {
                    if (message.Kind == OutgoingKind.Flush)
                    {
                        if (unflushed)
                        {
                            await this.output.FlushAsync().ConfigureAwait(false);
                            unflushed = false;
                        }

                        ((TaskCompletionSource)message.Value!).TrySetResult();
                    }
                    else if (this.TryFrame(writer, message, out var bytes))
                    {
                        await this.output.WriteAsync(bytes).ConfigureAwait(false);
                        unflushed = true;
                    }
                }

                if (unflushed)
                {
                    await this.output.FlushAsync().ConfigureAwait(false);
                }
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
            while (reader.TryRead(out var message))
            {
                if (message.Kind == OutgoingKind.Flush)
                {
                    ((TaskCompletionSource)message.Value!).TrySetResult(); // Messages that can no longer be sent are released.
                }
            }
        }
    }

    // One queued message: the text is the method of a request or notification, or the message of an error.
    private readonly record struct Outgoing(OutgoingKind Kind, RequestId? Id, string? Text, object? Value, JsonTypeInfo? TypeInfo, int Code = 0);
}
