// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Function Types and their conversions (SPEC 7.7.4, 8.10, 10.7, 15.3.7, 15.6.4).
// - Responsibility: a Function Type's syntax (BindFunctionType); the shape of a signature's per-call inputs (FunctionTypeBinder,
//   SignatureOwner, PerCallSignature, InputDependentBinder, PerCallInputs, InputOrigins); the erasure of a Closure or Function Item to
//   a common Function Type (ErasesToFunction, and ErasureSignatureFits, its signature part that applicability reads); and the solved
//   comparison of a named function's contract with a required signature (ContractFits).
// - Inputs: the Types compared, the use whose premises the comparison reads, and the Type relation (Binding.TypeRelations).
// - Outputs: the verdicts and, for ContractFits, the solved Origins and argument operations.
// - Invariants: an erasure's Shared receiver and Owned environment are judged after selection, never during applicability.
// - Prohibitions: the Type relation's walk stays in Binding.TypeRelations.Walk.cs; these judgments call its entries.
public sealed partial class Binding
{
    // The binder of a Function Type's own per-call inputs: its syntax, which quantifies an outer input Origin at that input's slot.
    internal static Koto? FunctionTypeBinder(BoundType signature)
    {
        var inputs = signature.Components[0];
        for (var i = 0; i < inputs.Components.Count; i++)
        {
            if (inputs.Components[i].Origin is { Kind: OriginKind.Input, Occurrence: null, Binder: FunctionTypeKoto binder } origin && origin.Slot == i)
            {
                return binder;
            }
        }

        return null;
    }

