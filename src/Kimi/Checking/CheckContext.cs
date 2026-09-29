// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Diagnostics;

namespace Kimi.Checking;

/// <summary>Carries one check through the shared check entry (SPEC 23.3.2).</summary>
/// <remarks>
/// Commands pass no context: they read the disk and render each result themselves. A context supplies the inputs and
/// collects what its caller finalizes: every diagnostic of the request in one owner, and the compilation the entry created.
/// </remarks>
internal sealed class CheckContext
{
    /// <summary>Initializes a new instance of the <see cref="CheckContext"/> class.</summary>
    /// <param name="inputs">The input source of the check.</param>
    public CheckContext(CheckInputSource inputs)
    {
        this.Inputs = inputs;
    }

    /// <summary>Gets the input source of the check.</summary>
    public CheckInputSource Inputs { get; }

    /// <summary>Gets the diagnostic owner of this check request (SPEC 23.3.6).</summary>
    public DiagnosticOwner Diagnostics { get; } = new();

    /// <summary>Gets or sets the compilation the entry created, if preparation got that far.</summary>
    public Compilation? Compilation { get; set; }

    /// <summary>Gets or sets a value indicating whether the front end ran after all inputs were read.</summary>
    public bool FrontEndRan { get; set; }
}
