// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Function Types and their conversions (SPEC 7.7.4, 8.10, 10.7, 15.3.7, 15.6.4).
// - Responsibility: a Function Type's syntax (BindFunctionType), which gives the Type its per-call binder (BoundType.Binder, the one
//   answer to whose per-call inputs a Function Type's are); a nested signature's written result's formation, a Function Type's and an
//   anonymous function's (FormNestedResult; SPEC 15.3.4, DECISIONS R4-M162); the shape of a signature's per-call inputs (SignatureOwner,
//   InputOrigins); the erasure of a Closure or Function Item to a common
//   Function Type (ErasesToFunction, and ErasureSignatureFits, its signature part that applicability reads); and the solved
//   comparison of a named function's contract with a required signature (ContractFits), of every part but the wildcards its caller
//   gives (SPEC 23.1.2, DECISIONS Q13 rule W: the parts that failed where written, RefutedSignatureParts), which only a function-group
//   reference gives, since it reads the candidate's signature verdict itself; an erasure, a requirement Item's fit and a Callable
//   proof give none and compare every part.
// - Inputs: the Types compared, the use whose premises the comparison reads, the Type relation (Binding.TypeRelations) and, for a
//   Closure's erasure, its call's retention (TyCtxt.Retains).
// - Outputs: the verdicts and, for ContractFits, the solved Origins and argument operations.
// - Invariants: an erasure's Shared receiver (SharedReceiver), the structural part of its signature and its environment
//   (ErasedArgumentsEnvironment: an Owned part the engine or the forming body refutes, or a result that depends on its receiver,
//   ReceiverIndependence) decide applicability, and a part decided only once the body is bound (Final) or a retention past its limits is
//   the located limit there; its Origin part and its other Owned verdicts are judged after selection.
// - Prohibitions: the Type relation's walk stays in Binding.TypeRelations.Walk.cs; these judgments call its entries.
public sealed partial class Binding
{
    // The declaration whose own per-call inputs the signature of a Function Item or closure names (SPEC 8.10), below the callee's
    // acquisition layers (CallableCore).
    internal static Koto? SignatureOwner(BoundType callee)
        => CallableCore(callee) is { Kind: BoundTypeKind.FunctionItem or BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto declaration } ? declaration : null;

    // SPEC 10.5: the input Origins a slot binding holds. `perCall` collects the parameters of S whose per-call Origin, the outer Origin
    // of a direct input that S quantifies, the binding holds, alone or in a meet; `foreign` is any other input Origin, except one that a
    // Function Type inside the binding quantifies itself (an inner per-call Origin never escapes its Function Type) and an input Origin
    // of a function enclosing the reference, which is fixed in its body (SPEC 15.6.5), so a bound argument may hold it.
    private static void InputOrigins(BoundType type, BoundType parameters, Koto? binder, Koto use, bool nested, ref ulong perCall, ref bool foreign)
    {
        Origin(type.Origin, parameters, binder, use, nested, ref perCall, ref foreign, 0);
        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            Origin(type.OriginArguments[i], parameters, binder, use, nested, ref perCall, ref foreign, 0);
        }

        nested |= type.Kind == BoundTypeKind.Function;
        for (var i = 0; i < type.Components.Count; i++)
        {
            InputOrigins(type.Components[i], parameters, binder, use, nested, ref perCall, ref foreign);
        }

        static void Origin(BoundOrigin? origin, BoundType parameters, Koto? binder, Koto use, bool nested, ref ulong perCall, ref bool foreign, int depth)
        {
            if (origin is null)
            {
                return;
            }

            if (origin.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < origin.Operands.Count && depth < 64; i++)
                {
                    Origin(origin.Operands[i], parameters, binder, use, nested, ref perCall, ref foreign, depth + 1);
                }

                return;
            }

            if (origin.Kind != OriginKind.Input)
            {
                return;
            }

