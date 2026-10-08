// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Use the verified declaration mapping, never repeat member lookup at a concrete call.
    private BoundCall? InstantiateRequirementCall(BoundCall call, BoundCall outer, BoundCall? destination = null)
    {
        var requirement = (FunctionKoto)call.Target.Declaration;
        if (call.ConformingType is { } open && AbstractTypes.HasAbstractPart(open))
        {
            // SPEC 8.10: substituted into a generic caller, such as a default's replica, a requirement call on a still abstract Type
            // stays that Type's requirement call, as in the caller's own body; only a closed Type selects its witness. The
            // intermediate call is reused per inner call, so the result is copied into its own storage.
            var forwarded = destination ?? new BoundCall();
            forwarded.Set(call.Target, call.ReturnType, call.Receiver, call.ArgumentToParameter, call.TypeArguments, call.ConformingType, call.DeclaringType, call.Origins, call.InputOrigins, call.ArgumentOperations, call.ReceiverOperation, call.BasePath, call.DefaultArguments, call.LengthArguments);
            forwarded.TupleOperator = call.TupleOperator;
            forwarded.RequirementContract = call.RequirementContract;
            return forwarded;
        }

        if (requirement.Accessor is not null)
        {
            return this.InstantiatePropertyRequirementCall(call, outer, destination);
        }

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

        if (call.ConformingType is { } integer && call.RequirementContract is { } positionContract && IsIntegerPosition(integer, positionContract))
        {
            return this.InstantiateIntegerPosition(call, integer, destination); // SPEC 4.6.2.
        }

        if (call.ConformingType is not { } self || this.InstanceReference(call, self, outer) is not { } contract ||
            this.ResolveConformance(self, contract, outer.Target.Declaration, out var path) != ConstraintProof.Proven ||
            path is not { IsVerified: true } || !path.WitnessMap.TryGetValue(new(call.Target, contract), out var witness) ||
            witness.Function is not { ObjectCompatibility: ConstraintProof.Proven } function ||
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
            return this.ProjectWitnessCall(resolved, function.BasePath, declaring) ? resolved : null;
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

    // The requirement already acquired and verified its receiver. Its Proven witness lends only the base prefix;
    // retain that acquisition's Origin and source, and change the physical receiver Type for the selected implementation.
    private bool ProjectWitnessCall(BoundCall call, BoundMemberPath? path, BoundType declaring)
    {
        if (path is null || call.Target.ReceiverIndex < 0)
        {
            return true;
        }

        var operation = call.ReceiverOperation;
        if (call.Receiver is null)
        {
            foreach (var argument in call.ArgumentOperations)
            {
                if (argument.ParameterIndex == call.Target.ReceiverIndex)
                {
                    operation = argument;
                    break;
                }
            }
        }

        if (operation.ParameterType is not { Semantics: SemanticsKind.Ref } receiver)
        {
            return false; // Exclusive receiver preservation remains OCC-X.
        }

        var projected = this.Reference(SemanticsKind.Ref, declaring, receiver.Origin);
        call.SetReceiverProjection(operation with { ParameterType = projected, AdaptedType = projected, Kind = ArgumentOperationKind.BaseBorrow, BasePath = path, ObjectCompatibility = ConstraintProof.Proven }, path);
        return true;
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
