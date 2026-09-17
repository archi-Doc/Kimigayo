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

    private CheckingContinuation Continuation()
    {
        var region = this.body.CheckingRegions[this.checkingRegion];
        return region.MixedTargets ? new(-1) : new(this.CheckingSeed(), region.Target);
    }

    private void BeginChecking(int seed, Koto? target = null)
    {
        // A transfer in dead source cannot replace the extent of the path that
        // made it unreachable. This also retains mixed-target propagation guards.
        var origin = this.body.CheckingRegions[this.checkingRegion];
        this.body.CheckingRegions.Add(new(seed, -1, Target: this.checkingRegion > 0 ? origin.Target : target, MixedTargets: origin.MixedTargets));
        this.checkingRegion = this.body.CheckingRegions.Count - 1;
    }

    // Internal exits/continues/yields feed the construct's ordinary CFG. Their
    // pre-transfer state must never masquerade as a path escaping an outer join.
    private void FilterTerminalSeeds(Koto source, int mark)
    {
        var write = mark;
        for (var i = mark; i < this.terminalSeeds.Count; i++)
        {
            var seed = this.terminalSeeds[i];
            var target = seed.Target;
            while (target is not null && !ReferenceEquals(target, source))
            {
                target = target.Parent;
            }

            if (target is null)
            {
                this.terminalSeeds[write++] = seed;
            }
        }

        this.terminalSeeds.RemoveRange(write, this.terminalSeeds.Count - write);
    }

    // Joins the terminal paths recorded since mark, then releases them. Every path
    // must be available; the join is the continuation of later dead source. A
    // mixed-target join can check its own dead source, but cannot supply a merged
    // state to an enclosing join that may consume only some of those targets.
    private void JoinChecking(Koto source, int mark)
    {
        this.FilterTerminalSeeds(source, mark);
        var end = this.terminalSeeds.Count;
        var count = end - mark;
        if (count == 0)
        {
            return;
        }

        var first = this.terminalSeeds[mark];
        var mixed = false;
        for (var i = mark; i < end; i++)
        {
            var seed = this.terminalSeeds[i];
            if (seed.Seed < 0 || (i > mark && this.body.LoanStates.Count > 0 && this.body.LoanStates[seed.Seed] != this.body.LoanStates[first.Seed]))
            {
                this.terminalSeeds.RemoveRange(mark, end - mark);
                return; // Different active Loan stacks need a separate lifetime join.
            }

            mixed |= !ReferenceEquals(seed.Target, first.Target);
        }

        this.checkingRegion = this.body.CheckingRegions.Count;
        if (count == 1)
        {
            this.body.CheckingRegions.Add(new(first.Seed, -1, Target: first.Target));
        }
        else
        {
            // A multi-seed region needs its entry now so that a nested continuation reads the join.
            this.body.CheckingRegions.Add(new(first.Seed, -1, this.body.CheckingSeeds.Count, count, first.Target, mixed));
            for (var i = mark; i < end; i++)
            {
                this.body.CheckingSeeds.Add(this.terminalSeeds[i].Seed);
            }

            this.current = -1;
            this.Emit(OwnershipOperationKind.Branch, source);
        }

        this.terminalSeeds.RemoveRange(mark, end - mark);
    }

    private readonly record struct CheckingContinuation(int Seed, Koto? Target = null);

    // This is a bounded continuation proof, not an executable-syntax allowlist.
    // Terminal branches of every selection record their seeds, so selections are
    // walked as ordinary children, as are completing loops. Divergent effects,
    // cleanup and short-circuit effects still need additional joins.
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

            if (node is LoopKoto && !owner.flow!.Nodes[node].CanCompleteNormally)
            {
                this.supported = owner.IsStateNeutralDivergence(node);
                return;
            }

            if (node is WhileKoto loop && !owner.flow!.Nodes[loop.Condition].CanCompleteNormally)
            {
                this.supported = (owner.localLoopProof ??= new(owner)).Check(loop, loop.Body);
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
