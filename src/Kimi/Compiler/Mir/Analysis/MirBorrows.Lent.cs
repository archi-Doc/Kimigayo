// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's exact lent positions (SPEC 15.6.3: a Reborrow carries its parent's Loans and lends a part of its referent; 15.6 Addresses and 5.7
// Reading a Field through a dereference: a read states the Origin of the last borrow or raw pointer dereferenced; Z5-F, the owner's O-482
// (a)): the storage a Place denotes, where the body forms it exactly, as a position, a root key (Key) and a run of steps that passes no
// borrow or raw Deref, for a Place of the view (Lent). A Place that passes no borrow or raw Deref is its own position. Past one, its
// position is the lent position of the borrow whose reference the last such Deref goes through (Lender), followed by the steps after
// that Deref; that borrow is found by the stores of the reference's root (MirBorrows.Loans' store
// record, the one writer of a local's stores): its one store, through whole Copies and Moves and a Tuple's or fixed array's operands.
// There is none (-1) where no one borrow forms the reference: an argument's, a call's result, a local stored twice, into a part or
// written through a pointer (ExposedHolder; an own-step entry's OwnerExposed is not read), a reference reached past a Deref, a runtime
// Index or a Downcast, a Cast, an Adt or Fill aggregate.
// A position's steps keep their kinds, classes, arguments and prefixes; a resolved step's Prefix is its own Place's, whose Type it reads.
// Each step keeps the body Place it applies to (LentPlace; G1276, E-a): the Place the position's steps before it name from the position's
// root (the root for the first step; for a later step, its Prefix row where that row continues the previous step's Place, so named, by
// the previous step); else its Prefix row from the root of the walk that gave it: the position's root in the first segment, else the
// root of the walk past whose Deref the step follows (a lender's or the Place's own).
// Inputs: the view's body, the Loans' store record and Exposed, final once the Addresses pre-pass has run (it completes Exposed with the
// holders a direct address names), and MirSteps' walk. Precondition: no Lent before the view's Addresses pre-pass has walked its uses
// (its Place asks every position after the walk and before any own-step entry it makes).
// Cost: positions are made on demand, the first time a view asks (none in a view that asks none), and kept per view: a read's by its Place,
// a borrow's by its location and a whole local's lender by the local, so each store is followed and each position's run copied once per
// view; the lender chain is a loop, not a recursion. Each store followed and each step copied is Work. Lent resolves in its own step
// buffer, so the one step walk may ask it. Readers: MirBorrows.Addresses' Place (R-C: LentRun, LentPlace); MirBorrows.Puns' PunRead
// (LentExactly).
// Must not issue a Loan, carry a value, record a problem, intern projections or compare Types.
internal sealed partial class MirBorrows
{
    private const int Unmade = -2; // lentAt, lenderOf: not made yet.

    private readonly MirTable<(int Root, int Projection)> lentOf = new(); // By a Place asked: its exact lent position, -1 for none.
    private readonly List<(int Root, int Start, int Count)> lentRuns = []; // The view's positions, each a root key and a run of lentSteps.
    private readonly List<(int Start, int Count, int Location, int Root)> lending = []; // Resolve: each Deref passed, its suffix, its lender to keep and the suffix's root.
    private readonly List<int> lenderSteps = []; // Lender: the projections still to take from the Place followed, the next one last.
    private readonly List<int> lenderLocals = []; // Lender: the whole locals passed with no step left, whose lender the walk's is.
    private MirStep[] lentSteps = [];
    private MirPlace[] lentPlaces = []; // By lentSteps index: the body Place the step applies to (LentPlace).
    private MirStep[] lentWalk = []; // Lent's own step buffer (Resolved).
    private MirStep[] suffixes = []; // Resolve: the steps after each Deref passed.
    private int[] lentAt = []; // By location: a borrow's exact lent position, -1 for none, or Unmade.
    private int[] lenderOf = []; // By local: the borrow its whole reference holds (Lender), -1 for none, or Unmade.
    private int lentStepCount;
    private bool lentBegun; // Whether the view's positions are begun (Lend), at its first Lent.

