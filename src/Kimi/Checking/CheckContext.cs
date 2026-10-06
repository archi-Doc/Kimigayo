// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Diagnostics;

namespace Kimi.Checking;

/// <summary>Carries one check through the shared check entry (SPEC 23.3.2).</summary>
/// <remarks>
/// Commands pass no context: they read the disk and render each result themselves. A context supplies the inputs and
/// collects what its caller finalizes: every diagnostic of the request in one owner, and the compilation whose front end ran.
/// </remarks>
/// <param name="inputs">The input source of the check.</param>
/// <param name="collectHover">Whether to collect optional editor information before releasing the compilation.</param>
internal sealed class CheckContext(CheckInputSource inputs, bool collectHover = false)
{
    /// <summary>Gets a value indicating whether this check requests optional editor information.</summary>
    public bool CollectHover { get; } = collectHover;

    /// <summary>Gets the input source of the check.</summary>
    public CheckInputSource Inputs { get; } = inputs;

    /// <summary>Gets the diagnostic owner of this check request (SPEC 23.3.6).</summary>
    public DiagnosticOwner Diagnostics { get; } = new();

    /// <summary>
    /// Gets or sets the compilation whose front end ran (SPEC 23.3.2, step 3), or <see langword="null"/> while input preparation
    /// has not established every input, which blocks the check (SPEC 23.3.3).
    /// </summary>
    public Compilation? Compilation { get; set; }
}