            if (binder is not null && ReferenceEquals(origin.Binder, binder) && origin.Occurrence is null && (uint)origin.Slot < (uint)Math.Min(parameters.Components.Count, 64) &&
                ReferenceEquals(parameters.Components[origin.Slot].Origin, origin))
            {
                perCall |= 1UL << origin.Slot;
            }
            else if (!(nested && origin.Binder is FunctionTypeKoto && !ReferenceEquals(origin.Binder, binder)) && !(origin.Binder is FunctionKoto owner && IsWithin(use, owner)))
            {
                foreign = true;
            }
        }
    }

    // SPEC 15.4.4: whether a written Type argument is the binding except in Origins it omits, which local inference would solve.
    private static bool SameExceptOmittedOrigins(BoundType written, BoundType binding)
    {
        if (ReferenceEquals(written, binding))
        {
            return true;
        }

        if (!written.SameExceptOrigins(binding) || !Omitted(written.Origin, binding.Origin))
        {
            return false;
        }

        for (var i = 0; i < written.OriginArguments.Count; i++)
        {
            if (!Omitted(written.OriginArguments[i], binding.OriginArguments[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < written.Components.Count; i++)
        {
            if (!SameExceptOmittedOrigins(written.Components[i], binding.Components[i]))
            {
                return false;
            }
        }

        return true;

        static bool Omitted(BoundOrigin? written, BoundOrigin? binding) => ReferenceEquals(written, binding) || written?.Kind == OriginKind.Inference;
    }

    // SPEC 15.4.4: whether a written Type argument omits an Origin, an inference variable that the binding from S solves.
    private static bool OmitsOrigins(BoundType written)
    {
        if (!written.CarriesOrigin)
        {
            return false;
        }

        if (written.Origin?.Kind == OriginKind.Inference)
        {
            return true;
        }

        for (var i = 0; i < written.OriginArguments.Count; i++)
        {
            if (written.OriginArguments[i].Kind == OriginKind.Inference)
            {
                return true;
            }
        }

        for (var i = 0; i < written.Components.Count; i++)
        {
            if (OmitsOrigins(written.Components[i]))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 10.7: the member of a declared Type that a contract walk's failure names by its path through nested Callable signatures
    // (ComparisonFailure.Path: one step per member, the outermost highest; a parameter n + 1 or the result), or null where the
    // failure is the Type's own or its path leaves the Function Types.
    private static BoundType? FailedMember(BoundType declared, in ComparisonFailure failure)
    {
        var member = declared;
        for (var depth = failure.Depth - 1; depth >= 0; depth--)
        {
            var step = (byte)(failure.Path >> (8 * depth));
            if (member is not { Kind: BoundTypeKind.Function, Components: [var inputs, var result] })
            {
                return null;
            }

            if (step != ComparisonFailure.ResultStep && (step == 0 || step > inputs.Components.Count))
            {
                return null;
            }

            member = step == ComparisonFailure.ResultStep ? result : inputs.Components[step - 1];
        }

        return failure.Depth == 0 ? null : member;
    }

    // DECISIONS BU-240 (temporary, PLAN G886): whether a Closure or Function Item value at a common Function Type carries the located limit
    // its erasure reported for an unformed split (ErasesToFunction).
    private static bool RestsOnErasure(Koto node, BoundType actual, BoundType expected)
        => node.BindingFailure == BindingFailure.Unsupported && actual.Kind is BoundTypeKind.Closure or BoundTypeKind.FunctionItem &&
            expected.Kind == BoundTypeKind.Function;

    // SPEC 10.7, 15.3.7: the whole-contract comparison ContractFits judges, without an implementation signature (it solves the
    // implementation's Origins itself): every proof, its solve's too, assumes the required signature's own well-formedness (the owner's
    // (a)), and `rows` is the sink of the reader that builds it (CallableInstance.Rows; null: a region conjunct is not proven).
    private static CallableInstance ContractComparison(BoundType required, List<BindingObligation>? rows)
        => new(null, null, required, required.Binder, null) { Rows = rows };

    // SPEC 7.7.4: an erasure's receiver condition, its source's minimum receiver Shared (BoundClosure.Receiver, classified with its body);
    // an Item, or a Closure with no signature, has none.
    private static bool SharedReceiver(BoundType actual) => (BoundClosure.Of(actual)?.Receiver ?? SemanticsKind.Ref) == SemanticsKind.Ref;

    // SPEC 7.7.4: a concrete Closure or Function Item converts to an expected common Function Type when its minimum receiver is Shared,
    // its contract fits under its conditions (DefinitionContractFits), its complete environment is Owned and its result contract does
    // not depend on its receiver (ReceiverIndependence). Initializations, returns and call arguments share this judgment. A failure
    // publishes its first failed part for its record (erasureOutcomes): the receiver, the signature's structure and the
    // receiver dependence, the consumer's Type mismatch; Owned (its Error's cause held, its member named) and a
    // condition, the value's own failure, recorded here at `node` (FailOwnedConversion, RecordCondition; DECISIONS BU-107), on which every
    // consumer rests: a call argument, a payload, a Tuple or array element, a grouping, a join arm. The conversion at the value is a
    // reader of its comparison's region conjuncts: it forms them as the body's rows when the signature fits (FormComparisonRows). An Owned
    // part whose proof read a region with no answer yet (ValueIn's deferral fact, while a body is bound) is no verdict: the erasure's
    // obligation (BindingObligationKind.Owned), judged once the body is bound (JudgeErasedOwned; the owner's O-471 (a)), its other parts
    // judged here.
    private bool ErasesToFunction(Koto node, BoundType actual, BoundType expected)
    {
        var unformedMark = this.unformedWrites;
        var rows = this.comparisonRows.Count;
        var fits = this.ErasureFits(actual, expected, node, this.comparisonRows, out var failure);
        if (this.UnformedSince(unformedMark))
        {
            // DECISIONS BU-240 (temporary, PLAN G886): the Item's signature rests on a Type no binding represents (ItemSignatureFits), so the
            // conversion is one located Unsupported_Kd, which forms no comparison row; its environment is still judged
            // (FailErasedOwned), that record the node's one.
            this.DropComparisonRows(rows);
            if (!this.FailErasedOwned(node, actual, expected))
            {
                this.Fail(node, BindingFailure.Unsupported, true);
            }

            return false;
        }

        if (fits)
        {
            this.FormComparisonRows(rows);
        }
        else
        {
            this.DropComparisonRows(rows); // A Function Item's contract (ContractFits) fails after the rows its earlier conditions left.
        }

        return this.DecideErasure(node, actual, expected, fits, failure);
    }

    // SPEC 7.7.4: an erasure's decision from the verdict of its receiver and signature part (`fits`, with the `failure` of a
    // signature that does not fit), and its published outcome. The verdict is ErasesToFunction's own (ErasureFits), or a Function
    // reference's, whose candidate search decided that its bound Item's signature meets the required Type in no case of its split
    // (BindFunctionReference; DECISIONS BU-240, temporary, PLAN G886), so that decision is not searched a second time.
    private bool DecideErasure(Koto node, BoundType actual, BoundType expected, bool fits, in ComparisonFailure failure)
    {
        var unformedMark = this.unformedWrites;
        var signature = this.ClosureSignature(actual) ?? this.FunctionItemSignature(actual);
        this.unformedWrites = unformedMark; // An Item that no case of its split fits fails by itself (ItemSignatureFits).
        OwnedFact owned = default;
        var independence = ConstraintProof.Proven;
        var shape = signature is null || !TyCtxt.StorageMatches(expected, signature); // A fit may adapt past a storage difference.
        var held = this.readsUnboundedRegion;
        this.readsUnboundedRegion = false;
        var part = !SharedReceiver(actual) ? ErasurePart.Receiver
            : shape && !fits ? ErasurePart.Shape
            : this.ProveErasedOwned(actual, node, this.formingBody, out owned) != ConstraintProof.Proven ? (shape ? ErasurePart.Shape : ErasurePart.Owned)
            : ErasurePart.Condition;
        // R7-O2 O2-F: a target Type naming a deferred slot's atom is a reading with no answer yet, as the deferral fact.
        var deferred = part == ErasurePart.Owned && (this.readsUnboundedRegion || this.DeferredAtomIn(expected) is not null) && this.formingBody.IsBody;
        this.readsUnboundedRegion = held;
        if (part == ErasurePart.Condition || deferred)
        {
            part = fits && (independence = this.ReceiverIndependence(actual)) != ConstraintProof.Proven ? ErasurePart.Dependence : ErasurePart.Condition;
        }

        if (independence == ConstraintProof.Error)
        {
            this.Fail(node, BindingFailure.Unsupported); // A retention past its limits decides nothing: the located limit at `node`.
        }

        if (part == ErasurePart.Condition && fits)
        {
            if (deferred)
            {
                this.AddObligation(new(BindingObligationKind.Owned, node, BindingDeadline.BodyOrigins, actual));
            }

            return true;
        }

        (this.erasureOutcomes ??= new())[(actual, expected)] = new(part, owned);
        if (part == ErasurePart.Condition)
        {
            this.RecordCondition(node, failure, signature!, expected);
        }
        else if (part == ErasurePart.Owned)
        {
            this.FailOwnedConversion(actual, expected, node, node);
        }

        return false;
    }

    // SPEC 7.7.4, 15.6.4 (the owner's O-28 (a), DECISIONS BU-116): whether a concrete Closure's result contract depends on its call
    // receiver: its call's retention (TyCtxt.Retains, the one source, whose rule A keeps every input at a raw layer without an Origin that
    // the call writes in its result) has the result retain the receiver, input 0, by its own value. A Function Item has no receiver
    // input (TyCtxt.HasReceiverInput). Proven when independent, Refuted when it depends; a retention past its limits decides nothing
    // (Error): the located limit, at applicability (ErasedArgumentsEnvironment) and at the erasure's value (ErasesToFunction) alike.
    private ConstraintProof ReceiverIndependence(BoundType actual)
    {
        if (!this.types.HasReceiverInput(actual))
        {
            return ConstraintProof.Proven;
        }

        var retention = this.types.Retains(actual, this.ClosureDefinitionSignature(actual)!, null);
        return retention.IsLimit ? ConstraintProof.Error
            : (retention.Result.Kinds[0] & RetentionKind.Value) != 0 ? ConstraintProof.Refuted : ConstraintProof.Proven;
    }

    // SPEC 7.7.4, 10.7, 15.3.7, 15.6.1: the receiver and signature part of an erasure as its conversion reads it, under the premises
    // of `use`: a Closure's Shared receiver and its contract (DefinitionContractFits), a Function Item's contract (ItemSignatureFits).
    // ErasesToFunction's alone: every fit before it, a join source's late Type included (LateAdaptation), reads the structural part
    // (ErasureSignatureFits; ArgFlow ruling 184). `rows` takes the comparison's region conjuncts (CallableInstance.Rows).
    private bool ErasureFits(BoundType actual, BoundType expected, Koto use, List<BindingObligation>? rows, out ComparisonFailure failure)
    {
        failure = default;
        return actual.Kind == BoundTypeKind.Closure
            ? expected.Kind == BoundTypeKind.Function && SharedReceiver(actual) &&
                this.ClosureSignature(actual) is not null && this.DefinitionContractFits(actual, expected, use, rows, out failure)
            : expected.Kind == BoundTypeKind.Function && actual.Kind == BoundTypeKind.FunctionItem &&
                this.ItemSignatureFits(actual, expected, use, rows, out failure);
    }

    // SPEC 10.7, 15.3.7: a Function value converts to an expected Function Type of its storage where it meets it, as a Closure or a
    // Function Item erases (ErasesToFunction), also as a Tuple or array literal's element: a contract whose condition fails fails the
    // value with that conversion's own record, by its walk's facts (RecordMismatch), never a mismatch of the literal holding it (held
    // 353a12e81). A Closure's or Function Item's erasure that recorded its failed condition or Owned part, or the located limit of an
    // unformed split (ErasesToFunction; DECISIONS BU-240), is the value's failure. The value's Type at the position: null when it failed;
    // the expected Type when it converted, written to the node's own Type (unlike an erasure, which keeps the node's Type and publishes
    // ErasedFunctionType for HIR's Erasure; subtyping inserts no operation, SPEC 3.8, so no adaptation records it), so every consumer's
    // fit (a declaration, a join, a store, a return) is identity and the conversion's comparison has one judge; otherwise its own.
    private BoundType? ConvertFunctionValue(Koto node, BoundType actual, BoundType expected)
    {
        if (node.BindingFailure == BindingFailure.OriginContract || RestsOnErasure(node, actual, expected) ||
            (node.BindingState == BindingState.Invalid && this.ErasureOf(actual, expected) is { Part: ErasurePart.Owned }))
        {
            return null;
        }

        if (expected.Kind != BoundTypeKind.Function || actual.Kind != BoundTypeKind.Function || ReferenceEquals(actual, expected) ||
            !TyCtxt.StorageMatches(expected, actual))
        {
            return actual;
        }

        if (this.CheckTypeUse(actual, expected, node, out var failure))
        {
            return node.BoundType = expected;
        }

        if (!failure.IsCondition)
        {
            return actual; // A limit (design-r2 A1) or a plain position stays the enclosing fit's record (S3Demo: at the declaration).
        }

        this.FailMismatch(node, node, actual, expected, failure);
        return null;
    }

    // SPEC 7.7.4, 10.5, 10.5.3, 10.7, 10.8, 15.6.1 (DECISIONS R4-M155, R4-M213; ArgFlow ruling 181): the signature part of an erasure,
    // which every fit before selection reads (a candidate trial, a checked part's query, FitPerCall, a join source's late Type:
    // LateAdaptation, ArgFlow ruling 184): the structural part of the
    // callable's signature's compatibility (FitsStructuralPartAt, every pre-selection fit's one entry), a Closure's with its Shared
    // receiver (SharedReceiver, a fact of its bound body), a Function Item's alike. Its Origin and Loan contract is the conversion's
    // alone (ErasesToFunction, ErasureFits), judged after selection at the argument, where a failure never selects another candidate; a
    // Closure's Owned environment is a Constraint of the candidate (ErasedArgumentsEnvironment). An Item whose signature formed a Type no
    // binding represents rests on its split as ItemSignatureFits does (DECISIONS BU-240, temporary, PLAN G886).
    private bool ErasureSignatureFits(BoundType actual, BoundType expected, Koto use)
    {
        if (expected.Kind != BoundTypeKind.Function || actual.Kind != BoundTypeKind.FunctionItem)
        {
            return expected.Kind == BoundTypeKind.Function && actual.Kind == BoundTypeKind.Closure && SharedReceiver(actual) &&
                this.ClosureSignature(actual) is { } closureSignature &&
                this.FitsStructuralPartAt(closureSignature, this.CompatibleSignature(closureSignature, expected), use);
        }

        var mark = this.unformedWrites;
        this.CarrySplitPart(this.splitPartStamp); // In a split search: what the enclosing part read before (the required Type) is handed on.
        if (this.FunctionItemSignature(actual) is { } itemSignature)
        {
            this.EnterSplitPart(); // The signature's inputs and result are parts of their own, each forming its own Type again.
            return this.FitsStructuralPartAt(itemSignature, this.CompatibleSignature(itemSignature, expected), use);
        }

        return this.UnformedSince(mark) && this.RestsOnSplitCases(
            mark, (actual, expected, use), static (binding, check) => !binding.ErasureSignatureFits(check.actual, check.expected, check.use));
    }

    // SPEC 7.7.4, 10.7: whether a Function Item's signature fits the required contract. DECISIONS BU-240 with rule W (BU-79; C-359 C1, C2;
    // temporary, PLAN G886): an Item whose signature formed a Type no binding represents has its binder fixed before any evidence, so
    // the same check is judged in each combination of split cases (RestsOnSplitCases). One that no combination fits fails by itself;
    // one that some combination fits rests on the split: it fits here, and its writes stay, so its reader rests on it too (the
    // conversion's decision, ErasesToFunction). `rows` takes the contract's region conjuncts in every combination
    // (CallableInstance.Rows), which its reader forms or drops.
    private bool ItemSignatureFits(BoundType item, BoundType expected, Koto use, List<BindingObligation>? rows, out ComparisonFailure failure)
    {
        failure = default;
        var mark = this.unformedWrites;
        this.CarrySplitPart(this.splitPartStamp); // In a split search: what the enclosing part read before (the required Type) is handed on.
        if (this.FunctionItemSignature(item) is not null)
        {
            this.EnterSplitPart(); // The contract's inputs and result are parts of their own, each forming its own Type again.
            return this.DefinitionContractFits(item, expected, use, rows, out failure);
        }

        return this.UnformedSince(mark) && this.RestsOnSplitCases(
            mark, (item, expected, use, rows), static (binding, check) => !binding.ItemSignatureFits(check.item, check.expected, check.use, check.rows, out _));
    }

    // SPEC 10.7, 15.3.7: the implementation's per-call Origins are solved against the required signature, whose quantifiers and fixed
    // Origins stay rigid, under its declared relations and result premises; then the required inputs fit the implementation's, its
    // result fits the required one, and its conditions are proven for the solution. The implementation's Types are its members within
    // `container` with `arguments` bound, or those of `item`, a Function Item or a Closure (its signature). `comparison` is the query
    // (ContractComparison): the required signature and the row sink that takes the region conjuncts its premises leave unproven.
    // `wildcards` (SPEC 23.1.2, DECISIONS Q13 rule W) are the implementation's parts its caller does not compare: those that failed where
    // written (RefutedSignatureParts), given only by a reader that reads the signature's verdict itself; every other caller gives none.
    private bool ContractFits(
        Koto use,
        BindingSymbol symbol,
        CallableInstance comparison,
        BoundType? container,
        BoundType? item,
        SlotFrame? frame,
        Span<BoundArgumentOperation> operations,
        BoundOrigin[] origins,
        BoundOrigin[] inputs,
        ulong wildcards)
    {
        // PLAN G1082: the comparison's proof requests share one premise environment of its use and comparison (Binding.OriginPremises.cs).
        var session = this.OpenPremiseSession();
        try
        {
            var function = (FunctionKoto)symbol.Declaration;
            var required = comparison.Expected!;
            var parameters = required.Components[0];
            var generic = function.GenericArguments.Count;
            var inputCount = InputOriginCount(function);
            var closure = item is { Kind: BoundTypeKind.Closure } ? this.ClosureSignature(item) : null;

            // DECISIONS BU-240 (G812 ruling 103 D1): in a split search each input and the result is a part of the check; a part's structural
            // misfit is blamed on the bindings its own Type reads, with those the required signature read before (CarrySplitPart), and
            // any other failure (an Origin, a premise, a clause) on the whole run: the solve joins every part.
            this.CarrySplitPart(this.splitPartStamp);
            var inference = this.BeginOriginInference(this.RentInference(use, function));
            try
            {
                inference.Comparison = comparison;
                this.contractFailure = default;
                for (var i = 0; i < function.Parameters.Count; i++)
                {
                    if (Read(Declared(i)) is not { } parameter)
                    {
                        return false;
                    }

                    // Only the implementation's per-call binders are inferred; the
                    // required signature's quantifiers and fixed Origins remain rigid.
                    this.MatchInputOrigins(parameter, parameters.Components[i], function, origins, inputs);
                    inference.Source = ((byte)(i + 1), null, false);
                    this.CollectOriginInference(parameter, parameters.Components[i], inference, use);
                }

                if (Read(Declared(function.Parameters.Count)) is { } produced)
                {
                    inference.Source = (ComparisonFailure.ResultStep, null, false);
                    this.CollectOriginInference(produced, required.Components[1], inference, use, result: true);
                }

                // The solver's own failure: the first bound, a member's position or a declared clause, that no solution satisfies.
                inference.Source = default;
                if (!this.SolveCallOriginInference(inference, origins, inputs, [], null))
                {
                    this.contractFailure = inference.Failure;
                    return false;
                }
            }
            finally
            {
                this.ReturnInference(inference); // Nothing reads the state after its solve.
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                this.EnterSplitPart();
                if (Read(Declared(i)) is not { } written || Substitute(written) is not { } parameter)
                {
                    return false;
                }

                var walk = new RelationWalk(this, this, use, OriginPart.Contract);
                if (!walk.Fits(parameters.Components[i], parameter, instance: comparison))
                {
                    if (RefutedPart(wildcards, i))
                    {
                        continue; // A wildcard (rule W): its misfit fails nothing and it has no premises of its own; the next part is compared.
                    }

                    this.contractFailure = walk.Failure.Within((byte)(i + 1));
                    BlameMisfit(Declared(i), walk.Failure);
                    return false;
                }

                this.EnterJointSplitPart();

                this.failedResultPremise = null;
                if (!this.ProvesInputPremises(parameter, parameters.Components[i], use, comparison))
                {
                    this.contractFailure = this.PremiseFailure(ComparisonFailureKind.WellFormedness, comparison) with { Part = i };
                    return false;
                }

                if (!operations.IsEmpty)
                {
                    // Compare the same substituted contract that established applicability. In particular,
                    // independently named per-call Origins are fixed to the required signature before ranking.
                    // References insert no adaptations and use no defaults; results never rank candidates.
                    operations[i] = new(null, null, parameter, ArgumentOperationKind.Value, ArgumentAdaptation.Exact);
                }
            }

            this.failedCallClause = null;
            if (!this.CheckCallOriginRelations(function, origins, inputs, use, null, comparison))
            {
                this.contractFailure = this.failedCallClause is { } clause
                    ? new ComparisonFailure(
                        this.ComparedRegionLimit(clause.Longer, clause.Shorter, clause.Equality, use) ? ComparisonFailureKind.Limit : ComparisonFailureKind.Clause,
                        clause.Longer,
                        clause.Shorter,
                        clause.Equality,
                        comparison) { Clause = clause.Syntax }
                    : default;
                return false;
            }

            this.EnterSplitPart();
            if (Read(Declared(function.Parameters.Count)) is not { } boundResult || Substitute(boundResult) is not { } result)
            {
                return false;
            }

            var resultWalk = new RelationWalk(this, this, use, OriginPart.Contract);
            if (!resultWalk.Fits(result, CompatibleResult(result, required.Components[1]), instance: comparison))
            {
                if (RefutedPart(wildcards, function.Parameters.Count))
                {
                    return true;
                }

                this.contractFailure = resultWalk.Failure.Within(ComparisonFailure.ResultStep);
                BlameMisfit(Declared(function.Parameters.Count), resultWalk.Failure);
                return false;
            }

            this.EnterJointSplitPart();

            this.failedResultPremise = null;
            if (!this.ProvesResultPremises(function, result, use, comparison))
            {
                this.contractFailure = this.PremiseFailure(ComparisonFailureKind.ResultPremise, comparison);
                return false;
            }

            return true;

            // DECISIONS BU-240 (G812 rulings 103 D1, 105; temporary, removed with (R)): in a split search, what a failed part of the contract is
            // blamed on. Any failure but a structural one joins every part (an Origin, a premise, a clause: the solve). A structural
            // misfit belongs to the layer that misfits: where the walk's failure names a member inside the part's nested Callable
            // signatures (FailedMember), that member is the failing part, formed again on its own (Read), so the run blames the bindings
            // its layer reads and not those of its siblings; otherwise the whole part, as formed.
            void BlameMisfit(BoundType? declared, in ComparisonFailure failure)
            {
                if (failure.Kind != ComparisonFailureKind.Shape)
                {
                    this.EnterJointSplitPart();
                }
                else if (this.splitOpen && declared is not null && FailedMember(declared, failure) is { } member)
                {
                    this.EnterSplitPart();
                    _ = Read(member) is { } layer ? Substitute(layer) : null;
                }
            }

            // The implementation's input `i`, or its result at i = the parameter count, as the definition declares it: a Closure's from its
            // signature, any other's written Type.
            BoundType? Declared(int i)
                => i < function.Parameters.Count
                    ? (closure is not null ? closure.Components[0].Components[i] : function.Parameters[i].Type.BoundType)
                    : closure is not null ? closure.Components[1] : symbol.Type;

            // A declared part (or a member of one) as the compared value reads it: a Closure's in the use's scope, any other's through the
            // definition's item, container or frame (Bound).
            BoundType? Read(BoundType? declared)
                => declared is null ? null : closure is not null ? this.ContractType(declared, this.ConstraintScope(use)) : Bound(declared);

            BoundType? Bound(BoundType type)
            {
                var bound = item is not null ? this.ItemType(type, item, function)
                    : frame is not { } slots ? this.MemberType(type, container) : this.Substitute(type, SubstitutionOf(function, container, slots.Types[..generic], slots.Lengths[..generic]));
                return bound is null ? null : this.ContractType(bound, this.ConstraintScope(use));
            }

            // The solution's images; null where an atom of the function has none (LacksImage).
            BoundType? Substitute(BoundType type)
                => this.LacksImage(type, function, origins.AsSpan(0, function.Origins.Count), inputs.AsSpan(0, inputCount))
                    ? null : this.SubstituteStoredOrigins(type, function, origins.AsSpan(0, function.Origins.Count), inputs.AsSpan(0, inputCount));
        }
        finally
        {
            session?.Close();
        }
    }

    // SPEC 15.3.3, 15.3.4, 15.6.1 (DECISIONS R4-M162; the owner's O-397 (A), PLAN G962): a nested signature's written result, a Function
    // Type's or an anonymous function's, is a Type of the declaration that contains it, formed where it is written: its Types' own clauses
    // are obligations at that occurrence, under that declaration's premises and the signature's own well-formed inputs, for every choice
    // of its per-call Origins (AddTypeClauseObligations forms a row only for a clause those premises do not entail). It is never a premise
    // of a body and never proven by a call. The one entry of both: BindFunctionType, wherever a Function Type is bound (a Closure header's
    // probe included; the obligation set keeps one), and an anonymous function's own written result once per binding of its header, at
    // its commitment (BindClosureHeader) or at its failed fit (FailClosureHeader), never by a header probe (ClosureSignatureFits). The
    // bound result passes through, also when there is no written result (`written` null) or its binding failed (null).
    private BoundType? FormNestedResult(BoundType? result, Koto? written)
    {
        if (result is not null && written is not null)
        {
            this.AddTypeClauseObligations(result, written);
        }

        return result;
    }

    private BoundType? BindFunctionType(FunctionTypeKoto function, BindingScope scope)
    {
        var tuple = function.Parameters as TupleTypeKoto;
        var count = tuple?.ElementNodes.Count ?? 1;
        var scratch = this.RentTypes(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var syntax = tuple is null ? function.Parameters : tuple.ElementNodes[i];
                if (this.BindType(syntax, scope, new(TypePosition.Parameter, function, i, true)) is not { } input)
                {
                    return null;
                }

                scratch[i] = input;
            }

            var parameters = count == 0 ? BoundType.Unit : this.types.Tuple(scratch.AsSpan(0, count));
            if (tuple is not null)
            {
                this.PublishType(tuple, parameters);
            }

            // The result is formed where it is written (FormNestedResult); the inputs add no obligation here (R4-M137).
            var result = this.FormNestedResult(this.BindType(function.ReturnType, scope, new(TypePosition.Result, function)), function.ReturnType);
            if (result is null)
            {
                return null;
            }

            return this.types.Intern(BoundTypeKind.Function, null, SemanticsKind.Owner, [parameters, result], binder: function);
        }
        finally
        {
            this.typeScratch.Return(scratch, clearArray: true);
        }
    }
}
