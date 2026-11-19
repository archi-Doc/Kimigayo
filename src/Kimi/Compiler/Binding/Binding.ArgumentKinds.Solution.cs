// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The solution of the call's `candidate`-th candidate from its evaluation: the call's slot arrays, scope, Self, explicit Type
    // arguments and the candidate's root rows.
    private SlotSolution Solution(in CallEvaluation evaluation, int candidate, FunctionKoto function, BoundType? declaringType)
        => new()
        {
            Rows = evaluation.Rows.AsSpan(candidate * evaluation.Call.ArgumentNodes.Count, evaluation.Call.ArgumentNodes.Count),
            ChildSlice = evaluation.ChildSlices.AsSpan(candidate * 2, 2),
            Binding = this,
            Call = evaluation.Call,
            Binder = function,
            Generic = evaluation.Generic,
            ExplicitTypes = evaluation.ExplicitTypes.AsSpan(0, evaluation.Generic?.TypeArguments.Count ?? 0),
            ExplicitLengths = evaluation.ExplicitLengths.AsSpan(0, evaluation.Generic?.TypeArguments.Count ?? 0),
            Scope = evaluation.Scope,
            Self = evaluation.Self,
            DeclaringType = declaringType,
            Frame = evaluation.Frames[candidate],
            Origins = evaluation.Origins,
            Inputs = evaluation.Inputs,
        };

    // SPEC 6.5.2, 10.8.1: a Case construction's solution: the enum owns its slots and binds its Origins; it has no Self, declaring Type,
    // explicit Type argument or input Origin, and its Type is never inferred apart from its slots. A Case without payload has no call,
    // and nothing reads it then.
    private SlotSolution Solution(
        InvocationKoto? call,
        EnumKoto declaration,
        BindingScope scope,
        SlotFrame frame,
        BoundOrigin[] origins)
        => new()
        {
            Binding = this,
            Call = call!,
            Binder = declaration,
            Scope = scope,
            Frame = frame,
            Origins = origins,
            Inputs = [],
            FixedConstruction = true,
        };

    // The SPEC 10.8 solve state of one candidate or one Case construction (DECISIONS BU-2): its Type, length and Origin slots and its
    // input Origins, with what substitution reads (scope, Self, declaring Type), its explicit Type arguments (their syntax and their
    // Types and lengths, bound once per call), whether a construction's inferred Type is checked fixed (ValidateFixedConstruction), the
    // slot bindings fixed before any evidence (Fixed, which FixCallSlots publishes as it seeds the frame), the Origin inference its
    // evidence begins, and a candidate's root rows, what each argument's position contributes to its call (F1, Rows; DECISIONS R4-M164).
    // The binder (a function or an enum) binds the Origins; the slots belong to the SlotOwner: the binder, or the structure for a
    // constructor. Invariant: a Case construction supplies data, and no operation of the shared walks (Pattern, Type, the evidence walk,
    // the defaults, the waiting-binds-F rule) reads Function; only a candidate's operations (its Constraints, its call's arguments) do.
    private ref partial struct SlotSolution
    {
        internal Binding Binding;
        internal InvocationKoto Call;
        internal Koto Binder;
        internal GenericsKoto? Generic;
        internal ReadOnlySpan<BoundType?> ExplicitTypes;
        internal ReadOnlySpan<BoundLength?> ExplicitLengths;
        internal FixedSlots Fixed;
        internal BindingScope Scope;
        internal BoundType? Self;
        internal BoundType? DeclaringType;
        internal SlotFrame Frame;
        internal BoundOrigin[] Origins;
        internal BoundOrigin[] Inputs;
        internal bool FixedConstruction;

        // Whether this run is the candidate's first solve, the one that composes its retained rows (SolveLate; DECISIONS BU-104 R3):
        // its openings start empty there (OpenWaited), and a later run keeps them. Every solution that reaches SolveLate carries a
        // slice (TryCandidate's: an evaluation's, unset when the candidate's evaluation begins, or a fixed construction's re-check,
        // which keeps the selected candidate's openings), so each evaluation's first solve clears them.
        internal bool FirstSolve;
        internal OriginInference? OriginInference;

        // F1 (R4-M164): each argument's root row for this candidate's call (Contribute; Binding.ArgumentKinds.Rows.cs), a slice of the
        // call's pooled CallEvaluation.Rows (one per argument of each candidate; none for a call without arguments), which lives until
        // the call's selected candidate is judged (JudgeSelectedCall).
        internal Span<PositionRow> Rows;

        // R3 (DECISIONS BU-104): the candidate's retained child rows in childRows, start and count (count -1 until its first solve
        // composes them, RetainChildRows); empty for a property update, whose typed parts have no child positions.
        internal Span<int> ChildSlice;

        // R4' (DECISIONS BU-104): the callee's Origin slots that an evidence position's pattern names (EvidenceNames), built once per solve
        // from the retained rows (CollectPositionPremises) and read by each discovery (DiscoverPremiseOrigins); its solve returns it.
        internal SlotSet EvidenceNamed;

        // SPEC 10.8 (Unbound slots), 4.3.1 Places: the Type slots that steps 3 and 4 of SPEC 10.1 leave unbound, published before the
        // late walk (LeaveUnbound; SolveLate, SolveConstruction) and read by it alone (InferComposite): only such a slot takes its
        // literals whole there; a slot those steps bound gives the parts at its positions their expected Type (10.8).
        internal SlotSet Left;

        // The solution's Type and length slots, read-only: the frame's writes are InferCtxt's (DECISIONS R7-I2).
        internal readonly ReadOnlySpan<BoundType?> Types => this.Frame.Types;

        internal readonly ReadOnlySpan<BoundLength?> Lengths => this.Frame.Lengths;

        // Publishes Left in `left`, the caller's scratch. Contract: nothing writes that scratch between this call and the end of the
        // late walk it precedes (SolveLate's is the trial's `unboundSlots`, which the trial recomputes only after SolveLate; a
        // nested call's trial has its own).
        internal void LeaveUnbound(ref SlotSet left)
        {
            left.Clear();
            for (var g = 0; g < CallSlotCount(this.Binder); g++)
            {
                if (this.Types[g] is null)
                {
                    left.Add(g);
                }
            }

            this.Left = left;
        }

        // The slots' owner, whether it is a constructor's structure rather than the binder, and the binder's Origin count.
        internal readonly Koto SlotOwner => CallSlotOwner(this.Binder);

        internal readonly bool Constructor => !ReferenceEquals(this.SlotOwner, this.Binder);

        internal readonly int OriginCount => CallOriginCount(this.Binder);

        // The binder as the unifier's input (Infer); a binder is a function or an enum.
        internal readonly SolveBinder Solve => this.Binder is FunctionKoto function ? new(function) : new((EnumKoto)this.Binder);

        // The candidate's function, for the operations of a call.
        internal readonly FunctionKoto Function => (FunctionKoto)this.Binder;

        // The candidate's substitution over `slots`, its Type slots as solved or as a proof supposes them (Binding.CallSubstitution).
        internal readonly CallSubstitution Substitution(ReadOnlySpan<BoundType?> slots)
            => SubstitutionOf(this.Binder, this.DeclaringType, slots, this.Lengths, this.Origins, this.Inputs);

        // The pattern with the solution substituted, or null while it holds an unbound slot.
        internal readonly BoundType? Type(BoundType pattern)
            => this.Binding.CallType(pattern, this.Binder, this.Types, this.Scope, this.Self, this.Origins, this.Inputs, this.DeclaringType, this.Lengths);

        // Whether every own slot is bound.
        internal readonly bool IsComplete()
        {
            var own = CallOwnSlots(this.Function);
            for (var i = 0; i < own.Count; i++)
            {
                if (own[i] is LengthParameterKoto ? this.Lengths[i] is null : this.Types[i] is null)
                {
                    return false;
                }
            }

            return true;
        }

        // SPEC 10.5.1 (D4): whether the result holds a Type or Semantics slot that neither evidence nor a waiting callable fixes with
        // no outer expectation (OpenWaitingSlots), as a construction's does (HoldsOpenSlot); a constructor's result holds all its slots.
        // Origin slots are not counted.
        internal readonly bool ResultHoldsUnsolved(int[] mapping)
        {
            var unsolved = this.Binding.RentSlotSet(CallSlotCount(this.Function));
            try
            {
                this.Binding.UnsolvedSlots(this.Call, this.Function, this.Types, this.Lengths, mapping, ref unsolved);
                var open = new OpenWaitingSlots(ref unsolved, this);
                for (var i = 0; i < this.Call.ArgumentNodes.Count; i++)
                {
                    this.Binding.TakeWaiting(this.Call.ArgumentNodes[i], this.Function.Parameters[mapping[i]].Type.BoundType!, this.SlotOwner, open);
                }

                return !unsolved.IsEmpty &&
                    (this.Function.IsConstructor || (this.Function.BoundSymbol?.Type is { } result && MentionsSlots(result, this.SlotOwner, unsolved)));
            }
            finally
            {
                this.Binding.ReturnSlotSet(unsolved);
            }
        }

        // SPEC 15.3.6, 10.8: a Callable proof that fails only because a slot holds the open region of an argument's own per-call Origin,
        // and holds once that per-call Origin stands in for it, is no inapplicability: the argument is reported after selection.
        internal readonly bool PerCallCallables(int[] mapping, Span<BoundArgumentOperation> marks)
        {
            var count = CallOwnSlots(this.Function).Count;
            var restored = this.Binding.typeScratch.Rent(count);
            try
            {
                // SPEC 10.7, 10.8: first instantiate each argument's per-call input at a required input over a fixed Origin; when every
                // Constraint then holds, that instantiation solves the slots and nothing is reported.
                this.Types[..count].CopyTo(restored);
                if (this.StandIns(mapping, restored.AsSpan(0, count), marks, true) && this.Prove(restored.AsSpan(0, count), false) == ConstraintProof.Proven)
                {
                    this.Frame.CopyTypes(restored.AsSpan(0, count));
                    this.CompleteOperationTypes(marks);
                    return true;
                }

                this.Types[..count].CopyTo(restored);
                var marked = this.StandIns(mapping, restored.AsSpan(0, count), marks, false);
                if (!marked || this.Prove(restored.AsSpan(0, count), false) != ConstraintProof.Proven)
                {
                    for (var i = 0; i < this.Call.ArgumentNodes.Count && marked; i++)
                    {
                        marks[i] = marks[i] with { MissingOrigin = false };
                    }

                    return false;
                }

                return true;
            }
            finally
            {
                this.Binding.typeScratch.Return(restored, clearArray: true);
            }
        }

        // J2 (SPEC 10.5.1, 10.8, 15.3.6; the owner's O-358 (a), O-362 (a), F's ruling 3; PLAN G936): a callee variable that evidence
        // names only at covariant positions and a position without evidence also names, neither inside a Function Type nor read by a
        // Constraint of the callee (CollectPositionNames; O-368 (a)), is an Origin variable that the waiting part's check bounds as well as the evidence's fit, so the call
        // opens it as its region; each relation is then judged after selection at its own source (JudgeSelectedCall, the waiting part's
        // completion, the uses of the result) and ownership analysis solves the region with the body's other regions: with covariant
        // uses exactly the meet of the bounds (SPEC 15.2.2). Such a Type slot, bound without an invariant binding and fixed by no
        // explicit or declaring Type, takes its solution with the Origins at its covariant positions opened (OpenSlotOrigins); its
        // structure and Semantics stay the evidence's, so applicability and ranking are unchanged and no candidate is solved twice. Such
        // an Origin parameter is published to the solve (OriginInference.WaitingNamed), which writes a covariant one as its evidence
        // candidate met with the call's region (CallRegion). Either way the solution is `(E and region)`, SPEC 15.3.6's candidate
        // with the region for the bounds not known yet, and E is kept in the candidate's openings (OriginInference.Opened), which
        // start empty at its first solve. A candidate without a Signature row reads nothing more.
        internal readonly void OpenWaited()
        {
            var signature = false;
            for (var i = 0; !signature && i < this.Rows.Length; i++)
            {
                signature = this.Rows[i].Contribution == Contribution.Signature;
            }

            if (!signature)
            {
                return;
            }

            var binding = this.Binding;
            var opened = binding.InferenceOf(this.Call, this.Function);
            if (this.FirstSolve)
            {
                opened.Opened.Clear();
            }

            var slots = CallSlotCount(this.Binder);
            var waiting = binding.RentSlotSet(slots + this.OriginCount);
            var covariant = binding.RentSlotSet(slots + this.OriginCount);
            var blocked = binding.RentSlotSet(slots + this.OriginCount);
            try
            {
                binding.CollectPositionNames(this, ref waiting, ref covariant, ref blocked);
                for (var g = 0; g < CallOwnSlots(this.Function).Count; g++)
                {
                    if (Opens(g, waiting, covariant, blocked) && this.Types[g] is { CarriesOrigin: true } solution &&
                        !this.Frame.IsInvariant(g) && !this.FixedSlot(g))
                    {
                        var ordinal = 0;
                        this.Frame.SetType(g, binding.OpenSlotOrigins(solution, this.Call, g, 1, ref ordinal, opened));
                    }
                }

                for (var k = 0; this.OriginInference is not null && k < this.OriginCount; k++)
                {
                    if (Opens(slots + k, waiting, covariant, blocked))
                    {
                        this.OriginInference.WaitingNamed.Add(k);
                    }
                }
            }
            finally
            {
                binding.ReturnSlotSet(blocked);
                binding.ReturnSlotSet(covariant);
                binding.ReturnSlotSet(waiting);
            }

            static bool Opens(int index, in SlotSet waiting, in SlotSet covariant, in SlotSet blocked)
                => waiting.Contains(index) && covariant.Contains(index) && !blocked.Contains(index);
        }

        // Each operation's parameter Type with the solution substituted, where it is complete.
        internal readonly void CompleteOperationTypes(Span<BoundArgumentOperation> operations)
        {
            for (var i = 0; i < operations.Length; i++)
            {
                if (operations[i] is { Source: not null, ParameterIndex: >= 0 } operation &&
                    this.Type(this.Function.Parameters[operation.ParameterIndex].Type.BoundType!) is { } completed)
                {
                    operations[i] = operation with { ParameterType = completed };
                }
            }
        }

        // SPEC 10.8: the Constraints and premises whose judgment needs an unsolved slot are not judged.
        internal readonly ConstraintProof Prove(ReadOnlySpan<BoundType?> slots, bool incomplete, SlotSet unbound = default)
        {
            var binding = this.Binding;
            var function = this.Function;
            var constructor = function.IsConstructor;
            var arguments = constructor ? this.Types[..CallSlotCount(function)] : slots;
            binding.BeginProof(); // A candidate's applicability is one conjunction (DECISIONS BU-22).
            var closedOwner = this.DeclaringType is { } declaringType
                ? (constructor ? binding.ConstructionType(function, declaringType, this.Types) : declaringType) : null;
            var reference = binding.activeRequirementContract; // The Contract reference CallSelection tries the candidate under (SPEC 8.6.4).
            var bindings = new CandidateBindings(
                function, function.BoundSymbol!, this.Substitution(arguments), this.Self, this.DeclaringType, closedOwner, reference, unbound, constructor);
            var proof = binding.StepFiveConjunction(bindings, this.Scope, incomplete, this.SlotOwner);
            proof = CombineProof(proof, binding.CheckSignatureTypeConstraints(function), true); // The trial's alone until R7-C2 (b) (PLAN G375).
            var fact = binding.ProofCause(proof) is null ? binding.ProofFact(proof) : null; // A limit takes precedence.
            var clause = binding.ProofClause(proof); // G351a: at the trial's verdict.
            proof = binding.EndProof(proof);
            if (fact is not null)
            {
                fact.Epoch = binding.frameEpoch; // The candidate trial's frame, whose consumer explains it (CallableOriginFailure).
            }

            binding.trialUnknown = fact;
            binding.trialClause = clause is { } trial ? trial with { Epoch = binding.frameEpoch } : null;
            return proof;
        }

        // SPEC 7.7.4, 10.7, 10.8, 15.3.6: whether `argument` is decided by its per-call stand-in (FitPerCall), a fact of the solve: an
        // implementation erased at the common Function Type, a function group or a Closure or Function Item value, whose own per-call
        // input left an open region in `type`. Any other argument keeps its fit (FitArgument): a Function value's fit is structural and
        // judged after selection (DECISIONS R4-M155), an anonymous function written in the call is completed by the selected candidate.
        internal readonly bool HasPerCallStandIn(Koto argument, BoundType type)
        {
            var node = KotoHelper.UnwrapParentheses(argument);
            return type.Kind == BoundTypeKind.Function &&
                (this.Binding.KindOf(node) == PartKind.Reference || node.BoundType is { Kind: BoundTypeKind.Closure or BoundTypeKind.FunctionItem }) &&
                this.Binding.PerCallStandIn(type, argument, type) is not null;
        }

        // SPEC 10.7, 10.8, 15.3.6: an argument with a per-call stand-in (HasPerCallStandIn) fits once its per-call input is instantiated at
        // a required input over a fixed Origin and no open region of it remains, or else only through its own per-call Origin, which is
        // reported after selection.
        internal readonly CandidateApplicability FitPerCall(Koto argument, BoundType type, out BoundArgumentOperation operation)
        {
            var binding = this.Binding;
            var node = KotoHelper.UnwrapParentheses(argument);
            operation = default;
            if (binding.PerCallStandIn(type, argument, type, true) is { } instantiated && binding.PerCallStandIn(instantiated, argument, type) is null &&
                this.FitsCallable(node, instantiated))
            {
                binding.InstantiateOpenOrigins(this.Frame, CallOwnSlots(this.Function).Count, argument, type);
                operation = new(argument, node.BoundType, instantiated, ArgumentOperationKind.Value, ArgumentAdaptation.Erasure);
                return CandidateApplicability.Applicable;
            }

            if (binding.PerCallStandIn(type, argument, type) is { } standIn && this.FitsCallable(node, standIn))
            {
                operation = new(argument, node.BoundType, type, ArgumentOperationKind.Value, ArgumentAdaptation.Erasure, MissingOrigin: true);
                return CandidateApplicability.Applicable;
            }

            return CandidateApplicability.Inapplicable;
        }

        // Whether the callable value converts to the common Function Type: a function group by one of its functions (SPEC 7.7.4).
        private readonly bool FitsCallable(Koto node, BoundType type)
        {
            var binding = this.Binding;
            if (node.BoundType is not { } actual)
            {
                return binding.FunctionGroupFits(node, node.BoundSymbol!, type, this.Scope);
            }

            var rows = binding.comparisonRows.Count; // SPEC 15.6.1: a selection takes and drops the region conjuncts.
            var fits = binding.ErasureSignatureFits(actual, type, node, binding.comparisonRows, out _);
            binding.DropComparisonRows(rows);
            return fits;
        }

        // Whether the Type slot `slot` is fixed before any evidence (FixCallSlots): an explicit Type argument, or a constructor's slot
        // that its declaring Type gives (an open construction slot is its own parameter there).
        private readonly bool FixedSlot(int slot)
            => slot < this.Fixed.Types.Length && this.Fixed.Types[slot] is { } bound && OwnSlot(bound, this.SlotOwner) != slot;

        private readonly Span<BoundOrigin> InputSpan() => this.Inputs.AsSpan(0, Math.Min(this.Inputs.Length, InputOriginCount(this.Binder)));

        // A slot's own validity: a length is Unknown while unbound; a Type slot is checked when bound and Proven when it is unbound only
        // because it is unsolved.
        // Replaces the open regions of each argument whose Callable signature holds one, marking that argument unless `fixedOnly`.
        private readonly bool StandIns(int[] mapping, Span<BoundType?> slots, Span<BoundArgumentOperation> targets, bool fixedOnly)
        {
            var binding = this.Binding;
            var replaced = false;
            for (var i = 0; i < this.Call.ArgumentNodes.Count; i++)
            {
                var source = this.Call.ArgumentNodes[i];
                if (this.Function.Parameters[mapping[i]].Type.BoundType is not { } parameter || this.CallableSlot(parameter) is not { } callableSlot ||
                    source.BoundType is null ||
                    !binding.TryCallable(callableSlot, binding.ConstraintScope(this.Function), out var requiredSignature, out _) ||
                    binding.CallMemberPattern(requiredSignature, this.Binder, this.DeclaringType) is not { } memberSignature ||
                    this.Type(binding.ContractType(memberSignature, this.Scope, this.Self)) is not { } required ||
                    binding.PerCallStandIn(required, source, required, fixedOnly) is null)
                {
                    continue;
                }

                for (var g = 0; g < slots.Length; g++)
                {
                    if (slots[g] is { } solution)
                    {
                        slots[g] = binding.ReplaceOpenOrigins(solution, source, required, fixedOnly);
                    }
                }

                if (!fixedOnly)
                {
                    targets[i] = targets[i] with { MissingOrigin = true };
                }

                replaced = true;
            }

            return replaced;
        }
    }
}
