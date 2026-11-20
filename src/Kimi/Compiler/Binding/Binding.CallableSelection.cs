// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly ScratchBuffers<CallableCandidate> callableCandidateScratch = new();
    private readonly CallableSelectionFailureRows callableSelectionFailures;
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

    // DECISIONS D3 (G812 ruling 158): the one walk that unmarks a completed split part's published Types (CompleteOnSplit), kept.
    private OpenMarkRemover? openMarkRemover;

    // SPEC 8.2, 8.4: a Type as a scope's premises make it, its associated projections normalized and in its identity class's form (ClassForm).
    internal BoundType CallableContractType(BoundType type, BindingScope scope)
        => this.ClassForm(this.ContractType(type, scope), scope, out _);

    private static SemanticsKind CallableReceiver(SemanticsMask mask)
        => mask == SemanticsMask.Ref ? SemanticsKind.Ref : mask == SemanticsMask.Uniq ? SemanticsKind.Uniq : SemanticsKind.Owner;

    private static SemanticsKind WeakestReceiver(SemanticsKind a, SemanticsKind b)
        => a == SemanticsKind.Ref || b == SemanticsKind.Ref ? SemanticsKind.Ref : a == SemanticsKind.Uniq || b == SemanticsKind.Uniq ? SemanticsKind.Uniq : SemanticsKind.Owner;

    // Whole-contract equality includes fixed Origins and alpha-equivalent per-call binders, not just the storage Type.
    private bool SameCallableSignature(BoundType a, BoundType b)
        => ReferenceEquals(a, b) || (this.IsSubtype(a, b) && this.IsSubtype(b, a));

    private bool SameProofPremise(BoundConstraint a, BoundConstraint b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Kind == ConstraintKind.Not && b.Kind == ConstraintKind.Not)
        {
            return this.SameProofPremise(a.Left!, b.Left!);
        }

        return a.Kind == ConstraintKind.Callable && b.Kind == ConstraintKind.Callable && a.Mask == b.Mask && ReferenceEquals(a.Subject, b.Subject) && this.SameCallableSignature(a.RequiredType!, b.RequiredType!);
    }

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
                while (index < count && !this.SameCallableSignature(candidates[index].Signature, signature))
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
            var positionBase = this.positionOperations.Count;
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
                this.positionOperations.RemoveRange(positionBase, this.positionOperations.Count - positionBase);
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
    //   check rests on nothing, so an anonymous function's body is judged; any other part that fails in no case and awaits the call is
    //   completed the same way at the canonical case's open formed parameter (CompletePartOnSplit, OpenForm), where every check that
    //   reads a position a split binding decides rests;
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

        var (positionBase, enclosing) = (this.positionOperations.Count, this.splitFailure);
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
                    : this.JudgeSplitPart((call, scope, item, expected, parts, i), static (binding, c) => binding.ItemPartFails(c.call, c.scope, c.item, c.expected, c.parts, c.i));
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
                else if (part is { Fails: false, Prepared: true } && this.CompletePartOnSplit(call, part, item, scope, i) is { } completed)
                {
                    failed ??= completed;
                }
            }

            return own || (!rests && !found) ? this.Fail(call, BindingFailure.NoApplicableCandidate)
                : failed is not null ? this.CompleteDependent(call, failed) : this.Fail(call, BindingFailure.Unsupported, true);
        }
        finally
        {
            this.positionOperations.RemoveRange(positionBase, this.positionOperations.Count - positionBase);
            this.splitParts.RemoveRange(parts, this.splitParts.Count - parts);
            this.splitFailure = enclosing;
        }
    }

    // G812 rulings 132, 136, 143: a part of a value call on the split that fails in no case, completed by the unsplit completion
    // (CompleteValueCallPart): one whose run read no split layer at the parameter that run formed (H2), any other part awaiting the call
    // at the Item's parameter in the canonical case, marked open where a split binding decides it (OpenForm, as ItemPart forms the
    // parameter; none past the Item's parameters: the call fails by its count). A typed part awaits nothing; one at a parameter open as a
    // whole rests (RestsAtOpenPosition). The part that failed so, else null (CompleteOnSplit).
    private Koto? CompletePartOnSplit(InvocationKoto call, in SplitPart part, BoundType item, BindingScope scope, int i)
        => (!part.Split ? part.Parameter
            : this.KindOf(call.ArgumentNodes[i]) != PartKind.Typed && ((FunctionKoto)item.Symbol!.Declaration).Parameters is var parameters && i < parameters.Count &&
                parameters[i].Type.BoundType is { } declared
                ? this.OpenForm(declared, (item, scope), static (binding, state, type) => binding.ItemPart(type, state.item, (FunctionKoto)state.item.Symbol!.Declaration, state.scope))
                : null) is { } parameter && this.CompleteOnSplit(call.ArgumentNodes[i], parameter, scope, call) ? call.ArgumentNodes[i] : null;

    // DECISIONS D3 (rule W): a resting part of a split call completed at `parameter` by the unsplit completion (CompleteValueCallPart),
    // read by no check of that call but its failure (SPEC 23.1.2: the call rests on a failed part, CompleteDependent; as
    // CompleteUnselected, no frame is open): whether it failed, by its own verdict (G812 ruling 151): its own record, or the record it
    // rests on, is reported (FailureReported, the one dependence test; G812 ruling 164). An input pending with no record (a join's arm
    // waiting for the call's solve) is no failure cause. A part that rests where a split binding decides its position
    // (RestsAtOpenPosition) or below it (RestsBelowOpenPosition) is the split's own limit and fails nothing. Every node of the part the
    // split leaves open rests on `call` (OpenMarkRemover, G812 ruling 165): the call cannot be formed, so its located record, or the
    // record it rests on, is that node's cause, and a check reading the node derives from a reported record.
    private bool CompleteOnSplit(Koto part, BoundType parameter, BindingScope scope, InvocationKoto call)
    {
        var consultation = this.SuspendConsultation();
        try
        {
            var failed = !RestsAtOpenPosition(part, parameter) && this.CompleteValueCallPart(part, parameter, scope) is null && this.FailureReported(part);
            (this.openMarkRemover ??= new(this)).Complete(part, call);
            return failed;
        }
        finally
        {
            this.ResumeConsultation(consultation);
        }
    }

    // DECISIONS D3 (temporary with BoundTypeKind.Open; G812 rulings 158, 165): the walk over a split call's completed part, the one
    // place the split completes. The completion is the one former of marks (OpenForm), and a mark is never a node's published Type, so
    // it gives each node of the part whose Type holds one the canonical case's Type (Unmarked), its state unchanged: a node that took its
    // Type from its marked position (a waiting call's inferred slot, a join, a Never Tuple element, a construction target). The binder
    // read the marked Type it returned; the HIR publisher's Broken check guards every other writer. A node the split leaves open (not
    // resolved, and no record of its own or below it is reported: FailureReported), such as a join's waiting arm, an open-typed closure
    // parameter's use or any argument still pending, rests on the call (CompleteDependent), whose located record (or the record it rests
    // on) becomes its cause; inner nodes first, so an enclosing one meets them resting.
    private sealed class OpenMarkRemover(Binding binding) : KotoVisitor
    {
        private InvocationKoto? call;

        public override void Visit(Koto node)
        {
            if (node.BoundType is { ContainsOpen: true } marked)
            {
                node.BoundType = binding.Unmarked(marked);
            }

            node.VisitChildren(this);
            if (node.BindingState == BindingState.Unresolved && !binding.FailureReported(node))
            {
                binding.CompleteDependent(node, this.call!);
            }
        }

        internal void Complete(Koto part, InvocationKoto call)
        {
            this.call = call;
            this.Visit(part);
            this.call = null;
        }
    }

    // G812 rulings 132, 156: whether one part of a split call's check (a value call's argument or its result after them, a named call's
    // argument) fails in every case of the bindings it reads, by its own search (`fails`, one run's judgment of the part), and the
    // waiting part it fails at with the canonical case's Type where every failing run failed there (none: a misfit, or a failure that
    // moves between its waiting parts by case: the call's own record).
    private (bool Fails, Koto? Waiting, BoundType? Type) JudgeSplitPart<TState>(TState check, Func<Binding, TState, bool> fails)
    {
        this.splitFailure = null;
        var failed = !this.RestsOnSplitCases(this.unformedWrites, (check, fails), static (binding, s) => binding.FoldRunFailure(s.fails(binding, s.check)));
        return failed && this.splitFailure is { Same: true } failure ? (true, failure.Waiting, failure.Type) : (failed, null, null);
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
        this.EnterRunPart();
        var function = (FunctionKoto)item.Symbol!.Declaration;
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
            fails = this.ArgumentPartFails(
                call.ArgumentNodes[i],
                function.Parameters[i].Type.BoundType,
                (item, function, scope),
                static (binding, s, type) => binding.ItemPart(type, s.item, s.function, s.scope),
                scope,
                call,
                out var parameter,
                out var split);
            this.splitParts[parts + i] = this.splitParts[parts + i] with { Parameter = parameter, Split = split };
        }

        return this.EndRunPart(i, fails);
    }

    // DECISIONS D3 (G812 rulings 132, 156): a run's judgment of one part of a split call's check starts (a part of its own,
    // EnterSplitPart, with no waiting part failed yet) and ends: a failure is the run's, with the waiting part it failed at (runFailure).
    private void EnterRunPart()
    {
        this.EnterSplitPart();
        this.runWaiting = default;
    }

    private bool EndRunPart(int part, bool fails)
    {
        this.runFailure = fails ? (part, this.runWaiting.Part, this.runWaiting.Type) : this.runFailure;
        return fails;
    }

    // SPEC 10.5.1, 23.1.2 (G812 rulings 109, 111, 122, 123, 156): in a run, whether an argument fails at the parameter `form` gives its
    // declared Type (`parameter`; none: it fails nothing), with whether that formation read a split layer (`split`): by its fit, or at
    // a part awaiting its call that the fit met (WaitingPartFails). A failing component the fit names (argumentMisfit) is formed again on
    // its own as the failing part, so the run blames the bindings its layers read. A value call's part forms at its Item (ItemPart), a
    // named call's at the canonical run's candidate (FailUnformedCall).
    private bool ArgumentPartFails<TState>(
        Koto argument, BoundType? declared, TState state, Func<Binding, TState, BoundType, BoundType?> form, BindingScope scope, Koto use, out BoundType? parameter, out bool split)
    {
        var (stamp, mark) = (this.splitReads, this.unformedWrites);
        parameter = declared is null ? null : form(this, state, declared);
        var fails = parameter is not null &&
            (this.FitArgument(argument, parameter, scope, use, out _) == CandidateApplicability.Inapplicable || this.WaitingPartFails(scope));
        split = this.ReadSplitSince(stamp, mark);
        if (fails && this.argumentMisfit.Names(declared!, out var element))
        {
            this.EnterSplitPart(); // A shape's failure names no member: the part forms nothing, so the run blames none (ruling 116).
            _ = element is null ? null : form(this, state, element);
        }

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

    private bool ReportCallableSelection(Koto node, DiagnosticCollection? store, DiagnosticRequirement requirement, DiagnosticCode code)
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
            node.ReportIn(
                store,
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
            node.ReportIn(store, requirement, code, evidence: code == DiagnosticCode.ReceiverShapeMismatch_Kd ? null : [failure.Count], related: related, note: note);
        }

        return true;
    }

    /// <summary>The `callableSelectionFailures` family's one accessor (DECISIONS BU-220): a pass family, a failed Callable value call's
    /// candidates (a range of callableSelectionParties) and its parameter-shape position, stored with its failure by the recorder
    /// (RecordedFact; a row may be all zero but for its presence). Its members keep the names its readers use.</summary>
    private readonly struct CallableSelectionFailureRows(Binding binding)
    {
        // The family's column in a NodeColumns, the store the recorder decides (RecordedFact, SetRecordedFact).
        internal static ref StampedColumn<(bool Failed, int Start, int Count, int Position, AcquisitionMode Mode, AcquisitionMode Other), PassCounter> Family(ref NodeColumns columns)
            => ref columns.CallableSelectionFailures;

        internal (int Start, int Count, int Position, AcquisitionMode Mode, AcquisitionMode Other) this[Koto call]
        {
            set => binding.SetRecordedFact(call, Family, (true, value.Start, value.Count, value.Position, value.Mode, value.Other));
        }

        internal bool TryGetValue(Koto call, out (bool Failed, int Start, int Count, int Position, AcquisitionMode Mode, AcquisitionMode Other) failure)
            => (failure = binding.RecordedFact(call, Family)).Failed;
    }

    private readonly record struct CallableCandidate(BoundType Signature, SemanticsKind Receiver, BoundConstraint Fact, Koto Context);

    private readonly record struct SplitPart(bool Prepared, BoundType? Parameter, bool Split, bool Fails, Koto? Waiting, BoundType? Type);
}
