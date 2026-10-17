// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the coverage vocabulary of a match: its state and what a non-exhaustive match lacks.
// Input: Binding.Patterns' coverage proof, written on BoundMatch.Coverage. Output: the state its consumers read and, for a
// non-exhaustive match, the requirement its diagnostic reports.
// Only a non-exhaustive match carries a reason; a missing Case or whole-payload reason names the Case's declaration index.
// Must not prove coverage itself: Binding.Patterns decides it.

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Shared match analysis vocabulary.

public enum MatchCoverageState : byte
{
    Pending,
    Invalid,
    NonExhaustive,
    Exhaustive,
}

public enum MatchCoverageReason : byte
{
    None,
    MissingCase,
    WholePayloadRequired,
    CatchAllRequired,
    BooleanValuesRequired,
}

public readonly record struct MatchCoverage(MatchCoverageState State, MatchCoverageReason Reason = MatchCoverageReason.None, int CaseIndex = -1)
{
    public bool? IsExhaustive => this.State switch
    {
        MatchCoverageState.Exhaustive => true,
        MatchCoverageState.NonExhaustive => false,
        _ => null,
    };

    /// <summary>Gets what a non-exhaustive match lacks; an unexplained one lacks a catch-all.</summary>
    public MatchCoverageReason Requirement => this.Reason == MatchCoverageReason.None ? MatchCoverageReason.CatchAllRequired : this.Reason;

    /// <summary>Describes what a non-exhaustive match lacks; the message states that the match must be exhaustive.</summary>
    /// <returns>The description.</returns>
    public string Describe() => this.Requirement switch
    {
        MatchCoverageReason.MissingCase => $"The Case at declaration index {this.CaseIndex} has no arm.",
        MatchCoverageReason.WholePayloadRequired => $"The Case at declaration index {this.CaseIndex} requires an unguarded whole-payload arm.",
        MatchCoverageReason.BooleanValuesRequired => "Unguarded true and false Patterns or a catch-all are required.",
        _ => "An unguarded whole-position catch-all is required.",
    };
}