    // The declaration whose own per-call inputs the signature of a Function Item or closure names (SPEC 8.10).
    internal static Koto? SignatureOwner(BoundType callee)
    {
        var owner = callee is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } borrowed ? borrowed.Components[0] : callee;
        return owner is { Kind: BoundTypeKind.FunctionItem or BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto declaration } ? declaration : null;
    }

    // SPEC 15.6.4: fresh per-call inputs and a result whose Origins, if any, are all static, so a call substitutes nothing.
    private static bool PerCallSignature(BoundType signature, Koto? own, bool any = false)
        => (!signature.Components[1].CarriesAnyOrigin || StaticOnly(signature.Components[1])) && PerCallInputs(signature, own, any);

    private static bool StaticOnly(BoundType type)
    {
        if (!StaticOrigin(OwnedOrigin(type)))
        {
            return false;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (!StaticOrigin(type.OriginArguments[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (!StaticOnly(type.Components[i]))
            {
                return false;
            }
        }

        return true;

        static bool StaticOrigin(BoundOrigin? origin)
        {
            if (origin?.Kind is null or OriginKind.Static)
            {
                return true;
            }

            if (origin.Kind != OriginKind.Intersection)
            {
                return false;
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (!StaticOrigin(origin.Operands[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    // SPEC 15.6.4: the binder of a signature whose inputs are fresh per-call borrows and whose result depends on those inputs
    // or on static storage alone; a call substitutes the arguments' Origins into that result.
    private static Koto? InputDependentBinder(BoundType signature, Koto? own, bool any = false)
    {
        if (!PerCallInputs(signature, own, any))
        {
            return null;
        }

        var inputs = signature.Components[0];
        Koto? binder = null;
        for (var i = 0; i < inputs.Components.Count; i++)
        {
            if (inputs.Components[i].Origin is { Kind: OriginKind.Input } origin)
            {
                if (binder is not null && !ReferenceEquals(binder, origin.Binder))
                {
                    return null;
                }

                binder = origin.Binder;
            }
        }

        return binder is not null && OverInputs(signature.Components[1], binder, inputs.Components.Count) ? binder : null;

        static bool OverInputs(BoundType part, Koto binder, int count)
        {
            if (!OverInput(part.Origin, binder, count))
            {
                return false;
            }

            for (var i = 0; i < part.OriginArguments.Count; i++)
            {
                if (!OverInput(part.OriginArguments[i], binder, count))
                {
                    return false;
                }
            }

            for (var i = 0; i < part.Components.Count; i++)
            {
                if (!OverInputs(part.Components[i], binder, count))
                {
                    return false;
                }
            }

            return true;
        }

        static bool OverInput(BoundOrigin? origin, Koto binder, int count)
        {
            if (origin is null || origin.Kind == OriginKind.Static)
            {
                return true;
            }

            if (origin.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < origin.Operands.Count; i++)
                {
                    if (!OverInput(origin.Operands[i], binder, count))
                    {
                        return false;
                    }
                }

                return true;
            }

            return origin.Kind == OriginKind.Input && ReferenceEquals(origin.Binder, binder) && origin.Slot < count;
        }
    }

    // SPEC 8.10, 15.6.4: every Origin-bearing input is a direct borrow over its own per-call input, an Origin that the signature's
    // own binder quantifies: a Function Type's own syntax or the declaration `own`. Any other Origin is fixed, even at its own slot.
    // An expected signature (`any`) may take a fixed Origin for a per-call one, which only asks more of the implementation.
    private static bool PerCallInputs(BoundType signature, Koto? own, bool any = false)
    {
        var inputs = signature.Components[0];
        for (var i = 0; i < inputs.Components.Count; i++)
        {
            var input = inputs.Components[i];
            if (input.CarriesAnyOrigin && !(IsBorrow(input.Semantics) && input is { Kind: BoundTypeKind.Semantics, Components.Count: 1, OriginArguments.Count: 0, Origin.Kind: OriginKind.Input } &&
                input.Origin.Slot == i && (any || input.Origin.Binder is FunctionTypeKoto || (own is not null && ReferenceEquals(input.Origin.Binder, own))) &&
                !input.Components[0].CarriesAnyOrigin))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 10.5: the input Origins a slot binding holds. `perCall` collects the parameters of S whose per-call Origin, the outer Origin
    // of a direct input that S quantifies, the binding holds, alone or in a meet; `foreign` is any other input Origin, except one that a
    // Function Type inside the binding quantifies itself (an inner per-call Origin never escapes its Function Type) and an input Origin
    // of a function enclosing the reference, which is fixed in its body (SPEC 15.6.5), so a bound argument may hold it.
    private static void InputOrigins(BoundType type, BoundType parameters, Koto? binder, Koto use, bool nested, ref ulong perCall, ref bool foreign, int depth)
    {
        if (depth > 64)
        {
            foreign = true;
            return;
        }

        Origin(type.Origin, parameters, binder, use, nested, ref perCall, ref foreign, 0);
        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            Origin(type.OriginArguments[i], parameters, binder, use, nested, ref perCall, ref foreign, 0);
        }

        nested |= type.Kind == BoundTypeKind.Function;
        for (var i = 0; i < type.Components.Count; i++)
        {
            InputOrigins(type.Components[i], parameters, binder, use, nested, ref perCall, ref foreign, depth + 1);
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
    private static bool SameExceptOmittedOrigins(BoundType written, BoundType binding, int depth)
    {
        if (ReferenceEquals(written, binding))
        {
            return true;
        }

        if (depth > 32 || !written.SameExceptOrigins(binding) || !Omitted(written.Origin, binding.Origin))
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
            if (!SameExceptOmittedOrigins(written.Components[i], binding.Components[i], depth + 1))
            {
                return false;
            }
        }

        return true;

        static bool Omitted(BoundOrigin? written, BoundOrigin? binding) => ReferenceEquals(written, binding) || written?.Kind == OriginKind.Inference;
    }

    // SPEC 7.7.4: a concrete Closure converts to an expected common Function Type when its signature fits under its conditions
    // (CallableSignatureFits), its minimum receiver is Shared and its complete environment is Owned. Initializations, returns and call
    // arguments share this judgment.
    // A condition that fails is the conversion's one record at `node`, made here from the judgment's failure (RecordsCondition).
    private bool ErasesToFunction(Koto node, BoundType actual, BoundType expected)
    {
        var failure = default(ComparisonFailure);
        var signature = actual.Kind == BoundTypeKind.Closure ? this.ClosureSignature(actual) : this.FunctionItemSignature(actual);
        var fits = actual.Kind == BoundTypeKind.Closure
            ? expected.Kind == BoundTypeKind.Function && BoundClosure.Of(actual) is { Receiver: SemanticsKind.Ref } && signature is not null &&
                this.CallableSignatureFits(signature, expected, SignatureOwner(actual), node, out failure)
            : this.ErasureSignatureFits(actual, expected, node, out failure);
        if (fits && this.ProveOwned(actual, node) == ConstraintProof.Proven)
        {
            return true;
        }

        // A failure publishes whether the signature part fit, for its Note (ClosureConversionNote).
        (this.erasureSignatures ??= new(ReferenceEqualityComparer.Instance))[node] = fits;
        if (fits)
        {
            return false;
        }

        // SPEC 15.2.3: an environment that is not Owned is the conversion's record first (RecordMismatch's Constraint record). A judged
        // erasure whose structure holds fails with its fact: a condition is its record, a limit the located Unsupported_Kd (design-r2 A1),
        // and none a broken contract (design-r2 §2); a Closure whose receiver is not Shared is judged by no comparison.
        if (signature is not null && this.ProveOwned(actual, node) == ConstraintProof.Proven && !this.RecordsCondition(node, failure, signature, expected) &&
            failure.Kind != ComparisonFailureKind.Shape && ReferenceTypes.StorageMatches(expected, signature) &&
            (actual.Kind != BoundTypeKind.Closure || BoundClosure.Of(actual) is { Receiver: SemanticsKind.Ref }))
        {
            this.Fail(node, failure.Kind == ComparisonFailureKind.Limit ? BindingFailure.Unsupported : BindingFailure.Internal);
        }

        return false;
    }

    // SPEC 10.7, 15.3.7: a Function value converts to an expected Function Type of its storage where it meets it, as a Closure or a
    // Function Item erases (ErasesToFunction), also as a Tuple or array literal's element: a contract whose condition fails fails the
    // value with that conversion's own record, by its walk's facts (RecordMismatch), never a mismatch of the literal holding it (held
    // 353a12e81). A Closure's or Function Item's erasure that recorded its failed condition (ErasesToFunction) is the value's failure.
    private bool FailsFunctionConversion(Koto node, BoundType actual, BoundType expected)
    {
        if (node.BindingFailure == BindingFailure.OriginContract)
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
            ((actual.Kind == BoundTypeKind.FunctionItem && this.FunctionItemSignature(actual) is { } signature &&
            this.ItemContractFits(actual, signature, expected, use, out failure)) ||
            (actual.Kind == BoundTypeKind.Closure && this.ClosureSignature(actual) is { } closureSignature &&
            this.CallableSubtype(closureSignature, expected, SignatureOwner(actual), out failure)));
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
        BoundType?[]? arguments,
        Span<BoundArgumentOperation> operations,
        BoundOrigin[] origins,
        BoundOrigin[] inputs,
        BoundLength?[]? lengths = null)
    {
        var parameters = required.Components[0];
        var generic = function.GenericArguments.Count;
        var inputCount = InputOriginCount(function);

        // The owner's (a): every proof of the comparison, its solve's too, assumes the required signature's own well-formedness.
        var comparison = new CallableInstance(null, null, required, FunctionTypeBinder(required), null);
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
        if (!this.SolveCallOriginInference(function, inference, origins, inputs, use, null))
        {
            this.contractFailure = inference.Failure;
            return false;
        }

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var parameter = Substitute(Bound(function.Parameters[i].Type.BoundType!)!);
            var walk = new RelationWalk(this, use, OriginPart.Judged);
            if (this.HasUnsubstitutedOrigin(parameter, function))
            {
                return false;
            }

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

        if (symbol.Type is not { } result || Bound(result) is not { } boundResult || this.HasUnsubstitutedOrigin(result = Substitute(boundResult), function))
        {
            return false;
        }

        var resultWalk = new RelationWalk(this, use, OriginPart.Judged);
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

        BoundType? Bound(BoundType type)
        {
            var bound = item is not null ? this.ItemType(type, item, function)
                : this.MemberType(type, container) is not { } member ? null : arguments is null ? member : this.SubstituteType(member, function, arguments.AsSpan(0, generic), lengths.AsSpan(0, lengths is null ? 0 : generic));
            return bound is null ? null : this.ContractType(bound, this.ConstraintScope(use));
        }

        BoundType Substitute(BoundType type)
            => this.SubstituteStoredOrigins(type, function, origins.AsSpan(0, function.Origins.Count), inputs.AsSpan(0, inputCount));
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

            var result = this.BindType(function.ReturnType, scope, new(TypePosition.Result, function));
            return result is null ? null : this.types.Intern(BoundTypeKind.Function, null, SemanticsKind.Owner, [parameters, result]);
        }
        finally
        {
            this.typeScratch.Return(scratch, clearArray: true);
        }
    }
}
