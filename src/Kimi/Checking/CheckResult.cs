// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Checking;

/// <summary>The outcome of one check (SPEC 23.3.3).</summary>
/// <remarks>
/// A cancelled check has no output: the check entry lets <see cref="OperationCanceledException"/> propagate, so cancellation
/// is never published.
/// </remarks>
public enum CheckOutcome : byte
{
    /// <summary>Checked, with or without errors.</summary>
    Completed,

    /// <summary>An input or the effective configuration could not be established; an <c>Input</c> Error explains it.</summary>
    Blocked,

    /// <summary>An exception inside the compiler, or a violation of the diagnostic contract; one <c>CheckFaulted_Kd</c> Error explains it.</summary>
    Faulted,
}

/// <summary>The mode of a check unit (SPEC 23.3.1).</summary>
public enum CheckMode : byte
{
    /// <summary>The product inputs.</summary>
    Product,

    /// <summary>Test preparation without discovery or execution.</summary>
    Test,
}

/// <summary>Whether a product check found <c>#Test</c> functions in the project's own sources (SPEC 23.3.1).</summary>
public enum TestPresence : byte
{
    /// <summary>None was found, and every such source parsed without source errors.</summary>
    No,

    /// <summary>A marker was found.</summary>
    Yes,

    /// <summary>The scan could not decide.</summary>
    Unknown,
}

/// <summary>What one check produced, before the language server attaches its recorded inputs.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="Accepted">Whether every selected target passed the front-end checks.</param>
/// <param name="Presence">The test presence of a product check.</param>
/// <param name="Diagnostics">The diagnostic records in result order (SPEC 23.3.6.6).</param>
/// <param name="Sources">The source table the records refer to (SPEC 23.3.6.3).</param>
public sealed record CheckOutput(CheckOutcome Outcome, bool Accepted, TestPresence Presence, CheckDiagnostic[] Diagnostics, DiagnosticSource[] Sources)
{
    /// <summary>Initializes a new instance of the <see cref="CheckOutput"/> class from finalized diagnostics.</summary>
    /// <param name="outcome">The outcome.</param>
    /// <param name="accepted">Whether every selected target passed the front-end checks.</param>
    /// <param name="presence">The test presence of a product check.</param>
    /// <param name="result">The finalized diagnostics.</param>
    public CheckOutput(CheckOutcome outcome, bool accepted, TestPresence presence, DiagnosticResult result)
        : this(outcome, accepted, presence, result.Diagnostics, result.Sources)
    {
    }

    /// <summary>Gets optional detached editor information, or null when not requested or unavailable.</summary>
    internal HoverSnapshot? Hover { get; init; }

    /// <summary>Gets an optional editor-generation failure for logging, without changing the check outcome.</summary>
    internal string? HoverFault { get; init; }

    /// <summary>Creates the output of a check that an input or its configuration blocked before the check entry ran (SPEC 23.3.3).</summary>
    /// <param name="code">The <c>Input</c> Error that explains it.</param>
    /// <param name="location">The input it concerns, such as the project file, or the default value for none.</param>
    /// <param name="note">The environment-dependent text of the failure, published as a bounded Note.</param>
    /// <returns>The Blocked output: that one record, without a range.</returns>
    public static CheckOutput Blocked(DiagnosticCode code, SourceIdentity location, string? note = null)
    {
        var owner = new DiagnosticOwner();
        owner.Report(DiagnosticPartition.Input, code, location.IsEmpty ? null : location.Value, note: note);
        return new(CheckOutcome.Blocked, false, TestPresence.Unknown, owner.Finalize());
    }
}
