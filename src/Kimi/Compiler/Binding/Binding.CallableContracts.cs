// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private ConstraintProof CompareCallableContracts(CallableContract requirement, CallableContract implementation, BindingScope scope, BoundType self, BoundType? declaringType, BoundType?[] arguments, BoundOrigin[] origins, BoundOrigin[] inputs, BoundMemberPath? path = null)
    {
        if (requirement.InputCount != implementation.InputCount)
        {
            return ConstraintProof.Refuted;
        }

        var inference = this.BeginOriginInference(requirement.Binder, implementation.Binder);
        var demanded = this.RentTypes(requirement.InputCount);
        var count = 0;
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

                demanded[count++] = required;
                this.MatchInputOrigins(actual, required, implementation.Binder, origins, inputs);
                this.CollectOriginInference(actual, required, inference);
            }

            var expected = requirement.Result is { } requiredOutput ? this.ContractType(requiredOutput, scope, self) : null;
            if (implementation.Result is { } produced && expected is not null && this.ProjectCallableType(produced, declaringType) is { } projected)
            {
                this.CollectOriginInference(projected, expected, inference, result: true);
            }

            // SPEC 15.3.7, 15.6.1, the owner's (a): a whole-contract comparison; each of its proofs, its solve's too, assumes the own
            // well-formedness of the requirement as substituted (Self, the Type's arguments), as 10.7's does (ContractFits).
            var demandedInputs = count == 0 ? BoundType.Unit : this.types.Tuple(demanded.AsSpan(0, count));
            var signature = this.types.Intern(BoundTypeKind.Function, null, SemanticsKind.Owner, [demandedInputs, expected ?? BoundType.Unit]);
            var comparison = new CallableInstance(null, null, signature, requirement.Binder, null);
            inference.Comparison = comparison;

            // SPEC 15.3.7: only the implementation's call Origins are solved, also under its result premises.
            if (!(implementation.Binder is FunctionKoto solved
                ? this.SolveCallOriginInference(solved, inference, origins, inputs, requirement.Binder, declaringType)
                : this.SolveOriginInference(inference, origins, inputs, requirement.Binder, declaringType)))
            {
                return ConstraintProof.Refuted;
            }

            for (int i = 0, j = 0; i < requirement.InputCount; i++)
            {
                if (requirement.Input(i) is null)
                {
                    continue;
                }

                var required = demanded[j++];
                var actual = Translate(implementation.Input(i)!);
                if (actual is null || !this.IsSubtypeAt(required, actual, requirement.Binder, comparison) ||
                    !this.ProvesInputPremises(actual, required, requirement.Binder, comparison))
                {
                    return ConstraintProof.Refuted;
                }
            }

            if (!this.CheckCallOriginRelations(implementation.Binder, origins, inputs, requirement.Binder, declaringType, comparison))
            {
                return ConstraintProof.Refuted;
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

            var fits = this.IsSubtypeAt(result, CompatibleResult(result, expected), requirement.Binder, comparison);
            return fits ? ConstraintProof.Proven : ConstraintProof.Refuted;
        }
        finally
        {
            this.typeScratch.Return(demanded, clearArray: true);
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

    private BoundType? ProjectCallableType(BoundType type, BoundType? declaringType)
        => declaringType is null ? type : this.StoredType(type, declaringType);

    // A view over existing declarations: no temporary FunctionKoto or copied parameter lists.
    private readonly struct CallableContract
    {
        private readonly FunctionKoto? function;
        private readonly BoundAccessor? accessor;

        internal CallableContract(FunctionKoto function) => this.function = function;

        internal CallableContract(BoundAccessor accessor) => this.accessor = accessor;

        internal Koto Binder => (Koto?)this.function ?? this.accessor!.Binder;

        internal int GenericCount => this.function?.GenericArguments.Count ?? 0;

        internal int OriginCount => this.function?.Origins.Count ?? this.accessor?.Declaration?.Origins.Count ?? 0;

        internal int InputCount => this.function?.Parameters.Count ?? 2;

        internal int ReceiverIndex => this.function is { } f ? f.BoundSymbol!.ReceiverIndex : this.accessor!.Receiver is null ? -1 : 0;

        internal BoundType? Result => this.function is { } f ? f.BoundSymbol?.Type : this.accessor!.Result;

        internal BoundType? Input(int index) => this.function is { } f ? f.Parameters[index].Type.BoundType : index == 0 ? this.accessor!.Receiver : this.accessor!.Input;
    }
}
