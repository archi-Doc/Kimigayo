// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;

namespace Kimi;

/// <summary>Defines the common settings serialized in a <c>.kimiproj</c> file.</summary>
[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class ProjectFile
{
    /// <summary>Gets or sets the LLVM-style target triples built by the project.</summary>
    public string[] Targets { get; set; } = [];

    /// <summary>Gets or sets the external Kotonoha library references.</summary>
    public KotonohaIdentifier[] KotonohaArray { get; set; } = [];

    /// <summary>Gets or sets the project-wide alias imports.</summary>
    public string[] Alias { get; set; } = [];
}
