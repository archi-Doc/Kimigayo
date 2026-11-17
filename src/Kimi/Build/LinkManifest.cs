// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the link manifest's format (schema 4; IMPL 20.3.2 leaves it implementation-defined; R9-B1): its fixed fields, the profile and
// backend supply identity this compiler states, as one row table that publication writes (WriteFixed) and the native build checks
// (CheckFixed) in the same order; and the manifest's readers (Field, Text, OptionalText, Items, Flag), which turn a field that is
// missing, of another JSON kind or, for an Int32 row, another number into InvalidDataException at the read (form J, the manifest's
// share of PLAN G983). Input: a Utf8JsonWriter (publication) or a manifest element (the native build, kernel32's and the backend's
// library entries). Output: the fixed fields' JSON; for a read, the field's value or InvalidDataException naming the field.
// Invariants: every manifest read goes through these readers, so no JsonElement accessor exception (KeyNotFound, InvalidOperation,
// Format) leaves a manifest read; the fixed head is written and checked by the one table. Must not: read the build record, the
// installation or the kernel32 records (their readers are PLAN G983's), or decide a variable field's value.

using System.Text.Json;
using Kimi.Compiler;

namespace Kimi.Build;

/// <summary>The link manifest's fixed fields and readers (R9-B1).</summary>
internal static class LinkManifest
{
    // The fixed fields in writing and checking order, each a string, an Int32 or a string list, at the top level (null) or in a section.
    private static readonly (string? Section, string Name, object Value)[] Rows =
    [
        (null, "schemaVersion", 4),
        (null, "target", WindowsProfile.Target),
        ("codegen", "profile", WindowsProfile.Name),
        ("codegen", "llvmVersion", ToolchainIdentity.LlvmVersion),
        ("codegen", "cpu", WindowsProfile.Cpu),
        ("codegen", "features", new[] { WindowsProfile.Features }),
        ("codegen", "relocationModel", WindowsProfile.RelocationModel),
        ("codegen", "codeModel", WindowsProfile.CodeModel),
        ("codegen", "unwindTables", WindowsProfile.UnwindTables),
        ("backendSupport", "packageId", ToolchainIdentity.BackendPackageId),
        ("backendSupport", "abiVersion", ToolchainIdentity.BackendAbiVersion),
        ("backendSupport", "packageVersion", ToolchainIdentity.BackendVersion),
        ("backendSupport", "library", WindowsProfile.BackendLibrary),
        ("backendSupport", "artifactSha256", ToolchainIdentity.BackendSha256),
        ("backendSupport", "providedSymbols", WindowsProfile.ProvidedSymbols),
        (null, "providedRuntimeSymbols", new[] { WindowsProfile.FloatMarker }),
        (null, "expectedUndefinedSymbols", Array.Empty<string>()),
    ];

    /// <summary>Writes the fixed fields, the manifest's head, in the table's order.</summary>
    /// <param name="json">The manifest's writer, inside its root object.</param>
    internal static void WriteFixed(Utf8JsonWriter json)
    {
        string? open = null;
        foreach (var (section, name, value) in Rows)
        {
            if (section != open)
            {
                if (open is not null)
                {
                    json.WriteEndObject();
                }

                if (section is not null)
                {
                    json.WriteStartObject(section);
                }

                open = section;
            }

            json.WritePropertyName(name);
            if (value is string text)
            {
                json.WriteStringValue(text);
            }
            else if (value is int number)
            {
                json.WriteNumberValue(number);
            }
            else
            {
                json.WriteStartArray();
                foreach (var item in (string[])value)
                {
                    json.WriteStringValue(item);
                }

                json.WriteEndArray();
            }
        }

        if (open is not null)
        {
            json.WriteEndObject();
        }
    }

    /// <summary>Checks that a manifest states every fixed field as this compiler writes it, in the table's order.</summary>
    /// <param name="root">The manifest's root.</param>
    /// <exception cref="InvalidDataException">The first field that is missing, of another kind or another value.</exception>
    internal static void CheckFixed(JsonElement root)
    {
        foreach (var (section, name, value) in Rows)
        {
            var kind = value switch
            {
                string => JsonValueKind.String,
                int => JsonValueKind.Number,
                _ => JsonValueKind.Array,
            };
            var field = Field(section is null ? root : Field(root, section, JsonValueKind.Object), name, kind);
            var states = value switch
            {
                string text => field.ValueEquals(text),
                int number => field.TryGetInt32(out var actual) && actual == number,
                _ => ArtifactFiles.SequenceEqual(field, (string[])value),
            };
            if (!states)
            {
                throw new InvalidDataException($"The link manifest's '{(section is null ? name : section + "." + name)}' is not this compiler's. Re-emit the manifest.");
            }
        }
    }

    /// <summary>Reads a field of the expected JSON kind.</summary>
    /// <param name="element">The object holding the field.</param>
    /// <param name="name">The field's name.</param>
    /// <param name="kind">The expected kind.</param>
    /// <returns>The field.</returns>
    /// <exception cref="InvalidDataException">The element is not an object, or the field is missing or of another kind.</exception>
    internal static JsonElement Field(JsonElement element, string name, JsonValueKind kind)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var field) || field.ValueKind != kind)
        {
            throw new InvalidDataException($"The link manifest's '{name}' is missing or not a JSON {kind}. Re-emit the manifest.");
        }

        return field;
    }

    /// <summary>Reads a string field.</summary>
    /// <param name="element">The object holding the field.</param>
    /// <param name="name">The field's name.</param>
    /// <returns>The string.</returns>
    internal static string Text(JsonElement element, string name) => Field(element, name, JsonValueKind.String).GetString()!;

    /// <summary>Reads a string field that may be absent.</summary>
    /// <param name="element">The object holding the field.</param>
    /// <param name="name">The field's name.</param>
    /// <returns>The string, or null when the field is absent.</returns>
    internal static string? OptionalText(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && !element.TryGetProperty(name, out _) ? null : Text(element, name);

    /// <summary>Reads an array field's items.</summary>
    /// <param name="element">The object holding the field.</param>
    /// <param name="name">The field's name.</param>
    /// <returns>The items.</returns>
    internal static JsonElement.ArrayEnumerator Items(JsonElement element, string name) => Field(element, name, JsonValueKind.Array).EnumerateArray();

    /// <summary>Reads a boolean field.</summary>
    /// <param name="element">The object holding the field.</param>
    /// <param name="name">The field's name.</param>
    /// <returns>The boolean.</returns>
    internal static bool Flag(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var field) && field.ValueKind is JsonValueKind.True or JsonValueKind.False ?
            field.GetBoolean() :
            throw new InvalidDataException($"The link manifest's '{name}' is missing or not a JSON boolean. Re-emit the manifest.");
}
