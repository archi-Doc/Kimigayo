// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns A5's Place steps (SPEC 15.6.2, 15.6.3; design A3 3, E13 (4), E17; DECISIONS R4-M94 D12, R4-M100): a Place's projections under the
// selected view as classed steps, root first. MirBorrows reads them now (it compares them by prefix and walks them for reads and stores),
// and MirOrigins from 2a-1a (DECISIONS R4-M94 D12).
// Input: a Place of a validated body (its projections, Types and the roles of its locals) and the selected view (MirCases). Output: Walk's
// steps, each with its projection kind and argument, its MirStepClass and its Prefix, the projection id of the Place the step applies to (0 at
// the root; the whole Place for a Reborrow's added Deref), whose case Type a Deref's class is read from; and Prepared, whether a root is a
// default template's PreparedInput.
// Invariants: a Base of depth d is d steps and an owner-case pair layer none; a Deref or another pair layer is classed by the Semantics of
// the Type it applies to; a default template's Deref directly on a PreparedInput is the caller's pending slot itself, Inline (E17). The
// scratch is the projection chain, reset by counts.
// Must not read MirBorrows' state, syntax or HIR, judge Origins or conflicts, intern projections, or copy the body per view.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

/// <summary>The class of a Place's step (A3 3): an inline step, a runtime index into inline storage, an owning handle's payload, a
/// shared or exclusive borrow Deref (a Slice's index is a shared one, E6), or a raw pointer's referent.</summary>
internal enum MirStepClass : byte
{
    Inline,
    Index,
    Own,
    Shared,
    Uniq,
    Raw,
}

/// <summary>A step of a Place: its projection kind and argument (a Base of depth 1), its class and its prefix.</summary>
/// <param name="Kind">The projection kind (a pair layer that is a Deref is one).</param>
/// <param name="Class">The step's class.</param>
/// <param name="Arg">The projection's argument.</param>
/// <param name="Prefix">The projection id of the Place prefix the step applies to: 0 at the root, the whole Place for a Reborrow's added
/// Deref, and for each step of a Base of depth d the Place before the Base.</param>
internal readonly record struct MirStep(MirProjectionKind Kind, MirStepClass Class, int Arg, int Prefix);

/// <summary>The steps of a Place under the selected view of <paramref name="cases"/> (A3 3).</summary>
/// <param name="cases">The Semantics case views, a view of the body selected.</param>
internal sealed class MirSteps(MirCases cases)
{
    private int[] chain = []; // A Place's projections, last first.

    /// <summary>Gets whether a root is a local whose role is PreparedInput, a default template's prepared input.</summary>
    /// <param name="body">The body.</param>
    /// <param name="root">The root: a local; a negative root (a static) or an index past the body's locals is not one.</param>
    /// <returns>Whether the root is a local whose role is PreparedInput.</returns>
    internal static bool Prepared(MirBody body, int root) => (uint)root < (uint)body.Locals.Length && body.Locals[root].Role == MirLocalRole.PreparedInput;

    /// <summary>Walks the steps of a Place under the view, root first (A3 3): a Base of depth d is d steps, an owner-case pair layer is none
    /// (SPEC 13.5.5) and another a Deref, the step a borrow Deref of its case is (A3 E13 (4), review N3), a default template's Deref
    /// directly on a PreparedInput is the caller's pending slot itself, Inline (A3 E17, SPEC 7.2.3; MirBorrows.Defaults), and a Reborrow
    /// adds its referent's exclusive Deref.</summary>
    /// <param name="body">The body of the Place.</param>
    /// <param name="place">The Place.</param>
    /// <param name="reborrow">Whether the Place is a Reborrow's, which adds its referent's exclusive Deref.</param>
    /// <param name="into">The steps, grown as needed.</param>
    /// <returns>The number of steps.</returns>
    internal int Walk(MirBody body, MirPlace place, bool reborrow, ref MirStep[] into)
    {
        var projections = body.Projections;
        var length = 0;
        for (var id = place.Projection; id > 0; id = projections[id].Prefix)
        {
            MirDataflow.Grow(ref this.chain, length + 1);
            this.chain[length++] = id;
        }

        var count = 0;
        for (var i = length - 1; i >= 0; i--)
        {
            var (id, row) = (this.chain[i], projections[this.chain[i]]);
            var prefix = place with { Projection = row.Prefix };
            var @class = row.Kind switch
            {
                MirProjectionKind.Deref => row.Prefix == 0 && Prepared(body, place.Root)
                    ? MirStepClass.Inline : Deref(cases.Type(body.TypeOf(prefix)!).Semantics),
                MirProjectionKind.PairLayer => cases.Layer(place with { Projection = id }) is { } layer ? Deref(layer.Semantics) : (MirStepClass?)null,
                MirProjectionKind.Index => MirTypes.View(cases.Type(body.TypeOf(prefix)!)) is { } view ? Deref(view) : MirStepClass.Index,
                _ => MirStepClass.Inline,
            };

            for (var depth = row.Kind == MirProjectionKind.Base ? row.Arg : 1; @class is { } known && depth > 0; depth--)
            {
                MirDataflow.Grow(ref into, count + 2);
                var kind = row.Kind == MirProjectionKind.PairLayer ? MirProjectionKind.Deref : row.Kind;
                into[count++] = new(kind, known, row.Kind == MirProjectionKind.Base ? 1 : row.Arg, row.Prefix);
            }
        }

        MirDataflow.Grow(ref into, count + 1);
        if (reborrow)
        {
            into[count++] = new(MirProjectionKind.Deref, MirStepClass.Uniq, 0, place.Projection);
        }

        return count;
    }

    // A Deref's class by the Semantics of the Type it applies to: a shared or exclusive borrow, an owning handle's payload, or a raw
    // pointer's referent.
    private static MirStepClass Deref(SemanticsKind semantics) => semantics switch
    {
        SemanticsKind.Ref or SemanticsKind.ObjRef => MirStepClass.Shared,
        SemanticsKind.Uniq or SemanticsKind.ObjUniq => MirStepClass.Uniq,
        SemanticsKind.Raw => MirStepClass.Raw,
        _ => MirStepClass.Own,
    };
}
