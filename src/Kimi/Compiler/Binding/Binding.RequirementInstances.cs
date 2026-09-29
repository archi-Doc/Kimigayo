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

        if (call.ConformingType is { } fixedSelf && call.RequirementContract is { } fixedContract && IsFixedArrayEntry(fixedSelf, fixedContract))
        {
            return this.InstantiateFixedArrayEntry(call, fixedSelf, fixedContract, destination); // PLAN G32.
        }

        if (call.ConformingType is not { } self || this.InstanceReference(call, self, outer) is not { } contract ||
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
                // An owning input has no outer Origin slot; keep that absence in its concrete witness.
                result[i] = source[i] is { } origin ? this.SubstituteStoredOrigin(origin, requirement, call.Origins, call.InputOrigins) : null!;
            }
        }
    }

    // SPEC 8.4.9: the Contract reference the call selected, as the instance's Type conforms to it: a Contract without Type
    // arguments is its own reference, and a bound reference has its Type arguments instantiated (`Indexable<Name>`, or
    // `C<E>` read as `C<i32>`). Null when the call recorded none or the instance has no such conformance.
    private BindingSymbol? InstanceReference(BoundCall call, BoundType self, BoundCall outer)
    {
        if (call.RequirementContract is not { } recorded)
        {
            return null;
        }

        if (!IsBoundContractReference(recorded))
        {
            return recorded;
        }

        if (this.InstantiateStorageType(recorded.Type!, outer) is not { } concrete || self.Symbol is not { } owner || !this.conformancesByType.TryGetValue(owner, out var identities))
        {
            return null;
        }

        for (var i = 0; i < identities.Count; i++)
        {
            // A conformance of a generic Type names its reference in the Type's own parameters (`Self is Indexable<K>`).
            if (identities[i].Contract.Type is { } formal && (ReferenceEquals(formal, concrete) || ReferenceEquals(this.StoredType(formal, self), concrete)))
            {
                return identities[i].Contract;
            }
        }

        return null;
    }
}
