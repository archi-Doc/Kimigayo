// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the link manifest's fixed fields and its readers (schema 6, whose external declarations each name their providing library
// (NamedProvider) and list the runtime's imports too (LinkAmbiguity); IMPL 20.3.2 leaves the format implementation-defined; R9-B1): the
// fixed fields, each target row's head (the profile from the row's columns, then its backend package's identity on a row that reserves
// the backend supply, ToolchainIdentity.Packages; the Tatara plan's B7b7, B9f), built once per row of the table's array as one table that publication writes for the compilation's row (WriteFixed) and the
// native build checks in the same order for the row the manifest's target names, which it returns (CheckFixed); the libraries entries of the compiler's supplies (IMPL 20.4's reserved names,
// ToolchainIdentity.Supplies), each written from its row (WriteCompilerSupply) and checked against it (CheckCompilerSupply); and the
// readers (Field, Text, OptionalText, NullableText, Items), which turn a field that is missing, of another JSON kind or, for an Int32 row, another
// number into InvalidDataException at the read (form J, the manifest's share of PLAN G983). The variable fields (optimization, the input,
// the output section, the libraries array and its programs' entries) are written by EmissionArtifacts, which orders and fills the
// libraries array, and read, through these readers, by NativeToolchain.Build. A removed member (Removed: the toolchain, Tatara B6b5) is
// written by no publication and rejected by CheckFixed with its own text, whatever its content.
// Input: a Utf8JsonWriter (publication) or a manifest element (the native build, a library entry), each field named by its path
// ("section.name" in a section or an array's items).
// Output: the fixed fields' and the compiler's supplies' JSON; for a read, the field's value or InvalidDataException naming the field's
// path, one text for a missing field or another kind, one for another fixed value and one for a compiler's supply's entry that is not
// this compiler's; a removed member's row's text.
// Invariants: every manifest read goes through these readers, so no JsonElement accessor exception (KeyNotFound, InvalidOperation,
// Format) leaves a manifest read; a member's lookup and an Int32's read are ArtifactFiles' (Member, Number), the toolchain files' one
// rule; the fixed fields are written and checked by the one table, a compiler's supply's entry by its row. Must not: read the build
// record (its reader compares ArtifactFiles' total reads with what it expects, PLAN G983), or decide a variable field's value.

using System.Text.Json;
using Kimi.Compiler;

namespace Kimi.Build;

/// <summary>The link manifest's fixed fields, the compiler's supplies' entries and the readers (R9-B1).</summary>
internal static class LinkManifest
{
    // Each row's head, built once, in TargetProfile.AllRows' order (a staged row's pair is published and built): the fixed fields in
    // writing and checking order, by path, each a string, an Int32 or a string list; the common rows from the row's columns, then its
    // backend package's identity rows.
    private static readonly (string Path, object Value)[][] Heads = [.. TargetProfile.AllRows.Select(static row => Head(row))];

    // The root members this compiler no longer accepts, each with its text, checked after the fixed fields. A later removal adds a row.
    private static readonly (string Name, string Text)[] Removed =
    [
        ("toolchain", "The link manifest's 'toolchain.llvmBin' was removed: Kimi carries its toolchain. Re-emit the manifest."),
    ];

