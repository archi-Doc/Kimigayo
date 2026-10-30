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

        // SPEC 11.9.2: an inherited Field is reached from the requirement receiver over the conforming Type by standard projection,
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
            // SPEC 11.9.2: a bridge is checked and executed under the premises of its conformance path, such as `when T is Copy`;
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
                BoundType = this.PartTypeIn(witness.Implementation.Property.Type!, witness.ImplementationType, path.Scope),
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
            member.BoundType = this.PartTypeIn(witness.Implementation.Property.Type!, witness.ImplementationType, path.Scope);
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

    // SPEC 11.9.2: a Property requirement call at the operation ResolveRequirementWitness verified: the accessor a call witness executes,
    // or the standard bridge.
    private CallPlan? InstantiatePropertyRequirementCall(CallPlan call, BoundType self, RequirementWitness resolved, CallPlan? destination)
    {
        var requirement = ((FunctionKoto)call.Target.Declaration).Accessor!;
        var witness = resolved.Property;
        if (this.PropertyWitnessFunction(resolved.Path!, witness) is not { } function)
        {
            return null;
        }

        // A standard bridge is a member of the conforming Type itself: its receiver is never projected to a base subobject.
        var storage = witness.Kind != PropertyWitnessKind.AccessorCall;
        var declaring = resolved.Declaring!;

        var count = storage ? call.InputOrigins.Length : witness.InputOrigins.Count;
        var inputs = this.originScratch.Rent(count);
        var originCount = storage ? call.Origins.Length : witness.Origins.Count;
        var origins = this.originScratch.Rent(originCount);
        try
        {
            for (var i = 0; i < count; i++)
            {
                inputs[i] = storage ? call.InputOrigins[i] : this.WitnessOrigin(witness.InputOrigins[i], requirement.Binder, call.Origins, call.InputOrigins);
            }

            for (var i = 0; i < originCount; i++)
            {
                origins[i] = storage ? call.Origins[i] : this.WitnessOrigin(witness.Origins[i], requirement.Binder, call.Origins, call.InputOrigins);
            }

            var result = destination ?? new CallPlan();
            result.Set(function.BoundSymbol!, call.ReturnType, call.Receiver, call.ArgumentToParameter, [], declaringType: storage ? self : declaring, origins: origins.AsSpan(0, originCount), inputOrigins: inputs.AsSpan(0, count), operations: call.ArgumentOperations, receiverOperation: call.ReceiverOperation);
            if (!storage)
            {
                this.ProjectWitnessCall(result, witness.BasePath, declaring);
            }

            return result;
        }
        finally
        {
            this.originScratch.Return(origins, clearArray: true);
            this.originScratch.Return(inputs, clearArray: true);
        }
    }
}
