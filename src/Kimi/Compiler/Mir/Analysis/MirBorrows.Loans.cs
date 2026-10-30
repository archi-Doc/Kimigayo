// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's Loans of the view (A3 4's issued, E3, E13 (4)): one per non-raw Ref and Reborrowing PairAcquire past no shared borrow Deref, by
// location, each with its Place's steps and whether it is a raw Place borrow's (Anchored: its Place passes a raw Deref, so its fresh
// anchor's, which no access reaches, R4-M156), in group order (MirBorrows.Groups); whether the view has an input of a Loan-stratum producer
// (D5, R4-M98): a Loan, a guard candidate, whose guard-local Origin a check follows without a Loan (E7), a Drop of a mutable static (R3) or
// a write rooted at a default template's PreparedInput (R26-access); for the effect rule (R4-M92) the view's effect roots, the mutable
// statics its Loans are on, and each static's A4 Field number; and whether the view has a raw Ref, whose address MirBorrows.Addresses
// forms.
internal sealed partial class MirBorrows
{
    private readonly List<int> effectRoots = []; // The root keys of the mutable statics the view's Loans are on, each once.
    private bool[] named = []; // By local: whether a direct Loan names it, so it is a flow holder.
    private bool[] borrowed = []; // By root key (Key): whether a Loan's Place has that root.
    private MirPlace[] lent = []; // By local: the Place a Ref lent into it, or the local itself.
    private Loan[] loans = [];
    private MirStep[] loanSteps = []; // The Loans' Places, each a run.
    private int[] loanAt = []; // By location (Location): the Loan its Ref or Reborrowing PairAcquire issues, or -1.
    private int loanCount;
    private int loanStepCount;
    private bool producing; // Whether the view has an input of a Loan-stratum producer, which each producer's scan below sets (D5).
    private bool rawRefs; // Whether the view has a raw Ref, whose address MirBorrows.Addresses forms.
    private int[] fields = []; // By static: A4's number of its Field (MirEffects.Field), -1 for none.

    // The Reborrow a statement's PairAcquire makes under the view (A3 E13 (4)): the borrow its Use's marker names (Uniq, or TwoPhaseUniq for
    // an invocation's input), or Raw where it Copies or the statement is none.
    private MirRefKind Reborrowing(in MirStatement statement)
        => statement is { Kind: MirStatementKind.Assign, Rvalue: { Kind: MirRvalueKind.Use, Op: not 0 } use } && cases.Reborrows(this.body.Operands[use.Start])
            ? (MirRefKind)use.Op : MirRefKind.Raw;

    // The Loans of the view (A3 4's issued, E3): a non-raw Ref of a Place that passes no shared borrow Deref and a PairAcquire the view makes a
    // Reborrow, of its Place's referent in its marker's mode (A3 E13 (4)), by location; each named by its whole Place, then ordered by group.
    private void Loans()
    {
        var (locals, locations) = (this.body.Locals.Length, this.body.Statements.Length + this.body.Blocks.Length);
        var keys = locals + this.body.Statics.Length;
        MirDataflow.Grow(ref this.loanAt, locations);
        MirDataflow.Grow(ref this.borrowed, keys);
        MirDataflow.Grow(ref this.named, locals);
        MirDataflow.Grow(ref this.lent, locals);
        Array.Fill(this.loanAt, -1, 0, locations);
        Array.Clear(this.borrowed, 0, keys);
        Array.Clear(this.named, 0, locals);
        MirDataflow.Grow(ref this.fields, this.body.Statics.Length);
        for (var i = 0; i < this.body.Statics.Length; i++)
        {
            this.fields[i] = effects.Field(this.body.Statics[i].Symbol);
        }

        this.producing = false;
        this.effectRoots.Clear();
        for (var i = 0; i < locals; i++)
        {
            this.lent[i] = new(i);
            this.producing |= this.MarkerOf(i) == Marker.Guard;
        }

        (this.loanCount, this.loanStepCount, this.rawRefs) = (0, 0, false);
        for (var block = 0; block < this.body.Blocks.Length; block++)
        {
            ref readonly var data = ref this.body.Blocks[block];
            for (var i = 0; i < data.Count; i++)
            {
                ref readonly var statement = ref this.body.Statements[data.Start + i];
                if (statement.Kind != MirStatementKind.Assign)
                {
                    continue;
                }

                this.producing |= MirSteps.Prepared(this.body, statement.Place.Root);
                var rvalue = statement.Rvalue.Kind == MirRvalueKind.Adapt ? cases.Adapt(statement.Rvalue, out _) : statement.Rvalue;
                this.rawRefs |= rvalue is { Kind: MirRvalueKind.Ref, Op: (byte)MirRefKind.Raw };
                if (rvalue.Kind == MirRvalueKind.Ref && (MirRefKind)rvalue.Op != MirRefKind.Raw)
                {
                    this.loanAt[data.Start + i] = this.Issue(rvalue.Place, reborrow: false, (MirRefKind)rvalue.Op, statement.Place.Root, data.Start + i);
                    if (statement.Place is { Projection: 0, Root: >= 0 } into)
                    {
                        this.lent[into.Root] = rvalue.Place;
                    }
                }
                else if (this.Reborrowing(statement) is var kind and not MirRefKind.Raw)
                {
                    var place = this.body.Operands[rvalue.Start].Place;
                    this.loanAt[data.Start + i] = this.Issue(place, reborrow: true, kind, statement.Place.Root, data.Start + i);
                }
            }

            var terminator = data.Terminator;
            this.producing |= this.DroppedStatic(terminator) >= 0 ||
                (terminator.Kind == MirTerminatorKind.Call && MirSteps.Prepared(this.body, terminator.Place.Root));
        }

        this.producing |= this.loanCount > 0;

        this.Order(moves.Paths.Length + this.body.Statics.Length);
        this.Renumber(this.loanAt.AsSpan(0, locations));
        this.Work += locations;
    }

