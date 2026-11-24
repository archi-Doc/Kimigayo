// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly ScratchBuffers<EvaluatedCandidate> candidateScratch = new();

    // R7-I2 c: one frame per candidate of a selection; a candidate's solution is its own frame, the winner's the call's.
    private readonly ScratchBuffers<SlotFrame> frameScratch = new();

    // SPEC 10.5.1: the operations of the element positions of each candidate's Tuple and array literal arguments, each element fitted as
    // an argument at its position, in each fit's order (BoundArgumentOperation.Positions; FitElement appends them): ranked by their
    // classes (SPEC 10.4 step 1) and read by step 5 as the arguments' (ErasedArgumentsEnvironment); a candidate evaluation removes what
    // it appended when it ends.
    private readonly List<BoundArgumentOperation> positionOperations = new();

    private enum CandidateApplicability : byte
    {
        Inapplicable,
        Applicable,
        Waiting,
        Pending,
        Error,
    }

    // DECISIONS BU-86: the closed vocabulary of PendingJudgment (UnformedSplit: the temporary BU-240 fact, PLAN G886).
    private enum PendingJudgmentKind : byte
    {
        FailedTypeArgument,
        EnvironmentResult,
        UnformedSplit,
    }

    // SPEC 10.4, 8.3.3: the candidates the ordinary comparison ranks; with premises, also each candidate whose applicability is unproven
    // only by an Unknown premise after every argument and result check, ranked as the applicable candidate it would be.
    private static bool Ranked(in EvaluatedCandidate candidate, bool premises)
        => candidate.State is CandidateApplicability.Applicable or CandidateApplicability.Waiting || (premises && candidate is { State: CandidateApplicability.Pending, PremiseUnknown: true });

    private int SelectBest(ReadOnlySpan<EvaluatedCandidate> candidates, BoundArgumentOperation[] operations, int stride)
        => StrictBestCandidate.Select(candidates.Length, new CandidateOrder(candidates, operations, stride, this, false));

    // SPEC 8.3.3: an Unknown premise cannot affect the selection when an applicable candidate is strictly best even with every
    // premise-unproven candidate ranked as applicable; no judgment of those premises then changes the winner. The ordinary comparison
    // decides, so a tie or an incomparable pair still defers.
    private bool PremisesCannotAffect(InvocationKoto call, ReadOnlySpan<EvaluatedCandidate> candidates, BoundArgumentOperation[] operations, int stride)
        => this.PremisedBest(call, candidates, operations, stride) is var best && best >= 0 &&
            candidates[best].State is CandidateApplicability.Applicable or CandidateApplicability.Waiting;

    // SPEC 10.1, 8.3.3: the candidate strictly best with every premise-unproven candidate ranked as the applicable candidate it would be
    // (compared as though applicable), or -1 when none is, or when candidates holding waiting callables are not comparable.
    private int PremisedBest(InvocationKoto call, ReadOnlySpan<EvaluatedCandidate> candidates, BoundArgumentOperation[] operations, int stride)
    {
        foreach (ref readonly var candidate in candidates)
        {
            if (candidate.State == CandidateApplicability.Waiting)
            {
                if (!this.ComparableCallableSlots(call, candidates, operations, stride, true))
                {
                    return -1;
                }

                break;
            }
        }

        return StrictBestCandidate.Select(candidates.Length, new CandidateOrder(candidates, operations, stride, this, true));
    }

    // R7-I2 c: a selection's frames, one per candidate, each clean (InferCtxt.Rent), keyed by the solve's node and judged in the use's
    // `view`; a candidate's trial and re-trials write only its own.
    private SlotFrame[] RentFrames(int count, int capacity, Koto node, BindingScope view)
    {
        var frames = this.frameScratch.Rent(count);
        for (var i = 0; i < count; i++)
        {
            frames[i] = this.infer.Rent(Math.Max(1, capacity), node, view);
        }

        return frames;
    }

    // Returns a selection's frames in reverse order of their rental; the winner's solution is read before.
    private void ReturnFrames(SlotFrame[] frames, int count)
    {
        for (var i = count - 1; i >= 0; i--)
        {
            this.infer.Return(frames[i]);
        }

        this.frameScratch.Return(frames, clearArray: true);
    }

    private readonly ref struct CandidateOrder : IStrictCandidateOrder
    {
        private readonly ReadOnlySpan<EvaluatedCandidate> candidates;
        private readonly BoundArgumentOperation[] operations;
        private readonly int stride;
        private readonly Binding binding;
        private readonly bool premises;

        internal CandidateOrder(ReadOnlySpan<EvaluatedCandidate> candidates, BoundArgumentOperation[] operations, int stride, Binding binding, bool premises)
        {
            this.candidates = candidates;
            this.operations = operations;
            this.stride = stride;
            this.binding = binding;
            this.premises = premises;
        }

        public bool IsEligible(int candidate) => Ranked(this.candidates[candidate], this.premises);

        public bool Better(int left, int right)
        {
            if (this.binding.MeasureCallInference)
            {
                this.binding.inferenceComparisons++;
            }

            var leftOperations = this.operations.AsSpan(left * this.stride, this.stride - 1);
            var rightOperations = this.operations.AsSpan(right * this.stride, this.stride - 1);
            return this.binding.BetterCandidate(this.candidates[left], this.candidates[right], leftOperations, rightOperations);
        }
    }

    // SPEC 10.2.2, 10.7: whether the remaining candidates erase one argument in some candidate and take it directly in another.
    private static bool ErasureIncomparable(in CallEvaluation evaluation)
    {
        for (var a = 0; a < evaluation.Call.ArgumentNodes.Count; a++)
        {
            var erased = false;
            var direct = false;
            for (var i = 0; i < evaluation.Count; i++)
            {
                if (evaluation.Evaluated[i].State is CandidateApplicability.Applicable or CandidateApplicability.Waiting)
                {
                    erased |= evaluation.OperationsOf(i)[a].Adaptation == ArgumentAdaptation.Erasure;
                    direct |= evaluation.OperationsOf(i)[a].Adaptation != ArgumentAdaptation.Erasure;
                }
            }

            if (erased && direct)
            {
                return true;
            }
        }

        return false;
    }

    // A failed candidate's explanation is its trial's clause fact (G351a; PLAN G352): the first clause at the trial's verdict
    // (E > R > L > U), read where the trial left it and never proven again (AGENTS Diagnostics). None when the trial left no fact, as for
    // a candidate that failed before its Constraints were judged, or when the clause did not bind (its binding failure is its own record).
    private static ReferenceConstraintFailure? ClauseFailure(ClauseFact? fact, BindingSymbol? declaration)
        => fact is { Constraint: { } constraint } clause ? new(clause.Clause, constraint, clause.Outcome, declaration) : null;

    // SPEC 10.2.2, 10.4 step 1: two adaptation classes at one argument or position; false when they are incomparable (an erasure and
    // any other row), which no other argument or tie-breaker rescues.
    private static bool CompareClasses(ArgumentAdaptation x, ArgumentAdaptation y, ref bool better, ref bool worse)
    {
        better |= x < y;
        worse |= x > y;
        return x == y || (x != ArgumentAdaptation.Erasure && y != ArgumentAdaptation.Erasure);
    }

    // SPEC 10.5, 10.8: a waiting argument at F, ref/F or uniq/F, whether its candidate's fixed expected call signature is closed, holds an
    // unsolved slot or is absent (no Callable Constraint on F), compares equal there when the candidates acquire it in one mode.
    private bool ComparableCallableSlots(
        InvocationKoto call, ReadOnlySpan<EvaluatedCandidate> candidates, BoundArgumentOperation[] operations, int stride, bool premises = false)
    {
        for (var argument = 0; argument < call.ArgumentNodes.Count; argument++)
        {
            if (!this.IsWaitingCallable(call.ArgumentNodes[argument]))
            {
                continue;
            }

            var first = true;
            SemanticsKind? acquisition = null;
            for (var candidate = 0; candidate < candidates.Length; candidate++)
            {
                if (!Ranked(candidates[candidate], premises))
                {
                    continue;
                }

                var operation = operations[(candidate * stride) + argument];
                if (operation.Adaptation == ArgumentAdaptation.Erasure)
                {
                    continue; // The comparison retains this distinct operation; it never ties with a concrete slot.
                }

                if (candidates[candidate].State != CandidateApplicability.Waiting)
                {
                    return false;
                }

                var function = (FunctionKoto)candidates[candidate].Symbol!.Declaration;
                var pattern = function.Parameters[operation.ParameterIndex].Type.BoundType!;
                SemanticsKind? mode = pattern.SafeReferent is not null ? pattern.Semantics : null;
                // SPEC 10.5: a waiting reference, like an anonymous body, never ranks the outer candidates; once their ordinary
                // inputs and defaults select one, its fixed call signature selects the reference, even when the signatures differ.
                if (CallableSlotType(pattern, CallSlotOwner(function)) is null || (!first && acquisition != mode))
                {
                    return false;
                }

                acquisition = mode;
                first = false;
            }
        }

        return true;
    }

    // SPEC 10.4: this comparison reads explicit source arguments only; receiver acquisition is common to the group. Step 1 compares each
    // argument's class and each element position's (Positions). Two fits of one argument walk its syntax alike, so they record the same
    // positions, unequal counts being a broken contract (InternalInvariant_Kd at the argument); a candidate that leaves the argument open
    // (no Type) records none and is compared at the argument only (step 2 then makes the pair incomparable).
    private bool BetterCandidate(
        in EvaluatedCandidate a, in EvaluatedCandidate b, ReadOnlySpan<BoundArgumentOperation> left, ReadOnlySpan<BoundArgumentOperation> right)
    {
        var better = false;
        var worse = false;
        var positions = CollectionsMarshal.AsSpan(this.positionOperations);
        for (var i = 0; i < left.Length; i++)
        {
            var x = positions[left[i].Positions];
            var y = positions[right[i].Positions];
            if (!CompareClasses(left[i].Adaptation, right[i].Adaptation, ref better, ref worse))
            {
                return false;
            }

            if (x.Length != y.Length)
            {
                if (!x.IsEmpty && !y.IsEmpty)
                {
                    this.Fail(left[i].Source!, BindingFailure.Internal);
                    return false;
                }

                continue;
            }

            for (var p = 0; p < x.Length; p++)
            {
                if (!CompareClasses(x[p].Adaptation, y[p].Adaptation, ref better, ref worse))
                {
                    return false;
                }
            }
        }

        if (worse || better)
        {
            return better && !worse;
        }

        for (var i = 0; i < left.Length; i++)
        {
            var x = left[i].ParameterType;
            var y = right[i].ParameterType;
            if (ReferenceEquals(x, y) && (x is not null || left[i].Source is null))
            {
                continue;
            }

            // SPEC 10.4 step 2: for each source argument the substituted parameter Types' structural parts must be identical (Origin
            // bindings never rank, even inside Function Types), or the candidates are incomparable; open slots are never identical, and a
            // Type holding an atom is identical only to itself (Binding.OpenSlots: a pair whose walk met an atom is incomparable; a
            // reference-equal pair is skipped above; SPEC 10.8 Unbound slots, ArgFlow ruling 166). A waiting checked part (a Call or Case,
            // checked at each candidate's Type, 10.5.1) is compared like any source argument (ArgFlow ruling 173). The one position not
            // compared is a waiting callable's at a parameter `F`, `ref/F` or `uniq/F` that it binds (its operation is not an Erasure):
            // SPEC 10.8 Unbound slots compares such an F by its form, "equal forms compare equal in step 2", and the candidates acquire it
            // in one mode (ComparableCallableSlots), so the position ties (ArgFlow ruling 175 C3); it is walked only when a side holds an
            // atom, the only thing it can meet there. At a Function Type the callable's operation is an Erasure, compared as above.
            var callable = a.State == CandidateApplicability.Waiting && b.State == CandidateApplicability.Waiting &&
                left[i].Adaptation != ArgumentAdaptation.Erasure && left[i].Source is { } source && this.IsWaitingCallable(source);
            var metOpenSlot = false;
            var fits = x is not null && y is not null && (!callable || x.ContainsOpenSlot || y.ContainsOpenSlot) && this.FitsStructuralPart(x, y, out metOpenSlot);
            if (metOpenSlot)
            {
                return false;
            }

            if (callable)
            {
                continue;
            }

            if (!fits)
            {
                return false;
            }
        }

        var aGeneric = a.Symbol?.Declaration is FunctionKoto { GenericArguments.Count: > 0 };
        var bGeneric = b.Symbol?.Declaration is FunctionKoto { GenericArguments.Count: > 0 };
        return aGeneric != bGeneric ? !aGeneric : a.DefaultsUsed < b.DefaultsUsed;
    }

    // A failed selection: its omitted header Types wait, and its record is the selection's (RecordSelection) with the parties built.
    private BoundType? FailWaitingSelection(InvocationKoto call, BindingFailure failure)
    {
        this.MarkWaitingHeaders(call);
        return this.RecordSelection(call, failure);
    }

    // The selection's one recorder (SPEC 10.1, 10.4, 23.1.2): the facts its caller built since the last record, recorded at a call
    // without a failure; a call that already failed keeps its first failure and drops them (the slot's drop). The scratch is cleared
    // either way, its capacity kept. Its callers' B1 statement (R7-E3 E3b delta 2.4): every check before each verdict that fails the call
    // returns, so the one input that can hold an earlier failure is the call's own, which Calls' NoneApplicable arm read before.
    [Transitional("R7", Reason = "Records only at a call without a failure, so the call keeps its first failure (d2: f<Missing>(1) records no fact-less NoApplicableOverload_Kd; h57: an unbound slot at a call already NoApplicableOverload_Kd records none), until R7-E5's per-problem inputs (D-924) decide a dependent record.")]
    private BoundType? RecordSelection(Koto use, BindingFailure failure)
    {
        if (this.RecordedFailure(use) == BindingFailure.None)
        {
            this.Record(use, failure, CollectionsMarshal.AsSpan(this.candidateFacts), unresolved: true);
        }
        else
        {
            this.Fail(use, failure, true);
        }

        this.candidateFacts.Clear();
        return null;
    }

    // SPEC 8.10, 7.7.4 (DECISIONS BU-86, amended by R7-E3 E3b-2): a selection that a single Pending candidate's EnvironmentResult judgment
    // blocks, the selection's deferral (RecordSelection): the argument whose result over its closure's environment is no evidence (role 6,
    // "argument") and the slot's Type parameter it leaves unbound (role 7, "declaration"), both shown as related locations.
    private BoundType? FailPendingJudgment(InvocationKoto call, PendingJudgment judgment)
    {
        this.MarkWaitingHeaders(call);
        this.candidateFacts.Add(new(ProblemFactKind.Related, this.FactSite(judgment.Part!), 6)); // An EnvironmentResult judgment names its argument and slot.
        this.candidateFacts.Add(new(ProblemFactKind.Related, this.FactSite(judgment.Slot!.Declaration), 7));
        return this.RecordSelection(call, BindingFailure.UnprovenConstraint);
    }

    // The pending judgment's record from its facts (FailPendingJudgment), read by role: the argument (role 6) and the slot's Type parameter
    // (role 7) related, any other shape a broken contract; its Note went by code (R7-E3 E3b-2, M3).
    private void ReportPendingJudgment(Koto node, DiagnosticCollection? store, int index, in BindingProblem problem)
    {
        if (this.report.Facts(index) is not [.., { Kind: ProblemFactKind.Related, Value: 6 } argument, { Kind: ProblemFactKind.Related, Value: 7 } slot])
        {
            throw new UnreachableException();
        }

        node.ReportIn(
            store,
            problem.Requirement,
            problem.Code,
            related: [("argument", this.compilation.SyntaxOf(argument.Site), null), ("declaration", this.compilation.SyntaxOf(slot.Site), null)],
            condition: problem.Condition);
    }

    // SPEC 8.3, 15.6.1: a selection that a single pending Callable proof blocks, explained by that Constraint record.
    private BoundType? FailCallableSelection(InvocationKoto call, CallableConstraintFact fact)
    {
        this.MarkWaitingHeaders(call);
        return this.FailExplained(CallableConstraintRows.Family, call, BindingFailure.UnprovenConstraint, fact, true);
    }

    // Keep a single pending declaration's concrete proof obligation before its candidate scratch is reused.
    private BoundType? FailPendingConstraint(InvocationKoto call, ReferenceConstraintFailure fact)
    {
        this.MarkWaitingHeaders(call);
        return this.FailClauseRecord(call, fact, unresolved: true);
    }

    // Omitted header Types need this selection's expectation. Keep that dependency explicit without checking
    // the body or turning an independent written-Type error into a consequence of the selection.
    private void MarkWaitingHeaders(InvocationKoto call)
    {
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var argument = call.ArgumentNodes[i];
            if (this.IsCheckedCall(argument))
            {
                this.CompleteDependent(argument, call);
            }
            else if (this.IsWaitingCallable(argument) && KotoHelper.UnwrapParentheses(argument) is FunctionKoto closure)
            {
                this.MarkOmittedHeaders(closure, call);
            }
        }
    }

    // A Callable signature has no declaration Symbol or own generic/default parameters. Ordinary candidates always have a Symbol.
    // ClosureReceiver: the one closure argument whose minimum call receiver is the candidate's only refuted condition (TryCandidate).
    // HeldRefusal: the refusal its trial read and held (TakeTrialRefusal), settled only if it is selected (SettleSelected).
    // Acquisition: the absent spelling of a bare Place that alone kept an otherwise Applicable trial from applying (SPEC 15.1.5), read by
    // a NoneApplicable selection through LastAcquisition.
    // A call's row is written in place, each field once: RentCandidates writes Symbol and Accessible, the evaluation (EvaluateCallCandidate)
    // ArgumentMap and the facts after the trial, the trial (TryCandidate) its verdict and facts. A continuation's trial (CheckDefaulted,
    // the waiting re-trial, the fixed re-check) writes a local row; CheckDefaulted takes back only State and Unsolved.
    private record struct EvaluatedCandidate(BindingSymbol? Symbol, CandidateApplicability State, BoundType? DeclaringType, int DefaultsUsed, bool Unsolved = false, ClosureReceiverRefutation? ClosureReceiver = null, CallArgumentMap ArgumentMap = default, bool Accessible = true, bool StableConstruction = false, bool IndependentRejection = false, ReferenceConstraintFailure? ConstraintFailure = null, bool PremiseUnknown = false, bool PremiseReadsMark = false)
    {
        internal Refusal? HeldRefusal { get; set; }

        internal AcquisitionFailure? Acquisition { get; set; }

        // SPEC 10.5.1: an inapplicable candidate's failed check of a part against its expected Type (TakeTrialChecked).
        internal CheckedPartFailure? CheckedFailure { get; set; }

        // SPEC 10.5.1 bullet 1 (ArgFlow rulings 160 C3, 171 C2): the trial left a checked part unqueried under the argument probe
        // (ProbedResultOpen), which gives the part no class: applicable, the candidate makes the call not independently typable
        // whatever the others give, so the call defers unranked (ClassifySelection, SolveSelected).
        internal bool Deferred { get; set; }
    }

    // SPEC 7.7.3, 8.10: the parameter whose Callable Constraint does not permit its closure argument's minimum call receiver.
    private readonly record struct ClosureReceiverRefutation(int Parameter, SemanticsKind Actual, SemanticsKind Required);

    // DECISIONS BU-86: a judgment that leaves a candidate Pending before its proof, written once where TryCandidateCore decides it.
    // FailedTypeArgument: an explicit Type argument that did not bind (Part), on which the selection rests (SPEC 23.1.2).
    // EnvironmentResult: the argument (Part) whose known signature's result over its closure's environment is no evidence for Slot
    // (SPEC 8.10, 7.7.4), a slot that no other evidence binds (SPEC 10.8).
    // UnformedSplit (DECISIONS BU-240, temporary until PLAN G886's representation): the candidate trial of Part rests on a formation under a
    // binding whose split no Type represents (Binding.Unformed's count), every check reading no unformed Type having passed; only
    // TryCandidate's verdict publishes it, and its call is then one located Unsupported_Kd.
    private readonly record struct PendingJudgment(PendingJudgmentKind Kind, Koto? Part, BindingSymbol? Slot = null)
    {
        // The epoch of the innermost open frame it is read in (design-r2 Amendment 5).
        internal long Epoch { get; init; }
    }
}
