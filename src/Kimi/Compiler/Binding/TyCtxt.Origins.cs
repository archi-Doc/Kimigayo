// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the C1-F4 view (SPEC 15.6.1, 15.6.5; DECISIONS R4-M94, R4-M101): a body's Origin rows and atom table, an atom's class in a body, and
// the body's fixed-fixed oracle, which A5 judges with (MirOrigins); Binding.OriginRows.cs computes them.
// Input: a body as an ExecutableBody (MIR's key form, MirDefKey.Body; a Getter's or Setter's execution function names its accessor), after
// Bind, and for an atom MIR reads the MIR site it is read at.
// Output: OriginRows, OriginAtom (an atom's index in the body's table), MeetAtom (a canonical meet's), MeetOperand (an operand of a table
// meet, as the meet holds it), UniversalRelations and OriginLayers (the layer units with rows).
// Every site the view publishes or takes is a SourceRef, the one way MIR names syntax (ARCH rows 29 and 90; DECISIONS R4-M113): the view
// converts its rows' sites once and reads a MIR site's syntax itself.
// Invariants: built on a pass's first read and immutable after Bind but for the atoms MIR appends; every end and atom resolved once, at its
// use: a row's end as its row reads it (a Compared row's as the slot), a MIR Origin at its site as the value, a meet's operands as the meet
// holds them; meets canonical (Meet); no row from a Constraint proof (T1); a row formed with no body (a declaration's) is Binding's
// to judge (DECISIONS BU-26) and in no slice; a constructor's own body's slice is its rows, then its layer's (the struct's Field
// initializers), each atom in its forming unit's part, Fixed atoms shared; a broken contract throws in a complete compilation
// (CheckFaulted_Kd, DECISIONS BU-102), so MIR trusts every row and atom it reads.
// Must not: decode a Projection slot for MIR (only ProjectionRole leaves the view, D7), read MirBodyKind, or judge a relation.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // The view's records.

/// <summary>Where a row comes from (2a0-D D2, DECISIONS R4-M101 E6).</summary>
internal enum OriginRowSource : byte
{
    Fit,
    Declared,
    WellFormed,

    // MirOrigins' own containment edges (SPEC 15.6.3); Binding forms none.
    Contained,
}

/// <summary>SPEC 15.6.5's kinds of an Origin relative to the checked body (2a0-D D6); Unclassed, which forms no edge (§3 row 9: another
/// declaration's or body's atom, a located limit); and Dependent (SPEC 23.1.2, DECISIONS R4-M118), an end a Binding failure left Unbound,
/// an unclassified Closure's pending atom (R4-M126 §2.5 (c)), or a meet with such an operand and no Unclassed one, never in a complete
/// compilation: never a source or a judged target, it records nothing itself, and a chain through it is judged between its ends.</summary>
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
/// (<c>MirBody.Binders</c>). <c>Clause</c>, <c>Destination</c>, <c>FixedBy</c> (the input whose equality fixed a call's fresh Origin, SPEC 15.6.1) and
/// <c>Supplier</c> (the longer end as Binding formed it, before the view resolved it, a Closure's receiver resolution included: the record shows
/// the Borrow that supplied its end from it, DECISIONS R4-M133) are the diagnostics adapter's: MirOrigins reads none of them.</summary>
internal readonly record struct OriginRow(
    SourceRef Site,
    int Longer,
    int Shorter,
    bool Equality,
    OriginRowSource Source,
    Koto? Clause,
    BoundType? Destination,
    ulong CaseMask,
    Koto? FixedBy,
    BoundOrigin Supplier);

/// <summary>An atom of a body's table with its class there; <c>EnclosingCallRegion</c>: the atom is an enclosing body's call region, or a meet
/// with such an operand (row 6), which A5's failing chains read (the nested-body limit); <c>Value</c>: an enclosing body's local region's solved
/// value (row 6; PLAN G492-a (ii), DECISIONS BU-127, O-422 (a)), whose operands A5 judges where the region fails at a longer end, null for
/// any other atom.</summary>
internal readonly record struct OriginAtomEntry(BoundOrigin Atom, OriginAtomKind Kind, ProjectionRole Role, bool EnclosingCallRegion = false, BoundOrigin? Value = null)
{
    // SPEC 15.3.6: the operands of what the entry holds as a meet's operands are read (TyCtxt.MeetOperand, A5's split of a failed source):
    // a row-6 local region's solved value, else the atom; one, the held Origin itself, when it is no meet.
    internal int HeldOperands => this.Held.Kind == OriginKind.Intersection ? this.Held.Operands.Count : 1;

    private BoundOrigin Held => this.Value ?? this.Atom;

    internal BoundOrigin HeldOperand(int index) => this.Held.Kind == OriginKind.Intersection ? this.Held.Operands[index] : this.Held;
}

internal sealed partial class TyCtxt
{
    // SPEC 15.6.1: a body's rows and atom table, a constructor's followed by its layer's.
    internal Binding.OriginRowSet OriginRows(ExecutableBody body) => binding.OriginRows(body);

    // SPEC 15.6.5: an atom's index in a body's table, resolved at its use (a MIR site) and classed there; an atom no row names is appended.
    internal int OriginAtom(ExecutableBody body, BoundOrigin origin, SourceRef site) => binding.OriginAtomIn(body, origin, site);

    // SPEC 15.3.6: the index of an operand of a meet atom of a body's table, as the meet holds it (the view resolved the meet once, at its
    // use), or of a row-6 local region's solved value (OriginAtomEntry.HeldOperand); never resolved again at the asking site, which only locates
    // a broken contract.
    internal int MeetOperand(ExecutableBody body, int meet, int index, SourceRef site) => binding.MeetOperandIn(body, meet, index, site);

    // SPEC 15.3.6: the index of the canonical meet of two atoms of a body's table (A5's meet law), interned there once; a meet of Fixed atoms
    // is Fixed, and its part is its operands' union (PartOf), as for every meet, also when their parts differ (a layer atom beside a body
    // atom): A5 chooses a derived edge's oracle by its in-edge's row, as for every row edge, and by the ends only for a containment in-edge.
    internal int MeetAtom(ExecutableBody body, int a, int b, SourceRef site) => binding.MeetAtomIn(body, a, b, site);

    // SPEC 15.3.6, 15.6.5: the body's fixed-fixed oracle, extracted at its root (a layer's at its struct); disposed after use.
    internal Binding.UniversalRelations UniversalRelations(ExecutableBody body) => binding.UniversalRelationsOf(body);

    // The layer units (a struct's Field initializers) that have rows, in formation order; MirOrigins.Findings locates G215's limit at
    // those no constructor's slice includes (DECISIONS R4-M168).
    internal IReadOnlyList<ExecutableBody> OriginLayers => binding.OriginLayers;
}
