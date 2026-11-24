// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The end of a Binding pass (Check; DECISIONS BU-81, refining R2b-D2).
// - Responsibility: the pass's failure producers (the definition obligations, the needed conformance paths and capability sites, the
//   call and runtime-test Constraint checks, the part prerequisites, Unvisited to Unresolved); in a Final pass, then, the HIR of the
//   pass's modules (PublishPass: the library pass's the library module, the program pass's the source modules) and H1's TransferRequired judgment over it
//   (CheckTransfers, DECISIONS BU-84), with one republication when it failed a node; then the counting of the node states and the
//   collection of the issues, which ends the pass's failure production (producing: a refusal after it fails no node, Binding.RefuseProof).
// - Inputs: the bound nodes, the obligations, the needed sites and paths, the facts the pass recorded.
// - Outputs: every failure the pass decides; the published HIR columns of a Final pass; the pass summary, issues and derived issues: in a
//   Final pass a node without a failure publishes its problems that set no slot but Origin records (a located limit, a Defect), and is
//   counted limited (BindingResult.LimitedCount), so the Bind is not complete.
// - Invariants: every step that decides a failure runs before the publication, so the published columns describe the pass's final
//   facts; PublishHir() after a Final pass returns its version without work. CheckBound runs the same three steps again.
// - Prohibitions: the counting decides no failure and writes no fact the publisher reads.
public sealed partial class Binding
{
    // Each call Binding synthesized in this pass, with its consumer (BindSynthesizedCall); read by Check (VerdictUse, SettleSynthesizedCalls)
    // and by the borrows' formation (CompleteArguments: a synthesized call's plan is its consumer's to commit).
    private readonly SynthesizedCallRows synthesizedCalls;

    // The pass's synthesized calls in the order their row was first written (AddSynthesizedCall), which SettleSynthesizedCalls walks, so
    // each call is settled once and a consumer with several failed calls rests on the last walked, as dev's Dictionary enumerated them:
    // a call's id is the history of the passes that first wrote it, and a call joins `nodes` again when its consumer is bound again.
    // Precedent: duplicateOrder (DuplicateDeclarations' working order).
    // - Owner: Check.
    // - Reset: ResetSyntheticCalls' Clear, its capacity kept, so a warm rebind allocates nothing. The rows read absent over the cleared
    //   list because every ResetPass, the one clear, follows BeginRun's advance or FreezeLibrary's release of the columns its pass wrote;
    //   the refused library's StartPass calls (Binding.Passes.cs) are excepted, after which no pass binds and no reader asks.
    // - Invariant: a call is listed iff its row is present; one site writes both (SynthesizedCallRows.Add).
    // - Removal: it goes with the column if SettleSynthesizedCalls stops walking the calls; otherwise it is the pass's working order.
    private readonly List<InvocationKoto> synthesizedOrder = [];

    // Whether a node failed now is this pass's failure: from a pass's start (RunPass: the library pass's and the program pass's) or a
    // re-check's (CheckBound) until Check has counted the node states. After it the pass is published: its lints, MIR, emission and the
    // editor's queries fail no node.
    private bool producing;

    private void Check(BindingMode mode)
    {
        this.producing = true; // A re-check's start (CheckBound); a pass raised it at its own (RunPass).
        try
        {
            this.ProduceFailures(mode);
            if (mode == BindingMode.Final)
            {
                this.PublishPass();
                if (this.CheckTransfers())
                {
                    // H1 (DECISIONS BU-84): its failed nodes are published Error, which the assertion checks creates no new subject.
                    this.PublishPass();
                    this.AssertTransfers();
                }
            }

            this.CountStates(mode);
        }
        finally
        {
            this.producing = false; // Also when a re-check throws: a later query fails no node.
        }
    }

