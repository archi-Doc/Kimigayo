// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The end of a Binding pass (Check; DECISIONS BU-81, refining R2b-D2).
// - Responsibility: the pass's failure producers (the definition obligations, the needed conformance paths and capability sites, the
//   call and runtime-test Constraint checks, the part prerequisites, Unvisited to Unresolved); in a Final pass, then, the HIR of every
//   module, the library included (PublishPass), and H1's TransferRequired judgment over it (CheckTransfers, DECISIONS BU-84), with one
//   republication when it failed a node; then the counting of the node states and the collection of the issues.
// - Inputs: the bound nodes, the obligations, the needed sites and paths, the facts the pass recorded.
// - Outputs: every failure the pass decides; the published HIR columns of a Final pass; the pass summary, issues and derived issues.
// - Invariants: every step that decides a failure runs before the publication, so the published columns describe the pass's final
//   facts; PublishHir() after a Final pass returns its version without work. CheckBound runs the same three steps again.
// - Prohibitions: the counting decides no failure and writes no fact the publisher reads.
public sealed partial class Binding
{
    private BindingResult Check(BindingMode mode)
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

        return this.CountStates(mode);
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
            else if (node is InvocationKoto { BoundCall: { } call })
            {
                this.RequireConstraint(node, this.CheckCallTypeConstraints(call, this.ConstraintScope(node)), mode);
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

        this.FailNeededSites(); // The sites the loop's checks needed (BU-76).
    }

    // The pass summary: each node's state, counted once the failures are decided, and each failed node's issue, or its derived issue.
    private BindingResult CountStates(BindingMode mode)
    {
        var resolved = 0;
        var unresolved = 0;
        var invalid = 0;
        for (var i = 0; i < this.nodes.Count; i++)
        {
            var node = this.nodes[i];
            switch (node.BindingState)
            {
                case BindingState.Resolved:
                    resolved++;
                    break;
                case BindingState.Invalid:
                    invalid++;
                    break;
                default:
                    unresolved++;
                    break;
            }

            if (mode == BindingMode.Final && node.BindingState != BindingState.Resolved && node.BindingFailure != BindingFailure.None)
            {
                if (this.IsDerived(node))
                {
                    this.derivedIssues.Add(node);
                }
                else
                {
                    this.issues.Add(new(node, this.CodeOf(node, node.BindingFailure)) { Failure = node.BindingFailure, Problem = this.SlotProblem(node) });
                }

                this.CollectProblems(node);
            }
        }

        return new(mode, resolved, unresolved, invalid);
    }
}
