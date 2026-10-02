// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kimi.Diagnostics;

#pragma warning disable SA1402 // The wire model is one vocabulary.

namespace Kimi.Lsp;

// The wire model never carries compiler objects; it mirrors the LSP 3.17 members the server reads or writes.

public sealed class InitializeParams
{
    public ClientCapabilities? Capabilities { get; set; }

    public JsonElement? InitializationOptions { get; set; }
}

public sealed class ClientCapabilities
{
    public WorkspaceClientCapabilities? Workspace { get; set; }

    public TextDocumentClientCapabilities? TextDocument { get; set; }
}

public sealed class TextDocumentClientCapabilities
{
    public PublishDiagnosticsClientCapabilities? PublishDiagnostics { get; set; }
}

public sealed class PublishDiagnosticsClientCapabilities
{
    public bool? RelatedInformation { get; set; }
}

public sealed class WorkspaceClientCapabilities
{
    public DynamicRegistrationCapability? DidChangeWatchedFiles { get; set; }
}

public sealed class DynamicRegistrationCapability
{
    public bool? DynamicRegistration { get; set; }
}

public sealed class InitializeResult
{
    public ServerCapabilities Capabilities { get; set; } = new();

    public ServerInfo ServerInfo { get; set; } = new();
}

public sealed class ServerCapabilities
{
    public string PositionEncoding { get; set; } = "utf-16";

    public TextDocumentSyncOptions TextDocumentSync { get; set; } = new();
}

public sealed class TextDocumentSyncOptions
{
    public bool OpenClose { get; set; }

    public int Change { get; set; }
}

public sealed class ServerInfo
{
    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;
}

public sealed class DidOpenTextDocumentParams
{
    public TextDocumentItem TextDocument { get; set; } = new();
}

public sealed class DidChangeTextDocumentParams
{
    public VersionedTextDocumentIdentifier TextDocument { get; set; } = new();

    public List<TextDocumentContentChangeEvent> ContentChanges { get; set; } = new();
}

public sealed class DidCloseTextDocumentParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
}

public sealed class TextDocumentItem
{
    public string Uri { get; set; } = string.Empty;

    public string LanguageId { get; set; } = string.Empty;

    public int Version { get; set; }

    [JsonConverter(typeof(DocumentTextConverter))]
    public string? Text { get; set; }
}

public class TextDocumentIdentifier
{
    public string Uri { get; set; } = string.Empty;
}

public sealed class VersionedTextDocumentIdentifier : TextDocumentIdentifier
{
    public int Version { get; set; }
}

public sealed class TextDocumentContentChangeEvent
{
    public SourceRange? Range { get; set; }

    public int? RangeLength { get; set; }

    [JsonConverter(typeof(DocumentTextConverter))]
    public string? Text { get; set; }
}

public sealed class DidChangeWatchedFilesParams
{
    public List<FileEvent> Changes { get; set; } = new();
}

public sealed class FileEvent
{
    public string Uri { get; set; } = string.Empty;

    public int Type { get; set; }
}

public sealed class RegistrationParams
{
    public Registration[] Registrations { get; set; } = [];
}

public sealed class Registration
{
    public string Id { get; set; } = string.Empty;

    public string Method { get; set; } = string.Empty;

    public DidChangeWatchedFilesRegistrationOptions? RegisterOptions { get; set; }
}

public sealed class DidChangeWatchedFilesRegistrationOptions
{
    public FileSystemWatcher[] Watchers { get; set; } = [];
}

public sealed class FileSystemWatcher
{
    public string GlobPattern { get; set; } = string.Empty;
}

public sealed class PublishDiagnosticsParams
{
    public string Uri { get; set; } = string.Empty;

    public int? Version { get; set; }

    public LspDiagnostic[] Diagnostics { get; set; } = [];
}

/// <summary>One published diagnostic; <c>severity</c> uses the LSP numbering (1 error to 4 hint), which matches <see cref="DiagnosticSeverity"/>.</summary>
/// <param name="Range">The range.</param>
/// <param name="Severity">The severity.</param>
/// <param name="Code">The code.</param>
/// <param name="Source">The source, <c>kimigayo</c>.</param>
/// <param name="Message">The message with its label, Note, Advice and the text of unsent related locations.</param>
/// <param name="RelatedInformation">The related locations sent as locations, when the client declares support.</param>
public sealed record LspDiagnostic(SourceRange Range, int Severity, string Code, string Source, string Message, LspRelatedInformation[]? RelatedInformation = null)
{
    /// <inheritdoc/>
    public bool Equals(LspDiagnostic? other)
        => other is not null && this.Range.Equals(other.Range) && this.Severity == other.Severity && this.Code == other.Code && this.Source == other.Source &&
            this.Message == other.Message && (this.RelatedInformation ?? []).AsSpan().SequenceEqual(other.RelatedInformation ?? []);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(this.Range, this.Severity, this.Code, this.Message, this.RelatedInformation?.Length ?? 0);
}