    /// <summary>Gets a Place's exact lent position (see <see cref="LentRun"/>), -1 for none; after the view's Addresses pre-pass.</summary>
    /// <param name="place">A Place of the view's body.</param>
    /// <returns>The position.</returns>
    internal int Lent(MirPlace place)
    {
        this.Lend();
        if (this.lentOf.TryGetValue((place.Root, place.Projection), out var known))
        {
            return known;
        }

        var lent = this.Resolved(place, reborrow: false);
        this.lentOf.Add((place.Root, place.Projection), lent);
        return lent;
    }

    /// <summary>Gets a position's steps and root key; the body Place each step applies to is <see cref="LentPlace"/>'s.</summary>
    /// <param name="position">A position of the view (<see cref="Lent"/>).</param>
    /// <param name="root">Its root key (Key).</param>
    /// <returns>Its steps, which pass no borrow or raw Deref.</returns>
    internal ReadOnlySpan<MirStep> LentRun(int position, out int root)
    {
        (root, var start, var count) = this.lentRuns[position];
        return this.lentSteps.AsSpan(start, count);
    }

    /// <summary>Gets the body Place a position's step applies to (Applied, decided once, when the position is made): the position's
    /// root for its first step; for a later step, its Prefix row from that root where the previous step's Place is the root's and that row
    /// is (that Place's projection, the previous step's Kind and Arg); else its Prefix row from the root of the walk that gave it.</summary>
    /// <param name="position">A position of the view (<see cref="Lent"/>).</param>
    /// <param name="step">The index of one of its steps.</param>
    /// <returns>The Place.</returns>
    internal MirPlace LentPlace(int position, int step) => this.lentPlaces[this.lentRuns[position].Start + step];

    // Forgets the view's positions, at the start of its Loans.
    private void Unlend() => this.lentBegun = false;

    // Begins the view's positions at its first Lent.
    private void Lend()
    {
        if (this.lentBegun)
        {
            return;
        }

        var (locals, locations) = (this.body.Locals.Length, this.body.Statements.Length + this.body.Blocks.Length);
        MirDataflow.Grow(ref this.lentAt, locations);
        MirDataflow.Grow(ref this.lenderOf, locals);
        Array.Fill(this.lentAt, Unmade, 0, locations);
        Array.Fill(this.lenderOf, Unmade, 0, locals);
        this.lentOf.Clear();
        this.lentRuns.Clear();
        (this.lentStepCount, this.lentBegun) = (0, true);
        this.Work += locals + locations;
    }

    // The exact position of the walk buffer's first `count` steps from a root, after the lenders `lending` lists: its root and steps where
    // they pass no borrow or raw Deref; else the lent position of the borrow whose reference the last one goes through, then the steps after
    // it; -1 for none. Each borrow passed is kept by its location, the deepest first.
    private int Resolve(int root, int count)
    {
        var (lent, suffixed) = (-1, 0);
        while (true)
        {
            var deref = this.LastDeref(count);
            if (deref < 0)
            {
                lent = this.Exact(this.Key(root), -1, this.walk.AsSpan(0, count), root);
                break;
            }

            var length = count - deref - 1;
            MirDataflow.Grow(ref this.suffixes, suffixed + length);
            this.walk.AsSpan(deref + 1, length).CopyTo(this.suffixes.AsSpan(suffixed));
            var location = this.Lender(new(root, this.walk[deref].Prefix));
            if (location < 0 || this.lentAt[location] != Unmade)
            {
                this.lending.Add((suffixed, length, -1, root));
                lent = location < 0 ? -1 : this.lentAt[location];
                break;
            }

            this.lending.Add((suffixed, length, location, root));
            (this.lentAt[location], suffixed) = (-1, suffixed + length); // Being made: a cycle of stores lends nothing exactly.
            count = this.Borrowed(location, out root);
        }

        for (var i = this.lending.Count - 1; i >= 0; i--)
        {
            var (start, length, location, from) = this.lending[i];
            if (location >= 0)
            {
                this.lentAt[location] = lent;
            }

            lent = lent < 0 || length == 0 ? lent : this.Exact(this.lentRuns[lent].Root, lent, this.suffixes.AsSpan(start, length), from);
        }

        return lent;
    }

