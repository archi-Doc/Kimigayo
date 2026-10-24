// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the C1-F4 view (SPEC 15.6.1, 15.6.5; DECISIONS R4-M94, R4-M101): a body's Origin rows and atom table, an atom's class in a body, and
// the body's fixed-fixed oracle, which A5 judges with (MirOrigins); Binding.OriginRows.cs computes them.
// Input: a body as an ExecutableBody (MIR's key form, MirDefKey.Body; a Getter's or Setter's execution function names its accessor), after
// Bind, and for an atom MIR reads the MIR site it is read at.
// Output: OriginRows, OriginAtom (an atom's index in the body's table), UniversalRelations and OriginFindings, the sites of the broken
// contracts the view's validation found, which MirOrigins.Findings publishes. Every site the view publishes or takes is a SourceRef, the
// one way MIR names syntax (ARCH rows 29 and 90; DECISIONS R4-M113): the view converts its rows' and findings' sites once and reads a MIR
// site's syntax itself.
// Invariants: built on a pass's first read and immutable after Bind but for the atoms MIR appends; every end and atom resolved at its use
// (OriginAtUse) and meets canonical (Meet); no row from a Constraint proof (T1); a row formed with no body (a declaration's) is Binding's
// to judge (DECISIONS BU-26) and in no slice; a constructor's own body's slice is its rows, then its layer's (the struct's Field
// initializers), which share only Fixed atoms with them.
// Must not: decode a Projection slot for MIR (only ProjectionRole leaves the view, D7), read MirBodyKind, or judge a relation.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // The view's records.

/// <summary>Where a row comes from (2a0-D D2, without <c>ResultFit</c> until 2a-2, DECISIONS R4-M101 E6).</summary>
internal enum OriginRowSource : byte
{
    Fit,
    Declared,
    WellFormed,
    CallBorrows,

    // MirOrigins' own containment edges (SPEC 15.6.3); Binding forms none.
    Contained,
}

/// <summary>SPEC 15.6.5's kinds of an Origin relative to the checked body (2a0-D D6); Unclassed, which forms no edge (§3 row 9: another
/// declaration's or body's atom, a located limit); and Dependent (SPEC 23.1.2, DECISIONS R4-M118), an end a Binding failure left Unbound,
/// or a meet with such an operand and no Unclassed one, never in a complete compilation: never a source or a judged target, it records
/// nothing itself, and a chain through it is judged between its ends.</summary>
internal enum OriginAtomKind : byte
{
    Fixed,
    Finite,
    Inferred,
    Unclassed,
    Dependent,
}

/// <summary>The role of a Projection's slot, which only the view decodes (2a0-D D7); None for any other atom.</summary>
internal enum ProjectionRole : byte
{
    None,
    Place,
    Parameter,
    Receiver,
    Helper,
    Environment,
    ClosureValue,
    CallResult,
    Storage,
}

/// <summary>One relation a body checks (2a0-D D2): its <c>Site</c>, the syntax it is checked at as a SourceRef (DECISIONS R4-M113); its ends,
/// indices in the body's atom table; <c>CaseMask</c>, the Semantics case it is required in, as bits of the body's pair binders
/// (<c>MirBody.Binders</c>). <c>Clause</c> and <c>Destination</c> are the diagnostics adapter's: MirOrigins reads neither.</summary>
internal readonly record struct OriginRow(
    SourceRef Site,
    int Longer,
    int Shorter,
    bool Equality,
    OriginRowSource Source,
    Koto? Clause,
    BoundType? Destination,
    ulong CaseMask);

/// <summary>An atom of a body's table with its class there.</summary>
internal readonly record struct OriginAtomEntry(BoundOrigin Atom, OriginAtomKind Kind, ProjectionRole Role);

internal sealed partial class TyCtxt
{
    // SPEC 15.6.1: a body's rows and atom table, a constructor's followed by its layer's.
    internal Binding.OriginRowSet OriginRows(ExecutableBody body) => binding.OriginRows(body);

    // SPEC 15.6.5: an atom's index in a body's table, resolved at its use (a MIR site) and classed there; an atom no row names is appended.
    internal int OriginAtom(ExecutableBody body, BoundOrigin origin, SourceRef site) => binding.OriginAtomIn(body, origin, site);

    // SPEC 15.3.6, 15.6.5: the body's fixed-fixed oracle, extracted at its root (a layer's at its struct); disposed after use.
    internal Binding.UniversalRelations UniversalRelations(ExecutableBody body) => binding.UniversalRelationsOf(body);

    // The sites whose rows or atoms broke the view's contract in a complete compilation, each an InternalInvariant_Kd (MirOrigins.Findings).
    internal IReadOnlyList<SourceRef> OriginFindings => binding.OriginFindings;
}