    // Every failure the pass decides at its end, in the order the nodes were bound. At Final the erasures' Owned obligations are judged in
    // the prologue (JudgeObligations' Owned kind, JudgeErasedOwned), which reads every region's candidate as complete: a region bound
    // recorded after it is a broken contract (R7-O2 A1+A2, the Front's D4).
    private void ProduceFailures(BindingMode mode)
    {
        var judged = -1;
        if (mode == BindingMode.Final)
        {
            this.formationCauses.Empty();
            this.JudgeObligations();
            judged = this.regionVersion;

            this.SettleSpecificationLinks();
            this.PublishNeededLimits();
            this.FailNeededSites();
            this.SettleDerivedUses();
        }

        for (var i = 0; i < this.nodes.Count; i++)
        {
            var node = this.nodes[i];
            // API and constraint validation can invalidate a target or Type after call selection; a node whose own selection calls a
            // declaration that failed (CallPlan.Called: a call's target, a Property use's accessors), or whose published decision read a
            // written Type its judgment failed later (FailedWrittenType: a call's explicit Type argument, a Property's Type, whose
            // certificate it withdraws), rests on that failure (SPEC 23.1.2; PLAN G464).
            if ((this.InvalidCalledDeclaration(node) ?? this.FailedWrittenType(node)) is { } failedInput && node.BindingFailure == BindingFailure.None)
            {
                this.CompleteDependent(node, failedInput);
                if (node is PropertyKoto { BoundSymbol.Property: { } property })
                {
                    property.IsVerified = false;
                }
            }
            else if (node is InvocationKoto invocation && this.CallOf(invocation) is { } call)
            {
                // A call's verdict at its use (VerdictUse); a synthesized call rests on its failed consumer, failed before or by this verdict.
                // DECISIONS BU-240 (G812 ruling 159): an obligation whose premise was Unknown at a split's mark rests (CallPlan.PremiseReadsMark).
                var consumer = this.synthesizedCalls.GetValueOrDefault(invocation);
                if (consumer is not { BindingFailure: not BindingFailure.None } && !call.PremiseReadsMark)
                {
                    var use = this.VerdictUse(node);
                    this.RequireClauses(use, this.CheckCallTypeConstraints(call, this.ConstraintScope(node), use), mode, this.Library.PresentedTarget(call.Target));
                }

                if (consumer is { BindingFailure: not BindingFailure.None })
                {
                    this.CompleteDependent(node, consumer);
                }
            }
            else if (node is IsKoto { BoundRuntimeTest: { } runtimeTest } test)
            {
                var proof = this.CheckRuntimeTestTypeConstraints(runtimeTest, this.ConstraintScope(test), test);
                this.RequireConstraint(test, proof, mode);
                if (proof != ConstraintProof.Proven)
                {
                    test.BoundRuntimeTest = null;
                }
            }

            if (mode == BindingMode.Final && node.BindingFailure is BindingFailure.NoApplicableCandidate or BindingFailure.TypeMismatch && this.UnformedFieldRead(node) is { } unformed)
            {
                this.partPrerequisites[node] = unformed; // Derived from the Field's formation failure (IsDerived).
            }

            if (node.BindingState == BindingState.Unvisited)
            {
                // Skipped dependent syntax has no Type evidence, not a proof of an unsupported feature.
                // Actual subset gates diagnose themselves; the final fallback still rejects unresolved work.
                node.BindingState = BindingState.Unresolved;
            }
        }

        this.SettleSynthesizedCalls();
        this.FailNeededSites(); // The sites the loop's checks needed (BU-76).
        if (judged >= 0 && this.regionVersion != judged)
        {
            throw new InvalidOperationException("A region bound was recorded at Final after the erasures' Owned obligations were judged (InternalInvariant; R7-O2 A1+A2).");
        }
    }

    // SPEC 10.1, 23.1.2 (DECISIONS: G572 U2a): a call Binding synthesizes for a consumer (an iteration's entry and step, an
    // index, a view, a range, an operator's or a comparison's requirement, a formatting write, an object creation, a Property use's
    // accessor call) is bound into `nodes`, so the Final loop judges its obligation, recorded at its consumer as an invocation's verdict
    // at the invocation, and a consumer rests on a call failed before that loop, by its selection (SettleSynthesizedCalls): no Resolved
    // node stands over a failed call.
    private BoundType? BindSynthesizedCall(Koto consumer, InvocationKoto call, BindingScope scope)
    {
        this.AddSynthesizedCall(consumer, call);
        return this.BindCall(call, scope, null);
    }

    // SPEC 23.1.2, 11.3 (PLAN G214): the node a call's verdict (its Type's formation, its step five) is recorded at, failed with its
    // Type: an invocation's (or a call its own selection failed) is itself, a synthesized call's its consumer, whose witness call it is
    // (an iteration's entry or step, an index, a view, a range, a requirement, a write, a Property use's accessor call), at its span.
    private Koto VerdictUse(Koto node)
        => node.BindingFailure == BindingFailure.None && node is InvocationKoto call && this.synthesizedCalls.GetValueOrDefault(call) is { } consumer
            ? consumer : node;

    // A synthesized call joins `nodes` with its consumer; an accessor call is bound by BindPropertyCall to the accessor's execution view
    // (SPEC 11.3: a read or write is a call of that function), the others by BindCall.
    private void AddSynthesizedCall(Koto consumer, InvocationKoto call)
    {
        this.nodes.Add(call);
        this.synthesizedCalls.Add(call, consumer);
    }

