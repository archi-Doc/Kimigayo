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
// - Outputs: every failure the pass decides; the published HIR columns of a Final pass; the pass summary, issues and derived issues.
// - Invariants: every step that decides a failure runs before the publication, so the published columns describe the pass's final
//   facts; PublishHir() after a Final pass returns its version without work. CheckBound runs the same three steps again.
// - Prohibitions: the counting decides no failure and writes no fact the publisher reads.
public sealed partial class Binding
{
    // Each call Binding synthesized in this pass, with its consumer (BindSynthesizedCall).
    private Dictionary<InvocationKoto, Koto>? synthesizedCalls;

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

    // Every failure the pass decides at its end, in the order the nodes were bound.
    private void ProduceFailures(BindingMode mode)
    {
        if (mode == BindingMode.Final)
        {
            this.formationCauses?.Clear();
            this.JudgeObligations();

            this.SettleSpecificationLinks();
            this.PublishNeededLimits();
            this.FailNeededSites();
            this.SettleDerivedUses();
        }

        for (var i = 0; i < this.nodes.Count; i++)
        {
            var node = this.nodes[i];
            // API and constraint validation can invalidate a target or Type after call selection; a node whose own selection calls a
            // declaration that failed (CallPlan.Called: a call's target, a Property use's accessors) rests on that failure (SPEC 23.1.2).
            if (this.InvalidCalledDeclaration(node) is { } invalidTarget && node.BindingFailure == BindingFailure.None)
            {
                this.CompleteDependent(node, invalidTarget);
            }
            else if (node is InvocationKoto synthesized && this.CallOf(synthesized) is not null &&
                this.synthesizedCalls?.GetValueOrDefault(synthesized) is { BindingFailure: not BindingFailure.None } consumer)
            {
                // SPEC 23.1.2: a synthesized call is its consumer's witness call (an iteration's entry or step, an index, a view, a range, a
                // requirement, a write); a consumer that has already failed leaves the call resting on it.
                this.CompleteDependent(synthesized, consumer);
            }
            else if (node is InvocationKoto invocation && this.CallOf(invocation) is { } call)
            {
                this.RequireClauses(node, this.CheckCallTypeConstraints(call, this.ConstraintScope(node)), mode, this.Library.PresentedTarget(call.Target));
            }
            else if (node is IsKoto { BoundRuntimeTest: { } runtimeTest } test)
            {
                var proof = this.CheckRuntimeTestTypeConstraints(runtimeTest, this.ConstraintScope(test));
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
    }

    // SPEC 10.1, 23.1.2 (DECISIONS: G572 U2a): a call Binding synthesizes for a consumer (an iteration's entry and step, an
    // index, a view, a range, an operator's or a comparison's requirement, a formatting write, an object creation) is bound into `nodes`,
    // so the Final loop judges its obligation, and with its consumer, which rests on it when that loop fails it (SettleSynthesizedCalls):
    // no Resolved node stands over a failed call.
    private BoundType? BindSynthesizedCall(Koto consumer, InvocationKoto call, BindingScope scope)
    {
        this.nodes.Add(call);
        (this.synthesizedCalls ??= new(ReferenceEqualityComparer.Instance))[call] = consumer;
        return this.BindCall(call, scope, null);
    }

    private void SettleSynthesizedCalls()
    {
        if (this.synthesizedCalls is not { } calls)
        {
            return;
        }

        foreach (var (call, consumer) in calls)
        {
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
        Span<int> counts = stackalloc int[3]; // Resolved, Unresolved, Invalid.
        for (var i = 0; i < this.nodes.Count; i++)
        {
            counts[Tally(this.nodes[i].BindingState)]++;
            this.CollectIssue(this.nodes[i], mode);
        }

        for (var i = 0; i < this.foreignFailures.Count; i++)
        {
            var (node, before, state, _) = this.foreignFailures[i];
            counts[Tally(state)]++;
            counts[Tally(before)]--;
            this.CollectIssue(node, mode);
        }

        this.pass.Part.Counts = new(mode, counts[0], counts[1], counts[2]);

        static int Tally(BindingState state) => state switch
        {
            BindingState.Resolved => 0,
            BindingState.Invalid => 2,
            _ => 1,
        };
    }

    // A failed node's issue of a Final pass, or its derived issue, with its problems.
    private void CollectIssue(Koto node, BindingMode mode)
    {
        var failure = mode == BindingMode.Final && this.RecordedState(node) != BindingState.Resolved ? this.RecordedFailure(node) : BindingFailure.None;
        if (failure == BindingFailure.None)
        {
            return;
        }

        if (this.IsDerived(node))
        {
            this.derivedIssues.Add(node);
        }
        else
        {
            this.issues.Add(new(node, this.CodeOf(node, failure)) { Failure = failure, Problem = this.SlotProblem(node) });
        }

        this.CollectProblems(node);
    }
}
