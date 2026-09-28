// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi;

/// <summary>Declares an exact source Package candidate or a local publication store.</summary>
[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class PackageSource
{
    /// <summary>Gets or sets the candidate's module ID.</summary>
    public string? PackageId { get; set; }

    /// <summary>Gets or sets the candidate's exact release version.</summary>
    public string? PackageVersion { get; set; }

    /// <summary>Gets or sets the candidate Package path.</summary>
    public string? Package { get; set; }

    /// <summary>Gets or sets a local store directory instead of an explicit candidate.</summary>
    public string? Store { get; set; }
}
