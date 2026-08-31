// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi;

/// <summary>Defines version settings shared by projects in a solution.</summary>
[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class ProjectConfiguration
{
    /// <summary>Gets or sets the selected Kimigayo language version.</summary>
    public string LangVersion { get; set; } = "0.0.1";

    /// <summary>Gets or sets the solution version.</summary>
    public string Version { get; set; } = "0.0.1";
}
