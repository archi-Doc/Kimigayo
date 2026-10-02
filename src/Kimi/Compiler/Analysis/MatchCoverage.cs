// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

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

    // Only syntax whose entire shape has a type-independent interpretation is considered.
    // Bound analysis always overrides this with validated Pattern metadata.
    internal static MatchCoverage FromSyntax(MatchKoto match, ControlFlowType? subject)
    {
        var whole = false;
        var hasTrue = false;
        var hasFalse = false;
        for (var i = 0; i < match.Arms.Count; i++)
        {
            var arm = match.Arms[i];
            var pattern = KotoHelper.UnwrapParentheses(arm.Pattern);
            if (pattern is IdentifierNameKoto { IdentifierName: "_" } || pattern is SyntaxFormKoto { Akind: KotoKind.BindingPattern })
            {
                whole |= arm.Guard is null;
            }
            else if (pattern is BoolLiteralKoto literal && subject == ControlFlowType.Boolean)
            {
                hasTrue |= arm.Guard is null && literal.Value;
                hasFalse |= arm.Guard is null && !literal.Value;
            }
            else if (pattern is UnitLiteralKoto && subject == ControlFlowType.Unit)
            {
                whole |= arm.Guard is null;
            }
            else
            {
                return default;
            }
        }

        return whole || (hasTrue && hasFalse) ? new(MatchCoverageState.Exhaustive) :
            subject is null ? default : new(MatchCoverageState.NonExhaustive, subject == ControlFlowType.Boolean ? MatchCoverageReason.BooleanValuesRequired : MatchCoverageReason.CatchAllRequired);
    }
}
