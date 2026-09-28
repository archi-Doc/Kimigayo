// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>
/// Identifies an external Kotonoha library dependency requested by a project.
/// </summary>
[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class KotonohaIdentifier
{
    /// <summary>
    /// Gets or sets the dependency name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the requested dependency version or version constraint.
    /// </summary>
    public string Version { get; set; } = string.Empty;
}
