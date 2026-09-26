// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Shared Pattern metadata.

public enum BoundPatternKind : byte
{
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

public readonly record struct PatternLiteral(PatternLiteralKind Kind, UInt128 Magnitude = default, bool Negative = false, string? Text = null);

/// <summary>A preorder position; direct children are walked by advancing to each child's End. A structural position selects
/// the referent of <see cref="ImplicitDerefs"/> safe value-reference layers of its matched Type (SPEC 14.8.1), and
/// <see cref="AccessMode"/> is the access its path grants to the selected value.</summary>
public readonly record struct BoundPattern(Koto Source, BoundType MatchedType, BoundPatternKind Kind, int Parent, int Element, int End,
    BoundEnumCase? Case = null, BindingSymbol? BodySymbol = null, PatternAcquisition Acquisition = PatternAcquisition.None,
    bool WholePosition = false, PatternLiteral Literal = default,
    PatternAccessMode AccessMode = PatternAccessMode.Owned, int ImplicitDerefs = 0,
    BindingSymbol? CandidateSymbol = null);

public readonly record struct BoundMatchArm(MatchArmKoto Syntax, int Pattern);

public readonly record struct PatternWarning(Koto Pattern, Koto CoveringPattern, int CoveringArm);

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

    internal BoundType? ExpectedType { get; set; }

    internal BoundType? ResultType { get; set; }

    internal bool Invalid { get; set; }

    internal bool Pending { get; set; }

    /// <summary>Gets or sets the borrow that acquires the Subject (SPEC 15.1.6 subject rule): the borrow of a bare Place in its mode,
    /// such as the exclusive Reborrow of a bare exclusive borrow value; null when the written Subject value itself is acquired.</summary>
    internal BoundType? SubjectBorrow { get; set; }

    /// <summary>Gets or sets the Subject mode: the access that the Subject Place grants (SPEC 15.1.6).</summary>
    internal SubjectMode Mode { get; set; }

    internal void Reset(MatchKoto? syntax)
    {
        this.Syntax = syntax!;
        this.IsCurrent = syntax is not null;
        this.Coverage = default;
        this.Invalid = this.Pending = false;
        this.SubjectBorrow = null;
        this.Mode = SubjectMode.Shared;
        this.ExpectedType = null;
        this.ResultType = null;
        this.PositionStorage.Clear();
        this.ArmStorage.Clear();
        this.CaseCoverage.Clear();
    }
}
