// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Name binding (SPEC 9; SPEC 10.5 for explicit Type arguments): a written name resolved to its symbol, and its value use bound.
// - Boundary: Names resolves a written name (BindName; BindExplicitReferenceName for `name<T...>`, whose selection it hands to
//   Binding.FunctionReferences.cs) and binds its use (BindReference). Binding.FunctionReferences.cs selects a function group under a
//   required Function Type (ExplicitReferenceAgrees there); Binding.MemberLookup.cs finds a Type's member.
public sealed partial class Binding
{
    // SPEC 10.5: a function Name with explicit Type arguments in a value position is a reference whose candidates take
    // those arguments. It is resolved here and selected as a value, or against the fixed expected call signature.
    private BoundType? BindExplicitReferenceName(GenericsKoto reference, BindingScope scope)
    {
        var name = reference.Identifier!;
        this.BindNodeCore(name, scope, null);
        if (name.BindingState != BindingState.Resolved || name.BoundSymbol is not { Kind: BindingSymbolKind.Function } group)
        {
            // SPEC 7.7, 10.5: a value has no Type parameters, so explicit Type arguments select nothing, as at a value call.
            this.BindUnknownChildren(reference, scope);
            var resolved = name.BindingState == BindingState.Resolved;
            return this.Fail(reference, resolved ? BindingFailure.NoApplicableCandidate : BindingFailure.Unsupported, !resolved);
        }

        for (var i = 0; i < reference.TypeArguments.Count; i++)
        {
            var syntax = reference.TypeArguments[i];
            if (this.IsLengthArgument(syntax, scope))
            {
                if (this.BindLength(syntax, scope) is null)
                {
                    return this.CompleteDependent(reference, syntax);
                }

                continue;
            }

            if (this.BindType(syntax, scope) is null)
            {
                return this.CompleteDependent(reference, syntax);
            }
        }

        reference.BoundSymbol = group;
        reference.BindingState = BindingState.Resolved;
        return null;
    }

    private BoundType? BindName(IdentifierNameKoto node, BindingScope scope)
    {
        var symbol = this.Lookup(node.IdentifierName, scope, node, false);
        if (symbol is null)
        {
            return this.Fail(node, BindingFailure.MissingName, true);
        }

        return this.BindReference(node, symbol, scope);
    }

