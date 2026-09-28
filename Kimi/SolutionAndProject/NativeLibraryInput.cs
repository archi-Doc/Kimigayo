// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Tinyhand.IO;

namespace Kimi;

/// <summary>A native supply record. Values are data, never shell command fragments.</summary>
/// <remarks>A self-targeted record's Kind, ContractId and Sha256 also declare the requirement they expand into (SPEC 20.8.2.1).</remarks>
[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class NativeLibraryInput
{
    /// <summary>Gets or sets the targeted module's native name; a shorthand entry's key.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the targeted dependency module, or null for the declaring Project itself.</summary>
    public NativePackageTarget? Package { get; set; }

    public string? Kind { get; set; }

    public string Input { get; set; } = string.Empty;

    public string? ContractId { get; set; }

    public string? Sha256 { get; set; }
}

/// <summary>Identifies the dependency module a native supply targets.</summary>
[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class NativePackageTarget
{
    public string PackageId { get; set; } = string.Empty;

    public string PackageVersion { get; set; } = string.Empty;
}

/// <summary>One target's native supplies: self-targeted records keyed by Name, then records targeting dependency modules (SPEC 20.8.2.1).</summary>
/// <remarks>Reads the Name-keyed map shorthand or an array of records. Writes the shorthand when every record is self-targeted, otherwise one array.</remarks>
public sealed class NativeLibrarySupplies : Dictionary<string, NativeLibraryInput>
{
    public NativeLibrarySupplies()
        : base(StringComparer.Ordinal)
    {
    }

    /// <summary>Gets or sets the records that target dependency modules, in source order.</summary>
    public NativeLibraryInput[] Packaged { get; set; } = [];

    internal static void Serialize(ref TinyhandWriter writer, NativeLibrarySupplies? value, TinyhandSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNil();
            return;
        }

        if (value.Packaged.Length == 0)
        {
            writer.WriteMapHeader(value.Count);
            foreach (var (name, record) in value)
            {
                writer.Write(name);
                TinyhandSerializer.Serialize(ref writer, record, options);
            }

            return;
        }

        writer.WriteArrayHeader(value.Count + value.Packaged.Length);
        foreach (var record in value.Values)
        {
            TinyhandSerializer.Serialize(ref writer, record, options);
        }

        foreach (var record in value.Packaged)
        {
            TinyhandSerializer.Serialize(ref writer, record, options);
        }
    }

    internal static NativeLibrarySupplies? Deserialize(ref TinyhandReader reader, TinyhandSerializerOptions options)
    {
        if (reader.TryReadNil())
        {
            return null;
        }

        var supplies = new NativeLibrarySupplies();
        if (reader.NextMessagePackType == MessagePackType.Array)
        {
            List<NativeLibraryInput>? packaged = null;
            var count = reader.ReadArrayHeader();
            for (var i = 0; i < count; i++)
            {
                var record = TinyhandSerializer.Deserialize<NativeLibraryInput>(ref reader, options) ?? throw new TinyhandException("NativeLibraries records must be mappings.");
                if (record.Package is not null)
                {
                    (packaged ??= new()).Add(record);
                }
                else if (record.Name is null || !supplies.TryAdd(record.Name, record))
                {
                    // A second self-targeted record for one name would otherwise replace the first.
                    throw new TinyhandException($"Missing or duplicate self-targeted NativeLibraries Name: {record.Name}");
                }
            }

            supplies.Packaged = packaged?.ToArray() ?? [];
        }
        else
        {
            var count = reader.ReadMapHeader();
            for (var i = 0; i < count; i++)
            {
                var name = reader.ReadString();
                var record = TinyhandSerializer.Deserialize<NativeLibraryInput>(ref reader, options);
                if (name is null || record is null || record.Package is not null || (record.Name is not null && record.Name != name) || !supplies.TryAdd(name, record))
                {
                    throw new TinyhandException($"The NativeLibraries shorthand entry '{name}' must be a unique self-targeted record whose Name, if present, equals its key.");
                }

                record.Name = name;
            }
        }

        return supplies;
    }
}

/// <summary>Per-target native supplies; each target reads either the map shorthand or an array of records (SPEC 20.8.2.1).</summary>
[TinyhandObject(External = true)]
public sealed partial class NativeLibraryMap : Dictionary<string, NativeLibrarySupplies>, ITinyhandSerializable<NativeLibraryMap>, ITinyhandReconstructable<NativeLibraryMap>, ITinyhandCloneable<NativeLibraryMap>
{
    public NativeLibraryMap()
        : base(StringComparer.Ordinal)
    {
    }

    static void ITinyhandSerializable<NativeLibraryMap>.Serialize(ref TinyhandWriter writer, scoped ref NativeLibraryMap? value, TinyhandSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNil();
            return;
        }

        writer.WriteMapHeader(value.Count);
        foreach (var (target, supplies) in value)
        {
            writer.Write(target);
            NativeLibrarySupplies.Serialize(ref writer, supplies, options);
        }
    }

    static void ITinyhandSerializable<NativeLibraryMap>.Deserialize(ref TinyhandReader reader, scoped ref NativeLibraryMap? value, TinyhandSerializerOptions options)
    {
        if (reader.TryReadNil())
        {
            value = null;
            return;
        }

        // The project loader has already rejected repeated targets in the source bytes.
        var map = new NativeLibraryMap();
        var count = reader.ReadMapHeaderOrEmptyArray();
        for (var i = 0; i < count; i++)
        {
            var target = reader.ReadString() ?? throw new TinyhandException("NativeLibraries targets must be strings.");
            map[target] = NativeLibrarySupplies.Deserialize(ref reader, options)!;
        }

        value = map;
    }

    static void ITinyhandReconstructable<NativeLibraryMap>.Reconstruct([System.Diagnostics.CodeAnalysis.NotNull] scoped ref NativeLibraryMap? value, TinyhandSerializerOptions options)
        => value ??= new();

    static NativeLibraryMap? ITinyhandCloneable<NativeLibraryMap>.Clone(scoped ref NativeLibraryMap? value, TinyhandSerializerOptions options)
    {
        if (value is null)
        {
            return null;
        }

        var map = new NativeLibraryMap();
        foreach (var (target, supplies) in value)
        {
            NativeLibrarySupplies? copy = null;
            if (supplies is not null)
            {
                copy = new() { Packaged = new NativeLibraryInput[supplies.Packaged.Length] };
                foreach (var (name, record) in supplies)
                {
                    copy.Add(name, Copy(record));
                }

                for (var i = 0; i < copy.Packaged.Length; i++)
                {
                    copy.Packaged[i] = Copy(supplies.Packaged[i]);
                }
            }

            map.Add(target, copy!);
        }

        return map;
    }

    private static NativeLibraryInput Copy(NativeLibraryInput record)
        => record with { Package = record.Package is null ? null : record.Package with { } };
}

/// <summary>A definition-side native requirement; semantic checking never reads native files.</summary>
[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class NativeRequirement
{
    public string? Kind { get; set; }

    public string? ContractId { get; set; }

    public string? Sha256 { get; set; }
}
