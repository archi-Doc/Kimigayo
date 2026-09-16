// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

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

    private void JoinChecking(Koto source, int start, int count)
    {
        var first = this.body.CheckingSeeds[start];
        if (first < 0)
        {
            return;
        }

        for (var i = 1; i < count; i++)
        {
            var seed = this.body.CheckingSeeds[start + i];
            if (seed < 0 || (this.body.LoanStates.Count > 0 && this.body.LoanStates[seed] != this.body.LoanStates[first]))
            {
                return; // Different active Loan stacks need a separate lifetime join.
            }
        }

        this.checkingRegion = this.body.CheckingRegions.Count;
        this.body.CheckingRegions.Add(new(first, -1, start, count));
        this.current = -1;
        this.Emit(OwnershipOperationKind.Branch, source);
    }

    // This is a bounded continuation proof, not an executable-syntax allowlist.
    // Closed terminal selections have their own checking joins. Partial transfers
    // and cleanup effects still need joins before a scope can carry their state.
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

            if (node is IfKoto conditional)
            {
                if (owner.flow!.Nodes[conditional].CanCompleteNormally)
                {
                    var previous = this.allowTermination;
                    this.allowTermination = false;
                    node.VisitChildren(this);
                    this.allowTermination = previous;
                    return;
                }

                for (var i = 0; i < conditional.Branches.Count; i++)
                {
                    var branch = conditional.Branches[i];
                    this.Visit(branch.Condition);
                    this.VisitBranchBody(branch.Body);
                }

                if (conditional.ElseBody is { } otherwise)
                {
                    this.VisitBranchBody(otherwise);
                }

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

        private void VisitBranchBody(CodeBlockKoto block)
        {
            var previous = this.allowTermination;
            this.allowTermination &= !owner.flow!.Nodes[block].CanCompleteNormally;
            this.Visit(block);
            this.allowTermination = previous;
        }
    }
}
