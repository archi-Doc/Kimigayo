// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization;
using Kimi.Diagnostics;

#pragma warning disable SA1402 // The wire model is one vocabulary.

namespace Kimi.Lsp;

// The wire model never carries compiler objects; it mirrors the LSP 3.17 members the server reads or writes.

/// <summary>One incoming JSON-RPC message.</summary>
public sealed class LspMessage
{
    public string Jsonrpc { get; set; } = string.Empty;

    public JsonElement? Id { get; set; }

    public string? Method { get; set; }

    public JsonElement? Params { get; set; }

    public JsonElement? Result { get; set; }

    public JsonElement? Error { get; set; }
}

public sealed class JsonRpcError
{
    public int Code { get; set; }

    public string Message { get; set; } = string.Empty;
}

public sealed class JsonRpcNotification<T>
{
    public string Jsonrpc { get; set; } = "2.0";

    public string Method { get; set; } = string.Empty;

    public T? Params { get; set; }
}

public sealed class JsonRpcRequest<T>
{
    public string Jsonrpc { get; set; } = "2.0";

    public int Id { get; set; }

    public string Method { get; set; } = string.Empty;

    public T? Params { get; set; }
}

public sealed class InitializeParams
{
    public ClientCapabilities? Capabilities { get; set; }

    public JsonElement? InitializationOptions { get; set; }
}

public sealed class ClientCapabilities
{
    public WorkspaceClientCapabilities? Workspace { get; set; }
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
public sealed record LspDiagnostic(SourceRange Range, int Severity, string Code, string Source, string Message);

public sealed class LogMessageParams
{
    public int Type { get; set; }

    public string Message { get; set; } = string.Empty;
}

/// <summary>An empty object for notifications and requests without parameters.</summary>
public sealed class EmptyParams
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
