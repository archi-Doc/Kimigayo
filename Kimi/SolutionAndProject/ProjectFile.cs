// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand.IO;

namespace Kimi;

/// <summary>Defines the common settings serialized in a <c>.kimiproj</c> file.</summary>
[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class ProjectFile
{
    /// <summary>Gets or sets whether startup is selected for an Application or excluded for a Library.</summary>
    [IgnoreMember]
    public OutputKind OutputKind { get; set; }

    /// <summary>Gets or sets the LLVM-style target triples built by the project.</summary>
    public string[] Targets { get; set; } = [];

    /// <summary>Gets or sets the external Kotonoha library references.</summary>
    public KotonohaIdentifier[] KotonohaArray { get; set; } = [];

    /// <summary>Gets or sets the project-wide alias imports.</summary>
    public string[] Alias { get; set; } = [];

    /// <summary>Gets or sets the exact language version, or null to inherit the solution/compiler default.</summary>
    public string? LangVersion { get; set; }

    /// <summary>Gets or sets the project-relative .ll destination; null selects the profile default.</summary>
    public string? OutputPath { get; set; }

    /// <summary>Gets or sets O0 or O2 for native code generation.</summary>
    public string Optimization { get; set; } = "O2";

    /// <summary>Gets or sets a legacy project-relative LLVM tool override; null uses the compiler toolchain.</summary>
    public string? LlvmBin { get; set; }

    /// <summary>Gets or sets target-specific library overrides. kernel32 is generated; kimi_backend defaults to the compiler toolchain.</summary>
    public Dictionary<string, Dictionary<string, NativeLibraryInput>> NativeLibraries { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets explicitly typed compile-time scalar settings.</summary>
    /// <remarks>Preserves case-sensitive names; the file loader rejects duplicate names before dictionary deserialization.</remarks>
    public Dictionary<string, CompileTimeSetting> CompileTimeSettings { get; set; } = new(StringComparer.Ordinal);

    // Convert once using Tinyhand's text grammar. Inspect the original map entries before
    // its dictionary formatter can overwrite duplicates, then deserialize the same bytes.
    internal static ProjectFile? Load(ReadOnlySpan<byte> utf8)
    {
        var writer = TinyhandWriter.CreateFromThreadStaticBuffer();
        try
        {
            TinyhandTreeConverter.FromUtf8ToBinary(utf8, ref writer, true);
            var reader = new TinyhandReader(writer);
            ValidateSettingNames(reader);
            return TinyhandSerializer.Deserialize<ProjectFile>(ref reader, TinyhandSerializerOptions.ConvertToString);
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static void ValidateSettingNames(TinyhandReader reader)
    {
        if (reader.TryReadNil())
        {
            return;
        }

        HashSet<string>? names = null;
        var count = reader.ReadMapHeaderOrEmptyArray();
        for (var i = 0; i < count; i++)
        {
            if (!reader.ReadStringSpan().SequenceEqual("CompileTimeSettings"u8))
            {
                reader.Skip();
                continue;
            }

            if (reader.TryReadNil())
            {
                continue;
            }

            var settingCount = reader.ReadMapHeaderOrEmptyArray();
            for (var j = 0; j < settingCount; j++)
            {
                var name = reader.ReadString();
                names ??= new(StringComparer.Ordinal);
                if (name is null || !names.Add(name))
                {
                    throw new TinyhandException($"Duplicate or null compile-time setting name: {name}");
                }

                reader.Skip();
            }
        }
    }

    // Preserve the specified text format and reject unknown names instead of enum defaulting.
    [Key("OutputKind")]
    private string OutputKindName
    {
        get => this.OutputKind switch
        {
            OutputKind.Application => "Application",
            OutputKind.Library => "Library",
            _ => throw new InvalidOperationException("Invalid output kind."),
        };
        set => this.OutputKind = value switch
        {
            "Application" => OutputKind.Application,
            "Library" => OutputKind.Library,
            _ => throw new ArgumentException("OutputKind must be Application or Library."),
        };
    }
}