/// <summary>A location related to a published diagnostic.</summary>
/// <param name="Location">The location.</param>
/// <param name="Message">The alternative text of the location.</param>
public sealed record LspRelatedInformation(LspLocation Location, string Message);

/// <summary>A range in a document.</summary>
/// <param name="Uri">The document URI.</param>
/// <param name="Range">The range.</param>
public sealed record LspLocation(string Uri, SourceRange Range);

public sealed class LogMessageParams
{
    public int Type { get; set; }

    public string Message { get; set; } = string.Empty;
}

/// <summary>The methods the server handles or sends.</summary>
internal static class LspMethods
{
    public const string Initialize = "initialize";
    public const string Initialized = "initialized";
    public const string Shutdown = "shutdown";
    public const string Exit = "exit";
    public const string DidOpen = "textDocument/didOpen";
    public const string DidChange = "textDocument/didChange";
    public const string DidClose = "textDocument/didClose";
    public const string DidChangeWatchedFiles = "workspace/didChangeWatchedFiles";
    public const string PublishDiagnostics = "textDocument/publishDiagnostics";
    public const string LogMessage = "window/logMessage";
    public const string RegisterCapability = "client/registerCapability";
}

/// <summary>A JSON-RPC request ID, which LSP limits to an integer or a string.</summary>
/// <param name="Number">The integer, when <paramref name="Text"/> is null.</param>
/// <param name="Text">The string, or null for an integer.</param>
internal readonly record struct RequestId(int Number, string? Text)
{
    /// <summary>Writes the ID as it was received.</summary>
    /// <param name="writer">The writer.</param>
    public void WriteTo(Utf8JsonWriter writer)
    {
        if (this.Text is { } text)
        {
            writer.WriteStringValue(text);
        }
        else
        {
            writer.WriteNumberValue(this.Number);
        }
    }
}

/// <summary>One incoming JSON-RPC message, whose parameters of a known method the receive loop has already read.</summary>
internal sealed class LspMessage
{
    /// <summary>Gets the request ID, or null for a notification or a response without one.</summary>
    public RequestId? Id { get; init; }

    /// <summary>Gets the method, or null for a response. A known method is the constant of <see cref="LspMethods"/>.</summary>
    public string? Method { get; init; }

    /// <summary>Gets the parameters of a known method, or null when they are absent.</summary>
    public object? Params { get; init; }

    /// <summary>Gets why the parameters of a known method could not be read.</summary>
    public string? ParamsError { get; init; }

    /// <summary>Gets the <c>error</c> member of a response as JSON text.</summary>
    public string? Error { get; init; }
}

/// <summary>
/// Reads document text, which may hold a lone surrogate: JSON escapes it, and the tokenizer reports it as
/// <c>InvalidSourceEncoding_Kd</c> (SPEC 23.3.4), so it must not make the whole change unreadable.
/// </summary>
internal sealed class DocumentTextConverter : JsonConverter<string>
{
    /// <inheritdoc/>
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Document text must be a string.");
        }

        try
        {
            return reader.GetString()!;
        }
        catch (InvalidOperationException)
        {
            return Unescape(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan);
        }
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);

    // The reader has validated every escape, so only the pairing of escaped surrogates is left unchecked here.
    // An escape is never shorter than the character it denotes, so the byte length bounds the text.
    private static string Unescape(ReadOnlySpan<byte> escaped)
    {
        var buffer = ArrayPool<char>.Shared.Rent(escaped.Length);
        try
        {
            var written = 0;
            while (true)
            {
                var backslash = escaped.IndexOf((byte)'\\');
                written += Encoding.UTF8.GetChars(backslash < 0 ? escaped : escaped[..backslash], buffer.AsSpan(written));
                if (backslash < 0)
                {
                    return new string(buffer, 0, written);
                }

                var kind = escaped[backslash + 1];
                if (kind == (byte)'u')
                {
                    Utf8Parser.TryParse(escaped.Slice(backslash + 2, 4), out ushort code, out _, 'X');
                    buffer[written++] = (char)code;
                    escaped = escaped[(backslash + 6)..];
                    continue;
                }

                buffer[written++] = kind switch
                {
                    (byte)'b' => '\b',
                    (byte)'f' => '\f',
                    (byte)'n' => '\n',
                    (byte)'r' => '\r',
                    (byte)'t' => '\t',
                    _ => (char)kind,
                };
                escaped = escaped[(backslash + 2)..];
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }
}
