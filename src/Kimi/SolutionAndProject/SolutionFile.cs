// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi;

/// <summary>Defines the project list and shared configuration serialized in a <c>.kimisln</c> file.</summary>
[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class SolutionFile
{
    /// <summary>Gets the project-file paths included by the solution.</summary>
    public List<string> Projects { get; init; } = [];

    /// <summary>Gets the solution-wide language and package configuration.</summary>
    public ProjectConfiguration Configuration { get; init; } = new();
}