    // A Loan of a Place issued into a local (-1: none) at a location, or -1 past a shared borrow Deref (E3).
    private int Issue(MirPlace place, bool reborrow, MirRefKind mode, int into, int location)
    {
        var count = this.placeSteps.Walk(this.body, place, reborrow, ref this.walk);
        var (direct, anchored) = (true, false);
        for (var i = 0; i < count; i++)
        {
            if (this.walk[i].Class == MirStepClass.Shared)
            {
                return -1;
            }

            direct &= this.walk[i].Class is not (MirStepClass.Uniq or MirStepClass.Raw);
            anchored |= this.walk[i].Class == MirStepClass.Raw;
        }

        MirDataflow.Grow(ref this.loans, this.loanCount + 1);
        MirDataflow.Grow(ref this.loanSteps, this.loanStepCount + count);
        this.walk.AsSpan(0, count).CopyTo(this.loanSteps.AsSpan(this.loanStepCount));
        var (key, root) = (this.Key(place.Root), direct ? place.Root : -1);
        this.loans[this.loanCount] = new()
        {
            Root = key,
            Place = place,
            Group = place.IsStatic ? moves.Paths.Length + ~place.Root : moves.Find(place, out _),
            Start = this.loanStepCount,
            Count = count,
            Mode = mode,
            Holder = into,
            Named = root,
            Direct = direct,
            Anchored = anchored,
            Location = location,
        };
        this.loanStepCount += count;
        if (place.IsStatic && !this.borrowed[key] && this.body.Statics[~place.Root].Mutable)
        {
            this.effectRoots.Add(key);
        }

        this.borrowed[key] = true;
        if (root >= 0)
        {
            this.named[root] = true;
        }

        return this.loanCount++;
    }

    /// <summary>A Loan (A3 3).</summary>
    private struct Loan
    {
        /// <summary>Gets or sets its Place's root key.</summary>
        internal int Root { get; set; }

        /// <summary>Gets or sets its Place, whose case Type names the storage it lends (a call's Reached entry, MirBorrows.Calls).</summary>
        internal MirPlace Place { get; set; }

        /// <summary>Gets or sets its Place's group: the Move Path of its longest inline prefix (MirMoveData), or for a static's Place the
        /// number of paths and the static's index.</summary>
        internal int Group { get; set; }

        /// <summary>Gets or sets the first of its Place's steps.</summary>
        internal int Start { get; set; }

        /// <summary>Gets or sets the number of its Place's steps.</summary>
        internal int Count { get; set; }

        /// <summary>Gets or sets its mode.</summary>
        internal MirRefKind Mode { get; set; }

        /// <summary>Gets or sets the slot it was issued into, a two-phase Loan's reservation holder (-1: none).</summary>
        internal int Holder { get; set; }

        /// <summary>Gets or sets the slot a direct Loan names, E's for a static's (G136 (i), R4-M92), -1 for none.</summary>
        internal int Named { get; set; }

        /// <summary>Gets or sets a value indicating whether it is direct: its Place passes no borrow or raw Deref.</summary>
        internal bool Direct { get; set; }

        /// <summary>Gets or sets a value indicating whether it is a raw Place borrow's: its Place passes a raw Deref, so it is its fresh
        /// anchor's, which no access reaches (SPEC 5.2.2, 15.6.2; DECISIONS R4-M156).</summary>
        internal bool Anchored { get; set; }

        /// <summary>Gets or sets a value indicating whether it is a destroying reservation: its call destroys its target in place.</summary>
        internal bool Destroying { get; set; }

        /// <summary>Gets or sets a value indicating whether it is an address's own-step entry (MirBorrows.Addresses): its steps are its
        /// owning Place's.</summary>
        internal bool OwnStep { get; set; }

        /// <summary>Gets or sets its issuing statement's or terminator's location (Location's index), whose subject is its anchor and whose
        /// scope its issue's (D14).</summary>
        internal int Location { get; set; }
    }
}
