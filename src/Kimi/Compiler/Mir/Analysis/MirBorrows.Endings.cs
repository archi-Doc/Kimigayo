// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's endings (E8, A3 4's destroying(site)): the Loans each location's destructions in place end, those of a Move Path's subtree one
// run of ids, and the reservations of a call that destroys its target in place.
internal sealed partial class MirBorrows
{
    private int[] ended = []; // By location: the set of the Loans its destructions in place end (0: none).

    // The Loans each location's destructions in place end (E8): a whole write of a Move Path (an Assign's Place, a returning Call's
    // destination), StorageLive and StorageDead of a root, and a call target its published row destroys in place (`replace`), the Place
    // its Ref lent, whose reservation is destroying (A3 4's destroying(site)).
    private void Endings()
    {
        var statements = this.body.Statements;
        MirDataflow.Grow(ref this.ended, statements.Length + this.body.Blocks.Length);
        for (var block = 0; block < this.body.Blocks.Length; block++)
        {
            ref readonly var data = ref this.body.Blocks[block];
            for (var i = 0; i < data.Count; i++)
            {
                var statement = statements[data.Start + i];
                this.ended[data.Start + i] = statement.Kind is MirStatementKind.Assign or MirStatementKind.StorageLive or MirStatementKind.StorageDead
                    ? this.Ending(statement.Place) : 0;
            }

            var terminator = data.Terminator;
            var set = terminator.Kind == MirTerminatorKind.Call && terminator.Target >= 0 ? this.Ending(terminator.Place) : 0;
            if (this.InPlaceTarget(terminator) is var target and >= 0)
            {
                set = this.sets.Union(set, this.Ending(this.lent[target]));
                this.Destroying(target);
            }

            this.ended[statements.Length + block] = set;
        }

        this.Work += statements.Length + this.body.Blocks.Length;
    }

    // The argument local whose target a Call's published row destroys in place (`replace`), or -1.
    private int InPlaceTarget(in MirTerminator call)
    {
        if (call is not { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Fn, Arg: 0, Symbol: { } symbol } } ||
            types.Published(symbol) is not { InPlace: true, Destroys: >= 0 } row)
        {
            return -1;
        }

        return this.body.Operands[call.Start + MirCallee.Position(symbol, row.Destroys)].Place is { Root: >= 0 } target ? target.Root : -1;
    }

    // The two-phase Loans issued into an argument holder whose call destroys its target in place: the Ref reads its target where it is
    // formed, and its activation writes it (A3 5's access and err(activate)).
    private void Destroying(int local)
    {
        var (first, end) = this.Subtree(new(this.lent[local].Root));
        for (var l = first; l < end; l++)
        {
            ref var loan = ref this.loans[l];
            loan.Destroying |= loan.Mode == MirRefKind.TwoPhaseUniq && loan.Holder == this.slots[local];
        }

        this.Work += end - first;
    }

    // The Loans destroying a Move Path in place ends (E8), those whose Places it exactly prefixes: the Loans whose groups lie in its
    // subtree. A Place through a Deref or an index under the view, or a part of a static, is no Move Path; a whole static is one, its
    // group's Loans ended (OQ-F5-1 (a), R4-M92).
    private int Ending(MirPlace destroyed)
    {
        if (destroyed.IsStatic && destroyed.Projection > 0)
        {
            return 0;
        }

        var count = this.Walk(destroyed, reborrow: false, ref this.walk);
        for (var i = 0; i < count; i++)
        {
            if (this.walk[i].Class != StepClass.Inline)
            {
                return 0;
            }
        }

        var (first, end) = this.Subtree(destroyed);
        return this.sets.Range(this.loanBase + first, end - first);
    }

    // The Loans whose groups lie in a Place's: the subtree [K, End) of a local's Move Path K (MirMoveData's pre-order), whose Places K
    // exactly prefixes (E8), or a static's group.
    private (int First, int End) Subtree(MirPlace place)
    {
        if (place.IsStatic)
        {
            var group = moves.Paths.Length + ~place.Root;
            return (this.loanStart[group], this.loanStart[group + 1]);
        }

        var path = moves.Find(place, out _);
        return (this.loanStart[path], this.loanStart[moves.Paths[path].End]);
    }
}
