// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BoundConformancePath Path, BoundAccessor Requirement), (BoundAccessor Bridge, PropertyWitnessKind Kind)> propertyBridges = new();

    // Standard operations are verified ordinary function bodies. No member lookup is repeated in an instance.
    private FunctionKoto? PropertyWitnessFunction(BoundConformancePath path, BoundPropertyWitness witness)
    {
        if (witness.Kind == PropertyWitnessKind.AccessorCall)
        {
            return this.AccessorFunction(witness.Implementation);
        }

        // SPEC 11.4.2: an inherited Field is reached from the requirement receiver over the conforming Type by standard projection,
        // as a direct access is (AdtDef.FindField), so a bridge for a base path has the same body as an own one. An edit that
        // replaces the requirement, the implementing Property or the operation rebuilds the body; otherwise its Types are refreshed.
        var key = (path, witness.Requirement);
        if (!this.propertyBridges.TryGetValue(key, out var cached) || !ReferenceEquals(cached.Bridge.Declaration, witness.Requirement.Declaration) ||
            !ReferenceEquals(cached.Bridge.Property, witness.Implementation.Property) || cached.Kind != witness.Kind)
        {
            BoundAccessor bridge;
            bridge = new(witness.Implementation.Property, witness.Requirement.Kind)
            {
                Declaration = witness.Requirement.Declaration,
                Receiver = witness.ReceiverType,
                Input = witness.InputType,
                Result = witness.ResultType,
            };
            // SPEC 11.4.2: a bridge is checked and executed under the premises of its conformance path, such as `when T is Copy`;
            // its scope is that path's, which ConstraintScope gives the bridge's body, and its container the conforming declaration.
            var scope = path.Scope;
            bridge.SelfSymbol = new("self", BindingSymbolKind.Parameter, bridge.Binder, scope) { Slot = 0, Type = bridge.Receiver };
            if (bridge.Input is not null)
            {
                bridge.ValueSymbol = new("value", BindingSymbolKind.Parameter, bridge.Binder, scope) { Slot = 1, Type = bridge.Input };
            }

            var function = this.AccessorFunction(bridge);
            function.BoundSymbol!.Scope = scope;
            function.Parent = path.Type.Declaration;
            var self = new IdentifierNameKoto(bridge.Binder, "self") { BoundSymbol = bridge.SelfSymbol, BoundType = bridge.Receiver, BindingState = BindingState.Resolved };
            var name = new IdentifierNameKoto(bridge.Binder, witness.Implementation.Property.Symbol.Name) { BoundSymbol = witness.Implementation.Property.Symbol, BindingState = BindingState.Resolved };
            var member = new MemberAccessKoto(bridge.Binder, self, name)
            {
                IsDirectStorage = true,
                BoundSymbol = witness.Implementation.Property.Symbol,
                BoundType = this.StoredType(witness.Implementation.Property.Type!, witness.ImplementationType),
                BindingState = BindingState.Resolved,
            };
            self.Parent = member;
            name.Parent = member;
            Koto body = member;
            if (witness.Kind == PropertyWitnessKind.StorageBorrow)
            {
                var target = new IdentifierNameKoto(bridge.Binder, "ref") { BoundType = bridge.Result, BindingState = BindingState.Resolved };
                body = new ConversionKoto(bridge.Binder, member, target) { ConversionBinding = ConversionBinding.Borrow, BoundType = bridge.Result, BindingState = BindingState.Resolved };
                member.Parent = body;
                target.Parent = body;
            }
            else if (witness.Kind == PropertyWitnessKind.StorageSet)
            {
                var value = new IdentifierNameKoto(bridge.Binder, "value") { BoundSymbol = bridge.ValueSymbol, BoundType = bridge.Input, BindingState = BindingState.Resolved };
                var move = new IdentifierNameKoto(bridge.Binder, "move") { BoundType = bridge.Input, BindingState = BindingState.Resolved };
                var transfer = new ConversionKoto(bridge.Binder, value, move) { ConversionBinding = ConversionBinding.Transfer, BoundType = bridge.Input, BindingState = BindingState.Resolved };
                value.Parent = move.Parent = transfer;
                body = new BinaryKoto(bridge.Binder, KotoKind.Equals, member, transfer) { BoundType = BoundType.Unit, BindingState = BindingState.Resolved };
                member.Parent = transfer.Parent = body;
            }

            function.SetPropertyWitnessBody(body, bridge.Result);
            this.propertyBridges[key] = (bridge, witness.Kind);
            return bridge.ExecutionFunction;
        }
        else
        {
            var bridge = cached.Bridge;
            bridge.Receiver = witness.ReceiverType;
            bridge.Result = witness.ResultType;
            bridge.Input = witness.InputType;
            bridge.SelfSymbol!.Type = bridge.Receiver;
            var function = bridge.ExecutionFunction!;
            var body = function.ExpressionBody!;
            var member = (MemberAccessKoto)(body is ConversionKoto or BinaryKoto { Akind: KotoKind.Equals } ? ((BinaryKoto)body).Left : body);
            member.Left.BoundType = bridge.Receiver;
            member.BoundType = this.StoredType(witness.Implementation.Property.Type!, witness.ImplementationType);
            if (body is ConversionKoto borrow)
            {
                borrow.BoundType = borrow.Right.BoundType = bridge.Result;
            }
            else if (body is BinaryKoto { Akind: KotoKind.Equals } assignment)
            {
                bridge.ValueSymbol!.Type = bridge.Input;
                var transfer = (ConversionKoto)assignment.Right;
                transfer.BoundType = transfer.Left.BoundType = transfer.Right.BoundType = bridge.Input;
            }

            function.RefreshAccessor();
            function.BoundSymbol!.Type = bridge.Result;
            function.SetPropertyWitnessBody(body, bridge.Result);
            return function;
        }
    }

    private CallPlan? InstantiatePropertyRequirementCall(CallPlan call, CallPlan outer, CallPlan? destination)
    {
        var requirement = ((FunctionKoto)call.Target.Declaration).Accessor!;
        if (call.ConformingType is not { } self || this.InstanceReference(call, self, outer) is not { } contract ||
            this.ResolveConformance(self, contract, outer.Target.Declaration, out var path) != ConstraintProof.Proven ||
            path is not { IsVerified: true } || !path.PropertyWitnessMap.TryGetValue((new(requirement.Property.Symbol, contract), requirement.Kind), out var witness) ||
            witness.ObjectCompatibility != ConstraintProof.Proven || this.PropertyWitnessFunction(path, witness) is not { } function ||
            this.StoredType(witness.ImplementationType, self) is not { } declaring)
        {
            return null;
        }

        // A standard bridge is a member of the conforming Type itself: its receiver is never projected to a base subobject.
        var storage = witness.Kind != PropertyWitnessKind.AccessorCall;

        var count = witness.Kind == PropertyWitnessKind.AccessorCall ? witness.InputOrigins.Count : call.InputOrigins.Length;
        var inputs = this.originScratch.Rent(count);
        var originCount = witness.Kind == PropertyWitnessKind.AccessorCall ? witness.Origins.Count : call.Origins.Length;
        var origins = this.originScratch.Rent(originCount);
        try
        {
            for (var i = 0; i < count; i++)
            {
                inputs[i] = witness.Kind != PropertyWitnessKind.AccessorCall ? call.InputOrigins[i] : witness.InputOrigins[i] is { } origin
                    ? this.SubstituteStoredOrigin(origin, requirement.Binder, call.Origins, call.InputOrigins) : null!;
            }

            for (var i = 0; i < originCount; i++)
            {
                origins[i] = witness.Kind != PropertyWitnessKind.AccessorCall ? call.Origins[i] : witness.Origins[i] is { } origin ? this.SubstituteStoredOrigin(origin, requirement.Binder, call.Origins, call.InputOrigins) : null!;
            }

            var result = destination ?? new CallPlan();
            result.Set(function.BoundSymbol!, call.ReturnType, call.Receiver, call.ArgumentToParameter, [], declaringType: storage ? self : declaring, origins: origins.AsSpan(0, originCount), inputOrigins: inputs.AsSpan(0, count), operations: call.ArgumentOperations, receiverOperation: call.ReceiverOperation);
            return storage || this.ProjectWitnessCall(result, witness.BasePath, declaring) ? result : null;
        }
        finally
        {
            this.originScratch.Return(inputs, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
    }
}
