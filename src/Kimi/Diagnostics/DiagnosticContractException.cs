// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Diagnostics;

/// <summary>The kind of a diagnostic contract violation (SPEC 23.3.6.7); a Faulted result reports it as its Reason.</summary>
public enum DiagnosticFault : byte
{
    /// <summary>The diagnostic catalog is malformed.</summary>
    Catalog,

    /// <summary>A report names a code without a catalog entry.</summary>
    UnknownCode,

    /// <summary>A location lies outside its source, or names a span without a source.</summary>
    InvalidLocation,

    /// <summary>A report's arguments do not match its code's definition.</summary>
    InvalidArgument,

    /// <summary>An exception ended the analysis while diagnostic collection stayed intact.</summary>
    Exception,

    /// <summary>Collecting or finalizing diagnostics failed.</summary>
    Collection,
}

/// <summary>Signals a violation of the diagnostic contract (SPEC 23.3.6.7). It is a compiler defect, never a property of the checked source.</summary>
internal sealed class DiagnosticContractException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="DiagnosticContractException"/> class.</summary>
    /// <param name="fault">The violation.</param>
    /// <param name="detail">A description for the compiler developer.</param>
    public DiagnosticContractException(DiagnosticFault fault, string detail)
        : base($"Diagnostic contract violation ({fault}): {detail}")
    {
        this.Fault = fault;
    }

    /// <summary>Gets the violation.</summary>
    public DiagnosticFault Fault { get; }
}