    // Whether every Place a reference may refer to has an exact lent position: the Place the borrow it holds lends (Lender), as a
    // Reborrow of it lends, or, for a whole local stored more than once (not an argument: its initial value is the caller's), each
    // referring store's (MirBorrows.Loans' Referring): a Ref's Place, or the referent of the reference a Reborrow, Copy or Move reads.
    // Exact, it is a Place the body forms by Type-keeping steps alone (Lender follows no Cast, call or Adt), which its own Type
    // designates (MirBorrows.Puns' PunRead).
    private bool LentExactly(MirPlace reference)
    {
        this.Lend();
        if (this.Resolved(reference, reborrow: true) >= 0)
        {
            return true;
        }

        if (reference is not { Root: >= 0, Projection: 0 } || this.body.Locals[reference.Root].Kind == MirLocalKind.Arg ||
            !this.Referring(reference.Root, out var store) || store < 0)
        {
            return false;
        }

        for (; store >= 0; store = this.referStores[(2 * store) + 1])
        {
            ref readonly var statement = ref this.body.Statements[this.referStores[2 * store]];
            var rvalue = statement.Rvalue.Kind == MirRvalueKind.Adapt ? cases.Adapt(statement.Rvalue, out _) : statement.Rvalue;
            var lent = rvalue.Kind == MirRvalueKind.Ref
                ? this.Resolved(rvalue.Place, reborrow: false) : this.Resolved(this.body.Operands[rvalue.Start].Place, reborrow: true);
            if (lent < 0)
            {
                return false;
            }
        }

        return true;
    }

    // Resolve over a Place's walk in Lent's own step buffer, so that the one step walk's buffer stays as it is (PunRead asks inside it).
    private int Resolved(MirPlace place, bool reborrow)
    {
        (this.walk, this.lentWalk) = (this.lentWalk, this.walk);
        this.lending.Clear();
        var lent = this.Resolve(place.Root, this.placeSteps.Walk(place, reborrow, ref this.walk));
        (this.walk, this.lentWalk) = (this.lentWalk, this.walk);
        return lent;
    }

    // A borrow's Place, by its location, into the walk buffer: its Ref's or Storage's (a Storage's with its element step, AddressWalk), or
    // the Place a Reborrow acquires with its referent's Deref; the step count, and its root.
    private int Borrowed(int location, out int root)
    {
        ref readonly var statement = ref this.body.Statements[location];
        var rvalue = statement.Rvalue.Kind == MirRvalueKind.Adapt ? cases.Adapt(statement.Rvalue, out _) : statement.Rvalue;
        var reborrow = this.Reborrowing(statement) != MirRefKind.Raw;
        var place = reborrow ? this.body.Operands[rvalue.Start].Place : rvalue.Place;
        root = place.Root;
        return reborrow ? this.placeSteps.Walk(place, reborrow: true, ref this.walk) : this.AddressWalk(place, location);
    }

