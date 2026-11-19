// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Function Types and their conversions (SPEC 7.7.4, 8.10, 10.7, 15.3.7, 15.6.4).
// - Responsibility: a Function Type's syntax (BindFunctionType), which gives the Type its per-call binder (BoundType.Binder, the one
//   answer to whose per-call inputs a Function Type's are); a nested signature's written result's formation, a Function Type's and an
//   anonymous function's (FormNestedResult; SPEC 15.3.4, DECISIONS R4-M162); the shape of a signature's per-call inputs (SignatureOwner,
//   InputOrigins); the erasure of a Closure or Function Item to a common
//   Function Type (ErasesToFunction, and ErasureSignatureFits, its signature part that applicability reads); and the solved
//   comparison of a named function's contract with a required signature (ContractFits).
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
    // The declaration whose own per-call inputs the signature of a Function Item or closure names (SPEC 8.10).
    internal static Koto? SignatureOwner(BoundType callee)
    {
        var owner = callee is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } borrowed ? borrowed.Components[0] : callee;
        return owner is { Kind: BoundTypeKind.FunctionItem or BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto declaration } ? declaration : null;
    }

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

    // SPEC 7.7.4: an erasure's receiver condition, its source's minimum receiver Shared (BoundClosure.Receiver, classified with its body);
    // an Item, or a Closure with no signature, has none.
    private static bool SharedReceiver(BoundType actual) => (BoundClosure.Of(actual)?.Receiver ?? SemanticsKind.Ref) == SemanticsKind.Ref;

    // SPEC 7.7.4: a concrete Closure or Function Item converts to an expected common Function Type when its minimum receiver is Shared,
    // its signature fits under its conditions (CallableSignatureFits), its complete environment is Owned and its result contract does
    // not depend on its receiver (ReceiverIndependence). Initializations, returns and call arguments share this judgment. A failure
    // publishes its first failed part for its record and Note (erasureOutcomes): the receiver, the signature's structure and the
    // receiver dependence, the consumer's Type mismatch; Owned (its Error's cause held, its member named) and a
    // condition, the value's own failure, recorded here at `node` (FailOwnedConversion, RecordCondition; DECISIONS BU-107), on which every
    // consumer rests: a call argument, a payload, a Tuple or array element, a grouping, a join arm.
    private bool ErasesToFunction(Koto node, BoundType actual, BoundType expected)
    {
        var failure = default(ComparisonFailure);
        var receiver = BoundClosure.Of(actual)?.Receiver ?? SemanticsKind.Ref; // An Item, or a closure with no signature, has none.
        var signature = this.ClosureSignature(actual) ?? this.FunctionItemSignature(actual);
        var fits = this.ErasureFits(actual, expected, node, out failure);
        OwnedFact owned = default;
        var independence = ConstraintProof.Proven;
        var shape = signature is null || !ReferenceTypes.StorageMatches(expected, signature); // A fit may adapt past a storage difference.
        var part = !SharedReceiver(actual) ? ErasurePart.Receiver
            : shape && !fits ? ErasurePart.Shape
            : this.ProveErasedOwned(actual, node, out owned) != ConstraintProof.Proven ? (shape ? ErasurePart.Shape : ErasurePart.Owned)
            : fits && (independence = this.ReceiverIndependence(actual)) != ConstraintProof.Proven ? ErasurePart.Dependence
            : ErasurePart.Condition;
        if (independence == ConstraintProof.Error)
        {
            this.Fail(node, BindingFailure.Unsupported); // A retention past its limits decides nothing: the located limit at `node`.
        }

        if (part == ErasurePart.Condition && fits)
        {
            return true;
        }

        (this.erasureOutcomes ??= new())[(actual, expected)] = new(part, fits, receiver, owned);
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

    // SPEC 7.7.4, 10.7, 15.3.7 (PLAN G504): the receiver and signature part of an erasure as its decision reads it, under the premises
    // of `use`: a Closure's Shared receiver and its signature (CallableSignatureFits), a Function Item's contract (ErasureSignatureFits).
    // ErasesToFunction's, and a join source's late Type (LateAdaptation), so a join decides no erasure apart from it.
    private bool ErasureFits(BoundType actual, BoundType expected, Koto use, out ComparisonFailure failure)
    {
        failure = default;
        return actual.Kind == BoundTypeKind.Closure
            ? expected.Kind == BoundTypeKind.Function && SharedReceiver(actual) &&
                this.ClosureSignature(actual) is { } signature && this.DefinitionContractFits(actual, signature, expected, use, out failure)
            : this.ErasureSignatureFits(actual, expected, use, out failure);
    }

    // SPEC 10.7, 15.3.7: a Function value converts to an expected Function Type of its storage where it meets it, as a Closure or a
    // Function Item erases (ErasesToFunction), also as a Tuple or array literal's element: a contract whose condition fails fails the
    // value with that conversion's own record, by its walk's facts (RecordMismatch), never a mismatch of the literal holding it (held
    // 353a12e81). A Closure's or Function Item's erasure that recorded its failed condition or Owned part (ErasesToFunction) is the
    // value's failure.
    private bool FailsFunctionConversion(Koto node, BoundType actual, BoundType expected)
    {
        if (node.BindingFailure == BindingFailure.OriginContract ||
            (node.BindingState == BindingState.Invalid && this.ErasureOf(actual, expected) is { Part: ErasurePart.Owned }))
        {
            return true;
        }

        if (expected.Kind != BoundTypeKind.Function || actual.Kind != BoundTypeKind.Function || ReferenceEquals(actual, expected) ||
            !ReferenceTypes.StorageMatches(expected, actual) || this.CheckTypeUse(actual, expected, node, out var failure) || !failure.IsCondition)
        {
            return false; // A limit (design-r2 A1) or a plain position stays the enclosing fit's record (S3Demo: at the declaration).
        }

        this.FailMismatch(node, node, actual, expected, failure);
        return true;
    }

    // SPEC 7.7.4, 10.5, 10.7, 10.8, 15.6.1: the signature part of an erasure, which a call's applicability uses. A Closure's is the
    // structural part of its signature's compatibility (FitsStructuralPartAt, every pre-selection fit's one entry, DECISIONS R4-M155)
    // with its Shared receiver (SharedReceiver, a fact of its bound body); its Owned environment is a Constraint of the candidate
    // (ErasedArgumentsEnvironment), and its Origin part is judged after selection, at the argument (ErasesToFunction), where a failure never
    // selects another candidate. A Function Item's Origin conditions still belong to its signature, proven from the required contract at
    // `use` (SPEC 15.3.7; PLAN G379's Item half).
    private bool ErasureSignatureFits(BoundType actual, BoundType expected, Koto use, out ComparisonFailure failure)
    {
        failure = default;
        return expected.Kind == BoundTypeKind.Function &&
            ((actual.Kind == BoundTypeKind.FunctionItem && this.FunctionItemSignature(actual) is { } signature &&
            this.DefinitionContractFits(actual, signature, expected, use, out failure)) ||
            (actual.Kind == BoundTypeKind.Closure && SharedReceiver(actual) && this.ClosureSignature(actual) is { } closureSignature &&
            this.FitsStructuralPartAt(closureSignature, this.CompatibleSignature(closureSignature, expected), use)));
    }

    // SPEC 10.7, 15.3.7: the implementation's per-call Origins are solved against the required signature, whose quantifiers and fixed
    // Origins stay rigid, under its declared relations and result premises; then the required inputs fit the implementation's, its
    // result fits the required one, and its conditions are proven for the solution. The implementation's Types are its members within
    // `container` with `arguments` bound, or those of the Function Item `item`.
    private bool ContractFits(
        Koto use,
        BindingSymbol symbol,
        FunctionKoto function,
        BoundType required,
        BoundType? container,
        BoundType? item,
        SlotFrame? frame,
        Span<BoundArgumentOperation> operations,
        BoundOrigin[] origins,
        BoundOrigin[] inputs)
    {
        var parameters = required.Components[0];
        var generic = function.GenericArguments.Count;
        var inputCount = InputOriginCount(function);
        var closure = item is { Kind: BoundTypeKind.Closure } ? this.ClosureSignature(item) : null;

        // The owner's (a): every proof of the comparison, its solve's too, assumes the required signature's own well-formedness.
        var comparison = new CallableInstance(null, null, required, required.Binder, null);
        var inference = this.BeginOriginInference(use, function);
        inference.Comparison = comparison;
        this.contractFailure = default;
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (Input(i) is not { } parameter)
            {
                return false;
            }

            // Only the implementation's per-call binders are inferred; the
            // required signature's quantifiers and fixed Origins remain rigid.
            this.MatchInputOrigins(parameter, parameters.Components[i], function, origins, inputs);
            inference.Source = ((byte)(i + 1), null, false);
            this.CollectOriginInference(parameter, parameters.Components[i], inference);
        }

        if (Output() is { } produced)
        {
            inference.Source = (ComparisonFailure.ResultStep, null, false);
            this.CollectOriginInference(produced, required.Components[1], inference, result: true);
        }

        // The solver's own failure: the first bound, a member's position or a declared clause, that no solution satisfies.
        inference.Source = default;
        if (!this.SolveCallOriginInference(function, inference, origins, inputs, null))
        {
            this.contractFailure = inference.Failure;
            return false;
        }

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (Input(i) is not { } written || Substitute(written) is not { } parameter)
            {
                return false;
            }

            var walk = new RelationWalk(this, this, use, OriginPart.Contract);
            if (!walk.Fits(parameters.Components[i], parameter, instance: comparison))
            {
                this.contractFailure = walk.Failure.Within((byte)(i + 1));
                return false;
            }

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
                ? new ComparisonFailure(ComparisonFailureKind.Clause, clause.Longer, clause.Shorter, clause.Equality, comparison) { Clause = clause.Syntax }
                : default;
            return false;
        }

        if (Output() is not { } boundResult || Substitute(boundResult) is not { } result)
        {
            return false;
        }

        var resultWalk = new RelationWalk(this, this, use, OriginPart.Contract);
        if (!resultWalk.Fits(result, CompatibleResult(result, required.Components[1]), instance: comparison))
        {
            this.contractFailure = resultWalk.Failure.Within(ComparisonFailure.ResultStep);
            return false;
        }

        this.failedResultPremise = null;
        if (!this.ProvesResultPremises(function, result, use, comparison))
        {
            this.contractFailure = this.PremiseFailure(ComparisonFailureKind.ResultPremise, comparison);
            return false;
        }

        return true;

        // The implementation's input `i` and result as the compared value reads them: a Closure's from its signature, any other's from
        // the definition's written Types through its item, container or frame (Bound).
        BoundType? Input(int i)
            => closure is not null ? this.ContractType(closure.Components[0].Components[i], this.ConstraintScope(use))
                : function.Parameters[i].Type.BoundType is { } written ? Bound(written) : null;

        BoundType? Output()
            => closure is not null ? this.ContractType(closure.Components[1], this.ConstraintScope(use)) : symbol.Type is { } written ? Bound(written) : null;

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
                Complete(tuple, parameters);
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
