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

    /// <summary>Gets or sets legacy references, retained only for migration diagnostics.</summary>
    public KotonohaIdentifier[] KotonohaArray { get; set; } = [];

    /// <summary>Gets or sets this module's optional package ID.</summary>
    public string? PackageId { get; set; }

    /// <summary>Gets or sets this module's exact package version.</summary>
    public string? PackageVersion { get; set; }

    /// <summary>Gets or sets direct product dependencies keyed by source reference name.</summary>
    public Dictionary<string, DependencyReference> Dependencies { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets local package candidates and publication stores.</summary>
    public PackageSource[] PackageSources { get; set; } = [];

    /// <summary>Gets or sets dependencies used only by the root's tests.</summary>
    public Dictionary<string, DependencyReference> TestDependencies { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets explicit project-relative test-only source paths.</summary>
    public string[] TestSources { get; set; } = [];

    /// <summary>Gets or sets a stable test identity independent of solution selection and absolute paths.</summary>
    public string? TestProjectId { get; set; }

    /// <summary>Gets or sets the project's test execution settings.</summary>
    public Testing.TestSettings Test { get; set; } = new();

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
            ValidateMapNames(reader);
            var file = TinyhandSerializer.Deserialize<ProjectFile>(ref reader, TinyhandSerializerOptions.ConvertToString);
            if (file is not null && DependencyConfiguration.Validate(file) is { } failure)
            {
                throw new TinyhandException(failure);
            }

            return file;
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static void ValidateMapNames(TinyhandReader reader)
    {
        if (reader.TryReadNil())
        {
            return;
        }

        HashSet<string>? names = null;
        HashSet<string>? dependencies = null;
        HashSet<string>? tests = null;
        var testSettingsSeen = false;
        var count = reader.ReadMapHeaderOrEmptyArray();
        for (var i = 0; i < count; i++)
        {
            var key = reader.ReadStringSpan();
            if (key.SequenceEqual("Test"u8))
            {
                if (testSettingsSeen)
                {
                    throw new TinyhandException("Repeated Test settings record.");
                }

                testSettingsSeen = true;
                ValidateTestMap(ref reader);
                continue;
            }

            var map = key.SequenceEqual("CompileTimeSettings"u8) ? 1 : key.SequenceEqual("Dependencies"u8) ? 2 : key.SequenceEqual("TestDependencies"u8) ? 3 : 0;
            if (map == 0)
            {
                reader.Skip();
                continue;
            }

            if (reader.TryReadNil())
            {
                continue;
            }

            var settingCount = reader.ReadMapHeaderOrEmptyArray();
            ref var seen = ref (map == 1 ? ref names : ref (map == 2 ? ref dependencies : ref tests));
            for (var j = 0; j < settingCount; j++)
            {
                var name = reader.ReadString();
                seen ??= new(StringComparer.Ordinal);
                if (name is null || !seen.Add(name))
                {
                    throw new TinyhandException($"Duplicate or null {(map == 1 ? "compile-time setting" : "dependency reference")} name: {name}");
                }

                reader.Skip();
            }
        }
    }

    private static void ValidateTestMap(ref TinyhandReader reader)
    {
        if (reader.TryReadNil())
        {
            throw new TinyhandException("Test must be a settings record.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var count = reader.ReadMapHeaderOrEmptyArray();
        for (var i = 0; i < count; i++)
        {
            var name = reader.ReadString();
            if (name is null || !names.Add(name) || name is not ("Timeout" or "RecoveryGrace" or "DiagnosticCount" or "DiagnosticBytes" or "LogBytes" or "Environment"))
            {
                throw new TinyhandException("Unknown or duplicate Test setting: " + name);
            }

            if (name != "Environment")
            {
                reader.Skip();
                continue;
            }

            if (reader.TryReadNil())
            {
                throw new TinyhandException("Test.Environment must be a map.");
            }

            var environment = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var entries = reader.ReadMapHeaderOrEmptyArray();
            for (var j = 0; j < entries; j++)
            {
                var variable = reader.ReadString();
                if (variable is null || !environment.Add(variable))
                {
                    throw new TinyhandException("Duplicate test environment name: " + variable);
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
