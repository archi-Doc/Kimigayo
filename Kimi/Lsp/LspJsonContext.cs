// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json.Serialization;
using Kimi.Diagnostics;

namespace Kimi.Lsp;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LspMessage))]
[JsonSerializable(typeof(DidOpenTextDocumentParams))]
[JsonSerializable(typeof(DidChangeTextDocumentParams))]
[JsonSerializable(typeof(DidCloseTextDocumentParams))]
[JsonSerializable(typeof(JsonRpcResponse))]
[JsonSerializable(typeof(JsonRpcNotification))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(PublishDiagnosticsParams))]
internal partial class LspJsonContext : JsonSerializerContext
{
}
