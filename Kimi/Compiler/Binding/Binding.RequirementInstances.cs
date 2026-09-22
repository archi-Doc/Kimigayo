// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Use the verified declaration mapping, never repeat member lookup at a concrete call.
    private BoundCall? InstantiateRequirementCall(BoundCall call, BoundCall outer)
    {
        var requirement = (FunctionKoto)call.Target.Declaration;
        if (call.ConformingType is not { } self || call.Target.Scope.Owner.BoundSymbol is not { } contract ||
            this.ResolveConformance(self, contract, outer.Target.Declaration, out var path) != ConstraintProof.Proven ||
            path is not { IsVerified: true } || !path.WitnessMap.TryGetValue(call.Target, out var witness) ||
            witness.Function is not { BasePath: null } function ||
            this.StoredType(function.DeclaringType, self) is not { } declaring)
        {
            return null;
        }

        var origins = Translate(function.Origins);
        var inputs = Translate(function.InputOrigins);
        var resolved = new BoundCall();
        resolved.Set(witness.Implementation, call.ReturnType, call.Receiver, call.ArgumentToParameter, call.TypeArguments, declaringType: declaring, origins: origins, inputOrigins: inputs, operations: call.ArgumentOperations, receiverOperation: call.ReceiverOperation, lengthArguments: call.LengthArguments, defaults: call.DefaultArguments);
        return resolved;

        BoundOrigin[] Translate(ReadOnlySpan<BoundOrigin> source)
        {
            var result = new BoundOrigin[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                result[i] = this.SubstituteStoredOrigin(source[i], requirement, call.Origins, call.InputOrigins);
            }

            return result;
        }
    }
}
