// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the record of a problem a MIR analysis finds (A6; DECISIONS R4-M3a, R4-M57, R5-M23, R4-M71), of a construction or collection
// record (R6-check H3-1), and its Reason, the relation an Origin problem names (2a0-D D18, DECISIONS R4-M113); its related sites are
// ProblemFact values (Core/ProblemFact.cs).
// Input: the values a recording rule gives: its code, the requirement it judged, its subject, the body it found it in, the Semantics case
// views that have it and, for an Origin problem, its relation. Output: MirProblem values, which MirReport keeps (the Reason in its key), Core's
// publisher (`Compilation.Publication`) publishes and the test MIR side (`MirTargetSide.MirSide`) reads.
// Invariants: a problem's requirement is the one its recording rule names (the Ownership partition's, or ControlFlow for A3, DIAGNOSTICS
// rule 1); a body problem's views are bits of its body's MirCases views; a problem no body describes (A4's obligation rows, at Binding's
// rows, the C1-F4 view's findings, DECISIONS R4-M118, and MirReport's merge defect) has NoBody and NoBodyViews, formed by WithoutBody, or
// by MirReport from such a problem (Defect, the merge, a held round, Keep and Replay copy its body and views or OR NoBodyViews with
// NoBodyViews); a construction or collection record (MirProgram.Report, the one path of a record) has its MirRecordClass as its body,
// ControlFlow and NoBodyViews, so it never shares a key with a NoBody problem or a record of the other class; NoBodyViews names no body's
// cases (A4's codes and the records take no case fact); a Reason's ends are the C1-F4 view's canonical atoms (its table's entries), so
// one relation has one Reason, and every other problem has the default Reason.
// Must not derive a requirement from a code, carry message text, or merge or drop problems (MirReport's).

#pragma warning disable SA1402 // The problem, its Reason and its record class belong together.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

/// <summary>A problem a MIR analysis finds (R4-M3a, R5-M23, R4-M71): its code, the requirement the recording rule judged (the Ownership
/// partition's, or ControlFlow for A3, DIAGNOSTICS rule 1; never looked up from the code), its subject, the body whose Semantics case views
/// (<see cref="MirCases"/>) number its views, and those views, one bit per view. Related sites are facts, which their first producer
/// adds.</summary>
/// <param name="Code">The code.</param>
/// <param name="Requirement">The requirement its recording rule names.</param>
/// <param name="Subject">The syntax the problem is about.</param>
/// <param name="Body">The ordinal of the body whose views <paramref name="Views"/> numbers, <see cref="NoBody"/>, or a record's
/// <see cref="MirRecordClass"/>.</param>
/// <param name="Views">The views that have the problem, bit i for view i (a problem no view decides: every view's bit,
/// <see cref="MirCases.Every"/>); a NoBody problem's and a record's are <see cref="NoBodyViews"/>.</param>
/// <param name="Reason">The relation an Origin problem names (D18); the default for any other problem.</param>
internal readonly record struct MirProblem(
    DiagnosticCode Code,
    DiagnosticRequirement Requirement,
    SourceRef Subject,
    int Body,
    ulong Views,
    MirReason Reason = default)
{
    /// <summary>The body of a problem no body describes: A4's obligation rows, which lie at Binding's rows (R4-M71), the C1-F4 view's
    /// findings (R4-M118) and MirReport's merge defect, which <see cref="WithoutBody"/> forms.</summary>
    internal const int NoBody = -1;

    /// <summary>The views of a problem no body describes and of a record: every bit, naming no body's cases (A4's codes and the records take
    /// no case fact).</summary>
    internal const ulong NoBodyViews = ulong.MaxValue;

    /// <summary>Gets a value indicating whether the problem is a construction or collection record, whose rows <c>MirProgram.Records</c> gives,
    /// rather than an analysis's problem.</summary>
    internal bool IsRecord => this.Body is (int)MirRecordClass.Construction or (int)MirRecordClass.Collection;

    /// <summary>Gets the record class of a construction or collection record, null for an analysis's problem.</summary>
    internal MirRecordClass? RecordClass => this.IsRecord ? (MirRecordClass)this.Body : null;

    /// <summary>A problem no body describes (<see cref="NoBody"/>, <see cref="NoBodyViews"/>): A4's obligation rows, which lie at Binding's
    /// rows (R4-M71), the C1-F4 view's findings (R4-M118) and MirReport's merge defect; the one form of such a problem.</summary>
    /// <param name="code">The code.</param>
    /// <param name="requirement">The requirement its recording rule names.</param>
    /// <param name="subject">The syntax the problem is about.</param>
    /// <returns>The problem.</returns>
    internal static MirProblem WithoutBody(DiagnosticCode code, DiagnosticRequirement requirement, SourceRef subject)
        => new(code, requirement, subject, NoBody, NoBodyViews);
}

/// <summary>The class of a record that is no body's problem (R6-check H3-1), its value the record's <see cref="MirProblem.Body"/>. No
/// member is 0 or -1: Body 0 is a body and -1 is <see cref="MirProblem.NoBody"/>.</summary>
internal enum MirRecordClass
{
    /// <summary>A construction record (MirProgram.Report): an Unsupported form, the default expansion limit or a broken HIR contract.</summary>
    Construction = -2,

    /// <summary>A collection record (InstanceCollector's, through MirProgram.Report): a capability gap, a limit, a layout failure or a broken
    /// contract of the checked set.</summary>
    Collection = -3,
}

/// <summary>The relation an Origin problem names (2a0-D D18): its longer and shorter ends, the C1-F4 view's canonical atoms of the
/// body, and whether it is an equality; two problems at one subject are two relations only when their Reasons differ.</summary>
/// <param name="Longer">The end required to outlive the other.</param>
/// <param name="Shorter">The other end.</param>
/// <param name="Equality">Whether the relation is an equality, failed in both directions.</param>
internal readonly record struct MirReason(BoundOrigin? Longer, BoundOrigin? Shorter, bool Equality);
