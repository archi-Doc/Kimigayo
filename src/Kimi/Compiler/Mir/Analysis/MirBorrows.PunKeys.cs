// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler.Mir;

// A5's pun rule, its Place keys (PLAN G667b; contract in MirBorrows.Puns.cs; SPEC 5.7 Calls). Responsibility: Binding's facts at a
// Place's key (TyCtxt.OwnLayers), folded once per program version (R4-M140): whether a raw layer is at the Place's own address, what
// its layers state (Stated; AnyStated, which A5's storing rule reads at a store's destination, MirBorrows.Storing), the Types a write through
// them writes and the keys past them, each past key with the class of its layer (PastLayer), a hidden layer (OwnLayer.Hidden) with neither, and
// whether every part there is hidden (WhollyHidden: a value that adds no reach of its own);
// whether W reads a holder at the key by the declared step (Declared: a part there is no layer the walked Type expression writes);
// a Place's key (KeyOf: TyCtxt.Select, TyCtxt.Base along its steps), an entry's once per run; a call input's key and the result's (InputKey);
// the call walk's gate (Below). Must not: decide a pun (MirBorrows.PunTypes' judge).
internal sealed partial class MirBorrows
{
    private readonly Dictionary<(PlaceKey Key, int Fold), int> punKeyIndex = []; // The version's keys, indexed by key and fold.
    private readonly List<BoundType[]> punFolds = [[]]; // The Handed lists keys are folded under, by fold; 0 the empty one. Kept across versions (a list is its content), so a warm run allocates none.
    private readonly List<PunKey> punKeys = []; // By index: a key's folded facts.
    private readonly List<BoundType> punViewTypes = []; // The keys' write Types (PunKey's spans).
    private readonly List<OwnLayer> punLayers = []; // OwnLayers' scratch.
    private readonly List<PlaceKey> punPastKeys = []; // The keys past each key's writable entries (PunKey's spans).
    private readonly List<PastLayer> punPastLayers = []; // Beside punPastKeys: the class of the layer each lies past (PastLayer).
    private readonly List<int> punWalk = []; // Below's walk: key indices.
    private int[] entryKeys = []; // Per run: an entry's key index plus one (0: not yet read).
    private int punRun = -1; // The run whose scans and entry keys the tables hold.
    private ulong punVersion; // The program version whose keys punKeys answers.
    private int punStamp; // Below's walks, which mark the keys they reach.

    // The class of the layer a key's past entry lies past (Key; SPEC 5.7 Calls, 15.6.4): a raw layer at the Place's own address stating no
    // Origin that the walked Type expression writes (Its: HoldContent.Its at HoldStep.Address) or that a declaration states (Any:
    // TyCtxt.RawStated, HoldContent.Any), a raw layer with any other step (Stated: an Origin, `static`, an undecided step), or an
    // exclusive borrow (Borrow).
    private enum PastLayer : byte
    {
        Its,
        Any,
        Stated,
        Borrow,
    }

    // Whether a fold's list holds `kept`: OwnLayer.Kept and OutputRetention.Handed name a kept Type by the one interned
    // erased Type (TyCtxt.Erase), compared by identity.
    private static bool Lists(ReadOnlySpan<BoundType> list, BoundType kept)
    {
        foreach (var type in list)
        {
            if (ReferenceEquals(type, kept))
            {
                return true;
            }
        }

        return false;
    }

    // Whether two Handed lists are one (Fold), by identity.
    private static bool Same(ReadOnlySpan<BoundType> list, ReadOnlySpan<BoundType> other)
    {
        for (var i = 0; list.Length == other.Length && i < list.Length; i++)
        {
            if (!ReferenceEquals(list[i], other[i]))
            {
                return false;
            }
        }

        return list.Length == other.Length;
    }

    // The per-run tables: scans and entry keys belong to one run; keys to one program version.
    private void PunRun()
    {
        if (this.punVersion != this.program.Version)
        {
            (this.punVersion, this.punRun) = (this.program.Version, -1);
            this.punKeyIndex.Clear();
            this.punKeys.Clear();
            this.punViewTypes.Clear();
            this.punPastKeys.Clear();
            this.punPastLayers.Clear();
        }

        if (this.punRun != this.run)
        {
            this.punRun = this.run;
            this.pastScans.Clear();
            this.pastPool.Clear();
            MirDataflow.Grow(ref this.entryKeys, this.loanCount + this.addressCount);
            Array.Clear(this.entryKeys);
        }
    }

    // The key index of the Place entry `entry` names: its root's key, stepped along the Place's steps (TyCtxt.Select, TyCtxt.Base).
    private int EntryKey(int entry)
    {
        this.PunRun();
        if (this.entryKeys[entry] > 0)
        {
            return this.entryKeys[entry] - 1;
        }

        var loan = this.loans[entry];
        var index = this.Key(this.KeyOf(loan.Place.Root, this.loanSteps.AsSpan(loan.Start, loan.Count)), 0);
        this.entryKeys[entry] = index + 1;
        return index;
    }

