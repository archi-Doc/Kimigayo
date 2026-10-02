// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Kimi.Lsp;

/// <summary>
/// Reads one JSON-RPC body in a single pass (SPEC 23.4.1). The parameters of a known method are deserialized straight
/// from the frame, and a known method name is its <see cref="LspMethods"/> constant, so no JSON is copied or parsed twice.
/// </summary>
internal static class LspMessageReader
{
    private static readonly (byte[] Utf8, string Name)[] Methods = [.. new[]
    {
        LspMethods.DidChange, LspMethods.DidOpen, LspMethods.DidClose, LspMethods.DidChangeWatchedFiles,
        LspMethods.Initialize, LspMethods.Initialized, LspMethods.Shutdown, LspMethods.Exit,
    }.Select(static x => (Encoding.UTF8.GetBytes(x), x))];

    /// <summary>Parses a body.</summary>
    /// <param name="body">The UTF-8 body.</param>
    /// <returns>An <see cref="LspMessage"/>, or an <see cref="InvalidFrame"/> for malformed JSON (-32700) or an invalid request (-32600).</returns>
    public static object Parse(ReadOnlySpan<byte> body)
    {
        var valid = true;
        var version = false;
        RequestId? id = null;
        string? method = null;
        (int Start, int Length) parameters = default, error = default;
        try
        {
            var reader = new Utf8JsonReader(body);
            reader.Read();
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                valid = false;
                reader.Skip();
            }
            else
            {
                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("jsonrpc"u8))
                    {
                        reader.Read();
                        version = reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("2.0"u8);
                    }
                    else if (reader.ValueTextEquals("id"u8))
                    {
                        reader.Read();
                        valid &= TryReadId(ref reader, out id);
                    }
                    else if (reader.ValueTextEquals("method"u8))
                    {
                        reader.Read();
                        valid &= TryReadMethod(ref reader, out method);
                    }
                    else if (reader.ValueTextEquals("params"u8))
                    {
                        reader.Read();
                        parameters = ValueRange(ref reader);
                    }
                    else if (reader.ValueTextEquals("error"u8))
                    {
                        reader.Read();
                        error = ValueRange(ref reader);
                    }
                    else
                    {
                        reader.Read();
                    }

                    reader.Skip(); // A container value that was not read, or nothing.
                }
            }

            while (reader.Read())
            {
                // A second top-level value throws; trailing white space ends the loop.
            }
        }
        catch (JsonException ex)
        {
            return new InvalidFrame(-32700, "Parse error: " + ex.Message);
        }

        if (!valid || !version)
        {
            return new InvalidFrame(-32600, "Invalid request.");
        }

        object? value = null;
        string? paramsError = null;
        if (parameters.Length != 0 && ParamsType(method) is { } typeInfo)
        {
            try
            {
                value = JsonSerializer.Deserialize(body.Slice(parameters.Start, parameters.Length), typeInfo);
            }
            catch (JsonException ex)
            {
                paramsError = ex.Message; // Reported where the method reads its parameters.
            }
        }

        return new LspMessage
        {
            Id = id,
            Method = method,
            Params = value,
            ParamsError = paramsError,
            Error = method is null && error.Length != 0 ? Encoding.UTF8.GetString(body.Slice(error.Start, error.Length)) : null,
        };
    }

    private static JsonTypeInfo? ParamsType(string? method) => method switch
    {
        LspMethods.DidChange => LspJsonContext.Default.DidChangeTextDocumentParams,
        LspMethods.DidOpen => LspJsonContext.Default.DidOpenTextDocumentParams,
        LspMethods.DidClose => LspJsonContext.Default.DidCloseTextDocumentParams,
        LspMethods.DidChangeWatchedFiles => LspJsonContext.Default.DidChangeWatchedFilesParams,
        LspMethods.Initialize => LspJsonContext.Default.InitializeParams,
        LspMethods.CodeAction => LspJsonContext.Default.CodeActionParams,
        _ => null,
    };

    // A null ID is absent; LSP IDs are integers or strings.
    private static bool TryReadId(ref Utf8JsonReader reader, out RequestId? id)
    {
        id = null;
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return true;
            case JsonTokenType.Number when reader.TryGetInt32(out var number):
                id = new(number, null);
                return true;
            case JsonTokenType.String:
                try
                {
                    id = new(0, reader.GetString());
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false; // A lone surrogate could not be echoed.
                }

            default:
                return false;
        }
    }

    // A null method is absent, which makes the message a response.
    private static bool TryReadMethod(ref Utf8JsonReader reader, out string? method)
    {
        method = null;
        if (reader.TokenType == JsonTokenType.Null)
        {
            return true;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            return false;
        }

        foreach (var (utf8, name) in Methods)
        {
            if (reader.ValueTextEquals(utf8))
            {
                method = name;
                return true;
            }
        }

        try
        {
            method = reader.GetString();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // Skips the current value and returns its byte range, or an empty range for null.
    private static (int Start, int Length) ValueRange(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        var start = (int)reader.TokenStartIndex;
        reader.Skip();
        return (start, (int)reader.BytesConsumed - start);
    }
}
