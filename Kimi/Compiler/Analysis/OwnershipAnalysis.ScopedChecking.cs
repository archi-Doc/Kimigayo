// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private readonly List<CheckingContinuation> normalCheckingSeeds = new();
    private readonly List<(Koto Target, CheckingContinuation Seed)> caughtCheckingSeeds = new();
    private readonly List<CheckingContinuation> groupedCheckingSeeds = new();
    private readonly Dictionary<BoundType, bool> checkingPatternTypes = new(ReferenceEqualityComparer.Instance);
    private ScopedCheckingProof? scopedCheckingProof;
    private MatchGuardCheckingProof? matchGuardCheckingProof;

    private bool SupportsMatchChecking(MatchKoto match)
    {
        // Tuple/enum leaves have scalar or built-in string responsibility. Whole
        // acquisition retains declared Copy/Move; no borrowed payload is admitted.
        // String guard protection ends before the true/false histories fork;
        // selected binding acquisition and Subject cleanup stay in each body.
        if (!ReferenceEquals(match.Expression.BoundType, BoundType.String) && !this.SupportsCheckingPatternType(match.Expression.BoundType))
        {
            return false;
        }

        for (var i = 0; i < match.Arms.Count; i++)
        {
            if (match.Arms[i].Guard is { } guard && !(this.matchGuardCheckingProof ??= new(this)).Check(guard))
            {
                return false;
            }
        }

        return true;
    }

    private bool SupportsCheckingPatternType(BoundType? type)
    {
        if (ScalarDefaults.SupportsValue(type) || ReferenceEquals(type, BoundType.String))
        {
            return true;
        }

        if (type is not { Semantics: SemanticsKind.Owner, Origin: null, OriginArguments.Count: 0 })
        {
            return false;
        }

        if (this.checkingPatternTypes.TryGetValue(type, out var supported))
        {
            return supported;
        }

        // SupportsType rejects inline cycles/growing substitutions before this
        // recursive shape proof. Check every Case, including unmatched payloads.
        this.checkingPatternTypes[type] = false;
        if (!this.SupportsType(type))
        {
            return false;
        }

        if (type.Kind == BoundTypeKind.Tuple)
        {
            supported = true;
            for (var i = 0; i < type.Components.Count && supported; i++)
            {
                supported = this.SupportsCheckingPatternType(type.Components[i]);
            }
        }
        else if (EnumStorage.IsEnum(type) && this.compilation.Binding.EnumStorage(type) is { } storage)
        {
            supported = true;
            for (var i = 0; i < storage.Count && supported; i++)
            {
                supported = this.SupportsCheckingPatternType(this.compilation.Binding.StoredType(storage[i], type));
            }
        }

        this.checkingPatternTypes[type] = supported;
        return supported;
    }

    private int CheckingSeed()
        => this.current >= 0 ? this.current :
            this.checkingRegion > 0 && this.body.CheckingRegions[this.checkingRegion].Entry < 0 ?
                this.body.CheckingRegions[this.checkingRegion].Seed : -1;

    private CheckingContinuation Continuation()
    {
        var region = this.body.CheckingRegions[this.checkingRegion];
        if (!region.MixedTargets)
        {
            return new(this.CheckingSeed(), region.Target, Replay: region.Entry < 0 ? region.Replay : -1, CaughtTarget: region.CaughtTarget);
        }

        // Local diagnostics use the common state. Enclosing extents must apply
        // later effects to each constituent before selecting escaping targets.
        var seed = this.CheckingSeed();
        // Freeze the seed range now: the caller may emit lexical cleanup that
        // gives this region an entry before it records the captured continuation.
        return this.CanReplayChecking(region, seed, out var graph) ? new(seed, SeedStart: this.ReplayCheckingSeeds(region, seed, graph), SeedCount: region.SeedCount) : new(-1);
    }

    private void BeginChecking(int seed, Koto? target = null, OwnershipCheckingRegion? previous = null, Koto? caughtTarget = null)
    {
        // A transfer in dead source cannot replace the extent of the path that
        // made it unreachable. Replay stops after operand acquisition, before
        // this transfer's implicit cleanup, and retains the original targets.
        var origin = previous ?? this.body.CheckingRegions[this.checkingRegion];
        var graph = false;
        var retained = origin.MixedTargets && this.CanReplayChecking(origin, seed, out graph);
        var start = retained ? this.ReplayCheckingSeeds(origin, seed, graph, caughtTarget) : 0;
        this.body.CheckingRegions.Add(new(seed, -1, start, retained ? origin.SeedCount : 0, this.checkingRegion > 0 ? origin.Target : target, origin.MixedTargets, origin.Entry < 0 ? origin.Replay : -1, origin.CaughtTarget ?? caughtTarget));
        this.checkingRegion = this.body.CheckingRegions.Count - 1;
    }

    private bool CanReplayChecking(OwnershipCheckingRegion region, int end, out bool graph)
    {
        graph = false;
        if (region.SeedCount == 0 || end < 0)
        {
            return false;
        }

        if (region.Entry < 0)
        {
            return end == region.Seed;
        }

        var owner = this.body.OperationRegions[region.Entry];
        for (var cursor = region.Entry; cursor <= end;)
        {
            if (this.body.OperationRegions[cursor] != owner)
            {
                return false;
            }

            if (cursor == end)
            {
                return true;
            }

            var next = this.body.LinearCheckingSuccessor(cursor);
            if (next <= cursor)
            {
                break;
            }

            cursor = next;
        }

        graph = this.body.CanReplayCheckingGraph(region.Entry, end);
        return graph;
    }

    private int ReplayCheckingSeeds(OwnershipCheckingRegion region, int end, bool graph, Koto? caughtTarget = null)
    {
        var needsReplay = region.Entry >= 0 && end != region.Entry;
        if (!needsReplay && caughtTarget is null)
        {
            return region.SeedStart;
        }

        var start = this.body.CheckingSeeds.Count;
        for (var i = 0; i < region.SeedCount; i++)
        {
            var seed = this.body.CheckingSeeds[region.SeedStart + i];
            var replay = seed.Replay;
            if (needsReplay)
            {
                replay = this.body.CheckingReplays.Count;
                this.body.CheckingReplays.Add(new(region.Entry, end, seed.Replay, graph));
            }

            this.body.CheckingSeeds.Add(seed with { Replay = replay, CaughtTarget = seed.CaughtTarget ?? caughtTarget });
        }

        return start;
    }

    private int CheckingLoanState(CheckingContinuation seed)
        => this.body.LoanStates[seed.Replay < 0 ? seed.Seed : this.body.CheckingReplays[seed.Replay].End];

    private bool CanForkCheckingBranches(IfKoto source)
    {
        var proof = this.scopedCheckingProof ??= new(this);
        var terminal = source.ElseBody is { } otherwise && !proof.Check(otherwise, false);
        for (var i = 0; i < source.Branches.Count; i++)
        {
            var branch = source.Branches[i];
            var conditionCompletes = this.flow!.Nodes[branch.Condition].CanCompleteNormally;
            terminal |= !conditionCompletes || !proof.Check(branch.Body, false);
        }

        // Keep closed normal CFGs intact, including those on loop backedges.
        return terminal && proof.Check(source);
    }

    private bool CanForkTerminalLoop(Koto loop, CodeBlockKoto block)
        => this.body.CheckingRegions[this.checkingRegion].MixedTargets &&
            this.flow!.Nodes[loop].CanCompleteNormally &&
            !this.flow.Nodes[block].CanCompleteNormally &&
            (this.scopedCheckingProof ??= new(this)).Check(block, backedgeTarget: loop);

    private OwnershipCheckingRegion? ForkChecking(int seed)
    {
        var region = this.body.CheckingRegions[this.checkingRegion];
        if (!region.MixedTargets || !this.CanReplayChecking(region, seed, out var graph))
        {
            return null;
        }

        // Both branches start from the same proven prefix, but retain separate
        // constituent histories when a terminal operand changes checking region.
        return new(seed, -1, this.ReplayCheckingSeeds(region, seed, graph), region.SeedCount, region.Target, true, CaughtTarget: region.CaughtTarget);
    }

    private void EnterCheckingBranch(int operation, OwnershipCheckingRegion? fork)
    {
        if (fork is { } region)
        {
            this.checkingRegion = this.body.CheckingRegions.Count;
            this.body.CheckingRegions.Add(region with { Entry = operation });
            this.body.OperationRegions[operation] = this.checkingRegion;
        }

        this.current = operation;
    }

    // Internal exits/continues/yields feed the construct's ordinary CFG. Their
    // pre-transfer state must never masquerade as a path escaping an outer join.
    private void FilterTerminalSeeds(Koto source, int mark)
    {
        var write = mark;
        for (var i = mark; i < this.terminalSeeds.Count; i++)
        {
            var seed = this.terminalSeeds[i];
            var target = seed.CaughtTarget ?? seed.Target;
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
    // mixed-target join checks local dead source using the common state. A
    // closed continuation can export each constituent with its own later effects.
    private void JoinChecking(Koto source, int mark)
    {
        this.FilterTerminalSeeds(source, mark);
        this.CreateCheckingJoin(source, this.terminalSeeds, mark);
    }

    private void JoinNormalChecking(Koto source, int mark, int entry)
        => this.JoinCheckingAt(source, this.normalCheckingSeeds, mark, entry);

    private void JoinCheckingAt(Koto source, List<CheckingContinuation> seeds, int mark, int entry)
    {
        // Terminal histories remain pending for enclosing extents. Only normal
        // tails seed this successor, after their branch-local cleanup.
        if (!this.CreateCheckingJoin(source, seeds, mark, entry))
        {
            this.checkingRegion = this.body.CheckingRegions.Count;
            this.body.CheckingRegions.Add(new(-1, entry));
        }

        this.body.OperationRegions[entry] = this.checkingRegion;
    }

    private bool CreateCheckingJoin(Koto source, List<CheckingContinuation> seeds, int mark, int entry = -1)
    {
        var end = seeds.Count;
        var count = end - mark;
        if (count == 0)
        {
            return false;
        }

        var first = seeds[mark];
        var mixed = false;
        for (var i = mark; i < end; i++)
        {
            var seed = seeds[i];
            if (seed.Seed < 0 || (i > mark && this.body.LoanStates.Count > 0 && this.CheckingLoanState(seed) != this.CheckingLoanState(first)))
            {
                seeds.RemoveRange(mark, end - mark);
                return false; // Different active Loan stacks need a separate lifetime join.
            }

            mixed |= !ReferenceEquals(seed.Target, first.Target) || !ReferenceEquals(seed.CaughtTarget, first.CaughtTarget);
        }

        if (mixed && count > 2)
        {
            this.CoalesceCheckingTargets(source, seeds, mark);
            end = seeds.Count;
            count = end - mark;
            first = seeds[mark];
        }

        this.checkingRegion = this.body.CheckingRegions.Count;
        if (count == 1)
        {
            this.body.CheckingRegions.Add(new(first.Seed, entry, Target: first.Target, Replay: first.Replay, CaughtTarget: first.CaughtTarget));
        }
        else
        {
            // A multi-seed region needs its entry now so that a nested continuation reads the join.
            this.body.CheckingRegions.Add(new(first.Seed, entry, this.body.CheckingSeeds.Count, count, first.Target, mixed, CaughtTarget: first.CaughtTarget));
            for (var i = mark; i < end; i++)
            {
                var seed = seeds[i];
                this.body.CheckingSeeds.Add(new(seed.Seed, seed.Target, seed.Replay, seed.CaughtTarget));
            }

            if (entry < 0)
            {
                this.current = -1;
                this.Emit(OwnershipOperationKind.Branch, source);
            }
        }

        this.SetCheckingEntryLoans(this.body.CheckingRegions[this.checkingRegion].Entry, first);
        seeds.RemoveRange(mark, end - mark);
        return true;
    }

    private void CoalesceCheckingTargets(Koto source, List<CheckingContinuation> seeds, int mark)
    {
        // Once histories share both transfer extents, their state join is enough
        // for every later transfer. Bound repeated guard/branch forks by distinct
        // targets instead of retaining every combination of earlier choices.
        this.groupedCheckingSeeds.Clear();
        for (var i = mark; i < seeds.Count; i++)
        {
            var first = seeds[i];
            var seen = false;
            for (var j = mark; j < i; j++)
            {
                if (ReferenceEquals(seeds[j].Target, first.Target) && ReferenceEquals(seeds[j].CaughtTarget, first.CaughtTarget))
                {
                    seen = true;
                    break;
                }
            }

            if (seen)
            {
                continue;
            }

            var start = this.body.CheckingSeeds.Count;
            for (var j = i; j < seeds.Count; j++)
            {
                var candidate = seeds[j];
                if (ReferenceEquals(candidate.Target, first.Target) && ReferenceEquals(candidate.CaughtTarget, first.CaughtTarget))
                {
                    this.body.CheckingSeeds.Add(new(candidate.Seed, candidate.Target, candidate.Replay, candidate.CaughtTarget));
                }
            }

            var count = this.body.CheckingSeeds.Count - start;
            if (count == 1)
            {
                this.body.CheckingSeeds.RemoveAt(start);
                this.groupedCheckingSeeds.Add(first);
                continue;
            }

            // This node has no runtime edges. Its region joins already-frozen
            // seeds, and is solved before the mixed-target region that uses it.
            var entry = this.New(OwnershipOperationKind.Branch, source);
            this.body.OperationRegions[entry] = this.body.CheckingRegions.Count;
            this.body.CheckingRegions.Add(new(first.Seed, entry, start, count, first.Target, CaughtTarget: first.CaughtTarget));
            this.SetCheckingEntryLoans(entry, first);
            this.groupedCheckingSeeds.Add(new(entry, first.Target, CaughtTarget: first.CaughtTarget));
        }

        seeds.RemoveRange(mark, seeds.Count - mark);
        seeds.AddRange(this.groupedCheckingSeeds);
        this.groupedCheckingSeeds.Clear();
    }

    private void SetCheckingEntryLoans(int entry, CheckingContinuation seed)
    {
        if (entry >= 0 && this.body.LoanStates.Count > 0)
        {
            // Seed replay may have ended a Loan since its original operation.
            // A synthetic join inherits the proven common stack, never the
            // construction cursor left by a different arm's cleanup.
            var loans = this.CheckingLoanState(seed);
            this.body.LoanInputs[entry] = loans;
            this.body.LoanStates[entry] = loans;
        }
    }

    private void AddTerminalSeed(CheckingContinuation continuation)
        => this.AddCheckingSeed(this.terminalSeeds, continuation);

    private void AddCheckingSeed(List<CheckingContinuation> seeds, CheckingContinuation continuation)
    {
        if (continuation.SeedCount == 0)
        {
            seeds.Add(continuation);
            return;
        }

        for (var i = 0; i < continuation.SeedCount; i++)
        {
            var seed = this.body.CheckingSeeds[continuation.SeedStart + i];
            seeds.Add(new(seed.Operation, seed.Target, Replay: seed.Replay, CaughtTarget: seed.CaughtTarget));
        }
    }

    private void RecordCaughtChecking(Koto target)
    {
        var continuation = this.Continuation();
        if (continuation.SeedCount == 0)
        {
            if (continuation.CaughtTarget is null)
            {
                this.caughtCheckingSeeds.Add((target, continuation));
            }
        }
        else
        {
            for (var i = 0; i < continuation.SeedCount; i++)
            {
                var seed = this.body.CheckingSeeds[continuation.SeedStart + i];
                if (seed.CaughtTarget is null)
                {
                    this.caughtCheckingSeeds.Add((target, new(seed.Operation, seed.Target, Replay: seed.Replay)));
                }
            }
        }
    }

    private void CollectCaughtChecking(Koto target, int mark)
    {
        var write = mark;
        for (var i = mark; i < this.caughtCheckingSeeds.Count; i++)
        {
            var arrival = this.caughtCheckingSeeds[i];
            if (ReferenceEquals(arrival.Target, target))
            {
                this.AddCheckingSeed(this.normalCheckingSeeds, arrival.Seed);
            }
            else
            {
                this.caughtCheckingSeeds[write++] = arrival;
            }
        }

        this.caughtCheckingSeeds.RemoveRange(write, this.caughtCheckingSeeds.Count - write);
    }

    private readonly record struct CheckingContinuation(int Seed, Koto? Target = null, int SeedStart = 0, int SeedCount = 0, int Replay = -1, Koto? CaughtTarget = null);

    private sealed class MatchGuardCheckingProof(OwnershipAnalysis owner) : KotoVisitor
    {
        private bool supported;

        public override void Visit(Koto node)
        {
            if (!this.supported || node is FunctionKoto or DeclarationContainerKoto)
            {
                return;
            }

            if (node is MacroKoto macro)
            {
                this.supported = macro.Operand is InvocationKoto call && ReferenceEquals(call.BoundCall?.Target, owner.compilation.Library.Abort);
                if (this.supported)
                {
                    node.VisitChildren(this);
                }

                return;
            }

            if (node is LoopKoto && !owner.flow!.Nodes[node].CanCompleteNormally)
            {
                // Only loop-local effects may be omitted from a divergent
                // guard's enclosing state. No runtime cleanup follows it.
                this.supported = owner.IsStateNeutralDivergence(node);
                return;
            }

            if (node is JumpKoto jump)
            {
                // Outward transfers abandon the guard. The arm builder retains
                // their pre-cleanup source histories separately from selection.
                // Continue needs a backedge proof before those histories can fork.
                if (jump is not (ReturnKoto or ExitKoto or YieldKoto) || owner.flow!.Targets.GetValueOrDefault(jump) is null)
                {
                    this.supported = false;
                    return;
                }
            }
            else if (node is DeferredBlockKoto or ForKoto ||
                (owner.flow!.Nodes.TryGetValue(node, out var info) && !info.CanCompleteNormally &&
                node is not (CodeBlockKoto or IfKoto or DoKoto or MatchKoto or LabeledKoto or ParenthesizedKoto or InvocationKoto or NotKoto or AndKoto or OrKoto)))
            {
                this.supported = false;
                return;
            }

            node.VisitChildren(this);
        }

        internal bool Check(Koto source)
        {
            this.supported = true;
            this.Visit(source);
            return this.supported;
        }
    }

    // This is a bounded continuation proof, not an executable-syntax allowlist.
    // Terminal branches of every selection record their seeds, so selections are
    // walked as ordinary children, as are completing loops. Divergent effects,
    // cleanup and region-changing mixed replay still need additional joins.
    // The visitor is retained and walks owned children without allocating arrays.
    private sealed class ScopedCheckingProof(OwnershipAnalysis owner) : KotoVisitor
    {
        private bool supported;
        private bool allowTermination;
        private Koto? backedgeTarget;

        public override void Visit(Koto node)
        {
            if (!this.supported || node is FunctionKoto or DeclarationContainerKoto)
            {
                return;
            }

            if (node is ContinueKoto again && ReferenceEquals(owner.flow!.Targets.GetValueOrDefault(again), this.backedgeTarget))
            {
                // A terminal body can still continue this loop. Forking its first
                // iteration would lose later histories; that needs a loop proof.
                this.supported = false;
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

            if (node is ForKoto or DeferredBlockKoto || (node is MatchKoto match && !owner.SupportsMatchChecking(match)))
            {
                this.supported = false;
                return;
            }

            node.VisitChildren(this);
        }

        internal bool Check(Koto source, bool allowTermination = true, Koto? backedgeTarget = null)
        {
            this.supported = true;
            this.allowTermination = allowTermination;
            this.backedgeTarget = backedgeTarget;
            this.Visit(source);
            this.backedgeTarget = null;
            return this.supported;
        }
    }
}