    // A Place's key: its root's, stepped along the Place's steps (TyCtxt.Select, TyCtxt.Base).
    private PlaceKey KeyOf(int root, ReadOnlySpan<MirStep> steps)
    {
        var key = PlaceKey.Root(this.body.TypeOf(new MirPlace(root))!);
        foreach (var step in steps)
        {
            key = step.Kind == MirProjectionKind.Base ? types.Base(key) : types.Select(key, step.Arg);
        }

        return key;
    }

    // A call input's key, read by W and the pun rule alike (SPEC 15.6.4, 5.7 Calls, 8.2.3; PLAN G823), and the result's at the result slot
    // (CallRetention.ResultOutput; PLAN G841 (3)): a called function's parameter key (TyCtxt.ParameterKey; at the result, the Type its
    // declaration writes for the result, Binding's SignatureResult), at which OwnLayers marks Hidden the layers its own Type parameters'
    // bindings hold; the root key of the input or result for any other callee (a value, an intrinsic).
    private PlaceKey InputKey(in MirTerminator call, int position, BoundType input)
        => call.Callee is { Kind: MirCalleeKind.Fn, Symbol: { Declaration: Parsing.FunctionKoto } function } && position < call.Count
            ? types.ParameterKey(function, position < 0 ? position : MirCallee.Slot(function, position), input) : PlaceKey.Root(input);

    // A key's facts, folded from TyCtxt.OwnLayers once per program version: whether a raw layer (or an abstract part) is at the Place's own
    // address; whether every raw layer there states an Origin other than `static` (Own, Shared, Exclusive), and whether one does
    // (SomeStated: with Stated false, the layers are partly stated, PunKey.PartlyStated); whether any states an Origin, `static` included
    // (AnyStated, the storing rule's); whether one has no answer (Unsupported); the write Types (the Part past each raw
    // layer and each exclusive borrow) and whether an abstract part leaves them unknown. A key at Binding's Limit is unknown.
    // Under `fold` (Fold: an output's Handed list), a hidden layer whose kept Type the list holds is folded as shown.
    private int Key(in PlaceKey key, int fold)
    {
        this.PunRun();
        if (this.punKeyIndex.TryGetValue((key, fold), out var index))
        {
            return index;
        }

        this.punLayers.Clear();
        var complete = types.OwnLayers(key, this.punLayers);
        var facts = new PunKey { RawInline = !complete, Stated = complete, FirstUnknown = !complete, Declared = !complete, Shown = !complete };
        var start = this.punViewTypes.Count;
        var pastStart = this.punPastKeys.Count;
        foreach (var own in this.punLayers)
        {
            var (layer, step, past) = own;

            // What W reads layer by layer (SPEC 5.7 Calls: a layer the walked Type expression writes bounds a store to its pointee): a raw
            // layer of class Its and a borrow; any other part keeps the declared step.
            var at = layer is null ? PastLayer.Borrow : step != HoldStep.Address ? PastLayer.Stated : TyCtxt.RawStated(layer) ? PastLayer.Any : PastLayer.Its;
            var its = layer is not null ? at == PastLayer.Its : step is HoldStep.Exclusive or HoldStep.Shared;
            facts = facts with { Declared = facts.Declared || !its };
            if (layer is not null)
            {
                var origin = step is HoldStep.Own or HoldStep.Shared or HoldStep.Exclusive; // A stated Origin other than `static`.
                facts = facts with
                {
                    RawInline = true, Stated = facts.Stated && origin, AnyStated = facts.AnyStated || origin || step == HoldStep.Static,
                    SomeStated = facts.SomeStated || origin, Unsupported = facts.Unsupported || step == HoldStep.Unsupported,
                };
            }
            else if (step is HoldStep.Abstract or HoldStep.Erased)
            {
                facts = facts with { RawInline = true, Stated = false, FirstUnknown = true };
            }

            // A layer hidden in a called function's own Type parameter's binding adds no reach of its own (SPEC 15.6.4; OwnLayer.Hidden at
            // a parameter key, TyCtxt.ParameterKey): no write Type, no key past it, unless a callable the callee's declaration
            // hands its body writes that layer's kept Type (the fold's Handed list holds OwnLayer.Kept; SPEC 15.6.4 "through its result or
            // its writes"): then the layer is read as shown (hidRawWTwoBad, handedSameBad, hidPunWBad), and another kept Type's stays hidden
            // (handedOtherOk).
            var hidden = own.Hidden && !Lists(this.punFolds[fold], own.Kept!);
            facts = hidden ? facts with { Hidden = true } : facts with { Shown = true };
            if ((layer is not null || step == HoldStep.Exclusive) && past.Type is { } to && !hidden && !own.Loan)
            {
                this.punViewTypes.Add(cases.Type(to));
                this.punPastKeys.Add(past);
                this.punPastLayers.Add(at);
            }
        }

        index = this.punKeys.Count;
        this.punKeys.Add(facts with
        {
            FirstStart = start, FirstCount = this.punViewTypes.Count - start, PastStart = pastStart, PastCount = this.punPastKeys.Count - pastStart,
            Fold = fold,
        });
        this.punKeyIndex.Add((key, fold), index);
        return index;
    }

