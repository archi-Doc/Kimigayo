// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 8.4.10.4: what a requirement runs on a Self that is not abstract, as a call's instantiation resolves it under a definition's
    // premises, each target with its declaring instance: a built-in comparison's plan leaves, another built-in target itself (no
    // instance), else the conformance's witness, an inherited one included; false where none resolves. The requirement takes `ref/Self`
    // (SPEC 4.7.5's rows and the comparisons), which no virtual function implements (virtual-dispatch 1, 6.3).
    internal bool Witnesses(BindingSymbol requirement, BoundType self, BindingSymbol? definition, List<(BindingSymbol Target, BoundType? Declaring)> into)
    {
        var target = this.CompilerRequirementTarget(requirement, self);
        var contract = requirement.Scope.Owner.BoundSymbol!;
        if (target.CompilerFunction is CompilerFunctionKind.BuiltinEquals or CompilerFunctionKind.BuiltinCompare)
        {
            return this.ComparisonPlan(target, self, contract.LibraryDeclaration!.Value, operators: false) is { } plan && Leaves(plan, into);
        }

        if (target.CompilerFunction != CompilerFunctionKind.None)
        {
            into.Add((target, null));
            return true;
        }

        if (this.ConformanceWitness(requirement, self, contract, this.EffectPremises(definition), out var witness) is not { } declaring)
        {
            return false;
        }

        into.Add((witness.Implementation, declaring));
        return true;

        static bool Leaves(BoundComparison plan, List<(BindingSymbol Target, BoundType? Declaring)> into)
        {
            if (plan.Implementation is { } call)
            {
                into.Add((call.Target, call.DeclaringType));
            }

            foreach (var part in plan.Parts)
            {
                _ = Leaves(part, into);
            }

            return true;
        }
    }

    // Use the verified declaration mapping, never repeat member lookup at a concrete call.
    private CallPlan? InstantiateRequirementCall(CallPlan call, CallPlan outer, CallPlan? destination = null)
    {
        var requirement = (FunctionKoto)call.Target.Declaration;
        if (call.ConformingType is { } open && open.HasAbstractPart)
        {
            // SPEC 8.10: substituted into a generic caller, such as a default's replica, a requirement call on a still abstract Type
            // stays that Type's requirement call, as in the caller's own body; only a closed Type selects its witness. The
            // intermediate call is reused per inner call, so the result is copied into its own storage.
            var forwarded = destination ?? new CallPlan();
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
            var intrinsic = destination ?? new CallPlan();
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
            this.ConformanceWitness(call.Target, self, contract, this.ConstraintScope(outer.Target.Declaration), out var witness) is not { } declaring)
        {
            return null;
        }

        var function = witness.Function!;
        var origins = this.originScratch.Rent(function.Origins.Length);
        var inputs = this.originScratch.Rent(function.InputOrigins.Length);
        try
        {
            Translate(function.Origins, origins);
            Translate(function.InputOrigins, inputs);
            var resolved = destination ?? new CallPlan();
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

    // SPEC 8.4.10.4: the witness a verified conformance of a Self to a Contract maps a requirement to, and the instance of the Type
    // that declares it (a base's for an inherited witness); null where none resolves.
    private BoundType? ConformanceWitness(BindingSymbol requirement, BoundType self, BindingSymbol contract, BindingScope scope, out BoundWitness witness)
    {
        witness = default;
        return this.ResolveConformance(self, contract, scope, out var path) == ConstraintProof.Proven && path is { IsVerified: true } &&
            path.WitnessMap.TryGetValue(new(requirement, contract), out witness) && witness.Function is { ObjectCompatibility: ConstraintProof.Proven } function
            ? this.StoredType(function.DeclaringType, self) : null;
    }

    // The requirement already acquired and verified its receiver. Its Proven witness lends only the base prefix;
    // retain that acquisition's Origin and source, and change the physical receiver Type for the selected implementation.
    private bool ProjectWitnessCall(CallPlan call, BoundMemberPath? path, BoundType declaring)
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

        var projected = this.types.Reference(SemanticsKind.Ref, declaring, receiver.Origin);
        call.SetReceiverProjection(operation with { ParameterType = projected, AdaptedType = projected, Kind = ArgumentOperationKind.BaseBorrow, BasePath = path, ObjectCompatibility = ConstraintProof.Proven }, path);
        return true;
    }

    // SPEC 8.4.9: the Contract reference the call selected, as the instance's Type conforms to it: a Contract without Type
    // arguments is its own reference, and a bound reference has its Type arguments instantiated (`Indexable<Name>`, or
    // `C<E>` read as `C<i32>`). Null when the call recorded none or the instance has no such conformance.
    private BindingSymbol? InstanceReference(CallPlan call, BoundType self, CallPlan outer)
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
