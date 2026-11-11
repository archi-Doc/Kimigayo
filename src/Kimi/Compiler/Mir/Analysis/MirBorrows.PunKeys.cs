// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler.Mir;

// A5's pun rule, its Place keys (PLAN G667b; contract in MirBorrows.Puns.cs; SPEC 5.7 Calls). Responsibility: Binding's facts at a
// Place's key (TyCtxt.OwnLayers), folded once per program version (R4-M140): whether a raw layer is at the Place's own address, what
// its layers state, the Types a write through them writes and the keys past them; an entry's key (TyCtxt.Select, TyCtxt.Base along
// its Place's steps) once per run; the call walk's gate (Below). Must not: decide a pun (MirBorrows.PunTypes' judge).
internal sealed partial class MirBorrows
{
    private readonly Dictionary<PlaceKey, int> punKeyIndex = []; // The version's keys, indexed.
    private readonly List<PunKey> punKeys = []; // By index: a key's folded facts.
    private readonly List<BoundType> punViewTypes = []; // The keys' write Types (PunKey's spans).
    private readonly List<OwnLayer> punLayers = []; // OwnLayers' scratch.
    private readonly List<PlaceKey> punPastKeys = []; // The keys past each key's writable entries (PunKey's spans).
    private readonly List<int> punWalk = []; // Below's walk: key indices.
    private int[] entryKeys = []; // Per run: an entry's key index plus one (0: not yet read).
    private int punRun = -1; // The run whose scans and entry keys the tables hold.
    private ulong punVersion; // The program version whose keys punKeys answers.
    private int punStamp; // Below's walks, which mark the keys they reach.

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
        var key = PlaceKey.Root(this.body.TypeOf(new MirPlace(loan.Place.Root))!);
        for (var s = loan.Start; s < loan.Start + loan.Count; s++)
        {
            key = this.loanSteps[s].Kind == MirProjectionKind.Base ? types.Base(key) : types.Select(key, this.loanSteps[s].Arg);
        }

        var index = this.Key(key);
        this.entryKeys[entry] = index + 1;
        return index;
    }

    // A key's facts, folded from TyCtxt.OwnLayers once per program version: whether a raw layer (or an abstract part) is at the Place's own
    // address; whether every raw layer there states an Origin other than `static` (Own, Shared, Exclusive); whether one has no answer
    // (Unsupported); the write Types (the Part past each raw layer and each exclusive borrow) and whether an abstract part leaves them
    // unknown. A key at Binding's Limit is unknown.
    private int Key(PlaceKey key)
    {
        this.PunRun();
        if (this.punKeyIndex.TryGetValue(key, out var index))
        {
            return index;
        }

        this.punLayers.Clear();
        var complete = types.OwnLayers(key, this.punLayers);
        var facts = new PunKey { RawInline = !complete, Stated = complete, FirstUnknown = !complete };
        var start = this.punViewTypes.Count;
        var pastStart = this.punPastKeys.Count;
        foreach (var (layer, step, past) in this.punLayers)
        {
            if (layer is not null)
            {
                var stated = facts.Stated && step is HoldStep.Own or HoldStep.Shared or HoldStep.Exclusive;
                facts = facts with { RawInline = true, Stated = stated, Unsupported = facts.Unsupported || step == HoldStep.Unsupported };
            }
            else if (step is HoldStep.Abstract or HoldStep.Erased)
            {
                facts = facts with { RawInline = true, Stated = false, FirstUnknown = true };
            }

            if ((layer is not null || step == HoldStep.Exclusive) && past.Type is { } to)
            {
                this.punViewTypes.Add(cases.Type(to));
                this.punPastKeys.Add(past);
            }
        }

        index = this.punKeys.Count;
        this.punKeys.Add(facts with
        {
            FirstStart = start, FirstCount = this.punViewTypes.Count - start, PastStart = pastStart, PastCount = this.punPastKeys.Count - pastStart,
        });
        this.punKeyIndex.Add(key, index);
        return index;
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
                var next = this.Key(this.punPastKeys[p]);
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

    /// <summary>A key's pun facts (MirBorrows.Puns).</summary>
    private readonly record struct PunKey
    {
        internal bool RawInline { get; init; }

        internal bool Stated { get; init; }

        internal bool Unsupported { get; init; }

        internal int FirstStart { get; init; }

        internal int FirstCount { get; init; }

        internal bool FirstUnknown { get; init; }

        internal byte Below { get; init; } // 0 not computed, 1 false, 2 true: whether a write past the key may write a Scalar or is unknown.

        internal int PastStart { get; init; }

        internal int PastCount { get; init; }

        internal int Stamp { get; init; } // The last Below walk that reached the key.
    }
}
