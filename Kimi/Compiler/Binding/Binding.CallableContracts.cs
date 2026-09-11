// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private ConstraintProof CompareCallableContracts(CallableContract requirement, CallableContract implementation, BindingScope scope, BoundType self, BoundType? declaringType, BoundType?[] arguments, BoundOrigin[] origins, BoundOrigin[] inputs)
    {
        if (requirement.InputCount != implementation.InputCount)
        {
            return ConstraintProof.Refuted;
        }

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
            actual = this.ProjectCallableType(actual, declaringType);
            if (actual is null || !SignatureEquals(required, this.ContractType(actual, scope), requirement.Binder, implementation.Binder))
            {
                return ConstraintProof.Refuted;
            }

            this.MatchInputOrigins(actual, required, implementation.Binder, origins, inputs);
        }

        for (var i = 0; i < requirement.InputCount; i++)
        {
            if (requirement.Input(i) is not { } input)
            {
                continue;
            }

            var required = this.ContractType(input, scope, self);
            var actual = Translate(implementation.Input(i)!);
            if (actual is null || !FitsType(required, actual))
            {
                return ConstraintProof.Refuted;
            }
        }

        var result = implementation.Result is { } output ? Translate(output) : null;
        var expected = requirement.Result is { } requiredOutput ? this.ContractType(requiredOutput, scope, self) : null;
        return result is null || expected is null ? ConstraintProof.Unknown : FitsType(result, expected) ? ConstraintProof.Proven : ConstraintProof.Refuted;

        BoundType? Translate(BoundType type)
        {
            var substituted = this.SubstituteType(type, implementation.Binder, arguments.AsSpan(0, requirement.GenericCount));
            substituted = substituted is null ? null : this.ProjectCallableType(substituted, declaringType);
            if (substituted is null)
            {
                return null;
            }

            substituted = this.SubstituteStoredOrigins(substituted, implementation.Binder, origins.AsSpan(0, implementation.OriginCount), inputs.AsSpan(0, implementation.InputCount), requirement.Binder);
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

        internal int OriginCount => this.function?.Origins.Count ?? 0;

        internal int InputCount => this.function?.Parameters.Count ?? 2;

        internal BoundType? Result => this.function is { } f ? f.BoundSymbol?.Type : this.accessor!.Result;

        internal BoundType? Input(int index) => this.function is { } f ? f.Parameters[index].Type.BoundType : index == 0 ? this.accessor!.Receiver : this.accessor!.Input;
    }
}
