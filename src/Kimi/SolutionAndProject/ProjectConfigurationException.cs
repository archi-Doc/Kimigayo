// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi;

/// <summary>A loaded project file whose configuration breaks <see cref="DependencyConfiguration"/>'s rules.</summary>
/// <param name="failures">One note per independent failure (SPEC 23.1.2), at least one.</param>
internal sealed class ProjectConfigurationException(IReadOnlyList<string> failures) : Exception(failures[0])
{
    /// <summary>Gets one note per independent failure; each is reported as its own record.</summary>
    internal IReadOnlyList<string> Failures { get; } = failures;
}
