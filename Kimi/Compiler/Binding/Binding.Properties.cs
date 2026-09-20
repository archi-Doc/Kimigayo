// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private PropertyTypeVisitor? propertyTypeVisitor;

    private static bool NarrowerAccess(ModifierKind inner, ModifierKind outer) => outer switch
    {
        ModifierKind.Public => inner is ModifierKind.ProtectedOrInternal or ModifierKind.Protected or ModifierKind.Internal or ModifierKind.ProtectedAndInternal or ModifierKind.Private,
        ModifierKind.ProtectedOrInternal => inner is ModifierKind.Protected or ModifierKind.Internal or ModifierKind.ProtectedAndInternal or ModifierKind.Private,
        ModifierKind.Protected or ModifierKind.Internal => inner is ModifierKind.ProtectedAndInternal or ModifierKind.Private,
        ModifierKind.ProtectedAndInternal => inner == ModifierKind.Private,
        _ => false,
    };

    private static BoundAccessor Accessor(PropertyAccessorKoto syntax)
    {
        var property = ((PropertyKoto)syntax.Parent!).BoundSymbol!.Property!;
        return syntax.AccessorKind == PropertyAccessorKind.Get ? property.Getter : property.Setter;
    }

    private static Koto? PropertySignatureOwner(Koto use)
    {
        for (var node = use; node.Parent is { } parent; node = parent)
        {
            if (parent is PropertyAccessorKoto accessor)
            {
                return ReferenceEquals(node, accessor.ReceiverType) || ReferenceEquals(node, accessor.ValueType) || ReferenceEquals(node, accessor.ReturnType) ? accessor : null;
            }

            if (parent is PropertyKoto property)
            {
                return ReferenceEquals(node, property.TypeKoto) ? property : null;
            }

            if (parent is FunctionKoto)
            {
                return null;
            }
        }

        return null;
    }

    private void IndexAccessor(PropertyAccessorKoto syntax, BindingScope scope)
    {
        var accessor = Accessor(syntax);
        var property = accessor.Property;
        if (accessor.SignatureSymbol is not { } signature || !ReferenceEquals(signature.Declaration, syntax))
        {
            accessor.SignatureSymbol = new(syntax.AccessorText, BindingSymbolKind.PropertyAccessor, syntax, property.Symbol.Scope);
        }

        accessor.SignatureSymbol.Scope = property.Symbol.Scope;
        accessor.SignatureSymbol.Type = null;
        syntax.BoundSymbol = accessor.SignatureSymbol;
        if (syntax.ReceiverType is not null || (!accessor.IsStandard && property.Symbol.Scope.Owner is StructKoto or ContractKoto))
        {
            if (accessor.SelfSymbol is null || !ReferenceEquals(accessor.SelfSymbol.Declaration, syntax))
            {
                accessor.SelfSymbol = new("self", BindingSymbolKind.Parameter, syntax, scope) { Slot = 0 };
            }

            accessor.SelfSymbol.Scope = scope;
            scope.Values.Add("self", accessor.SelfSymbol);
        }

        if (syntax.AccessorKind == PropertyAccessorKind.Set && !accessor.IsStandard)
        {
            if (accessor.ValueSymbol is null || !ReferenceEquals(accessor.ValueSymbol.Declaration, syntax))
            {
                accessor.ValueSymbol = new("value", BindingSymbolKind.Parameter, syntax, scope) { Slot = 1 };
            }

            accessor.ValueSymbol.Scope = scope;
            scope.Values.Add("value", accessor.ValueSymbol);
        }

        if (property.IsStored && syntax.Body is not null)
        {
            accessor.StorageSymbol ??= new("storage", BindingSymbolKind.Storage, property.Declaration, scope);
            accessor.StorageSymbol.Scope = scope;
            scope.Values.Add("storage", accessor.StorageSymbol);
        }
    }

    private void BindPropertyHeader(BoundProperty property)
    {
        var syntax = property.Declaration;
        var scope = this.DeclarationScope(property.Symbol);
        this.BindAccessorReceiver(property.Getter);
        this.BindAccessorReceiver(property.Setter);
        var getterScope = property.Getter.Declaration is { } get ? this.scopes[get] : scope;
        property.Symbol.Type = syntax.TypeKoto is { } type ? this.BindType(type, property.IsStored ? scope : getterScope) : null;
        // Only an initializer can infer storage. Accessor bodies never supply its Type.
        if (property.IsStored && property.Type is null && syntax.TypeKoto is null && syntax.InitializerKoto is { } initializer)
        {
            property.Symbol.Type = this.BindNode(initializer, scope);
        }

        this.BindAccessorSignature(property.Getter);
        this.BindAccessorSignature(property.Setter);
    }

    private void BindAccessorReceiver(BoundAccessor accessor)
    {
        if (!accessor.IsPresent)
        {
            return;
        }

        var property = accessor.Property;
        var syntax = accessor.Declaration;
        var declaredAccess = syntax is null ? ModifierKind.NoModifier : syntax.Modifier.ExtractAccessibilityModifiers();
        accessor.Access = declaredAccess == ModifierKind.NoModifier ? DeclarationAccess(property.Symbol) : declaredAccess;
        if (accessor.IsStandard)
        {
            return;
        }

        var scope = this.scopes[syntax!];
        if (syntax!.ReceiverType is { } receiver)
        {
            accessor.Receiver = this.BindType(receiver, scope);
        }
        else if (property.Symbol.Scope.Owner is StructKoto or ContractKoto)
        {
            var self = this.SelfType(property.Symbol.Scope.Owner.BoundSymbol!);
            accessor.Receiver = this.InternType(BoundTypeKind.Semantics, null, accessor.Kind == PropertyAccessorKind.Get ? SemanticsKind.Ref : SemanticsKind.Uniq, [self], origin: this.OriginAtom(syntax, OriginKind.Input, 0));
        }

        if (accessor.SelfSymbol is { } symbol)
        {
            symbol.Type = accessor.Receiver;
        }
    }

    private void BindAccessorSignature(BoundAccessor accessor)
    {
        if (!accessor.IsPresent)
        {
            return;
        }

        var property = accessor.Property;
        var syntax = accessor.Declaration;
        if (accessor.IsStandard)
        {
            accessor.Input = accessor.Kind == PropertyAccessorKind.Set ? property.Type : null;
            accessor.Result = accessor.Kind == PropertyAccessorKind.Get ? property.Type : BoundType.Unit;
            return;
        }

        var scope = this.scopes[syntax!];
        if (property.IsStored && property.Type is { } storageType)
        {
            var inheritedSyntax = accessor.Kind == PropertyAccessorKind.Set ? syntax!.ValueType : syntax!.ReturnType;
            if (inheritedSyntax is not null)
            {
                this.InheritOriginContract(inheritedSyntax, storageType);
            }
        }

        if (accessor.Kind == PropertyAccessorKind.Set)
        {
            accessor.Input = syntax!.ValueType is { } input ? this.BindType(input, scope) : this.BindImplicitSetterInput(property, accessor, scope);
            if (accessor.ValueSymbol is { } symbol)
            {
                symbol.Type = accessor.Input;
            }
        }

        accessor.Result = syntax!.ReturnType is { } result ? this.BindType(result, scope) : accessor.Kind == PropertyAccessorKind.Get ? property.Type : BoundType.Unit;
        accessor.SignatureSymbol!.Type = accessor.Result;
        if (accessor.StorageSymbol is { } storage)
        {
            storage.Type = property.Type;
        }
    }

    private BoundType? BindImplicitSetterInput(BoundProperty property, BoundAccessor setter, BindingScope scope)
    {
        if (property.Declaration.TypeKoto is not { } syntax)
        {
            return null;
        }

        if (property.Type is { } completed && !HasDeclaredOrigins(completed))
        {
            return completed;
        }

        // The shared header has two position-sensitive meanings. Reuse syntax temporarily,
        // retaining only the getter's annotation on it and the setter's complete semantic Type.
        var visitor = this.propertyTypeVisitor ??= new();
        var start = visitor.Snapshots.Count;
        visitor.Visit(syntax);
        try
        {
            var result = this.BindType(syntax, scope, new(TypePosition.Parameter, setter.Binder, 1, true));
            for (var i = start; i < visitor.Snapshots.Count; i++)
            {
                var node = visitor.Snapshots[i].Node;
                if (node.BindingFailure != BindingFailure.None)
                {
                    Fail(setter.Binder, node.BindingFailure);
                }
            }

            return result;
        }
        finally
        {
            visitor.Restore(start);
        }
    }

    private void ValidateProperties(BindingMode mode)
    {
        // Projection normalization must not erase signature access or input constraints.
        // Check before publishing Property verification or building conformance witnesses.
        for (var i = 0; i < this.projectionUses.Count; i++)
        {
            var use = this.projectionUses[i];
            var declaration = PropertySignatureOwner(use.Use);
            var syntax = declaration as PropertyKoto ?? declaration?.Parent as PropertyKoto;
            if (syntax?.BoundSymbol?.Property is not { } property)
            {
                continue;
            }

            var domain = syntax.IsContractRequirement ? property.Symbol.Scope.Owner.BoundSymbol! : declaration!.BoundSymbol!;
            if (!ProjectionAccessCovers(use.Use, use.Type, use.Contract, domain))
            {
                Fail(declaration!, BindingFailure.Access);
            }

            this.RequireConstraint(declaration!, this.CheckTypeConstraints(use.Type, this.ConstraintScope(use.Use)), mode);
        }

        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is not PropertyKoto syntax || syntax.BoundSymbol?.Property is not { } property)
            {
                continue;
            }

            // The header/storage Type belongs to the Property domain even when
            // every accessor has narrower access. Requirements inherit the Contract domain.
            var domain = syntax.IsContractRequirement ? property.Symbol.Scope.Owner.BoundSymbol! : property.Symbol;
            if (property.Type is { } type && !TypeAccessCovers(type, domain, domain))
            {
                Fail(syntax, BindingFailure.Access);
            }

            var proof = this.ValidateAccessor(property.Getter);
            proof = CombineProof(proof, this.ValidateAccessor(property.Setter), true);
            property.IsVerified = proof == ConstraintProof.Proven && !InvalidDeclarationContext(syntax);
            if (proof != ConstraintProof.Proven)
            {
                this.RequireConstraint(syntax, proof, mode, this.ConformanceDiagnosticCause(property.Symbol.Scope.Owner));
            }
        }
    }

    private ConstraintProof ValidateAccessor(BoundAccessor accessor)
    {
        if (!accessor.IsPresent)
        {
            return ConstraintProof.Proven;
        }

        var property = accessor.Property;
        var syntax = accessor.Declaration;
        if (syntax?.BindingState == BindingState.Invalid || InvalidDeclarationContext(property.Declaration))
        {
            return ConstraintProof.Error;
        }

        if (property.Type is null || accessor.Result is null || UnresolvedTypeDeclarationContext(property.Declaration))
        {
            return ConstraintProof.Unknown;
        }

        // A resolved signature can still contain a constructed Type whose input
        // constraints fail. Validate before publishing a Property certificate.
        var scope = syntax is null ? this.DeclarationScope(property.Symbol) : this.scopes[syntax];
        var formation = CombineProof(this.CheckTypeConstraints(property.Type, scope), this.CheckTypeConstraints(accessor.Result, scope), true);
        if (accessor.Input is { } inputType)
        {
            formation = CombineProof(formation, this.CheckTypeConstraints(inputType, scope), true);
        }

        if (accessor.Receiver is { } receiverSignature)
        {
            formation = CombineProof(formation, this.CheckTypeConstraints(receiverSignature, scope), true);
        }

        if (formation != ConstraintProof.Proven)
        {
            return formation;
        }

        if (syntax is not null && syntax.Modifier.ExtractAccessibilityModifiers() != ModifierKind.NoModifier && !NarrowerAccess(accessor.Access, DeclarationAccess(property.Symbol)))
        {
            Fail(syntax, BindingFailure.Access);
            return ConstraintProof.Error;
        }

        if (accessor.IsStandard)
        {
            return ConstraintProof.Proven;
        }

        var owner = property.Symbol.Scope.Owner;
        if (owner is StructKoto or ContractKoto
            ? !this.IsReceiverType(accessor.Receiver, owner.BoundSymbol!)
            : accessor.Receiver is not null)
        {
            Fail(syntax!, BindingFailure.InvalidTypeFormation);
            return ConstraintProof.Error;
        }

        if (property.IsStored && owner is StructKoto)
        {
            var semantics = accessor.Kind == PropertyAccessorKind.Get ? SemanticsKind.Ref : SemanticsKind.Uniq;
            if (accessor.Receiver is not { Kind: BoundTypeKind.Semantics } receiver || receiver.Semantics != semantics || !ReferenceEquals(receiver.Components[0], this.SelfType(owner.BoundSymbol!)))
            {
                Fail(syntax!, BindingFailure.TypeMismatch);
                return ConstraintProof.Error;
            }
        }

        var compared = accessor.Kind == PropertyAccessorKind.Get ? accessor.Result : accessor.Input;
        if (compared is null)
        {
            return ConstraintProof.Unknown;
        }

        if (((property.IsStored || accessor.Kind == PropertyAccessorKind.Get) && !SameType(compared, property.Type)) || (accessor.Kind == PropertyAccessorKind.Set && !SameType(accessor.Result, BoundType.Unit)))
        {
            Fail(syntax!, BindingFailure.TypeMismatch);
            return ConstraintProof.Error;
        }

        var domain = property.Declaration.IsContractRequirement ? property.Symbol.Scope.Owner.BoundSymbol! : accessor.SignatureSymbol!;
        if (!TypeAccessCovers(accessor.Result, domain, domain) ||
            (accessor.Input is { } input && !TypeAccessCovers(input, domain, domain)) ||
            (accessor.Receiver is { } receiverType && !TypeAccessCovers(receiverType, domain, domain)))
        {
            Fail(syntax!, BindingFailure.Access);
            return ConstraintProof.Error;
        }

        return property.IsStored && accessor.Kind == PropertyAccessorKind.Get ? this.ProveCopy(property.Type, property.Declaration) : ConstraintProof.Proven;
    }

    private BoundType? BindAccessorBody(PropertyAccessorKoto syntax, BindingScope scope)
    {
        var accessor = Accessor(syntax);
        this.BindHeader(accessor.Property.Symbol);
        if (syntax.Body is { } body)
        {
            var discards = ReferenceEquals(accessor.Result, BoundType.Unit);
            var actual = this.BindNode(body, scope, discards ? null : accessor.Result);
            var structural = this.resultStructure ??= new(item => ReferenceEquals(item.BoundType, BoundType.Never));
            structural.Clear();
            if (!discards && body is not CodeBlockKoto && (KotoHelper.IsBodyExpression(body) || structural.CanComplete(body)) &&
                actual is not null && accessor.Result is { } result && !FitsType(actual, result))
            {
                Fail(body, BindingFailure.TypeMismatch);
            }
        }

        return Complete(syntax, accessor.Result);
    }

    private sealed class PropertyTypeVisitor : KotoVisitor
    {
        // Type and Origin share one semantic slot; restoring them separately would erase the saved Type.
        internal List<(Koto Node, object? Meaning, BindingSymbol? Symbol, BindingState State, BindingFailure Failure)> Snapshots { get; } = new();

        public override void Visit(Koto node)
        {
            this.Snapshots.Add((node, node.BoundMeaning, node.BoundSymbol, node.BindingState, node.BindingFailure));
            node.BoundMeaning = null;
            node.BindingState = BindingState.Unvisited;
            node.BindingFailure = BindingFailure.None;
            node.VisitChildren(this);
        }

        internal void Restore(int start)
        {
            for (var i = this.Snapshots.Count - 1; i >= start; i--)
            {
                var saved = this.Snapshots[i];
                saved.Node.BoundMeaning = saved.Meaning;
                saved.Node.BoundSymbol = saved.Symbol;
                saved.Node.BindingState = saved.State;
                saved.Node.BindingFailure = saved.Failure;
            }

            this.Snapshots.RemoveRange(start, this.Snapshots.Count - start);
        }
    }
}