    // The borrow whose reference a Place holds, by its location (SPEC 15.6.3; 5.7 Reading a Field: a read through the Place states that
    // borrow's Origin): the Place's root has one store, into its whole Place, none into a part, and no pointer the body formed may write it
    // (ExposedHolder; an own-step entry's OwnerExposed is not read: it exposes its owner's storage, a struct's or Case's value or a
    // handle's payload, which no store followed here passes); that store is the borrow where it is a Ref or Storage (raw ones included) or a PairAcquire the view makes
    // a Reborrow and no step is left, and is followed where it Copies or Moves a Place (that Place, then the steps left) or is a Tuple's or
    // fixed array's aggregate (the operand at the next step's position: a Tuple element, or a fixed array's constant index, MirSteps'
    // ConstIndex reading of an Index row's Position; then the steps after it); -1 for any other store (a call's result, a Cast, an Adt or
    // Fill aggregate, a constant), for none (an argument's), or past the body's statements. A whole local passed with no step left keeps
    // the result.
    private int Lender(MirPlace place)
    {
        var (lender, stores) = (-1, this.body.Statements.Length);
        this.lenderSteps.Clear();
        this.lenderLocals.Clear();
        for (var hops = 0; hops <= stores; hops++)
        {
            if (place is { Root: >= 0, Projection: 0 } && this.lenderSteps.Count == 0)
            {
                if (this.lenderOf[place.Root] != Unmade)
                {
                    lender = this.lenderOf[place.Root];
                    break;
                }

                this.lenderOf[place.Root] = -1; // Being followed: a cycle of stores lends nothing exactly.
                this.lenderLocals.Add(place.Root);
            }

            for (var id = place.Projection; id > 0; id = this.body.Projections[id].Prefix)
            {
                this.lenderSteps.Add(id);
            }

            var first = place.Root >= 0 ? this.referFirst[place.Root] : -1;
            this.Work++;
            if (first < 0 || (this.named[place.Root] & (ExposedHolder | PartStore)) != 0 || this.referStores[(2 * first) + 1] >= 0 ||
                this.referStores[2 * first] >= stores)
            {
                break;
            }

            var location = this.referStores[2 * first];
            ref readonly var statement = ref this.body.Statements[location];
            var rvalue = statement.Rvalue.Kind == MirRvalueKind.Adapt ? cases.Adapt(statement.Rvalue, out _) : statement.Rvalue;
            if (rvalue.Kind is MirRvalueKind.Ref or MirRvalueKind.Storage || this.Reborrowing(statement) != MirRefKind.Raw)
            {
                lender = this.lenderSteps.Count == 0 ? location : -1;
                break;
            }

            var index = rvalue.Kind == MirRvalueKind.Use ? 0 : -1;
            if (rvalue.Kind == MirRvalueKind.Aggregate && (MirAggregateKind)rvalue.Op is MirAggregateKind.Tuple or MirAggregateKind.FixedArray && this.lenderSteps.Count > 0)
            {
                var step = this.body.Projections[this.lenderSteps[^1]];
                this.lenderSteps.RemoveAt(this.lenderSteps.Count - 1);
                var at = step.Kind == MirProjectionKind.Index ? step.Position
                    : step.Kind is MirProjectionKind.Field or MirProjectionKind.ConstIndex ? step.Arg : -1;
                index = at < rvalue.Count ? at : -1;
            }

            if (index < 0 || this.body.Operands[rvalue.Start + index] is not { Kind: not MirOperandKind.Const } operand)
            {
                break;
            }

            place = operand.Place;
        }

        foreach (var local in this.lenderLocals)
        {
            this.lenderOf[local] = lender;
        }

        return lender;
    }

    // A new position: a root key, the steps of a position (-1: none), then the given steps, walked from the Place root `from`. A lender's
    // steps keep their Places (the same root); each given step takes its Place by Applied.
    private int Exact(int root, int lent, ReadOnlySpan<MirStep> steps, int from)
    {
        var (start, head) = (this.lentStepCount, lent < 0 ? 0 : this.lentRuns[lent].Count);
        MirDataflow.Grow(ref this.lentSteps, start + head + steps.Length);
        MirDataflow.Grow(ref this.lentPlaces, start + head + steps.Length);
        if (lent >= 0)
        {
            this.lentSteps.AsSpan(this.lentRuns[lent].Start, head).CopyTo(this.lentSteps.AsSpan(start));
            this.lentPlaces.AsSpan(this.lentRuns[lent].Start, head).CopyTo(this.lentPlaces.AsSpan(start));
        }

        steps.CopyTo(this.lentSteps.AsSpan(start + head));
        var named = this.RootOf(root);
        for (var i = start + head; i < start + head + steps.Length; i++)
        {
            this.lentPlaces[i] = this.Applied(named, from, start, i);
        }

        this.lentStepCount += head + steps.Length;
        this.Work += head + steps.Length;
        this.lentRuns.Add((root, start, head + steps.Length));
        return this.lentRuns.Count - 1;
    }

    // The body Place the step at lentSteps index `i` of a position run from `start` applies to (LentPlace): the Place the steps before it
    // name from the position's root `named`, which passes no borrow or raw Deref (R4-M234), where the body has its row: the root itself for
    // the first step; for a later one its Prefix row where that row continues the previous step's Place, so named, by the previous step
    // (rows are independent of their roots, so a lender's row may name it: `*r` where r lends a handle's payload `*h`). Else the Prefix
    // row from `from`, the root of the walk that gave the step: the position's root in the first segment (after a ConstIndex step), else
    // past that walk's Deref.
    private MirPlace Applied(int named, int from, int start, int i)
    {
        if (i == start)
        {
            return new(named, 0);
        }

        var (before, last, prefix) = (this.lentPlaces[i - 1], this.lentSteps[i - 1], this.lentSteps[i].Prefix);
        var row = this.body.Projections[prefix];
        return before.Root == named && row.Prefix == before.Projection && row.Kind == last.Kind && row.Arg == last.Arg
            ? new(named, prefix) : new(from, prefix);
    }
}