    // The pass's synthesized calls in the order their row was first written (synthesizedOrder), each with its consumer's latest row.
    private void SettleSynthesizedCalls()
    {
        foreach (var call in this.synthesizedOrder)
        {
            var consumer = this.synthesizedCalls.GetValueOrDefault(call)!;
            if (call.BindingFailure != BindingFailure.None && consumer.BindingFailure == BindingFailure.None)
            {
                this.CompleteDependent(consumer, call);
            }
        }
    }

    // The pass's part of the summary (PassPart.Counts): each node's state, counted once the failures are decided, and each failed node's
    // issue, or its derived issue: the pass's own nodes, then the nodes of other modules it failed (PassPart.ForeignFailures), in the
    // order it failed them. Such a node was counted by its own pass in its earlier state, so the pass adds the state it recorded and
    // takes the earlier one off: the Bind's Result counts every node once, by its final state. A CheckBound rerun keeps the list (its
    // Fail calls return at the recorded failures), so the record is published once again, not twice.
    private void CountStates(BindingMode mode)
    {
        Span<int> counts = stackalloc int[4]; // Resolved, Unresolved, Invalid; and Resolved with a published problem (limited).
        for (var i = 0; i < this.nodes.Count; i++)
        {
            counts[Tally(this.nodes[i].BindingState)]++;
            counts[3] += this.CollectIssue(this.nodes[i], mode) ? 1 : 0;
        }

        for (var i = 0; i < this.foreignFailures.Count; i++)
        {
            var (node, before, state, _) = this.foreignFailures[i];
            counts[Tally(state)]++;
            counts[Tally(before)]--;
            counts[3] += this.CollectIssue(node, mode) ? 1 : 0;
        }

        this.pass.Part.Counts = new(mode, counts[0], counts[1], counts[2], counts[3]);

        static int Tally(BindingState state) => state switch
        {
            BindingState.Resolved => 0,
            BindingState.Invalid => 2,
            _ => 1,
        };
    }

    // A failed node's issue of a Final pass, or its derived issue, with its problems; a node without a failure publishes its problems that
    // set no slot (a located limit, RecordLocatedLimit, or a report's Defect; its Origin records are ReportResolvedOriginRecords'). True
    // when such a node published one: a rejected part, so the Bind is not complete (BindingResult.LimitedCount).
    private bool CollectIssue(Koto node, BindingMode mode)
    {
        var failure = mode == BindingMode.Final && this.RecordedState(node) != BindingState.Resolved ? this.RecordedFailure(node) : BindingFailure.None;
        if (failure == BindingFailure.None)
        {
            if (mode != BindingMode.Final || this.RecordedFailure(node) != BindingFailure.None)
            {
                return false;
            }

            var published = this.issues.Count + this.derivedIssues.Count;
            this.CollectProblems(node);
            return this.issues.Count + this.derivedIssues.Count != published;
        }

        this.CollectProblem(node, failure, this.CodeOf(node, failure), this.SlotProblem(node));
        this.CollectProblems(node);
        return false;
    }

    /// <summary>The `synthesizedCalls` family's one accessor (DECISIONS BU-220): a pass family, each call Binding synthesized in the pass with
    /// its consumer (AddSynthesizedCall), a row in the call's module's NodeColumns at its SyntaxId. Its rows are the running pass's working
    /// state, read only by that pass's checks (ProduceFailures, VerdictUse, SettleSynthesizedCalls, CompleteArguments), so the freeze
    /// releases them (NodeColumns.Freeze). Its members keep the names its readers use.</summary>
    private readonly struct SynthesizedCallRows(Binding binding)
    {
        // A call's row and its listing (synthesizedOrder's invariant): a call is listed at its row's first write; a later write
        // replaces its consumer.
        internal void Add(InvocationKoto call, Koto consumer)
        {
            ref var row = ref HirTables.Of(call).Families.SynthesizedCalls.Ref(call.SyntaxId, binding);
            if (row is null)
            {
                binding.synthesizedOrder.Add(call);
            }

            row = consumer;
        }

        internal Koto? GetValueOrDefault(InvocationKoto call) => call.SyntaxOwner is { } table ? table.Hir.Families.SynthesizedCalls.Get(call.SyntaxId, binding) : null;

        internal bool ContainsKey(InvocationKoto call) => this.GetValueOrDefault(call) is not null;
    }
}