    private BoundType? BindReference(Koto node, BindingSymbol symbol, BindingScope scope)
    {
        node.BoundSymbol = symbol;
        if (Receiver(node) is { } receiver && ((symbol.Declaration is FunctionKoto { BaseInitializer: { } initializer } && IsWithin(node, initializer)) ||
            (!receiver.Whole && (node.Parent is not MemberAccessKoto access || !ReferenceEquals(access.Left, node)))))
        {
            return this.Fail(node, BindingFailure.InvalidAssignment);
        }

        if (symbol.ConditionalDeclaration is not null && symbol.Kind != BindingSymbolKind.Function)
        {
            var conditionalType = node is MemberAccessKoto conditionalMember && this.memberSelections.TryGetValue(conditionalMember, out var memberSelection) ? memberSelection.DeclaringType : null;
            var proof = this.ProveMemberConditions(symbol, conditionalType, scope);
            if (proof != ConstraintProof.Proven)
            {
                this.RequireConstraint(node, proof, this.capabilityMode);
                return null;
            }
        }

        // SPEC 7.7.2: a static Property's storage there is a static access, never a capture (StaticStorageAccess).
        if (symbol.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage or BindingSymbolKind.PatternCandidate or BindingSymbolKind.Capture && scope.Function != symbol.Scope.Function &&
            !StaticStorageAccess(symbol, scope))
        {
            if (this.CaptureOuterUse(node, symbol, scope) is not { } capture)
            {
                return this.Fail(node, BindingFailure.Capture);
            }

            symbol = capture;
            node.BoundSymbol = symbol;
        }

        if (symbol.Kind == BindingSymbolKind.Function)
        {
            this.BindHeader(symbol);
            if (symbol.Next is null && symbol.Declaration is FunctionKoto { IsAnonymous: false } named && (named.Modifier & ModifierKind.Unsafe) != 0 && IsValueUse(node))
            {
                // SPEC 7: an unsafe function supports direct calls only. An overload group is checked after selection.
                return this.Fail(node, BindingFailure.UnsafeFunctionValue);
            }

            // A function group is resolved for call selection, but not an inferred first-class value.
            node.BindingState = BindingState.Resolved;
            return null;
        }

        if (symbol.Type is null && symbol.Declaration is VariableKoto variable)
        {
            if (variable is PropertyKoto)
            {
                // Property and storage Types belong to the header. A use never re-enters accessor bodies.
                var declaration = variable.BoundSymbol!;
                if (!declaration.HeaderBound && declaration.Resolving)
                {
                    this.Fail(variable, BindingFailure.Cycle, true);
                }
                else
                {
                    this.BindHeader(declaration);
                }

                if (symbol.Type is null)
                {
                    this.Consulted(variable.TypeKoto ?? variable.InitializerKoto ?? variable);
                }
            }
            else
            {
                this.BindVariable(variable, symbol.Scope);
            }
        }

        if (symbol.Kind == BindingSymbolKind.Storage && symbol.Scope.Owner is PropertyAccessorKoto storageAccessor)
        {
            this.BindStorageProjection(node, Accessor(storageAccessor));
        }

        if (symbol.Property is { } property)
        {
            var target = Grouped(node);
            var assignment = target.Parent is BinaryKoto binary && ReferenceEquals(binary.Left, target) && binary.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals;
            var write = assignment && target.Parent!.Akind == KotoKind.Equals;
            var update = (assignment && !write) || target.Parent?.Akind is KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement;
            if (IsSpecialField(node, out var specialFunction) && specialFunction.IsConstructor)
            {
                if (!ReferenceEquals(symbol.Scope.Owner, specialFunction.BoundSymbol!.Scope.Owner) || !property.IsStored ||
                    (!write && (!property.Getter.IsStandard || (update && !property.Setter.IsStandard))))
                {
                    // SPEC 6.3.4, 11.7: construction forbids inherited Field and computed access and custom accessor calls on self.
                    return this.Fail(node, BindingFailure.InvalidAssignment);
                }

                return Complete(node, this.MemberRead(node, symbol, scope));
            }

            var operation = write ? property.Setter : property.Getter;
            var sourceReceiver = (node as MemberAccessKoto)?.Left;
            if ((write || update) && sourceReceiver?.BoundType is { Semantics: SemanticsKind.Ref or SemanticsKind.ObjRef or SemanticsKind.Rc or SemanticsKind.Arc })
            {
                return this.FailWrite(node, node);
            }

            if (!operation.IsPresent || !this.Accessible(symbol, scope, operation.Access, sourceReceiver?.BoundType))
            {
                return this.Fail(node, BindingFailure.Access);
            }

            if (update && (!property.Setter.IsPresent || !this.Accessible(symbol, scope, property.Setter.Access, sourceReceiver?.BoundType)))
            {
                return this.Fail(node, BindingFailure.Access);
            }

            if (!operation.IsStandard || (update && !property.Setter.IsStandard))
            {
                if (node is MemberAccessKoto projected && sourceReceiver?.BoundType is { } sourceType && this.memberSelections.TryGetValue(projected, out var pathSelection) &&
                    (pathSelection.Path is not null || IsObjectSemantics(sourceType.Semantics)) && operation.Receiver is { } declaredReceiver && this.MemberType(declaredReceiver, pathSelection.DeclaringType) is { } required)
                {
                    // SPEC 7.3, 13.7: an accessor receiver is a Receiver Expression and is acquired implicitly.
                    if (!this.AdaptInput(sourceReceiver, required, sourceType, scope, pathSelection.Path, pathSelection.DeclaringType, out var projectedReceiver, out var quality, out var kind, explicitBorrow: update, receiver: true))
                    {
                        // SPEC 11.3: a receiver that only the accessor's wrongly shaped written receiver rejects rests on that declaration.
                        if (this.ReceiverRestsOnAccessorShape(node, operation, sourceReceiver, sourceType, pathSelection.DeclaringType, pathSelection.Path, scope, update))
                        {
                            return null;
                        }

                        var objectProjection = IsObjectSemantics(sourceType.Semantics);
                        return this.Fail(node, objectProjection ? BindingFailure.Unsupported : BindingFailure.TypeMismatch, objectProjection);
                    }

                    var compatibility = kind == ArgumentOperationKind.PayloadProjection ? ConstraintProof.Proven : ProjectedReceiverProof(symbol, operation);
                    this.receiverOperations[node] = new(sourceReceiver, sourceType, projectedReceiver, kind, quality, pathSelection.Path, 0, compatibility);
                }

                var input = target.Parent is BinaryKoto parentBinary ? parentBinary.Right : target.Parent;
                if (update && target.Parent is { } updateExpression)
                {
                    input = this.PropertyUpdateInput(node, updateExpression);
                }

                if ((!operation.IsStandard && !this.BindPropertyCall(node, operation, scope, write ? input : null)) ||
                    (update && !property.Setter.IsStandard && (input is null || !this.BindPropertyCall(node, property.Setter, scope, input))))
                {
                    return null;
                }

                return update ? this.BindPropertyUpdate(node, scope) : Complete(node, this.AccessorUseType(node, operation));
            }

            if (node is MemberAccessKoto stored && sourceReceiver?.BoundType is { } storedSource && this.memberSelections.TryGetValue(stored, out var storageSelection) && storageSelection.Path is not null)
            {
                this.receiverOperations[node] = new(sourceReceiver, storedSource, storageSelection.DeclaringType, ArgumentOperationKind.StorageProjection, ArgumentAdaptation.Exact, storageSelection.Path);
            }
        }

        if (symbol.Type is null)
        {
            this.ConsultTypeSource(symbol);
        }

        return Complete(node, node is MemberAccessKoto ? this.MemberRead(node, symbol, scope) : symbol.Type);
    }
}
