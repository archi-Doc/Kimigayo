// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi;

/// <summary>Requests one exact module release from a Project or source Package.</summary>
[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class DependencyReference
{
    /// <summary>Gets or sets the originating module ID.</summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>Gets or sets the exact, case-sensitive release version.</summary>
    public string PackageVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets a path relative to the declaring Project.</summary>
    public string? Project { get; set; }

    /// <summary>Gets or sets an explicit source Package path.</summary>
    public string? Package { get; set; }
}
