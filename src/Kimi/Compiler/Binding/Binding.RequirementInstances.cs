// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 8.11.4: what a requirement runs on a Self that is not abstract, as the witness core resolves it under a definition's
    // premises, each target with its declaring instance: a built-in comparison's plan leaves, another built-in target itself (no
    // instance), else the conformance's witness, an inherited one included; false where none resolves. The requirement takes `ref/Self`
    // (SPEC 4.9.6's rows and the comparisons), which no virtual function implements (virtual-dispatch 1, 6.3).
    internal bool Witnesses(BindingSymbol requirement, BoundType self, BindingSymbol? definition, List<(BindingSymbol Target, BoundType? Declaring)> into)
    {
        var contract = requirement.Scope.Owner.BoundSymbol!;
        var witness = this.ResolveRequirementWitness(requirement, self, contract, this.EffectPremises(definition));
        if (witness is { Kind: RequirementWitnessKind.Builtin, Target: { } target } &&
            target.CompilerFunction is CompilerFunctionKind.BuiltinEquals or CompilerFunctionKind.BuiltinCompare)
        {
            return this.ComparisonPlan(target, self, contract.LibraryDeclaration!.Value, operators: false) is { } plan && Leaves(plan, into);
        }

        if (witness.Kind is not (RequirementWitnessKind.Builtin or RequirementWitnessKind.Conformance))
        {
            return false;
        }

        into.Add((witness.Target!, witness.Declaring));
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
        if (call.ConformingType is { } open && open.HasAbstractPart)
        {
            // SPEC 8.15: substituted into a generic caller, such as a default's replica, a requirement call on a still abstract Type
            // stays that Type's requirement call, as in the caller's own body; only a closed Type selects its witness. The
            // intermediate call is reused per inner call, so the result is copied into its own storage.
            var forwarded = Copy(call.Target);
            forwarded.RequirementContract = call.RequirementContract;
            return forwarded;
        }

        if (call.ConformingType is not { } self)
        {
            return null;
        }

        var contract = this.InstanceReference(call, self, outer);
        var witness = this.ResolveRequirementWitness(call.Target, self, contract, this.ConstraintScope(outer.Target.Declaration));
        return witness.Kind switch
        {
            RequirementWitnessKind.Builtin => Copy(witness.Target!),
            RequirementWitnessKind.Conformance => this.InstantiateConformanceCall(call, witness, destination),
            RequirementWitnessKind.Property => this.InstantiatePropertyRequirementCall(call, self, witness, destination),
            RequirementWitnessKind.FixedArrayEntry => this.InstantiateFixedArrayEntry(call, self, witness.Target!, destination),
            RequirementWitnessKind.IntegerPosition => this.InstantiateIntegerPosition(call, self, witness.Target!, destination),
            _ => null, // None, or Refused: exclusive receiver preservation remains OCC-X.
        };

        CallPlan Copy(BindingSymbol target)
        {
            var copy = destination ?? new CallPlan();
            copy.Set(target, call.ReturnType, call.Receiver, call.ArgumentToParameter, call.TypeArguments, call.ConformingType, call.DeclaringType, call.Origins, call.InputOrigins, call.ArgumentOperations, call.ReceiverOperation, call.BasePath, call.DefaultArguments, call.LengthArguments);
            copy.TupleOperator = call.TupleOperator;
            return copy;
        }
    }

    // SPEC 21.3.1: a conformance witness's call at the requirement call's arguments, its Origins in the call's terms.
    private CallPlan InstantiateConformanceCall(CallPlan call, RequirementWitness witness, CallPlan? destination)
    {
        var requirement = (FunctionKoto)call.Target.Declaration;
        var function = witness.Function!;
        var origins = this.originScratch.Rent(function.Origins.Length);
        var inputs = this.originScratch.Rent(function.InputOrigins.Length);
        try
        {
            Translate(function.Origins, origins);
            Translate(function.InputOrigins, inputs);
            var resolved = destination ?? new CallPlan();
            resolved.Set(witness.Target!, call.ReturnType, call.Receiver, call.ArgumentToParameter, call.TypeArguments, declaringType: witness.Declaring, origins: origins.AsSpan(0, function.Origins.Length), inputOrigins: inputs.AsSpan(0, function.InputOrigins.Length), operations: call.ArgumentOperations, receiverOperation: call.ReceiverOperation, lengthArguments: call.LengthArguments, defaults: call.DefaultArguments);
            this.ProjectWitnessCall(resolved, function.BasePath, witness.Declaring!);
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

    // The requirement already acquired and verified its receiver, `ref` for a base-path witness (ResolveRequirementWitness). Its Proven
    // witness lends only the base prefix; retain that acquisition's Origin and source, and change the physical receiver Type for the
    // selected implementation.
    private void ProjectWitnessCall(CallPlan call, BoundMemberPath? path, BoundType declaring)
    {
        if (path is null || call.Target.ReceiverIndex < 0)
        {
            return;
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

        var projected = this.types.Reference(SemanticsKind.Ref, declaring, operation.ParameterType!.Origin);
        call.SetReceiverProjection(operation with { ParameterType = projected, AdaptedType = projected, Kind = ArgumentOperationKind.BaseBorrow, BasePath = path, ObjectCompatibility = ConstraintProof.Proven }, path);
    }

    // SPEC 8.6.1: the Contract reference the call selected, as the instance's Type conforms to it: a Contract without Type
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
