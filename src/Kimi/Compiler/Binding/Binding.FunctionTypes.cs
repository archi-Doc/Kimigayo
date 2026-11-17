// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Function Types and their conversions (SPEC 7.7.4, 8.10, 10.7, 15.3.7, 15.6.4).
// - Responsibility: a Function Type's syntax (BindFunctionType), which gives the Type its per-call binder (BoundType.Binder, the one
//   answer to whose per-call inputs a Function Type's are); the shape of a signature's per-call inputs (SignatureOwner,
//   InputOrigins); the erasure of a Closure or Function Item to a common
//   Function Type (ErasesToFunction, and ErasureSignatureFits, its signature part that applicability reads); and the solved
//   comparison of a named function's contract with a required signature (ContractFits).
// - Inputs: the Types compared, the use whose premises the comparison reads, the Type relation (Binding.TypeRelations) and, for a
//   Closure's erasure, its call's retention (TyCtxt.Retains).
// - Outputs: the verdicts and, for ContractFits, the solved Origins and argument operations.
// - Invariants: an erasure's Shared receiver, Owned environment and receiver-free result are judged after selection, never during
//   applicability.
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

    // DECISIONS D3 (temporary, PLAN G886): whether a Closure or Function Item value at a common Function Type carries the located limit
    // its erasure reported for an unformed split (ErasesToFunction).
    private static bool RestsOnErasure(Koto node, BoundType actual, BoundType expected)
        => node.BindingFailure == BindingFailure.Unsupported && actual.Kind is BoundTypeKind.Closure or BoundTypeKind.FunctionItem &&
            expected.Kind == BoundTypeKind.Function;

    // SPEC 7.7.4: a concrete Closure or Function Item converts to an expected common Function Type when its minimum receiver is Shared,
    // its signature fits under its conditions (CallableSignatureFits), its complete environment is Owned and its result contract does
    // not depend on its receiver (ResultDependsOnReceiver). Initializations, returns and call arguments share this judgment. A failure
    // publishes its first failed part for its record and Note (erasureOutcomes): the receiver, the signature's structure and the
    // receiver dependence, the consumer's Type mismatch; Owned (its Error's cause held, its member named) and a
    // condition, the value's own failure, recorded here at `node` (FailOwnedConversion, RecordCondition; DECISIONS BU-107), on which every
    // consumer rests: a call argument, a payload, a Tuple or array element, a grouping, a join arm.
    private bool ErasesToFunction(Koto node, BoundType actual, BoundType expected)
    {
        var unformedMark = this.unformedWrites;
        var fits = this.ErasureFits(actual, expected, node, out var failure);
        if (this.UnformedSince(unformedMark))
        {
            // DECISIONS D3 (temporary, PLAN G886): the Item's signature rests on a Type no binding represents (ItemSignatureFits), so the
            // conversion is one located Unsupported_Kd; its environment is still judged (FailErasedOwned), that record the node's one.
            if (!this.FailErasedOwned(node, actual, expected))
            {
                this.Fail(node, BindingFailure.Unsupported, true);
            }

            return false;
        }

        return this.DecideErasure(node, actual, expected, fits, failure);
    }

    // SPEC 7.7.4: an erasure's decision from the verdict of its receiver and signature part (`fits`, with the `failure` of a
    // signature that does not fit), and its published outcome. The verdict is ErasesToFunction's own (ErasureFits), or a Function
    // reference's, whose candidate search decided that its bound Item's signature meets the required Type in no case of its split
    // (BindFunctionReference; DECISIONS D3, temporary, PLAN G886), so that decision is not searched a second time.
    private bool DecideErasure(Koto node, BoundType actual, BoundType expected, bool fits, in ComparisonFailure failure)
    {
        var receiver = BoundClosure.Of(actual)?.Receiver ?? SemanticsKind.Ref; // An Item, or a closure with no signature, has none.
        var unformedMark = this.unformedWrites;
        var signature = this.ClosureSignature(actual) ?? this.FunctionItemSignature(actual);
        this.unformedWrites = unformedMark; // An Item that no case of its split fits fails by itself (ItemSignatureFits).
        OwnedFact owned = default;
        var shape = signature is null || !ReferenceTypes.StorageMatches(expected, signature); // A fit may adapt past a storage difference.
        var part = receiver != SemanticsKind.Ref ? ErasurePart.Receiver
            : shape && !fits ? ErasurePart.Shape
            : this.ProveErasedOwned(actual, node, out owned) != ConstraintProof.Proven ? (shape ? ErasurePart.Shape : ErasurePart.Owned)
            : fits && this.ResultDependsOnReceiver(node, actual) ? ErasurePart.Dependence
            : ErasurePart.Condition;
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
    // input (TyCtxt.HasReceiverInput). A retention past its limits decides nothing: the erasure is the located Unsupported_Kd at `node`.
    private bool ResultDependsOnReceiver(Koto node, BoundType actual)
    {
        if (!this.types.HasReceiverInput(actual))
        {
            return false;
        }

        var retention = this.types.Retains(actual, this.ClosureDefinitionSignature(actual)!, null);
        if (retention.IsLimit)
        {
            this.Fail(node, BindingFailure.Unsupported);
            return true;
        }

        return (retention.Result.Kinds[0] & RetentionKind.Value) != 0;
    }

    // SPEC 7.7.4, 10.7, 15.3.7 (PLAN G504): the receiver and signature part of an erasure as its decision reads it, under the premises
    // of `use`: a Closure's Shared receiver and its signature (CallableSignatureFits), a Function Item's contract (ErasureSignatureFits).
    // ErasesToFunction's, and a join source's late Type (LateAdaptation), so a join decides no erasure apart from it.
    private bool ErasureFits(BoundType actual, BoundType expected, Koto use, out ComparisonFailure failure)
    {
        failure = default;
        return actual.Kind == BoundTypeKind.Closure
            ? expected.Kind == BoundTypeKind.Function && (BoundClosure.Of(actual)?.Receiver ?? SemanticsKind.Ref) == SemanticsKind.Ref &&
                this.ClosureSignature(actual) is { } signature && this.CallableSignatureFits(signature, expected, SignatureOwner(actual), use, out failure)
            : this.ErasureSignatureFits(actual, expected, use, out failure);
    }

    // SPEC 10.7, 15.3.7: a Function value converts to an expected Function Type of its storage where it meets it, as a Closure or a
    // Function Item erases (ErasesToFunction), also as a Tuple or array literal's element: a contract whose condition fails fails the
    // value with that conversion's own record, by its walk's facts (RecordMismatch), never a mismatch of the literal holding it (held
    // 353a12e81). A Closure's or Function Item's erasure that recorded its failed condition or Owned part, or the located limit of an
    // unformed split (ErasesToFunction; DECISIONS D3), is the value's failure.
    private bool FailsFunctionConversion(Koto node, BoundType actual, BoundType expected)
    {
        if (node.BindingFailure == BindingFailure.OriginContract || RestsOnErasure(node, actual, expected) ||
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

    // SPEC 7.7.4, 10.5, 10.7: the signature part of an erasure, which a call's applicability uses; the Shared receiver, the Owned
    // environment and a Closure's Origin conditions are judged after selection, at the argument (SPEC 15.6.1), and a failure there never
    // selects another candidate. A Function Item's Origin conditions belong to its signature, proven from the required contract at `use`
    // (SPEC 15.3.7).
    private bool ErasureSignatureFits(BoundType actual, BoundType expected, Koto use, out ComparisonFailure failure)
    {
        failure = default;
        return expected.Kind == BoundTypeKind.Function &&
            ((actual.Kind == BoundTypeKind.FunctionItem && this.ItemSignatureFits(actual, expected, use, out failure)) ||
            (actual.Kind == BoundTypeKind.Closure && this.ClosureSignature(actual) is { } closureSignature &&
            this.CallableSubtype(closureSignature, expected, SignatureOwner(actual), out failure)));
    }

    // SPEC 7.7.4, 10.7: whether a Function Item's signature fits the required contract. DECISIONS D3 with rule W (BU-79; C-359 C1, C2;
    // temporary, PLAN G886): an Item whose signature formed a Type no binding represents has its binder fixed before any evidence, so
    // the same check is judged in each combination of split cases (RestsOnSplitCases). One that no combination fits fails by itself;
    // one that some combination fits rests on the split: it fits here, and its writes stay, so its reader rests on it too (a
    // candidate trial's verdict, TryCandidate; the conversion's decision, ErasesToFunction).
    private bool ItemSignatureFits(BoundType item, BoundType expected, Koto use, out ComparisonFailure failure)
    {
        failure = default;
        var mark = this.unformedWrites;
        this.CarrySplitPart(this.splitPartStamp); // In a split search: what the enclosing part read before (the required Type) is handed on.
        if (this.FunctionItemSignature(item) is { } signature)
        {
            this.EnterSplitPart(); // The contract's inputs and result are parts of their own, each forming its own Type again.
            return this.ItemContractFits(item, signature, expected, use, out failure);
        }

        return this.UnformedSince(mark) && this.RestsOnSplitCases(
            mark, (item, expected, use), static (binding, check) => !binding.ItemSignatureFits(check.item, check.expected, check.use, out _));
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

        // The owner's (a): every proof of the comparison, its solve's too, assumes the required signature's own well-formedness.
        // DECISIONS D3 (G812 ruling 103 D1): in a split search each input and the result is a part of the check; a part's structural
        // misfit is blamed on the bindings its own Type reads, with those the required signature read before (CarrySplitPart), and
        // any other failure (an Origin, a premise, a clause) on the whole run: the solve joins every part.
        this.CarrySplitPart(this.splitPartStamp);
        var comparison = new CallableInstance(null, null, required, required.Binder, null);
        var inference = this.BeginOriginInference(use, function);
        inference.Comparison = comparison;
        this.contractFailure = default;
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (function.Parameters[i].Type.BoundType is not { } written || Bound(written) is not { } parameter)
            {
                return false;
            }

            // Only the implementation's per-call binders are inferred; the
            // required signature's quantifiers and fixed Origins remain rigid.
            this.MatchInputOrigins(parameter, parameters.Components[i], function, origins, inputs);
            inference.Source = ((byte)(i + 1), null, false);
            this.CollectOriginInference(parameter, parameters.Components[i], inference);
        }

        if (symbol.Type is { } writtenResult && Bound(writtenResult) is { } produced)
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
            this.EnterSplitPart();
            if (Substitute(Bound(function.Parameters[i].Type.BoundType!)!) is not { } parameter)
            {
                return false;
            }

            var walk = new RelationWalk(this, use, OriginPart.Contract);
            if (!walk.Fits(parameters.Components[i], parameter, instance: comparison))
            {
                this.contractFailure = walk.Failure.Within((byte)(i + 1));
                BlameMisfit(function.Parameters[i].Type.BoundType!, walk.Failure);
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
                ? new ComparisonFailure(ComparisonFailureKind.Clause, clause.Longer, clause.Shorter, clause.Equality, comparison) { Clause = clause.Syntax }
                : default;
            return false;
        }

        this.EnterSplitPart();
        if (symbol.Type is not { } output || Bound(output) is not { } boundResult || Substitute(boundResult) is not { } result)
        {
            return false;
        }

        var resultWalk = new RelationWalk(this, use, OriginPart.Contract);
        if (!resultWalk.Fits(result, CompatibleResult(result, required.Components[1]), instance: comparison))
        {
            this.contractFailure = resultWalk.Failure.Within(ComparisonFailure.ResultStep);
            BlameMisfit(output, resultWalk.Failure);
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

        // DECISIONS D3 (G812 rulings 103 D1, 105; temporary, removed with (R)): in a split search, what a failed part of the contract is
        // blamed on. Any failure but a structural one joins every part (an Origin, a premise, a clause: the solve). A structural
        // misfit belongs to the layer that misfits: where the walk's failure names a member inside the part's nested Callable
        // signatures (FailedMember), that member is the failing part, formed again on its own, so the run blames the bindings its
        // layer reads and not those of its siblings; otherwise the whole part, as formed.
        void BlameMisfit(BoundType declared, in ComparisonFailure failure)
        {
            if (failure.Kind != ComparisonFailureKind.Shape)
            {
                this.EnterJointSplitPart();
            }
            else if (this.splitOpen && FailedMember(declared, failure) is { } member)
            {
                this.EnterSplitPart();
                _ = Bound(member) is { } layer ? Substitute(layer) : null;
            }
        }

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

            // SPEC 15.3.4, 15.6.1 (DECISIONS R4-M162): the result is a written Type of the enclosing declaration, so its Types' own
            // clauses are obligations where it is written, under the enclosing premises and this signature's inputs; inputs add none.
            var result = this.BindType(function.ReturnType, scope, new(TypePosition.Result, function));
            if (result is null)
            {
                return null;
            }

            this.AddTypeClauseObligations(result, function.ReturnType);
            return this.types.Intern(BoundTypeKind.Function, null, SemanticsKind.Owner, [parameters, result], binder: function);
        }
        finally
        {
            this.typeScratch.Return(scratch, clearArray: true);
        }
    }
}
