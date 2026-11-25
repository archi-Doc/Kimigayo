// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private ConstraintProof CompareCallableContracts(FnSig requirement, FnSig implementation, BindingScope scope, QuerySite site, BoundType self, BoundType? declaringType, BoundType?[] arguments, BoundOrigin[] origins, BoundOrigin[] inputs, BoundMemberPath? path = null)
    {
        if (requirement.InputCount != implementation.InputCount)
        {
            return ConstraintProof.Refuted;
        }

        var held = this.heldPairs; // The held sink's mark (RestsOnHeld): a judgment of the comparison that read a held pair is Unknown.
        var inference = this.BeginOriginInference(this.RentInference(requirement.Binder, implementation.Binder));
        var demanded = this.RentTypes(requirement.InputCount);
        var written = this.RentTypes(requirement.InputCount);
        var count = 0;
        var session = this.OpenPremiseSession(); // PLAN G1082: the comparison's proof requests share its premise environment.
        try
        {
            for (var i = 0; i < requirement.InputCount; i++)
            {
                var required = requirement.Input(i);
                var actual = implementation.Input(i);
                if (required is null || actual is null)
                {
                    if (required != actual)
                    {
                        return ConstraintProof.Refuted;
                    }

                    continue;
                }

                required = this.ContractType(required, scope, self);
                if (i == requirement.ReceiverIndex && path is not null)
                {
                    required = this.ProjectRequirementReceiver(required, self, declaringType)!;
                    if (required is null)
                    {
                        return ConstraintProof.Refuted;
                    }
                }

                // SPEC 8.6.3, 8.4.2: the slots correspond before the requirement's premises in `scope` normalize either side.
                var corresponded = Corresponding(actual);
                actual = this.ProjectCallableType(actual, declaringType);
                if (actual is null || corresponded is null ||
                    !SignatureEquals(required, this.ContractType(corresponded, scope), requirement.Binder, implementation.Binder))
                {
                    return ConstraintProof.Refuted;
                }

                written[count] = actual;
                demanded[count++] = required;
            }

            var expected = requirement.Result is { } requiredOutput ? this.ContractType(requiredOutput, scope, self) : null;

            // SPEC 15.3.7, 15.6.1, the owner's (a): a whole-contract comparison; each of its proofs, its solve's too, assumes the own
            // well-formedness of the requirement as substituted (Self, the Type's arguments), as 10.7's does (ContractFits).
            var comparison = this.RequirementComparison(demanded.AsSpan(0, count), expected, requirement.Binder, scope);
            inference.Comparison = comparison;

            // The comparison is formed before any of its judgments, the bounds its solve collects included (PLAN G801).
            for (var i = 0; i < count; i++)
            {
                this.MatchInputOrigins(written[i], demanded[i], implementation.Binder, origins, inputs);
                this.CollectOriginInference(written[i], demanded[i], inference, site);
            }

            if (implementation.Result is { } produced && expected is not null && this.ProjectCallableType(produced, declaringType) is { } projected)
            {
                this.CollectOriginInference(projected, expected, inference, site, result: true);
            }

            // SPEC 15.3.7: only the implementation's call Origins are solved, also under its result premises.
            if (!(implementation.Binder is FunctionKoto
                ? this.SolveCallOriginInference(inference, origins, inputs, [], declaringType)
                : this.SolveOriginInference(inference, origins, inputs, [], declaringType)))
            {
                return this.RestsOnHeld(held, ConstraintProof.Refuted); // SPEC 15.3.6: no candidate or a conflict may rest on a skipped held pair.
            }

            for (int i = 0, j = 0; i < requirement.InputCount; i++)
            {
                if (requirement.Input(i) is null)
                {
                    continue;
                }

                var required = demanded[j++];
                var actual = Translate(implementation.Input(i)!);
                if (actual is null || !this.IsSubtypeAt(this.ClassForm(required, scope, out _), this.ClassForm(actual, scope, out _), requirement.Binder, comparison) ||
                    !this.ProvesInputPremises(actual, required, requirement.Binder, comparison))
                {
                    return ConstraintProof.Refuted;
                }
            }

            if (!this.CheckCallOriginRelations(implementation.Binder, origins, inputs, requirement.Binder, declaringType, comparison))
            {
                return this.RestsOnHeld(held, ConstraintProof.Refuted); // It reads the solution above.
            }

            var result = implementation.Result is { } output ? Translate(output) : null;
            if (result is not null && implementation.Binder is FunctionKoto function &&
                !this.ProvesResultPremises(function, result, requirement.Binder, comparison))
            {
                return ConstraintProof.Refuted; // SPEC 15.3.7: the implementation's result premises follow from the requirement's.
            }

            if (result is null || expected is null)
            {
                return ConstraintProof.Unknown;
            }

            // R7-C3 (C-f4; design §3, J3; SPEC 8.2.3, 8.6.3): each relation of the whole-contract comparison reads its pair in the comparison's
            // one view, the requirement's premises at this conformance (`scope`), each side in its form there; the slots' correspondence
            // above and the Origin inference read the Types as given.
            var (formedResult, formedExpected) = (this.ClassForm(result, scope, out _), this.ClassForm(expected, scope, out _));
            var fits = this.IsSubtypeAt(formedResult, CompatibleResult(formedResult, formedExpected), requirement.Binder, comparison);
            return fits ? this.RestsOnHeld(held, ConstraintProof.Proven) : ConstraintProof.Refuted;
        }
        finally
        {
            session?.Close();
            this.typeScratch.Return(written, clearArray: true);
            this.typeScratch.Return(demanded, clearArray: true);
            this.ReturnInference(inference);
        }

        // An implementation Type with the requirement's slots for its own and its declaring Type's arguments projected.
        BoundType? Corresponding(BoundType type)
            => this.SubstituteType(type, implementation.Binder, arguments.AsSpan(0, requirement.GenericCount)) is { } substituted ? this.ProjectCallableType(substituted, declaringType) : null;

        BoundType? Translate(BoundType type)
        {
            if (Corresponding(type) is not { } substituted)
            {
                return null;
            }

            substituted = this.SubstituteStoredOrigins(substituted, implementation.Binder, origins.AsSpan(0, implementation.OriginCount), inputs.AsSpan(0, InputOriginCount(implementation.Binder)), requirement.Binder);
            return this.ContractType(substituted, scope, self);
        }
    }

    // SPEC 8.8.2, 15.3.7: the whole-contract comparison of one requirement as substituted, its required inputs `inputs` and result
    // `result`, judged in the conformance scope `scope` of D, P and the requirement's premises, which gives its relations their
    // premises and its pair binders their admitted sets (Binding.RelationScope; PLAN G801). The one producer of a comparison's scope:
    // a callable implementation's comparison (CompareCallableContracts) and a stored Field's bridge (MatchPropertyOperation) form theirs here.
    // It has no row sink (CallableInstance.Rows): a contract match is no reader of region conjuncts, so its premises alone decide (B-3).
    // Its scope gives its relations the held arm (heldPairs), which its readers read.
    private CallableInstance RequirementComparison(ReadOnlySpan<BoundType> inputs, BoundType? result, Koto binder, BindingScope scope)
    {
        var demanded = inputs.Length == 0 ? BoundType.Unit : this.types.Tuple(inputs);
        var signature = this.types.Intern(BoundTypeKind.Function, null, SemanticsKind.Owner, [demanded, result ?? BoundType.Unit]);
        return new(null, null, signature, binder, null) { Scope = scope };
    }

    private BoundType? ProjectCallableType(BoundType type, BoundType? declaringType)
        => declaringType is null ? type : this.StoredType(type, declaringType);
}
