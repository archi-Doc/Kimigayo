// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly PropertyCallRows propertyCalls;
    private readonly StorageProjectionRows storageProjections;
    private readonly PropertyUpdateStorageRows propertyUpdateStorage;
    private readonly PropertyUpdateInputRows propertyUpdateInputs;

    internal static bool IsGetterResult(Koto node)
        => KotoHelper.UnwrapParentheses(node) is not MemberAccessKoto { IsDirectStorage: true } &&
            KotoHelper.UnwrapParentheses(node).BoundSymbol?.Property is { Getter.IsStandard: false };

    // SPEC 11.3: the function a custom accessor executes. A library accessor's is made once, by the library steps
    // (MakeLibraryAccessorFunctions) or an earlier use after its header, and read from then on without a write (census §8 item 1): what
    // it writes is the library's syntax and its header's interned Types, the same in every pass. A program accessor's is refreshed at each use.
    internal FunctionKoto AccessorFunction(BoundAccessor accessor)
    {
        if (accessor.ExecutionFunction is { } made && ReferenceEquals(made.CodeContext.Kotonoha, this.Library.Kotonoha))
        {
            return made;
        }

        var function = accessor.ExecutionFunction ??= new(accessor);
        function.RefreshAccessor();
        function.BoundSymbol ??= new(function.Name, BindingSymbolKind.Function, function, accessor.Property.Symbol.Scope);
        function.BoundSymbol.Type = accessor.Result;
        function.BoundSymbol.ReceiverIndex = accessor.Receiver is null ? -1 : 0;
        function.IsRequirement = accessor.Property.Declaration.IsContractRequirement;
        function.BindingState = BindingState.Resolved;
        return function;
    }

    internal InvocationKoto? PropertyCall(Koto node, PropertyAccessorKind kind)
    {
        node = KotoHelper.UnwrapParentheses(node);
        if (node.BindingState != BindingState.Resolved || node.BoundSymbol?.Property is not { } property ||
            (IsSpecialField(node, out var special) && special.IsConstructor))
        {
            return null;
        }

        return !(kind == PropertyAccessorKind.Get ? property.Getter : property.Setter).IsStandard &&
            this.propertyCalls.GetValueOrDefault((node, kind)) is { BindingState: BindingState.Resolved } call ? call : null;
    }

    internal MemberAccessKoto? StorageProjection(Koto node)
        => node.BoundSymbol?.Kind == BindingSymbolKind.Storage && this.storageProjections.GetValueOrDefault(node) is { BindingState: BindingState.Resolved } projection ? projection : null;

    // PLAN G949 S1-e: every library accessor with a body gets its executing function, numbered, by the last declaration step, after the
    // headers gave its Types: the set the MIR builder executes (MirBuilder.Module), so no later phase numbers a library node (c17).
    private void MakeLibraryAccessorFunctions()
    {
        var library = this.Library.Kotonoha;
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is PropertyAccessorKoto { Body: not null } syntax && ReferenceEquals(syntax.CodeContext?.Kotonoha, library) &&
                syntax.BoundSymbol?.Kind == BindingSymbolKind.PropertyAccessor)
            {
                this.AccessorFunction(Accessor(syntax));
            }
        }
    }

    private EvaluatedKoto PropertyUpdateInput(Koto target, Koto update)
    {
        if (!this.propertyUpdateInputs.TryGetValue(target, out var input))
        {
            input = new(update);
            this.propertyUpdateInputs.Add(target, input);
        }

        return input;
    }

    private bool ValidPropertyWritePath(Koto node, BindingScope scope)
    {
        node = KotoHelper.UnwrapParentheses(node);
        while (node is BinaryKoto projection && (projection is MemberAccessKoto || ElementAccess.IsSyntax(this, projection)))
        {
            node = KotoHelper.UnwrapParentheses(projection.Left);
            if (node.BoundType?.Semantics != SemanticsKind.Owner)
            {
                break; // A reference reaches a separate referent with its own authority.
            }

            if (node.BoundSymbol?.Property is { } parent &&
                (!parent.Getter.IsStandard || !parent.Setter.IsStandard ||
                    !this.Accessible(parent.Symbol, scope, parent.Setter.Access, (node as MemberAccessKoto)?.Left.BoundType)))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 13.7.1, 13.7.2 (DECISIONS BU-50): an update with a custom accessor completes its two sides once, as a read and a write
    // complete theirs: the read Type (the node's) and the destination Type (its computed input's, PropertyUpdateDestination), to which
    // the update's operation fits its result. A standard `get` reads and a standard `set` writes the Field's Place as the body reads it
    // (MemberRead), which also types the storage node; a custom accessor's Type is its call's. A side without a Type leaves the update
    // unresolved, as the read is. The storage is reached through the receiver the target names: a bare static Property names none, so
    // its update is the located Unsupported_Kd until P42 binds a static Property's update (the qualified form is the later phases' limit).
    private BoundType? BindPropertyUpdate(Koto target, BindingScope scope)
    {
        var property = target.BoundSymbol!.Property!;
        var stored = this.MemberRead(target, property.Symbol, scope);
        var result = property.Getter.IsStandard ? stored : this.AccessorUseType(target, property.Getter);
        var destination = property.Setter.IsStandard ? stored : this.AccessorUseType(target, property.Setter);
        if (result is null || destination is null)
        {
            return Complete(target, null);
        }

        if (target is not MemberAccessKoto node)
        {
            return this.Fail(target, BindingFailure.Unsupported);
        }

        Complete(this.propertyUpdateInputs[node], destination);
        var owner = node.Left.BoundType!;
        var referent = owner is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq } ? owner.Components[0] : owner;
        var type = this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Uniq, [referent], origin: this.PlaceOrigin(node.Left));
        if (!this.AdaptInput(node.Left, FixedPattern.Open(type), owner, scope, null, null, out var adapted, out var quality, out var kind, receiver: true))
        {
            this.FailWrite(node, node);
            return null;
        }

        this.receiverOperations[node] = new(node.Left, owner, adapted, kind, quality, ParameterIndex: 0);
        if (!this.propertyUpdateStorage.TryGetValue(node, out var storage))
        {
            // The stored field is reached through the receiver the update locates once; it takes that borrow's Type.
            var receiver = new EvaluatedKoto(node.Left);
            storage = new(node, receiver, node.Right) { IsDirectStorage = true };
            receiver.Parent = storage;
            this.propertyUpdateStorage.Add(node, storage);
        }

        Complete(storage.Left, adapted);
        storage.BoundSymbol = property.Symbol;
        Complete(storage, stored);
        return Complete(node, result);
    }

    // SPEC 13.7.1: the destination Type of an update with a custom accessor (BindPropertyUpdate), to which its operation fits the result.
    private BoundType? PropertyUpdateDestination(Koto target)
        => this.propertyUpdateInputs.GetValueOrDefault(KotoHelper.UnwrapParentheses(target)) is { BindingState: BindingState.Resolved } input
            ? input.BoundType : null;

    // SPEC 13.7.1: the Type a custom accessor's bound call gives its use at `node`: a `set`'s value input (the destination Type), a
    // `get`'s result (the read Type).
    private BoundType? AccessorUseType(Koto node, BoundAccessor accessor)
        => accessor.Kind != PropertyAccessorKind.Set ? this.propertyCalls[(node, accessor.Kind)].BoundType
            : this.propertyCalls.GetValueOrDefault((node, accessor.Kind)) is { } setter && this.CallOf(setter) is { ArgumentOperations.Length: > 0 } call
                ? call.ArgumentOperations[0].ParameterType : null;

    private void BindStorageProjection(Koto node, BoundAccessor accessor)
    {
        if (accessor.SelfSymbol is not { } self)
        {
            return;
        }

        if (!this.storageProjections.TryGetValue(node, out var projection))
        {
            var receiver = new IdentifierNameKoto(node, "self");
            projection = new(node, receiver, accessor.Property.Declaration.NameKoto) { IsDirectStorage = true };
            this.storageProjections.Add(node, projection);
        }

        projection.Left.BoundSymbol = self;
        Complete(projection.Left, self.Type);
        projection.BoundSymbol = accessor.Property.Symbol;
        Complete(projection, accessor.Property.Type);
    }

    private bool BindPropertyCall(Koto node, BoundAccessor accessor, BindingScope scope, Koto? input)
    {
        // Accessors use the same complete declaring Type as methods, including nominal Types with only Origin slots.
        var selection = node is MemberAccessKoto selected && this.memberSelections.TryGetValue(selected, out var memberSelection) ? memberSelection : default;
        var declaringType = selection.DeclaringType;

        var receiver = accessor.Receiver is null ? null : (node as MemberAccessKoto)?.Left;
        var requirement = accessor.Property.Declaration.IsContractRequirement && node is MemberAccessKoto requirementUse &&
            this.requirementGroups.TryGetValue(requirementUse, out var requirementGroup) && requirementGroup.Properties.Count == 1 ? requirementGroup : null;
        var previousContract = this.activeRequirementContract;
        if (requirement is not null)
        {
            this.activeRequirementContract = requirement.PropertyContracts[0];
        }

        try
        {
            return BindCall();
        }
        finally
        {
            this.activeRequirementContract = previousContract;
        }

        bool BindCall()
        {
            // SPEC 11.3, 11.4, 23.1.2: a use of an accessor whose `get` result or stored `set` input is not its Property's Type (the
            // declaration's TypeMismatch_Kd) takes the Type those rules fix, so what reads the use is judged against it; once validation
            // has failed the declaration, the use rests on it (Check, CallPlan.Called).
            var mistyped = this.AccessorTypeMismatch(accessor);
            var inputType = accessor.Input is { } declaredInput ? Signature(mistyped ? accessor.Property.Type! : declaredInput) : null;
            // Origin-bearing inputs retain their own execution milestone. An owning or object-form written receiver
            // is the declaration error of SPEC 11.3, not a limit of this path.
            var receiverUnsupported = requirement is null && accessor.Receiver is { } receiverType && (!ReferenceTypes.IsStruct(receiverType) || receiverType.Components[0].Kind is not (BoundTypeKind.Nominal or BoundTypeKind.Constructed) ||
                (receiverType.Components[0].Kind == BoundTypeKind.Constructed && declaringType is null));
            if ((requirement is null && accessor.Declaration?.Body is null) || accessor.Result is not { } declaredResult ||
                Signature(mistyped && accessor.Kind == PropertyAccessorKind.Get ? accessor.Property.Type! : declaredResult) is not { } result ||
                (accessor.Input is not null && inputType is null) || inputType is { CarriesAnyOrigin: true } ||
                receiverUnsupported)
            {
                if (!(receiverUnsupported && receiver?.BoundType is { } shapedActual && this.ReceiverRestsOnAccessorShape(node, accessor, receiver, shapedActual, declaringType, null, scope, false)))
                {
                    this.Fail(node, BindingFailure.Unsupported, true);
                }

                return false;
            }

            BoundArgumentOperation receiverOperation = default;
            if (accessor.Receiver is { } declaredReceiver && Signature(declaredReceiver) is { } required)
            {
                if (receiver?.BoundType is not { } actual)
                {
                    this.FailWrite(node, node);
                    return false;
                }

                if (!this.AdaptInput(receiver, FixedPattern.Open(required), actual, scope, selection.Path, declaringType, out var adapted, out var quality, out var kind, receiver: true))
                {
                    if (!this.ReceiverRestsOnAccessorShape(node, accessor, receiver, actual, declaringType, null, scope, false))
                    {
                        this.FailWrite(node, node);
                    }

                    return false;
                }

                var proof = selection.Path is null ? ConstraintProof.Proven : ProjectedReceiverProof(accessor.Property.Symbol, accessor);
                if (proof != ConstraintProof.Proven)
                {
                    this.Fail(node, proof == ConstraintProof.Unknown ? BindingFailure.Unsupported : BindingFailure.UnprovenConstraint, proof == ConstraintProof.Unknown);
                    return false;
                }

                if (this.ObjectCases(actual, scope, out _) != SemanticsMask.None && required.Semantics != SemanticsKind.Ref)
                {
                    this.Fail(node, BindingFailure.Unsupported, true);
                    return false;
                }

                receiverOperation = new(receiver, actual, adapted, kind, quality, selection.Path, 0, proof);
            }

            // SPEC 13.7.1: the input the update at `node` computed (its propertyUpdateInputs row) stands for an operand the expansion
            // already evaluated, the call's Evaluated argument; a write's input is its source operand.
            var evaluated = input is not null && ReferenceEquals(input, this.propertyUpdateInputs.GetValueOrDefault(node));
            if (!this.propertyCalls.TryGetValue((node, accessor.Kind), out var call) ||
                call.ArgumentNodes.Count != (input is null ? 0 : 1) + (receiver is null ? 0 : 1) ||
                (input is not null && !ReferenceEquals(call.ArgumentNodes[0], input)) ||
                (receiver is not null && !ReferenceEquals(call.ArgumentNodes[^1], receiver)))
            {
                var method = new IdentifierNameKoto(node, accessor.Property.Symbol.Name + "." + accessor.Kind);
                Koto[] arguments = input is null ? receiver is null ? [] : [receiver] : receiver is null ? [input] : [input, receiver];
                call = new(node, method, arguments) { CallStorage = new() { Evaluated = evaluated ? 0 : -1 } };
                this.propertyCalls[(node, accessor.Kind)] = call;
            }

            var function = this.AccessorFunction(accessor);
            call.Method.BoundSymbol = function.BoundSymbol;
            Complete(call.Method, null);
            var operations = this.argumentOperationScratch.Rent(call.ArgumentNodes.Count + 1);
            operations[call.ArgumentNodes.Count] = default; // Common call judgment's separate receiver slot.
            Span<int> mapping = stackalloc int[2];
            var count = 0;
            if (input is not null)
            {
                if (evaluated)
                {
                    Complete(input, inputType); // The update supplies its checked Output, never the original RHS.
                }

                var slot = receiver is null ? 0 : 1;
                mapping[count] = slot;
                operations[count++] = new(input, inputType, inputType, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, ParameterIndex: slot);
            }

            if (receiver is not null)
            {
                mapping[count] = 0;
                operations[count++] = receiverOperation;
            }

            var originCount = accessor.Declaration!.Origins.Count;
            var inputCount = InputOriginCount(accessor.Binder);
            var origins = this.originScratch.Rent(originCount);
            var inputs = this.originScratch.Rent(inputCount);
            var rows = this.rowScratch.Rent(count);
            Array.Clear(origins);
            Array.Clear(inputs);
            try
            {
                if (receiver is not null && accessor.Receiver is { } receiverPattern && receiverOperation.ParameterType is { } acquired)
                {
                    var receiverContract = Signature(receiverPattern)!;
                    this.MatchInputOrigins(receiverContract, acquired, accessor.Binder, origins, inputs);
                    operations[count - 1] = receiverOperation with
                    {
                        ParameterType = this.SubstituteStoredOrigins(receiverContract, accessor.Binder, origins.AsSpan(0, originCount), inputs.AsSpan(0, inputCount)),
                        AdaptedType = acquired,
                    };
                }

                if (this.LacksImage(result, accessor.Binder, origins.AsSpan(0, originCount), inputs.AsSpan(0, inputCount)))
                {
                    this.Fail(node, BindingFailure.Unsupported, true);
                    return false;
                }

                result = this.SubstituteStoredOrigins(result, accessor.Binder, origins.AsSpan(0, originCount), inputs.AsSpan(0, inputCount));

                call.CallStorage!.Set(this, function.BoundSymbol!, result, null, mapping[..count], [], conformingType: requirement?.Self, declaringType: declaringType, origins: origins.AsSpan(0, originCount), inputOrigins: inputs.AsSpan(0, inputCount), operations: operations.AsSpan(0, count));
                call.CallStorage.RequirementContract = requirement?.PropertyContracts[0];
                for (var i = 0; i < count; i++)
                {
                    // The update's value and the receiver are typed parts: Evidence rows at their operations' parameter Types.
                    rows[i] = new(i, -1, 0, operations[i].Source!, operations[i].ParameterType, operations[i].ParameterType, Contribution.Evidence);
                }

                var solution = new SlotSolution
                {
                    Binding = this, Call = call, Binder = function, Origins = origins, Inputs = inputs, DeclaringType = declaringType, Rows = rows.AsSpan(0, count),
                };
                this.JudgeSelectedCall(call, solution, operations.AsSpan(0, count + 1), []);
                this.RequireResultPremises(function, result, call); // SPEC 15.6.1, 15.6.4 step 3: the getter's result premises.
                this.AddSynthesizedCall(node, call); // SPEC 11.3, 15.6.4 step 3: its Types' formation, judged as every call's, at the use (Check).
                Complete(call, result);
                return true;
            }
            finally
            {
                this.rowScratch.Return(rows, clearArray: true);
                this.originScratch.Return(inputs, clearArray: true);
                this.originScratch.Return(origins, clearArray: true);
                this.argumentOperationScratch.Return(operations, clearArray: true);
            }

            BoundType? Signature(BoundType type) => this.MemberType(type, declaringType) is { } member ? this.ContractType(member, scope, requirement?.Self) : null;
        }
    }

    /// <summary>The `propertyCalls` family's one accessor (DECISIONS BU-220): a kept family, a property access's synthesized accessor calls
    /// (SPEC 13.7), its composite key (the source node, its accessor kind) kept in the source's module's NodeColumns at its SyntaxId as a
    /// row of two calls indexed directly by its accessor kind. Each call is made once and reset in place by each pass
    /// (ResetSyntheticCalls). Its members keep the names its readers use.</summary>
    private readonly struct PropertyCallRows(Binding binding)
    {
        internal FamilyRows<InvocationKoto?[]?> Values => new(binding, static (table, _) => table.Hir.Families.PropertyCalls.Enumerate());

        internal InvocationKoto this[(Koto Source, PropertyAccessorKind Kind) key]
        {
            get => this.GetValueOrDefault(key) ?? throw new KeyNotFoundException($"No `propertyCalls` row at `{key.Source}`.");
            set => (HirTables.Of(key.Source).Families.PropertyCalls.Ref(key.Source.SyntaxId) ??= new InvocationKoto?[2])[(int)key.Kind] = value;
        }

        internal InvocationKoto? GetValueOrDefault((Koto Source, PropertyAccessorKind Kind) key)
            => key.Source.SyntaxOwner is { } table && table.Hir.Families.PropertyCalls.Get(key.Source.SyntaxId) is { } row ? row[(int)key.Kind] : null;

        internal bool TryGetValue((Koto Source, PropertyAccessorKind Kind) key, [NotNullWhen(true)] out InvocationKoto? value)
            => (value = this.GetValueOrDefault(key)) is not null;
    }

    /// <summary>The `storageProjections` family's one accessor (DECISIONS BU-220): a kept family, an accessor body's synthesized `self`
    /// storage projection (SPEC 13.7.2), made once and reset in place by each pass (ResetSyntheticCalls), kept in its key node's module's
    /// NodeColumns at its SyntaxId. Its members keep the names its readers use.</summary>
    private readonly struct StorageProjectionRows(Binding binding)
    {
        internal FamilyRows<MemberAccessKoto?> Values => new(binding, static (table, _) => table.Hir.Families.StorageProjections.Enumerate());

        internal void Add(Koto node, MemberAccessKoto value)
        {
            ref var row = ref HirTables.Of(node).Families.StorageProjections.Ref(node.SyntaxId);
            row = row is null ? value : throw new ArgumentException($"`storageProjections` already holds a row at `{node}`.", nameof(node));
        }

        internal MemberAccessKoto? GetValueOrDefault(Koto node)
            => node.SyntaxOwner is { } table ? table.Hir.Families.StorageProjections.Get(node.SyntaxId) : null;

        internal bool TryGetValue(Koto node, [NotNullWhen(true)] out MemberAccessKoto? value)
            => (value = this.GetValueOrDefault(node)) is not null;
    }

    /// <summary>The `propertyUpdateStorage` family's one accessor (DECISIONS BU-220): a kept family, a property update's synthesized
    /// storage access (SPEC 13.7.1), made once and reset in place by each pass (ResetSyntheticCalls), kept in its key node's module's
    /// NodeColumns at its SyntaxId. Its members keep the names its readers use.</summary>
    private readonly struct PropertyUpdateStorageRows(Binding binding)
    {
        internal FamilyRows<MemberAccessKoto?> Values => new(binding, static (table, _) => table.Hir.Families.PropertyUpdateStorage.Enumerate());

        internal void Add(Koto node, MemberAccessKoto value)
        {
            ref var row = ref HirTables.Of(node).Families.PropertyUpdateStorage.Ref(node.SyntaxId);
            row = row is null ? value : throw new ArgumentException($"`propertyUpdateStorage` already holds a row at `{node}`.", nameof(node));
        }

        internal bool TryGetValue(Koto node, [NotNullWhen(true)] out MemberAccessKoto? value)
            => (value = node.SyntaxOwner is { } table ? table.Hir.Families.PropertyUpdateStorage.Get(node.SyntaxId) : null) is not null;
    }

    /// <summary>The `propertyUpdateInputs` family's one accessor (DECISIONS BU-220): a kept family, a property update's synthesized input
    /// (SPEC 13.7.1), made once and reset in place by each pass (ResetSyntheticCalls), kept in its key node's module's NodeColumns at its
    /// SyntaxId. Its members keep the names its readers use.</summary>
    private readonly struct PropertyUpdateInputRows(Binding binding)
    {
        internal FamilyRows<EvaluatedKoto?> Values => new(binding, static (table, _) => table.Hir.Families.PropertyUpdateInputs.Enumerate());

        internal EvaluatedKoto this[Koto node] => this.GetValueOrDefault(node) ?? throw new KeyNotFoundException($"No `propertyUpdateInputs` row at `{node}`.");

        internal void Add(Koto node, EvaluatedKoto value)
        {
            ref var row = ref HirTables.Of(node).Families.PropertyUpdateInputs.Ref(node.SyntaxId);
            row = row is null ? value : throw new ArgumentException($"`propertyUpdateInputs` already holds a row at `{node}`.", nameof(node));
        }

        internal EvaluatedKoto? GetValueOrDefault(Koto node)
            => node.SyntaxOwner is { } table ? table.Hir.Families.PropertyUpdateInputs.Get(node.SyntaxId) : null;

        internal bool TryGetValue(Koto node, [NotNullWhen(true)] out EvaluatedKoto? value)
            => (value = this.GetValueOrDefault(node)) is not null;
    }
}
