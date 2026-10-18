// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's Loans of the view (A3 4's issued, E3, E13 (4)): one per non-raw Ref and Reborrowing PairAcquire past no shared borrow Deref, by
// location, each with its Place's steps, in group order.
internal sealed partial class MirBorrows
{
    private bool[] named = []; // By local: whether a direct Loan names it, so it is a flow holder.
    private bool[] borrowed = []; // By root key (Key): whether a Loan's Place has that root.
    private MirPlace[] lent = []; // By local: the Place a Ref lent into it, or the local itself.
    private Loan[] loans = [];
    private Step[] loanSteps = []; // The Loans' Places, each a run.
    private int[] loanAt = []; // By location (Location): the Loan its Ref or Reborrowing PairAcquire issues, or -1.
    private int[] loanStart = []; // By group (Loan.Group): its first Loan, the Loans being in group order.
    private Loan[] ordered = []; // The Loans being put in group order.
    private int[] renumbered = []; // By Loan in issue order: its id in group order.
    private int loanCount;
    private int loanStepCount;

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
        for (var i = 0; i < locals; i++)
        {
            this.lent[i] = new(i);
        }

        (this.loanCount, this.loanStepCount) = (0, 0);
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

                var rvalue = statement.Rvalue.Kind == MirRvalueKind.Adapt ? cases.Adapt(statement.Rvalue, out _) : statement.Rvalue;
                if (rvalue.Kind == MirRvalueKind.Ref && (MirRefKind)rvalue.Op != MirRefKind.Raw)
                {
                    this.loanAt[data.Start + i] = this.Issue(rvalue.Place, reborrow: false, (MirRefKind)rvalue.Op, statement.Place.Root, statement.Source);
                    if (statement.Place is { Projection: 0, Root: >= 0 } into)
                    {
                        this.lent[into.Root] = rvalue.Place;
                    }
                }
                else if (this.Reborrowing(statement) is var kind and not MirRefKind.Raw)
                {
                    var place = this.body.Operands[rvalue.Start].Place;
                    this.loanAt[data.Start + i] = this.Issue(place, reborrow: true, kind, statement.Place.Root, statement.Source);
                }
            }
        }

        this.Order(moves.Paths.Length + this.body.Statics.Length);
        this.Renumber(this.loanAt.AsSpan(0, locations));
        this.Work += locations;
    }

    // Orders the Loans by group, stably, so the Loans a Move Path prefixes are one run of ids (Subtree).
    private void Order(int groups)
    {
        MirDataflow.Grow(ref this.loanStart, groups + 1);
        MirDataflow.Grow(ref this.ordered, this.loanCount);
        MirDataflow.Grow(ref this.renumbered, this.loanCount);
        Array.Clear(this.loanStart, 0, groups + 1);
        for (var l = 0; l < this.loanCount; l++)
        {
            this.loanStart[this.loans[l].Group + 1]++;
        }

        for (var g = 1; g <= groups; g++)
        {
            this.loanStart[g] += this.loanStart[g - 1]; // Each group's start.
        }

        for (var l = 0; l < this.loanCount; l++)
        {
            var id = this.loanStart[this.loans[l].Group]++; // Each group's start becomes its end.
            (this.renumbered[l], this.ordered[id]) = (id, this.loans[l]);
        }

        this.loanStart.AsSpan(0, groups).CopyTo(this.loanStart.AsSpan(1));
        this.loanStart[0] = 0;
        (this.loans, this.ordered) = (this.ordered, this.loans);
        this.Work += groups + (2 * this.loanCount);
    }

    // The sites' Loans in group order.
    private void Renumber(Span<int> sites)
    {
        foreach (ref var loan in sites)
        {
            loan = loan >= 0 ? this.renumbered[loan] : -1;
        }

        this.Work += sites.Length;
    }

    // A Loan of a Place issued into a local (-1: none), or -1 past a shared borrow Deref (E3).
    private int Issue(MirPlace place, bool reborrow, MirRefKind mode, int into, in SourceInfo source)
    {
        var count = this.Walk(place, reborrow, ref this.walk);
        var direct = true;
        for (var i = 0; i < count; i++)
        {
            if (this.walk[i].Class == StepClass.Shared)
            {
                return -1;
            }

            direct &= this.walk[i].Class is not (StepClass.Uniq or StepClass.Raw);
        }

        MirDataflow.Grow(ref this.loans, this.loanCount + 1);
        MirDataflow.Grow(ref this.loanSteps, this.loanStepCount + count);
        this.walk.AsSpan(0, count).CopyTo(this.loanSteps.AsSpan(this.loanStepCount));
        var (key, root) = (this.Key(place.Root), direct ? place.Root : -1);
        this.loans[this.loanCount] = new()
        {
            Root = key,
            Group = place.IsStatic ? moves.Paths.Length + ~place.Root : moves.Find(place, out _),
            Start = this.loanStepCount,
            Count = count,
            Mode = mode,
            Holder = into,
            Named = root,
            Direct = direct,
            Site = this.body.Subject(source),
        };
        this.loanStepCount += count;
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

        /// <summary>Gets or sets the slot a direct Loan names (-1: none).</summary>
        internal int Named { get; set; }

        /// <summary>Gets or sets a value indicating whether it is direct: its Place passes no borrow or raw Deref.</summary>
        internal bool Direct { get; set; }

        /// <summary>Gets or sets a value indicating whether it is a destroying reservation: its call destroys its target in place.</summary>
        internal bool Destroying { get; set; }

        /// <summary>Gets or sets its anchor.</summary>
        internal SourceRef Site { get; set; }
    }
}