    // The fold of a call's output, the one read of OutputRetention.Handed (the kept Types a callable the callee's
    // declaration hands its body writes; SPEC 15.6.4 "through its result or its writes"): 0 for none, else the list's index in
    // punFolds, equal lists one fold. W's walk of the output and the pun rule at the output's input both read it (Keep, PunCall).
    private int Fold(OutputRetention? output)
    {
        this.PunRun();
        var handed = output is null ? default : output.Handed;
        if (handed.IsEmpty)
        {
            return 0;
        }

        for (var fold = 1; fold < this.punFolds.Count; fold++)
        {
            if (Same(handed, this.punFolds[fold]))
            {
                return fold;
            }
        }

        this.punFolds.Add(handed.ToArray());
        return this.punFolds.Count - 1;
    }

    // A key's write Types (its first step's).
    private ReadOnlySpan<BoundType> Writes(int index)
        => CollectionsMarshal.AsSpan(this.punViewTypes).Slice(this.punKeys[index].FirstStart, this.punKeys[index].FirstCount);

    // Whether a write the key's layers lead to, transitively, may write a Scalar or is unknown (PunCall's gate), once per key and version:
    // a walk over the keys' memoized facts by index (each key once per walk, marked by the walk's stamp), no second OwnLayers read.
    private bool Below(int index)
    {
        if (this.punKeys[index].Below != 0)
        {
            return this.punKeys[index].Below == 2;
        }

        var stamp = ++this.punStamp;
        var below = false;
        this.punWalk.Clear();
        this.punWalk.Add(index);
        this.punKeys[index] = this.punKeys[index] with { Stamp = stamp };
        for (var i = 0; !below && i < this.punWalk.Count; i++)
        {
            var at = this.punKeys[this.punWalk[i]];
            below = at.FirstUnknown || this.AnyScalar(at.FirstStart, at.FirstCount);
            for (var p = at.PastStart; !below && p < at.PastStart + at.PastCount; p++)
            {
                var next = this.Key(this.punPastKeys[p], at.Fold);
                if (this.punKeys[next].Stamp != stamp)
                {
                    this.punKeys[next] = this.punKeys[next] with { Stamp = stamp };
                    this.punWalk.Add(next);
                }
            }
        }

        this.punKeys[index] = this.punKeys[index] with { Below = (byte)(below ? 2 : 1) };
        return below;
    }

    private bool AnyScalar(int start, int count)
    {
        for (var i = start; i < start + count; i++)
        {
            if (this.Scalared(this.punViewTypes[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A key's facts (MirBorrows.Puns; AnyStated: MirBorrows.Storing).</summary>
    private readonly record struct PunKey
    {
        internal bool RawInline { get; init; }

        internal bool Stated { get; init; }

        internal bool AnyStated { get; init; }

        internal bool SomeStated { get; init; } // A raw layer at the key states an Origin other than `static` (Key).

        // Whether a raw layer at the key states an Origin other than `static` while another raw layer or an unknown part there does not: a
        // placed pun's verdict differs by the layer the layout lands it at (MirBorrows.Puns' PlacedAt).
        internal bool PartlyStated => this.SomeStated && !this.Stated;

        internal bool Unsupported { get; init; }

        internal int FirstStart { get; init; }

        internal int FirstCount { get; init; }

        internal bool FirstUnknown { get; init; }

        internal bool Hidden { get; init; } // A part hidden in a called function's own Type parameter's binding (OwnLayer.Hidden).

        internal bool Shown { get; init; } // A part not hidden there, or a part not known (the layers not all known).

        // Whether every part the value at the key holds lies in a called function's own Type parameter's binding, so that the value adds
        // no reach of its own and, as storage, holds an argument's own value only where the premises make it one (SPEC 15.6.4; DECISIONS
        // BU-202; the owner's O-353 (a)).
        internal bool WhollyHidden => this.Hidden && !this.Shown;

        // Whether W reads a holder at the key by the declared step, not layer by layer (MirBorrows.Reach): a part there is neither a raw
        // layer the walked Type expression writes without an Origin nor a borrow (a declaration's own layer, a layer stating an Origin,
        // an own step, an abstract or unbound part), or the layers are not all known.
        internal bool Declared { get; init; }

        internal byte Below { get; init; } // 0 not computed, 1 false, 2 true: whether a write past the key may write a Scalar or is unknown.

        internal int PastStart { get; init; }

        internal int PastCount { get; init; }

        internal int Stamp { get; init; } // The last Below walk that reached the key.

        internal int Fold { get; init; } // The fold the key was folded under (Fold); its past keys are folded under it too.
    }
}
