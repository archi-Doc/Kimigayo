// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Diagnostics;

/// <summary>
/// A range of diagnostic facts that is invalidated as a whole, in phase order (docs/dev/DIAGNOSTICS.md §4.2).
/// A decision reads only the partitions that precede it.
/// </summary>
public enum DiagnosticPartition : byte
{
    /// <summary>Inputs and configuration; kept for the whole check request.</summary>
    Input,

    /// <summary>Lexing and parsing, one list per module; invalidated when the module's syntax tree is rebuilt.</summary>
    Syntax,

    /// <summary>Binding; invalidated with every source analysis.</summary>
    Binding,

    /// <summary>Startup checks; invalidated with every source analysis.</summary>
    Startup,

    /// <summary>Control-flow analysis; invalidated with every source analysis.</summary>
    ControlFlow,

    /// <summary>Ownership analysis; invalidated with every source analysis.</summary>
    Ownership,

    /// <summary>Emission, a later phase with its own result.</summary>
    Emission,
}
