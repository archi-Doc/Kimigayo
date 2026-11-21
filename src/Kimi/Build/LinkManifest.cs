// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the link manifest's fixed fields and its readers (schema 4; IMPL 20.3.2 leaves the format implementation-defined; R9-B1): the
// fixed fields, the profile and backend supply identity this compiler states, as one row table that publication writes (WriteFixed) and
// the native build checks (CheckFixed) in the same order; and the readers (Field, Text, OptionalText, Items, Flag), which turn a field
// that is missing, of another JSON kind or, for an Int32 row, another number into InvalidDataException at the read (form J, the
// manifest's share of PLAN G983). The variable fields (optimization, the output section, the libraries, the toolchain) are written by
// EmissionArtifacts; all but the toolchain are read, through these readers, by NativeToolchain.Build, Kernel32Imports and
// ToolchainResolver. The toolchain's llvmBin is accepted and not read (Tatara design A4 §2.1; removed in B6).
// Input: a Utf8JsonWriter (publication) or a manifest element (the native build, kernel32's and the backend's library entries), each
// field named by its path ("section.name" in a section or an array's items).
// Output: the fixed fields' JSON; for a read, the field's value or InvalidDataException naming the field's path, one text for a missing
// field or another kind and one for another fixed value.
// Invariants: every manifest read goes through these readers, so no JsonElement accessor exception (KeyNotFound, InvalidOperation,
// Format) leaves a manifest read; a member's lookup and an Int32's read are ArtifactFiles' (Member, Number), the toolchain files' one
// rule; the fixed fields are written and checked by the one table. Must not: read the build record, the installation or the kernel32
// records (their readers compare ArtifactFiles' total reads with what they expect, PLAN G983), or decide a variable field's value.

using System.Text.Json;
using Kimi.Compiler;

namespace Kimi.Build;

/// <summary>The link manifest's fixed fields and readers (R9-B1).</summary>
internal static class LinkManifest
{
    // The fixed fields in writing and checking order, by path, each a string, an Int32 or a string list.
    private static readonly (string Path, object Value)[] Rows =
    [
        ("schemaVersion", 4),
        ("target", WindowsProfile.Target),
        ("codegen.profile", WindowsProfile.Name),
        ("codegen.llvmVersion", ToolchainIdentity.LlvmVersion),
        ("codegen.cpu", WindowsProfile.Cpu),
        ("codegen.features", new[] { WindowsProfile.Features }),
        ("codegen.relocationModel", WindowsProfile.RelocationModel),
        ("codegen.codeModel", WindowsProfile.CodeModel),
        ("codegen.unwindTables", WindowsProfile.UnwindTables),
        ("backendSupport.packageId", ToolchainIdentity.BackendPackageId),
        ("backendSupport.abiVersion", ToolchainIdentity.BackendAbiVersion),
        ("backendSupport.packageVersion", ToolchainIdentity.BackendVersion),
        ("backendSupport.library", WindowsProfile.BackendLibrary),
        ("backendSupport.artifactSha256", ToolchainIdentity.BackendSha256),
        ("backendSupport.providedSymbols", WindowsProfile.ProvidedSymbols),
    ];

    /// <summary>Writes the fixed fields, the manifest's head, in the table's order.</summary>
    /// <param name="json">The manifest's writer, inside its root object.</param>
    internal static void WriteFixed(Utf8JsonWriter json)
    {
        var open = ReadOnlySpan<char>.Empty;
        foreach (var (path, value) in Rows)
        {
            var section = Section(path);
            if (!section.SequenceEqual(open))
            {
                if (!open.IsEmpty)
                {
                    json.WriteEndObject();
                }

                if (!section.IsEmpty)
                {
                    json.WriteStartObject(section);
                }

                open = section;
            }

            json.WritePropertyName(Leaf(path));
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

        if (!open.IsEmpty)
        {
            json.WriteEndObject();
        }
    }

    /// <summary>Checks that a manifest states every fixed field as this compiler writes it, in the table's order.</summary>
    /// <param name="root">The manifest's root.</param>
    /// <exception cref="InvalidDataException">The first field that is missing, of another kind or another value.</exception>
    internal static void CheckFixed(JsonElement root)
    {
        foreach (var (path, value) in Rows)
        {
            var kind = value switch
            {
                string => JsonValueKind.String,
                int => JsonValueKind.Number,
                _ => JsonValueKind.Array,
            };
            var section = Section(path);
            var field = Field(section.IsEmpty ? root : Field(root, section, JsonValueKind.Object), path, kind);
            var states = value switch
            {
                string text => field.ValueEquals(text),
                int number => ArtifactFiles.Number(field) == number,
                _ => ArtifactFiles.SequenceEqual(field, (string[])value),
            };
            if (!states)
            {
                throw new InvalidDataException($"The link manifest's '{path}' is not this compiler's. Re-emit the manifest.");
            }
        }
    }

    /// <summary>Reads a field of the expected JSON kind; a boolean, either of JSON's two kinds, is read as <see cref="JsonValueKind.True"/>
    /// (<see cref="Flag"/>).</summary>
    /// <param name="element">The object holding the field.</param>
    /// <param name="path">The field's path, whose last name is the field's.</param>
    /// <param name="kind">The expected kind.</param>
    /// <returns>The field.</returns>
    /// <exception cref="InvalidDataException">The element is not an object, or the field is missing or of another kind.</exception>
    internal static JsonElement Field(JsonElement element, ReadOnlySpan<char> path, JsonValueKind kind)
    {
        var field = ArtifactFiles.Member(element, Leaf(path));
        if ((field.ValueKind == JsonValueKind.False ? JsonValueKind.True : field.ValueKind) != kind)
        {
            var expected = kind == JsonValueKind.True ? "Boolean" : kind.ToString();
            throw new InvalidDataException($"The link manifest's '{path}' is missing or not a JSON {expected}. Re-emit the manifest.");
        }

        return field;
    }

    /// <summary>Reads a string field.</summary>
    /// <param name="element">The object holding the field.</param>
    /// <param name="path">The field's path.</param>
    /// <returns>The string.</returns>
    internal static string Text(JsonElement element, string path) => Field(element, path, JsonValueKind.String).GetString()!;

    /// <summary>Reads a string field that may be absent.</summary>
    /// <param name="element">The object holding the field.</param>
    /// <param name="path">The field's path.</param>
    /// <returns>The string, or null when the field is absent.</returns>
    internal static string? OptionalText(JsonElement element, string path)
        => element.ValueKind == JsonValueKind.Object && ArtifactFiles.Member(element, Leaf(path)).ValueKind == JsonValueKind.Undefined ? null : Text(element, path);

    /// <summary>Reads an array field's items.</summary>
    /// <param name="element">The object holding the field.</param>
    /// <param name="path">The field's path.</param>
    /// <returns>The items.</returns>
    internal static JsonElement.ArrayEnumerator Items(JsonElement element, string path) => Field(element, path, JsonValueKind.Array).EnumerateArray();

    /// <summary>Reads a boolean field.</summary>
    /// <param name="element">The object holding the field.</param>
    /// <param name="path">The field's path.</param>
    /// <returns>The boolean.</returns>
    internal static bool Flag(JsonElement element, string path) => Field(element, path, JsonValueKind.True).GetBoolean();

    // A path's section, empty at the top level, and its field's name.
    private static ReadOnlySpan<char> Section(ReadOnlySpan<char> path) => path[..Math.Max(path.LastIndexOf('.'), 0)];

    private static ReadOnlySpan<char> Leaf(ReadOnlySpan<char> path) => path[(path.LastIndexOf('.') + 1)..];
}
