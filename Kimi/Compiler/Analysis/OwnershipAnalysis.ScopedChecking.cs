// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private ScopedCheckingProof? scopedCheckingProof;

    private int CheckingSeed()
        => this.current >= 0 ? this.current :
            this.checkingRegion > 0 && this.body.CheckingRegions[this.checkingRegion].Entry < 0 ?
                this.body.CheckingRegions[this.checkingRegion].Seed : -1;

    private int BranchCheckingSeed(CodeBlockKoto block, int continuation)
        => !this.flow!.Nodes[block].CanCompleteNormally ? continuation :
            (this.scopedCheckingProof ??= new(this)).Check(block, false) ? this.current : -1;

    // Joins the terminal paths recorded since mark, then releases them. Every path
    // must be available; the join is the continuation of later dead source. An
    // unknown labeled path (-2) is omitted from a selection's join, as before these
    // joins existed, but a scope cannot carry a state that may never leave it.
    private void JoinChecking(Koto source, int mark, bool labeled)
    {
        var end = this.terminalSeeds.Count;
        var write = mark;
        for (var i = mark; i < end; i++)
        {
            var seed = this.terminalSeeds[i];
            if (seed == -2 && source is IfKoto)
            {
                continue;
            }

            if (seed < 0 || (write > mark && this.body.LoanStates.Count > 0 && this.body.LoanStates[seed] != this.body.LoanStates[this.terminalSeeds[mark]]))
            {
                this.terminalSeeds.RemoveRange(mark, end - mark);
                return; // Different active Loan stacks need a separate lifetime join.
            }

            this.terminalSeeds[write++] = seed;
        }

        var count = write - mark;
        var first = this.terminalSeeds[mark];
        this.checkingRegion = this.body.CheckingRegions.Count;
        if (count == 1)
        {
            this.body.CheckingRegions.Add(new(first, -1, Labeled: labeled));
        }
        else
        {
            // A multi-seed region needs its entry now so that a nested continuation reads the join.
            this.body.CheckingRegions.Add(new(first, -1, this.body.CheckingSeeds.Count, count, labeled));
            this.body.CheckingSeeds.AddRange(CollectionsMarshal.AsSpan(this.terminalSeeds).Slice(mark, count));
            this.current = -1;
            this.Emit(OwnershipOperationKind.Branch, source);
        }

        this.terminalSeeds.RemoveRange(mark, end - mark);
    }

    // This is a bounded continuation proof, not an executable-syntax allowlist.
    // Terminal branches of every selection record their seeds, so selections are
    // walked as ordinary children. Loops, cleanup and short-circuit effects still
    // need joins before a scope can carry their state.
    // The visitor is retained and walks owned children without allocating arrays.
    private sealed class ScopedCheckingProof(OwnershipAnalysis owner) : KotoVisitor
    {
        private bool supported;
        private bool allowTermination;

        public override void Visit(Koto node)
        {
            if (!this.supported || node is FunctionKoto or DeclarationContainerKoto)
            {
                return;
            }

            if (!this.allowTermination && (node is JumpKoto ||
                (owner.flow!.Nodes.TryGetValue(node, out var info) && !info.CanCompleteNormally)))
            {
                this.supported = false; // A normal branch must not hide another terminal path.
                return;
            }

            if (node is LoopKoto)
            {
                this.supported = owner.IsStateNeutralDivergence(node);
                return;
            }

            if (node is WhileKoto loop)
            {
                this.supported = !owner.flow!.Nodes[loop.Condition].CanCompleteNormally &&
                    (owner.localLoopProof ??= new(owner)).Check(loop, loop.Body);
                this.Visit(loop.Condition);
                return;
            }

            if (node is MatchKoto or ForKoto or RequireKoto or DeferredBlockKoto ||
                node is BinaryKoto { Akind: KotoKind.And or KotoKind.Or })
            {
                this.supported = false;
                return;
            }

            node.VisitChildren(this);
        }

        internal bool Check(CodeBlockKoto block, bool allowTermination = true)
        {
            this.supported = true;
            this.allowTermination = allowTermination;
            this.Visit(block);
            return this.supported;
        }
    }
}
