// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json.Serialization;

namespace Kimi.Lsp;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(InitializeParams))]
[JsonSerializable(typeof(DidOpenTextDocumentParams))]
[JsonSerializable(typeof(DidChangeTextDocumentParams))]
[JsonSerializable(typeof(DidCloseTextDocumentParams))]
[JsonSerializable(typeof(DidChangeWatchedFilesParams))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(JsonRpcError))]
[JsonSerializable(typeof(JsonRpcNotification<PublishDiagnosticsParams>))]
[JsonSerializable(typeof(JsonRpcNotification<LogMessageParams>))]
[JsonSerializable(typeof(JsonRpcRequest<RegistrationParams>))]
internal partial class LspJsonContext : JsonSerializerContext
{
}
