// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the C1-F4 view (SPEC 15.6.1, 15.6.5; DECISIONS R4-M94, R4-M101): a body's Origin rows and atom table, an atom's class in a body, and
// the body's fixed-fixed oracle, which A5 judges with (MirOrigins); Binding.OriginRows.cs computes them.
// Input: a body as an ExecutableBody (MIR's key form; a Getter's or Setter's execution function names its accessor), after Bind.
// Output: OriginRows, OriginAtom, UniversalRelations and OriginFindings, the broken contracts the view's validation found.
// Invariants: built on a pass's first read and immutable after Bind but for the atoms MIR appends; every end and atom resolved at its use
// (OriginAtUse) and meets canonical (Meet); no row from a Constraint proof (T1); a row formed with no body (a declaration's) is in no slice;
// a constructor's slice is its own rows, then its layer's (the struct's Field initializers), which share only Fixed atoms with them.
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

/// <summary>SPEC 15.6.5's kinds of an Origin relative to the checked body (2a0-D D6), and Unclassed, which forms no edge (§3 row 9: an
/// Unbound end or another declaration's or body's atom, a located limit).</summary>
internal enum OriginAtomKind : byte
{
    Fixed,
    Finite,
    Inferred,
    Unclassed,
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

/// <summary>One relation a body checks (2a0-D D2): its ends index the body's atom table; <c>CaseMask</c> is the Semantics case it is required
/// in, as bits of the body's pair binders (<c>MirBody.Binders</c>).</summary>
internal readonly record struct OriginRow(
    Koto Site,
    int Longer,
    int Shorter,
    bool Equality,
    OriginRowSource Source,
    Koto? Clause,
    BoundType? Destination,
    ulong CaseMask);

/// <summary>An atom of a body's table with its class there.</summary>
internal readonly record struct OriginAtomEntry(BoundOrigin Atom, OriginAtomKind Kind, ProjectionRole Role);

/// <summary>A body's slice of the view: its rows, a constructor's layer rows from <see cref="LayerStart"/>, and its atom table.</summary>
internal sealed class OriginRowSet
{
    internal ExecutableBody Body { get; private set; }

    /// <summary>Gets the layer whose rows follow <see cref="LayerStart"/>, judged with its oracle (DECISIONS R4-M101 E4); no body when none.</summary>
    internal ExecutableBody Layer { get; private set; }

    internal int LayerStart { get; set; }

    internal IReadOnlyList<OriginRow> Rows => this.RowList;

    internal IReadOnlyList<OriginAtomEntry> Atoms => this.AtomList;

    internal List<OriginRow> RowList { get; } = [];

    internal List<OriginAtomEntry> AtomList { get; } = [];

    // Each atom's table index, and the parts (1 own, 2 layer) whose rows name it.
    internal Dictionary<BoundOrigin, int> Index { get; } = new(ReferenceEqualityComparer.Instance);

    internal List<byte> Parts { get; } = [];

    internal bool Owns(ExecutableBody body) => body.IsBody && (body == this.Body || body == this.Layer);

    internal void Reset(ExecutableBody body, ExecutableBody layer)
    {
        (this.Body, this.Layer, this.LayerStart) = (body, layer, 0);
        this.RowList.Clear();
        this.AtomList.Clear();
        this.Index.Clear();
        this.Parts.Clear();
    }
}

internal sealed partial class TyCtxt
{
    // SPEC 15.6.1: a body's rows and atom table, a constructor's followed by its layer's.
    internal OriginRowSet OriginRows(ExecutableBody body) => binding.OriginRows(body);

    // SPEC 15.6.5: an atom's class in a body, resolved at its use and appended to the body's table when no row names it.
    internal OriginAtomEntry OriginAtom(ExecutableBody body, BoundOrigin origin, Koto site) => binding.OriginAtomIn(body, origin, site);

    // SPEC 15.3.6, 15.6.5: the body's fixed-fixed oracle, extracted at its root (a layer's at its struct); disposed after use.
    internal Binding.UniversalRelations UniversalRelations(ExecutableBody body) => binding.UniversalRelationsOf(body);

    // The sites whose rows or atoms broke the view's contract in a complete compilation, each an InternalInvariant_Kd.
    internal IReadOnlyList<Koto> OriginFindings => binding.OriginFindings;
}
