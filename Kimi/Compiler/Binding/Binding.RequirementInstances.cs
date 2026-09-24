// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Use the verified declaration mapping, never repeat member lookup at a concrete call.
    private BoundCall? InstantiateRequirementCall(BoundCall call, BoundCall outer, BoundCall? destination = null)
    {
        var requirement = (FunctionKoto)call.Target.Declaration;
        var builtin = this.CompilerRequirementTarget(call.Target, call.ConformingType);
        if (builtin.CompilerFunction != CompilerFunctionKind.None)
        {
            var intrinsic = destination ?? new BoundCall();
            intrinsic.Set(builtin, call.ReturnType, call.Receiver, call.ArgumentToParameter, call.TypeArguments, call.ConformingType, call.DeclaringType, call.Origins, call.InputOrigins, call.ArgumentOperations, call.ReceiverOperation, call.BasePath, call.DefaultArguments, call.LengthArguments);
            intrinsic.TupleOperator = call.TupleOperator;
            return intrinsic;
        }

        if (call.ConformingType is not { } self || call.Target.Scope.Owner.BoundSymbol is not { } contract ||
            this.ResolveConformance(self, contract, outer.Target.Declaration, out var path) != ConstraintProof.Proven ||
            path is not { IsVerified: true } || !path.WitnessMap.TryGetValue(call.Target, out var witness) ||
            witness.Function is not { BasePath: null } function ||
            this.StoredType(function.DeclaringType, self) is not { } declaring)
        {
            return null;
        }

        var origins = this.originScratch.Rent(function.Origins.Length);
        var inputs = this.originScratch.Rent(function.InputOrigins.Length);
        try
        {
            Translate(function.Origins, origins);
            Translate(function.InputOrigins, inputs);
            var resolved = destination ?? new BoundCall();
            resolved.Set(witness.Implementation, call.ReturnType, call.Receiver, call.ArgumentToParameter, call.TypeArguments, declaringType: declaring, origins: origins.AsSpan(0, function.Origins.Length), inputOrigins: inputs.AsSpan(0, function.InputOrigins.Length), operations: call.ArgumentOperations, receiverOperation: call.ReceiverOperation, lengthArguments: call.LengthArguments, defaults: call.DefaultArguments);
            return resolved;
        }
        finally
        {
            this.originScratch.Return(inputs, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }

        void Translate(ReadOnlySpan<BoundOrigin> source, Span<BoundOrigin> result)
        {
            for (var i = 0; i < source.Length; i++)
            {
                result[i] = this.SubstituteStoredOrigin(source[i], requirement, call.Origins, call.InputOrigins);
            }
        }
    }
}