    /// <summary>Writes the fixed fields, the row's head, in the table's order.</summary>
    /// <param name="json">The manifest's writer, inside its root object.</param>
    /// <param name="row">The compilation's target row.</param>
    internal static void WriteFixed(Utf8JsonWriter json, TargetProfile row)
    {
        var open = ReadOnlySpan<char>.Empty;
        foreach (var (path, value) in Heads[Array.IndexOf(TargetProfile.AllRows, row)])
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

    /// <summary>Checks that a manifest's target names a row of the target table and that the manifest states every field of that row's
    /// head as this compiler writes it, in the table's order, and no removed member.</summary>
    /// <param name="root">The manifest's root.</param>
    /// <returns>The manifest's row.</returns>
    /// <exception cref="InvalidDataException">The target that is missing, of another kind or no row's; else the first field that is
    /// missing, of another kind or another value; else the first removed member.</exception>
    internal static TargetProfile CheckFixed(JsonElement root)
    {
        var index = Array.IndexOf(TargetProfile.AllRows, TargetProfile.Find(Text(root, "target")));
        if (index < 0)
        {
            throw new InvalidDataException("The link manifest's 'target' is not this compiler's. Re-emit the manifest.");
        }

        foreach (var (path, value) in Heads[index])
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

        foreach (var (name, text) in Removed)
        {
            if (ArtifactFiles.Member(root, name).ValueKind != JsonValueKind.Undefined)
            {
                throw new InvalidDataException(text);
            }
        }

        return TargetProfile.AllRows[index];
    }

    /// <summary>Writes a compiler's supply's libraries entry: its name, its kind and its row's identifying fields, in that order.</summary>
    /// <param name="json">The manifest's writer, inside its libraries array.</param>
    /// <param name="supply">The supply's row.</param>
    internal static void WriteCompilerSupply(Utf8JsonWriter json, CompilerSupply supply)
    {
        json.WriteStartObject();
        json.WriteString("name", supply.Supply.Name);
        json.WriteString("kind", supply.Supply.Kind);
        foreach (var (name, value) in supply.Entry)
        {
            json.WriteString(name, value);
        }

        json.WriteEndObject();
    }

    /// <summary>Checks that a libraries entry naming a compiler's supply is the one this compiler writes: its kind and every identifying
    /// field equal, no input, and no sha256 on a supply of several files; any other member is ignored, a sha256 being an assertion the
    /// native build checks against the supply's one file.</summary>
    /// <param name="entry">The libraries entry.</param>
    /// <param name="supply">The row of the supply the entry names.</param>
    /// <exception cref="InvalidDataException">The entry is not this compiler's.</exception>
    internal static void CheckCompilerSupply(JsonElement entry, CompilerSupply supply)
    {
        if (!ArtifactFiles.States(entry, ("kind", supply.Supply.Kind)) || !ArtifactFiles.States(entry, supply.Entry) ||
            ArtifactFiles.Member(entry, "input").ValueKind != JsonValueKind.Undefined ||
            (supply.Files.Length != 1 && ArtifactFiles.Member(entry, "sha256").ValueKind != JsonValueKind.Undefined))
        {
            throw new InvalidDataException($"The link manifest's 'libraries' entry '{supply.Supply.Name}' is not this compiler's. Re-emit the manifest.");
        }
    }

    /// <summary>Reads a field of the expected JSON kind.</summary>
    /// <param name="element">The object holding the field.</param>
    /// <param name="path">The field's path, whose last name is the field's.</param>
    /// <param name="kind">The expected kind.</param>
    /// <returns>The field.</returns>
    /// <exception cref="InvalidDataException">The element is not an object, or the field is missing or of another kind.</exception>
    internal static JsonElement Field(JsonElement element, ReadOnlySpan<char> path, JsonValueKind kind)
    {
        var field = ArtifactFiles.Member(element, Leaf(path));
        if (field.ValueKind != kind)
        {
            throw new InvalidDataException($"The link manifest's '{path}' is missing or not a JSON {kind}. Re-emit the manifest.");
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

    /// <summary>Reads a string field whose value may be JSON null (an Application's subsystem on a row without one).</summary>
    /// <param name="element">The object holding the field.</param>
    /// <param name="path">The field's path.</param>
    /// <returns>The string, or null when the field is JSON null.</returns>
    internal static string? NullableText(JsonElement element, string path)
        => ArtifactFiles.Member(element, Leaf(path)).ValueKind == JsonValueKind.Null ? null : Text(element, path);

    /// <summary>Reads an array field's items.</summary>
    /// <param name="element">The object holding the field.</param>
    /// <param name="path">The field's path.</param>
    /// <returns>The items.</returns>
    internal static JsonElement.ArrayEnumerator Items(JsonElement element, string path) => Field(element, path, JsonValueKind.Array).EnumerateArray();

    // A row's head (Heads): the common rows from the row's columns, then its backend package's identity rows when the row reserves the
    // backend supply (windows-x64, none-x64; linux-x64 has no backend archive, C1): the package's id, ABI version and archive hash
    // (ToolchainIdentity.Packages), the compiler's version, and the row's backend supply's name and symbols.
    private static (string Path, object Value)[] Head(TargetProfile row) =>
    [
        ("schemaVersion", 6),
        ("target", row.Target),
        ("codegen.profile", row.Name),
        ("codegen.llvmVersion", ToolchainIdentity.LlvmVersion),
        ("codegen.cpu", row.Cpu),
        ("codegen.features", new[] { row.Features }),
        ("codegen.relocationModel", row.RelocationModel),
        ("codegen.codeModel", row.CodeModel),
        ("codegen.unwindTables", row.UnwindTables),
        .. ToolchainIdentity.Packages.Where(x => x.Row == row).SelectMany(static x => new (string, object)[]
        {
            ("backendSupport.packageId", x.PackageId),
            ("backendSupport.abiVersion", x.AbiVersion),
            ("backendSupport.packageVersion", ToolchainIdentity.BackendVersion),
            ("backendSupport.library", ToolchainIdentity.Backend.Name),
            ("backendSupport.artifactSha256", x.ArtifactSha256),
            ("backendSupport.providedSymbols", x.Row.Supply(ToolchainIdentity.Backend.Name)!.Symbols),
        }),
    ];

    // A path's section, empty at the top level, and its field's name.
    private static ReadOnlySpan<char> Section(ReadOnlySpan<char> path) => path[..Math.Max(path.LastIndexOf('.'), 0)];

    private static ReadOnlySpan<char> Leaf(ReadOnlySpan<char> path) => path[(path.LastIndexOf('.') + 1)..];
}
