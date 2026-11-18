// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly ScratchBuffers<CallableCandidate> callableCandidateScratch = new();
    private readonly Dictionary<Koto, (int Start, int Count, int Position, AcquisitionMode Mode, AcquisitionMode Other)> callableSelectionFailures = new(ReferenceEqualityComparer.Instance);
    private readonly List<CallableCandidate> callableSelectionParties = [];

    // G812 ruling 132: what a value call's split check found at each argument (from the check's own start in the list): whether it
    // was prepared (one that failed there is rule W's wildcard), the parameter a run formed for it and whether that run read a split
    // layer (a binding of a search, or a formation that formed none); then the judgment, whether it fails in every case of what it
    // reads, and at which waiting part, with the canonical case's Type (none: a misfit, the call's own record).
    private readonly List<SplitPart> splitParts = [];

    // DECISIONS D3 with rule W (G812 rulings 123-132; temporary with the search): of a value call's split check, the part awaiting
    // its call (a named Call or a contextual Case) that failed the current part of a run, with the Type the run formed for its
    // position (runWaiting, WaitingFails); the current run's failure, the part (an argument's index, the result's after them, -1 the
    // call's argument count) with that waiting part, none for a misfit (runFailure, ItemPartFails); and the failure of the search's
    // first run, the canonical case (each binding at its first admitted Semantics, the combination the first run takes whatever order
    // later runs take; ruling 130), with whether every failing run failed at that same part and waiting part (splitFailure,
    // FoldRunFailure; null before the first run).
    private (Koto? Part, BoundType? Type) runWaiting;
    private (int Part, Koto? Waiting, BoundType? Type) runFailure;
    private (int Part, Koto? Waiting, BoundType? Type, bool Same)? splitFailure;

    // DECISIONS D3 (G812 ruling 139): the first run's selection of a named call's split check (SelectionOnSplit; null before it).
    private ProbedSelection? canonicalSelection;

    // SPEC 8.2, 8.4: a Type as a scope's premises make it, its associated projections normalized and its Type-identity premises substituted.
    internal BoundType CallableContractType(BoundType type, BindingScope scope)
        => this.SubstituteIdentityPremises(this.ContractType(type, scope), scope);

    // Whole-contract equality includes fixed Origins and alpha-equivalent per-call binders, not just the storage Type.
    private static bool SameCallableSignature(BoundType a, BoundType b)
        => ReferenceEquals(a, b) || (IsSubtype(a, b, a.Binder) && IsSubtype(b, a, b.Binder));

    private static bool SameProofPremise(BoundConstraint a, BoundConstraint b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Kind == ConstraintKind.Not && b.Kind == ConstraintKind.Not)
        {
            return SameProofPremise(a.Left!, b.Left!);
        }

        return a.Kind == ConstraintKind.Callable && b.Kind == ConstraintKind.Callable && a.Mask == b.Mask && ReferenceEquals(a.Subject, b.Subject) && SameCallableSignature(a.RequiredType!, b.RequiredType!);
    }

    private static SemanticsKind CallableReceiver(SemanticsMask mask)
        => mask == SemanticsMask.Ref ? SemanticsKind.Ref : mask == SemanticsMask.Uniq ? SemanticsKind.Uniq : SemanticsKind.Owner;

    private static SemanticsKind WeakestReceiver(SemanticsKind a, SemanticsKind b)
        => a == SemanticsKind.Ref || b == SemanticsKind.Ref ? SemanticsKind.Ref : a == SemanticsKind.Uniq || b == SemanticsKind.Uniq ? SemanticsKind.Uniq : SemanticsKind.Owner;

    private BoundType? SelectValueCall(InvocationKoto call, BindingScope scope, BoundType type, BoundType? expected)
    {
        var owner = this.CallableContractType(type.Kind == BoundTypeKind.Semantics ? type.Components[0] : type, scope);

        // The candidates are the callee's available Callable rows (TryCallable's), one per normalized signature with its weakest
        // receiver; a failure names a candidate's first row by its Source (the written premise, or the clause and its scope).
        var start = this.premiseRows.Count;
        this.FirstRows(owner, scope, this.premiseRows);
        var rows = this.premiseRows.Count - start;
        var candidates = this.callableCandidateScratch.Rent(rows);
        var count = 0;
        try
        {
            for (var r = start; r < start + rows; r++)
            {
                if (this.premiseRows[r] is not { Available: true, Clause: { Kind: ConstraintKind.Callable } fact } row)
                {
                    continue;
                }

                var signature = this.CallableContractType(fact.RequiredType!, scope);
                var receiver = CallableReceiver(fact.Mask);
                var index = 0;
                while (index < count && !SameCallableSignature(candidates[index].Signature, signature))
                {
                    index++;
                }

                if (index == count)
                {
                    candidates[count++] = new(signature, receiver, row.Raw ?? fact, (row.Stated ?? scope).Owner);
                }
                else
                {
                    candidates[index] = candidates[index] with { Receiver = WeakestReceiver(candidates[index].Receiver, receiver) };
                }
            }

            this.premiseRows.RemoveRange(start, rows);
            var choices = candidates.AsSpan(0, count);
            for (var a = 0; a < count; a++)
            {
                for (var b = 0; b < a; b++)
                {
                    if (candidates[a].Receiver != candidates[b].Receiver)
                    {
                        return this.FailCallableCandidates(call, choices, BindingFailure.ReceiverShapeMismatch);
                    }

                    var left = candidates[a].Signature.Components[0].Components;
                    var right = candidates[b].Signature.Components[0].Components;
                    for (var p = 0; p < Math.Min(left.Count, right.Count); p++)
                    {
                        if (!ReferenceEquals(left[p], right[p]) && this.ParameterShapesConflict(left[p], right[p], this.ShapeOf(left[p], scope.Owner, true), this.ShapeOf(right[p], scope.Owner, true), out var mode, out var other, out _, out _))
                        {
                            return this.FailCallableCandidates(call, choices, BindingFailure.ParameterShapeMismatch, p, mode, other);
                        }
                    }
                }
            }

            if (call.Method is GenericsKoto)
            {
                return this.FailCallableCandidates(call, choices, BindingFailure.NoApplicableCandidate);
            }

            var unknownArgument = !this.PrepareCallArguments(call, scope, callableContext: true);
            var stride = call.ArgumentNodes.Count + 1;
            var operations = this.argumentOperationScratch.Rent(count * stride);
            var evaluated = this.candidateScratch.Rent(count);
            var classBase = this.positionClasses.Count;
            try
            {
                var applicable = 0;
                for (var i = 0; i < count; i++)
                {
                    var map = MapValueCallArguments(call, candidates[i].Signature);
                    var state = map.Valid ? this.ProbeValueCall(call, scope, candidates[i].Signature, expected, operations.AsSpan(i * stride, stride)) : CandidateApplicability.Inapplicable;
                    evaluated[i] = new(null, state, null, 0, ArgumentMap: map);
                    if (state is CandidateApplicability.Applicable or CandidateApplicability.Waiting)
                    {
                        applicable++;
                    }
                }

                if (unknownArgument && AnyArgumentsMatch(evaluated.AsSpan(0, count), null, []))
                {
                    return Complete(call, null); // As at a named call (BindCallCore): only argument matching's NoApplicable is reported.
                }

                var selected = this.SelectBest(evaluated.AsSpan(0, count), operations, stride);
                if (selected >= 0)
                {
                    return this.BindValueCall(call, scope, candidates[selected].Signature, candidates[selected].Receiver);
                }

                if (applicable != 0)
                {
                    var remaining = 0;
                    for (var i = 0; i < count; i++)
                    {
                        if (evaluated[i].State is CandidateApplicability.Applicable or CandidateApplicability.Waiting)
                        {
                            candidates[remaining++] = candidates[i];
                        }
                    }

                    choices = candidates.AsSpan(0, remaining);
                }

                return this.FailCallableCandidates(call, choices, applicable == 0 ? BindingFailure.NoApplicableCandidate : BindingFailure.Ambiguous);
            }
            finally
            {
                this.argumentOperationScratch.Return(operations, clearArray: true);
                this.candidateScratch.Return(evaluated, clearArray: true);
                this.positionClasses.RemoveRange(classBase, this.positionClasses.Count - classBase);
            }
        }
        finally
        {
            this.callableCandidateScratch.Return(candidates, clearArray: true);
        }
    }

    // DECISIONS D3 with rule W (BU-79; C-359 C1; G812 rulings 123-132; temporary, PLAN G886): a value call whose callee signature (a
    // Function Item's) formed a Type no binding represents. Its binder is fixed before any evidence, so the call is probed in each
    // combination of split cases (RestsOnSplitCases, ItemCallFails), an argument that failed its preparation being rule W's
    // wildcard, and each part is then judged as the unsplit path (BindValueCall) judges it, every failing part keeping its record
    // (SPEC 23.1.2; ruling 132):
    // - where the call fails in every combination, each part from the canonical case's first failing one on is judged in every case
    //   of the bindings it reads: the search's verdict where every run failed there, else the part's own search (JudgeSplitPart);
    //   one that fails so is a misfit, the call's own record, or a waiting Call or Case, completed at the canonical case's Type,
    //   where its failure is its own record (CompleteFailedPart); the parts before it passed that case, and the result is judged
    //   only where it failed first (a call that forms no result checks none: BindValueCall);
    // - a part whose parameter reads no split layer is completed at it as the unsplit path completes it (CompleteValueCallPart): its
    //   check rests on nothing, so an anonymous function's body is judged; in any other part that fails in no case, each anonymous
    //   function its tree holds is completed at the canonical case's Type for its position (CompletePartOnSplit, CompleteSplitPart);
    // - the call then fails by itself on a misfit, or where it fails in every combination with no part failing so; else it rests on
    //   a failed part (rule W), else on the split (one located Unsupported_Kd, O-332 (a)).
    private BoundType? BindValueCallOnSplit(InvocationKoto call, BindingScope scope, BoundType callee, BoundType? expected, int mark)
    {
        if (call.Method is GenericsKoto || this.CalledThrough(callee, scope) is not { Kind: BoundTypeKind.FunctionItem, Symbol.Declaration: FunctionKoto } item)
        {
            return this.Fail(call, BindingFailure.Unsupported, true);
        }

        var count = call.ArgumentNodes.Count;
        var parts = this.splitParts.Count;
        for (var i = 0; i < count; i++)
        {
            this.splitParts.Add(new(this.PrepareArgument(call.ArgumentNodes[i], scope, default, callableContext: true), null, true, false, null, null));
        }

        for (var i = 0; i < count; i++)
        {
            this.PrepareWaitingCases(call.ArgumentNodes[i], scope); // A run binds nothing (G812 ruling 123).
        }

        var (classBase, enclosing) = (this.positionClasses.Count, this.splitFailure);
        try
        {
            this.splitFailure = null;
            var rests = this.RestsOnSplitCases(
                mark,
                (call, scope, item, expected, parts),
                static (binding, check) => binding.FoldRunFailure(binding.ItemCallFails(check.call, check.scope, check.item, check.expected, check.parts)));
            var canonical = this.splitFailure.GetValueOrDefault((-1, null, null, false));
            var (own, found) = (!rests && canonical.Part < 0, false);
            for (var i = rests ? -1 : canonical.Part; i >= 0 && (i < count || i == canonical.Part); i++)
            {
                var (fails, waiting, type) = i == canonical.Part && canonical.Same ? (true, canonical.Waiting, canonical.Type)
                    : this.JudgeSplitPart((call, scope, item, expected, parts, i));
                (own, found) = (own || (fails && waiting is null), found || fails);
                if (fails && i < count)
                {
                    this.splitParts[parts + i] = this.splitParts[parts + i] with { Fails = true, Waiting = waiting, Type = type };
                }
            }

            Koto? failed = null;
            for (var i = 0; i < count; i++)
            {
                var part = this.splitParts[parts + i];
                if (part is { Fails: true, Waiting: { } waiting })
                {
                    this.CompleteFailedPart(waiting, part.Type!, scope);
                    failed ??= waiting;
                }
                else if (part is { Fails: false, Prepared: true } && this.CompletePartOnSplit(call.ArgumentNodes[i], part, item, scope, i) is { } completed)
                {
                    failed ??= completed;
                }
            }

            return own || (!rests && !found) ? this.Fail(call, BindingFailure.NoApplicableCandidate)
                : failed is not null ? this.CompleteDependent(call, failed) : this.Fail(call, BindingFailure.Unsupported, true);
        }
        finally
        {
            this.positionClasses.RemoveRange(classBase, this.positionClasses.Count - classBase);
            this.splitParts.RemoveRange(parts, this.splitParts.Count - parts);
            this.splitFailure = enclosing;
        }
    }

    // G812 rulings 132, 136: a part of a value call on the split that fails in no case, completed: one whose run read no split layer at
    // the parameter that run formed (H2), any other by the walk of its tree (CompleteSplitPart), each anonymous function in it at the
    // Type the canonical case forms for its position, as ItemPart forms the Item's parameter (none past the Item's parameters: the
    // call fails by its count). The part that failed so, else null.
    private Koto? CompletePartOnSplit(Koto argument, in SplitPart part, BoundType item, BindingScope scope, int i)
        => !part.Split ? (part.Parameter is { } parameter && this.CompleteValueCallPart(argument, parameter, scope) is null ? argument : null)
            : ((FunctionKoto)item.Symbol!.Declaration).Parameters is var parameters && i < parameters.Count && parameters[i].Type.BoundType is { } declared
                ? this.CompleteSplitPart(argument, declared, (item, scope), static (binding, state, type) => binding.ItemPart(type, state.item, (FunctionKoto)state.item.Symbol!.Declaration, state.scope), scope)
                : null;

    // G812 ruling 132: whether one part of a value call's split check (an argument, or the result after them) fails in every case of
    // the bindings it reads, by its own search, and the waiting part it fails at with the canonical case's Type where every failing
    // run failed there (none: a misfit, or a failure that moves between its waiting parts by case: the call's own record).
    private (bool Fails, Koto? Waiting, BoundType? Type) JudgeSplitPart((InvocationKoto Call, BindingScope Scope, BoundType Item, BoundType? Expected, int Parts, int Part) check)
    {
        this.splitFailure = null;
        var fails = !this.RestsOnSplitCases(
            this.unformedWrites,
            check,
            static (binding, check) => binding.FoldRunFailure(binding.ItemPartFails(check.Call, check.Scope, check.Item, check.Expected, check.Parts, check.Part)));
        return fails && this.splitFailure is { Same: true } failure ? (true, failure.Waiting, failure.Type) : (fails, null, null);
    }

    // G812 rulings 126, 130, 132: a failing run's failure (runFailure) folded into its search's: the first run's is kept, the canonical
    // case's, and stays the same while every failing run failed at that part and waiting part. How a part fails can differ with the
    // Type (`pick(.None)` is ambiguous at `Box` and has no candidate at `raw/Box`), so a record is the canonical case's (SPEC 8.15.2
    // leaves the enumeration unspecified, 23.1.2 groups one failure found under several cases).
    private bool FoldRunFailure(bool fails)
    {
        if (fails)
        {
            var run = this.runFailure;
            this.splitFailure = this.splitFailure is { } kept
                ? (kept.Part, kept.Waiting, kept.Type, kept.Same && kept.Part == run.Part && ReferenceEquals(kept.Waiting, run.Waiting))
                : (run.Part, run.Waiting, run.Type, true);
        }

        return fails;
    }

    // SPEC 10.1.1, 10.5.1, 12.5.2: whether a value call fails against a Function Item's signature in a run: the argument count, then
    // each part in order (ItemPartFails); the run's failure is its first failing part's, which the search blames (RestsOnSplitCases).
    private bool ItemCallFails(InvocationKoto call, BindingScope scope, BoundType item, BoundType? expected, int parts)
    {
        this.runFailure = (-1, null, null);
        if (((FunctionKoto)item.Symbol!.Declaration).Parameters.Count != call.ArgumentNodes.Count)
        {
            return true;
        }

        for (var i = 0; i <= call.ArgumentNodes.Count; i++)
        {
            if (this.ItemPartFails(call, scope, item, expected, parts, i))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 10.1.1, 10.5.1, 12.5.2: whether one part of a value call fails against a Function Item's signature in a run, probed as
    // against a Callable candidate's (MapValueCallArguments, ProbeValueCall) but formed parameter by parameter: argument `i` at its
    // parameter (a label fails it), or with `i` past the arguments the result at `expected`. A search over split cases so reads only
    // the bindings of the part (RestsOnSplitCases). A part that forms no Type fails nothing. An argument that fails at a component of
    // its parameter (a Tuple's element, a constructed Type's argument, an array's element), a literal's or a typed value's, fails by
    // that component: the fit names it (argumentMisfit), and the declared component, formed again on its own, is the failing part,
    // so the run blames the bindings its layers read and not its siblings' (the owner's O-392 (c), G812 rulings 109, 111; the
    // contract fit's member is the same reading, ContractFits). A failure at the own shape of a part that formation keeps (a Tuple's
    // arity, an array's kind) reads no binding: the failing part forms nothing, so the run blames none (ruling 116). The Item is the
    // selected candidate, so the parts awaiting their call that the fit met are judged at the Types the run formed (WaitingPartFails;
    // rulings 118, 122, 123). The argument's parameter and whether the run read a split layer for it are kept (splitParts), and a
    // failure is the run's (runFailure).
    private bool ItemPartFails(InvocationKoto call, BindingScope scope, BoundType item, BoundType? expected, int parts, int i)
    {
        this.EnterSplitPart();
        var (stamp, mark, function) = (this.splitReads, this.unformedWrites, (FunctionKoto)item.Symbol!.Declaration);
        this.runWaiting = default;
        bool fails;
        if (i == call.ArgumentNodes.Count)
        {
            fails = expected is not null && this.ItemPart(item.Symbol.Type, item, function, scope) is { } formed &&
                !this.FitsStructuralPartAt(formed, expected, call) && this.ExpectedAdaptation(call, formed, expected, recordPair: false) is null;
        }
        else if (call.GetArgumentLabel(i) is not null)
        {
            fails = true;
        }
        else
        {
            var declared = function.Parameters[i].Type.BoundType;
            var parameter = this.ItemPart(declared, item, function, scope);
            fails = parameter is not null &&
                (this.FitArgument(call.ArgumentNodes[i], parameter, scope, call, out _) == CandidateApplicability.Inapplicable || this.WaitingPartFails(scope));
            this.splitParts[parts + i] = this.splitParts[parts + i] with { Parameter = parameter, Split = this.ReadSplitSince(stamp, mark) };
            if (fails && this.argumentMisfit.Names(declared!, out var element))
            {
                this.EnterSplitPart();
                _ = this.ItemPart(element, item, function, scope);
            }
        }

        this.runFailure = fails ? (i, this.runWaiting.Part, this.runWaiting.Type) : this.runFailure;
        return fails;
    }

    // SPEC 10.5.1, 23.1.2 (G812 rulings 122, 123): the value call's one candidate checks the parts awaiting their call that its
    // argument's fit met (waitingParts), each at the Type the run formed for its position, as the selected candidate completes them
    // (WaitingFails); a part met inside a Case's payload is judged below the Case. The failing part's components from the argument
    // are argumentMisfit's. The parts are copied first (judgedParts), since a Call's probe fits its own arguments.
    private bool WaitingPartFails(BindingScope scope)
    {
        var first = this.judgedParts.Count;
        this.judgedParts.AddRange(this.waitingParts);
        try
        {
            for (var i = first; i < this.judgedParts.Count; i++)
            {
                if (this.WaitingFails(this.judgedParts[i], scope))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            this.judgedParts.RemoveRange(first, this.judgedParts.Count - first);
        }
    }

    // One waiting part at its position's Type: a contextual Case by FitCaseAt, whose own waiting parts join the judged ones below it;
    // a named Call by its own selection there, judged in full (ProbeSelection, SelectionFails: no candidate, an ambiguity or an unproven premise
    // is its failure, at its position). Any other selection, or another part, waits for its call. A failing one is the run's
    // (runWaiting), with that Type.
    private bool WaitingFails(in WaitingPart waiting, BindingScope scope)
    {
        var node = KotoHelper.UnwrapParentheses(waiting.Part);
        if (this.KindOf(node) != PartKind.Case)
        {
            var fails = node is InvocationKoto nested && SelectionFails(this.ProbeSelection(nested, scope, waiting.Type).Kind);
            this.argumentMisfit = waiting.Path; // After the probe, whose own fits clear it.
            this.runWaiting = fails ? (node, waiting.Type) : default;
            return fails;
        }

        var met = this.waitingParts.Count;
        if (this.FitCaseAt(node, waiting.Type, scope) == CandidateApplicability.Inapplicable)
        {
            this.argumentMisfit = this.argumentMisfit.Within(waiting.Path);
            this.runWaiting = (node, waiting.Type);
            return true;
        }

        for (var k = met; k < this.waitingParts.Count; k++)
        {
            this.judgedParts.Add(this.waitingParts[k] with { Path = this.waitingParts[k].Path.Within(waiting.Path) });
        }

        this.waitingParts.RemoveRange(met, this.waitingParts.Count - met);
        return false;
    }

    // One part of a Function Item's signature as its value call reads it (TryCallable: ItemType, then the scope's contract Type).
    private BoundType? ItemPart(BoundType? declared, BoundType item, FunctionKoto function, BindingScope scope)
        => declared is not null && this.ItemType(declared, item, function) is { } part ? this.ContractType(part, scope) : null;

    private CandidateApplicability ProbeValueCall(InvocationKoto call, BindingScope scope, BoundType signature, BoundType? expected, Span<BoundArgumentOperation> operations)
    {
        operations.Clear();
        var parameters = signature.Components[0].Components;
        for (var i = 0; i < parameters.Count; i++)
        {
            // SPEC 10.5.1: each argument fits its parameter as a call argument does; a waiting nested call is checked by the selected signature.
            var state = this.FitArgument(call.ArgumentNodes[i], parameters[i], scope, call, out var operation);
            if (state is not (CandidateApplicability.Applicable or CandidateApplicability.Waiting))
            {
                return state;
            }

            operations[i] = operation with { ParameterIndex = i };
        }

        var result = signature.Components[1];
        return expected is not null && !this.FitsStructuralPartAt(result, expected, call) && this.ExpectedAdaptation(call, result, expected, recordPair: false) is null
            ? CandidateApplicability.Inapplicable : CandidateApplicability.Applicable;
    }

    private BoundType? FailCallableCandidates(InvocationKoto call, ReadOnlySpan<CallableCandidate> candidates, BindingFailure failure, int position = -1, AcquisitionMode mode = default, AcquisitionMode other = default)
    {
        var start = this.callableSelectionParties.Count;
        foreach (var candidate in candidates)
        {
            this.callableSelectionParties.Add(candidate);
        }

        this.callableSelectionFailures[call] = (start, candidates.Length, position, mode, other);
        return this.FailWaitingSelection(call, failure);
    }

    private bool ReportCallableSelection(Koto node, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        if (!this.callableSelectionFailures.TryGetValue(node, out var failure))
        {
            return false;
        }

        var related = new (string Role, Koto At, string? Label)[failure.Count];
        for (var i = 0; i < failure.Count; i++)
        {
            var candidate = this.callableSelectionParties[failure.Start + i];
            var at = candidate.Context;

            // After the Bind (ReportDiagnostics): every pass's nodes.
            foreach (var syntax in this.nodesView)
            {
                if (syntax is IsKoto clause && ReferenceEquals(clause.BoundConstraint, candidate.Fact))
                {
                    at = clause;
                    break;
                }
            }

            related[i] = ("candidate", at, $"Callable<{candidate.Receiver.ToString().ToLowerInvariant()}, {DiagnosticTypeName(candidate.Signature)}>");
        }

        if (code == DiagnosticCode.ParameterShapeMismatch_Kd)
        {
            node.Report(
                requirement,
                code,
                failure.Mode,
                failure.Other,
                evidence: [$"position {failure.Position + 1} of a Callable call", "overlapping Types"],
                related: related,
                note: "All available Callable signatures must agree on acquisition before arguments exclude any candidate (SPEC 8.10, 7.4)");
        }
        else
        {
            var note = code == DiagnosticCode.ReceiverShapeMismatch_Kd ? "Distinct Callable signatures require one receiver; each signature uses its weakest declared receiver (SPEC 8.10)"
                : code == DiagnosticCode.AmbiguousBinding_Kd ? "No Callable signature is better than every other applicable signature under the ordinary argument ranking rules (SPEC 10.4)"
                : "No Callable signature applies; candidate checks use positional arguments and any fixed expected result (SPEC 8.10, 10.1)";
            node.Report(requirement, code, evidence: code == DiagnosticCode.ReceiverShapeMismatch_Kd ? null : [failure.Count], related: related, note: note);
        }

        return true;
    }

    private readonly record struct CallableCandidate(BoundType Signature, SemanticsKind Receiver, BoundConstraint Fact, Koto Context);

    private readonly record struct SplitPart(bool Prepared, BoundType? Parameter, bool Split, bool Fails, Koto? Waiting, BoundType? Type);
}
