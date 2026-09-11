// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;

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

    /// <summary>Gets or sets explicitly typed compile-time scalar settings.</summary>
    /// <remarks>Preserves written names so preparation can diagnose case-insensitive collisions instead of overwriting entries.</remarks>
    public Dictionary<string, CompileTimeSetting> CompileTimeSettings { get; set; } = new(StringComparer.Ordinal);

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
