// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// A5's Place steps (A3 3): a Place's projections under the view as classed steps, root first, which Loans and accesses compare by
// prefix and reads and stores walk.
internal sealed partial class MirBorrows
{
    private Step[] walk = []; // A Place's steps.
    private int[] chain = []; // A Place's projections, last first.

    /// <summary>The class of a Place's step (A3 3): an inline step, a runtime index into inline storage, an owning handle's payload, a
    /// shared or exclusive borrow Deref (a Slice's index is a shared one, E6), or a raw pointer's referent.</summary>
    private enum StepClass : byte
    {
        Inline,
        Index,
        Own,
        Shared,
        Uniq,
        Raw,
    }

    // A Deref's class by the Semantics of the Type it applies to: a shared or exclusive borrow, an owning handle's payload, or a raw
    // pointer's referent.
    private static StepClass Deref(SemanticsKind semantics) => semantics switch
    {
        SemanticsKind.Ref or SemanticsKind.ObjRef => StepClass.Shared,
        SemanticsKind.Uniq or SemanticsKind.ObjUniq => StepClass.Uniq,
        SemanticsKind.Raw => StepClass.Raw,
        _ => StepClass.Own,
    };

    // The steps of a Place under the view, root first (A3 3): a Base of depth d is d steps, an owner-case pair layer is none (SPEC 13.5.5)
    // and another a Deref, the step a borrow Deref of its case is (A3 E13 (4), review N3), and a Reborrow adds its referent's exclusive
    // Deref; returns their number.
    private int Walk(MirPlace place, bool reborrow, ref Step[] into)
    {
        var projections = this.body.Projections;
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
                MirProjectionKind.Deref => Deref(cases.Type(this.body.TypeOf(prefix)!).Semantics),
                MirProjectionKind.PairLayer => cases.Layer(place with { Projection = id }) is { } layer ? Deref(layer.Semantics) : (StepClass?)null,
                MirProjectionKind.Index => MirTypes.View(cases.Type(this.body.TypeOf(prefix)!)) is { } view ? Deref(view) : StepClass.Index,
                _ => StepClass.Inline,
            };

            for (var depth = row.Kind == MirProjectionKind.Base ? row.Arg : 1; @class is { } known && depth > 0; depth--)
            {
                MirDataflow.Grow(ref into, count + 2);
                var kind = row.Kind == MirProjectionKind.PairLayer ? MirProjectionKind.Deref : row.Kind;
                into[count++] = new(kind, row.Kind == MirProjectionKind.Base ? 1 : row.Arg, known);
            }
        }

        MirDataflow.Grow(ref into, count + 1);
        if (reborrow)
        {
            into[count++] = new(MirProjectionKind.Deref, 0, StepClass.Uniq);
        }

        return count;
    }

    // Whether a Place passes a borrow or raw Deref, so writing it uses its root.
    private bool Through(MirPlace place)
    {
        var count = this.Walk(place, reborrow: false, ref this.walk);
        for (var i = 0; i < count; i++)
        {
            if (this.walk[i].Class is StepClass.Shared or StepClass.Uniq or StepClass.Raw)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A step of a Place: its projection kind and argument (a Base of depth 1) and its class.</summary>
    private readonly record struct Step(MirProjectionKind Kind, int Arg, StepClass Class);
}
