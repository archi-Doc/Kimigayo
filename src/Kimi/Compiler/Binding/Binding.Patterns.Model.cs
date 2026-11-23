// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Shared Pattern metadata.

public enum BoundPatternKind : byte
{
    /// <summary>A leaf that tests, selects and binds nothing: its Pattern failed with its own record, a payload Pattern of its Case was not
    /// read, or it rests on the failed Subject (<see cref="BoundPattern.MatchedType"/> null; SPEC 14.8.1, 23.1.2). Every other position
    /// is buildable: a Case position has a child for each payload of its selected Case, whose Type was read at the position, and a Tuple
    /// position one for each element; a position the late recheck refutes keeps its kind and its test.</summary>
    Invalid,
    Wildcard,
    Binding,
    Unit,
    Literal,
    Case,
    Tuple,
}

public enum PatternAcquisition : byte
{
    None,
    Copy,
    Borrow,
    Move,
    CopyOrMove,
    Deferred,
}

public enum PatternAccessMode : byte
{
    Owned,
    Shared,
    Exclusive,
}

/// <summary>SPEC 15.1.6: the acquisition of a match or for Subject, fixed by its outermost written operation.</summary>
public enum SubjectMode : byte
{
    Shared,
    Exclusive,
    ByValue,
}

public enum PatternLiteralKind : byte
{
    None,
    Integer,
    Boolean,
    Character,
    String,
}

/// <summary>A Literal Pattern's constant (SPEC 14.8.1). Bits is an Integer's value in its Type's N-bit sign-extended form, the one
/// encoding of an Integer constant (Binding.TryIntegerLiteral, as Folded; a generic literal's value, every instance's payload), a
/// Boolean's 0 or 1, or a Character's scalar value.</summary>
public readonly record struct PatternLiteral(PatternLiteralKind Kind, Int128 Bits = default, string? Text = null);

/// <summary>A preorder position; direct children are walked by advancing to each child's End. A position selects the referent of
/// <see cref="ImplicitFollows"/> safe value-reference layers of its matched Type (SPEC 14.8.1, 13.5.5: a structural position every
/// layer that lacks its structure, a Binding reached with owned access the qualifying pair layer it would otherwise take by value),
/// <see cref="AccessMode"/> is the access its path grants to the selected value, and <see cref="OwnedPart"/> whether that value may
/// lie in the owned Subject or iteration item: its path passed only pair layers admitting owner, whose owner case selects the
/// Place itself (SPEC 15.1.6 "a part of an owned Subject or iteration item", 13.5.5). <see cref="MatchedType"/> is the Type the
/// position matched, null exactly at a resting leaf: the Invalid position of an arm whose Pattern rests on the failed Subject, which
/// matched no Type (SPEC 23.1.2).</summary>
public readonly record struct BoundPattern(Koto Source, BoundType? MatchedType, BoundPatternKind Kind, int Parent, int Element, int End,
    BoundEnumCase? Case = null, BindingSymbol? BodySymbol = null, PatternAcquisition Acquisition = PatternAcquisition.None,
    bool WholePosition = false, PatternLiteral Literal = default,
    PatternAccessMode AccessMode = PatternAccessMode.Owned, int ImplicitFollows = 0,
    BindingSymbol? CandidateSymbol = null, bool OwnedPart = false);

public readonly record struct BoundMatchArm(MatchArmKoto Syntax, int Pattern);

public readonly record struct PatternWarning(Koto Pattern, int CoveringArm);

public sealed class BoundMatch
{
    public bool IsCurrent { get; internal set; }

    public MatchKoto Syntax { get; internal set; } = null!;

    public MatchCoverage Coverage { get; internal set; }

    public IReadOnlyList<BoundPattern> Positions => this.PositionStorage;

    public IReadOnlyList<BoundMatchArm> Arms => this.ArmStorage;

    internal List<BoundPattern> PositionStorage { get; } = new();

    internal List<BoundMatchArm> ArmStorage { get; } = new();

    internal List<byte> CaseCoverage { get; } = new();

    /// <summary>Gets or sets a value indicating whether a Pattern position or a guard of the match has a located failure: the match's
    /// coverage is not computed (<see cref="MatchCoverageState.Invalid"/>), and the plan is still built (a failed position is an Invalid
    /// leaf or keeps its test); <see cref="Pending"/> keeps it unbuilt.</summary>
    internal bool Invalid { get; set; }

    internal bool Pending { get; set; }

    /// <summary>Gets or sets the borrow that acquires the Subject (SPEC 15.1.6 subject rule): the borrow of a bare Place in its mode,
    /// such as the exclusive Reborrow of a bare exclusive borrow value; null when the written Subject value itself is acquired.</summary>
    internal BoundType? SubjectBorrow { get; set; }

    /// <summary>Gets or sets the Subject mode: the access that the Subject Place grants (SPEC 15.1.6).</summary>
    internal SubjectMode Mode { get; set; }

    /// <summary>Gets a value indicating whether the plan is a try's, whose success binding extracts its payload as a value (SPEC 17.3)
    /// and is no Pattern binding of SPEC 15.1.6.</summary>
    internal bool ExtractsValue => this.Syntax is TryKoto;

    /// <summary>Gets the Pattern plan (PatternPlan, plan 5.3) the current pass made for a match or a try, or the Some/None decomposition
    /// of a for loop over a user iteration entry, whose positions bind its iteration bindings; null for another node or loop.</summary>
    /// <param name="binding">The reader's Binding, whose pass made the plan.</param>
    /// <param name="node">The match, try or for loop.</param>
    /// <returns>The plan, or null.</returns>
    internal static BoundMatch? Of(Binding binding, Koto node) => node switch
    {
        MatchKoto match => binding.TryGetMatch(match, out var plan) ? plan : null,
        ForKoto { Iteration.Decomposition: { IsCurrent: true } decomposition } => decomposition,
        _ => null,
    };

    internal void Reset(MatchKoto? syntax)
    {
        this.Syntax = syntax!;
        this.IsCurrent = syntax is not null;
        this.Coverage = default;
        this.Invalid = this.Pending = false;
        this.SubjectBorrow = null;
        this.Mode = SubjectMode.Shared;
        this.PositionStorage.Clear();
        this.ArmStorage.Clear();
        this.CaseCoverage.Clear();
    }
}
