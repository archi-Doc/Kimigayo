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

    /// <summary>An exception inside the compiler.</summary>
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

/// <summary>One diagnostic record of a check (SPEC 23.3.3). No compiler object escapes into it.</summary>
/// <param name="Code">The diagnostic entry name.</param>
/// <param name="Severity">The severity.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Location">The source identity, or the default value when the record has no location.</param>
/// <param name="Range">The range within the location, if known.</param>
public sealed record CheckDiagnostic(string Code, DiagnosticSeverity Severity, string Message, SourceIdentity Location, SourceRange? Range)
{
    /// <summary>Orders records within one URI by range, code, severity and message (SPEC 23.4.7).</summary>
    /// <param name="left">The first record.</param>
    /// <param name="right">The second record.</param>
    /// <returns>The ordering.</returns>
    public static int Compare(CheckDiagnostic left, CheckDiagnostic right)
    {
        var order = (left.Range ?? default).CompareTo(right.Range ?? default);
        if (order == 0)
        {
            order = string.CompareOrdinal(left.Code, right.Code);
        }

        if (order == 0)
        {
            order = left.Severity.CompareTo(right.Severity);
        }

        return order == 0 ? string.CompareOrdinal(left.Message, right.Message) : order;
    }
}

/// <summary>What one check produced, before the language server attaches its recorded inputs.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="Accepted">Whether every selected target passed the front-end checks.</param>
/// <param name="Presence">The test presence of a product check.</param>
/// <param name="Diagnostics">The diagnostic records.</param>
public sealed record CheckOutput(CheckOutcome Outcome, bool Accepted, TestPresence Presence, CheckDiagnostic[] Diagnostics);

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
