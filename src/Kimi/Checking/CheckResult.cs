// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Checking;

/// <summary>The outcome of one check (SPEC 23.3.3).</summary>
public enum CheckOutcome : byte
{
    /// <summary>Checked, with or without errors.</summary>
    Completed,

    /// <summary>An input or the effective configuration could not be established.</summary>
    Blocked,

    /// <summary>An exception inside the compiler, or a violation of the diagnostic contract.</summary>
    Faulted,

    /// <summary>Command cancellation; never published.</summary>
    Cancelled,
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
}

/// <summary>Signals a read of an open document that is out of sync (SPEC 23.4.2); it is reported as <c>DocumentDesynchronized_Kd</c>.</summary>
internal sealed class DesynchronizedInputException : IOException
{
    /// <summary>The observed failure of a desynchronized document, which is its unestablished identity.</summary>
    public const string Failure = "DocumentDesynchronized: the editor document is out of sync";

    /// <summary>Initializes a new instance of the <see cref="DesynchronizedInputException"/> class.</summary>
    public DesynchronizedInputException()
        : base(Failure)
    {
    }
}

/// <summary>Signals that a check needs an input with an event after its base (SPEC 23.4.6); the check takes no effect.</summary>
internal sealed class PendingInputException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PendingInputException"/> class.</summary>
    /// <param name="path">The path of the pending input.</param>
    public PendingInputException(string path)
        : base("Pending input: " + path)
    {
    }
}
