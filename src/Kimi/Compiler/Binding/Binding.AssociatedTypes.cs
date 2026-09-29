// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BoundConformancePath Path, BindingSymbol Associated), AssociatedBinding> associatedBindings = new();
    private readonly HashSet<(BoundType Type, BindingScope Scope)> normalizingAssociated = new();

    private static BoundType? EnclosingContractSelf(BindingScope scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Owner is ContractKoto)
            {
                return current.Owner.BoundSymbol!.Type;
            }
        }

        return null;
    }

    // The bound Contract reference of the conformance being verified in `scope`, if it takes Type arguments.
    private static BindingSymbol? ReferenceContract(BindingScope scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.ConformancePath?.Contract is { Type: { } reference } contract)
            {
                return ReferenceEquals(reference.Symbol, contract) ? null : contract;
            }
        }

        return null;
    }

    // SPEC 8.4.9: a specification qualified by a bound Contract reference (`Indexable<isize>.Element`) belongs to the
    // conformance of that reference or of a refinement of it, not to a sibling reference of the same declaration.
    private static bool SpecifiesContract(IsKoto clause, BindingSymbol contract)
    {
        var head = AssociatedHead(clause);
        head = head is OriginApplicationKoto applied ? UnwrapAssociatedHead(applied.Type) : head;
        if (head is not MemberAccessKoto { Left.BoundSymbol: { Type.Kind: BoundTypeKind.Constructed } reference })
        {
            return true;
        }

        return ReferenceEquals(contract, reference) || contract.Contract?.Ancestors.Contains(reference) == true;
    }

    private BindingSymbol? FindAssociated(BoundType type, BindingScope scope, string name, BindingSymbol? qualifier, Koto use)
        => this.FindAssociated(type, scope, name, qualifier, use, out _);

    private BindingSymbol? FindAssociated(BoundType type, BindingScope scope, string name, BindingSymbol? qualifier, Koto use, out BindingSymbol? reference)
    {
        BindingSymbol? found = null;
        BindingSymbol? foundReference = null;
        var ambiguous = false;
        if (qualifier?.Contract is { } qualified)
        {
            Search(qualified);
        }
        else
        {
            if (type.Symbol?.Contract is { } own)
            {
                Search(own);
            }

            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Constraints is not { Invalid: false } environment)
                {
                    continue;
                }

                foreach (var fact in environment.Facts)
                {
                    if (fact.Kind == ConstraintKind.Contract && AssociatedIdentityMatches(fact.Subject, type) && fact.Contract?.Contract is not null)
                    {
                        Search(this.AppliedAssociatedContract(fact, type).Contract!);
                    }
                }
            }

            if (type.Symbol is { } symbol && this.conformancesByType.TryGetValue(symbol, out var list))
            {
                for (var i = 0; i < list.Count; i++)
                {
                    Search(list[i].Contract.Contract!);
                }
            }
        }

        reference = foundReference;
        if (ambiguous)
        {
            Fail(use, BindingFailure.Ambiguous);
            return null;
        }

        return found;

        void Search(BoundContract shape)
        {
            for (var i = 0; i < shape.AssociatedTypes.Count; i++)
            {
                var associated = shape.AssociatedTypes[i];
                if (associated.Name == name)
                {
                    var owner = associated.Scope.Owner.BoundSymbol!;
                    var candidate = shape.Symbol;
                    if (!ReferenceEquals(candidate.Declaration, owner.Declaration))
                    {
                        for (var j = 0; j < shape.Ancestors.Count; j++)
                        {
                            if (ReferenceEquals(shape.Ancestors[j].Declaration, owner.Declaration))
                            {
                                candidate = shape.Ancestors[j];
                                break;
                            }
                        }
                    }

                    ambiguous |= found is not null && (!ReferenceEquals(found, associated) || !ReferenceEquals(foundReference, candidate));
                    found = associated;
                    foundReference = candidate;
                }
            }
        }
    }

    private BoundType? BindAssociatedProjection(MemberAccessKoto syntax, BindingScope scope, bool applyingOrigins = false)
    {
        if (TypeSpelling(syntax.Right) is not { } name)
        {
            return null;
        }

        Koto receiver = syntax.Left;
        BindingSymbol? qualifier = null;
        if (receiver is MemberAccessKoto qualified && this.ProjectionQualifier(qualified, scope, out var baseSyntax) is { Declaration: ContractKoto } contract)
        {
            if (applyingOrigins && qualified.Right is not ParenthesizedTypeKoto)
            {
                return Fail(syntax, BindingFailure.InvalidAssociatedType);
            }

            qualifier = contract;
            receiver = baseSyntax!;
            qualified.Right.BoundSymbol = contract;
            Complete(qualified.Right, BoundType.Unit);
        }

        var receiverSymbol = this.TypeName(receiver, scope, false);
        if (receiverSymbol?.Kind == BindingSymbolKind.Container || (receiverSymbol?.Declaration is ContractKoto && TypeSpelling(receiver) != "Self"))
        {
            return null;
        }

        var type = this.BindType(receiver, scope);
        if (type is null || type.Semantics != SemanticsKind.Owner)
        {
            return null;
        }

        var associated = this.FindAssociated(type, scope, name, qualifier, syntax, out var reference);
        if (associated is null)
        {
            return null;
        }

        if (this.UnappliedFamily(associated.Declaration, applyingOrigins))
        {
            return Fail(syntax, BindingFailure.InvalidAssociatedType);
        }

        var evidence = qualifier ?? reference ?? associated.Scope.Owner.BoundSymbol!;
        this.projectionUses.Add((syntax, type, evidence));
        if (!this.bindingConstraintTypes && this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: evidence)), scope) == ConstraintProof.Proven)
        {
            // Expand only a referenced receiver's proved contract. This avoids eagerly generating
            // an unbounded chain for recursive associated requirements such as E is SelfContract.
            this.AddContractPremises(evidence.Contract!, type, scope);
        }

        // SPEC 8.4.9: an associated Type of a generic Contract is identified by its declaring bound reference.
        // Families with Origin parameters keep the unqualified form their applications use.
        var projection = type.Symbol?.Declaration is not ContractKoto && reference?.Type is { Kind: BoundTypeKind.Constructed } declaring &&
            ReferenceEquals(declaring.Symbol?.Declaration, associated.Scope.Owner) && this.AssociatedParameters(associated.Declaration).Length == 0
            ? this.InternType(BoundTypeKind.AssociatedProjection, associated, SemanticsKind.Owner, [type, declaring])
            : this.InternType(BoundTypeKind.AssociatedProjection, associated, SemanticsKind.Owner, [type]);
        syntax.BoundSymbol = associated;
        syntax.Right.BoundSymbol = associated;
        Complete(syntax.Right, projection);
        if (!ReferenceEquals(receiver, syntax.Left))
        {
            Complete(syntax.Left, type);
        }

        return this.NormalizedProjection(projection, scope, applyingOrigins);
    }

    // SPEC 8.4.3: a family with Origin parameters names a Type only once its Origin arguments are applied.
    private bool UnappliedFamily(Koto declaration, bool applyingOrigins) => !applyingOrigins && this.AssociatedParameters(declaration).Length != 0;

    // A projection is normalized through the available Constraints, except while Constraint Types are bound or before the
    // Origin arguments that the caller applies.
    private BoundType NormalizedProjection(BoundType projection, BindingScope scope, bool applyingOrigins)
        => this.bindingConstraintTypes || applyingOrigins ? projection : this.ContractType(projection, scope);

    private BindingSymbol? ProjectionQualifier(MemberAccessKoto syntax, BindingScope scope, out Koto? receiver)
    {
        if (syntax.Left is MemberAccessKoto left && this.ProjectionQualifier(left, scope, out receiver) is { Kind: BindingSymbolKind.Container } prefix && this.scopes.TryGetValue(prefix.Declaration, out var members) && TypeSpelling(syntax.Right) is { } name && members.Types.TryGetValue(name, out var target) && this.Accessible(target, scope))
        {
            syntax.Right.BoundSymbol = target;
            Complete(syntax.Right, BoundType.Unit);
            Complete(syntax.Left, BoundType.Unit);
            return target;
        }

        var symbol = this.TypeName(syntax.Right, scope, false);
        var referenceSyntax = syntax.Right;
        while (referenceSyntax is ParenthesizedTypeKoto groupedReference)
        {
            referenceSyntax = groupedReference.Type;
        }

        if (symbol?.Declaration is ContractKoto && UnwrapAssociatedHead(referenceSyntax) is GenericsKoto)
        {
            symbol = this.BindContractReference(referenceSyntax, symbol, scope);
        }

        receiver = syntax.Left;
        if (symbol?.Kind == BindingSymbolKind.Container || symbol?.Declaration is ContractKoto)
        {
            var qualifierName = syntax.Right;
            while (true)
            {
                qualifierName.BoundSymbol = symbol;
                Complete(qualifierName, BoundType.Unit);
                if (qualifierName is ParenthesizedTypeKoto grouped)
                {
                    qualifierName = grouped.Type;
                }
                else if (qualifierName is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner })
                {
                    qualifierName = inner;
                }
                else
                {
                    break;
                }
            }
        }

        return symbol?.Kind == BindingSymbolKind.Container || symbol?.Declaration is ContractKoto ? symbol : null;
    }

    private void BindAssociatedSpecification(IsKoto clause, BindingScope scope, BindingSymbol? owner = null)
    {
        if (clause.FormationType is not null)
        {
            Fail(clause, BindingFailure.InvalidConstraint); // SPEC 8.4.3: a specification fixes its Type; only a requirement may end with a formation Type.
            return;
        }

        var self = this.SelfType(owner ?? scope.Owner.BoundSymbol!);
        var head = AssociatedHead(clause);
        var applied = head as OriginApplicationKoto;
        head = applied is null ? head : UnwrapAssociatedHead(applied.Type);
        var name = head;
        BindingSymbol? qualifier = null;
        if (head is MemberAccessKoto member)
        {
            name = member.Right;
            qualifier = this.TypeName(member.Left, scope, false);
            if (qualifier?.Declaration is ContractKoto && UnwrapAssociatedHead(member.Left) is GenericsKoto)
            {
                // SPEC 8.4.9: `Indexable<isize>.Element` names the associated Type of that bound reference's conformance.
                qualifier = this.BindContractReference(member.Left, qualifier, scope);
            }

            if (qualifier?.Declaration is not ContractKoto)
            {
                Fail(clause, BindingFailure.InvalidAssociatedType);
                return;
            }

            member.Left.BoundSymbol = qualifier;
            Complete(member.Left, BoundType.Unit);
        }

        var associated = TypeSpelling(name!) is { } spelling ? this.FindAssociated(self, scope, spelling, qualifier, clause) : null;
        var ambiguous = false;
        if (associated is null || this.ConformanceByDeclaration(self.Symbol!, qualifier ?? associated.Scope.Owner.BoundSymbol!, out ambiguous) is not { } conformance || conformance.Paths.Count == 0)
        {
            Fail(clause, ambiguous ? BindingFailure.Ambiguous : BindingFailure.InvalidAssociatedType);
            return;
        }

        var parameters = this.AssociatedParameters(associated.Declaration);
        if (parameters.Length != (applied?.ArgumentNodes.Count ?? 0))
        {
            Fail(clause, BindingFailure.InvalidAssociatedType);
            return;
        }

        var projection = this.InternType(BoundTypeKind.AssociatedProjection, associated, SemanticsKind.Owner, [self], originArguments: parameters);
        clause.BoundSymbol = associated;
        clause.Left.BoundSymbol = associated;
        name!.BoundSymbol = associated;
        Complete(name, projection);
        Complete(clause.Left, projection);
        if (head is MemberAccessKoto qualified && !ReferenceEquals(qualified, clause.Left) && applied is null)
        {
            qualified.BoundSymbol = associated;
            Complete(qualified, projection); // A qualifier with Type arguments keeps the head inside a Type wrapper.
        }

        var requirement = this.BindRequirement(clause.Right, projection, false, this.NodeScope(clause, scope));
        if (parameters.Length != 0 && HasUnsupportedAssociatedIdentity(requirement))
        {
            Fail(clause, BindingFailure.Unsupported, true);
            return;
        }

        clause.BoundConstraint = parameters.Length == 0 ? requirement : this.CanonicalAssociatedOrigins(requirement, clause, parameters);
        if (applied is not null)
        {
            Complete(applied.Type, projection);
            Complete(applied, projection);
        }

        Complete(clause, BoundType.Boolean);
    }

    private void PrepareAssociatedBindings()
    {
        for (var i = 0; i < this.activeConformancePaths.Count; i++)
        {
            var path = this.activeConformancePaths[i];
            if (!ReferenceEquals(path.RootPath, path))
            {
                continue;
            }

            var shape = path.RootContract.Contract!;
            // Ancestor paths inherit the root declaration's associated identities, not a
            // separately declared ancestor's conditions or specifications.
            for (var j = 0; j < shape.AssociatedTypes.Count; j++)
            {
                var key = (path, shape.AssociatedTypes[j]);
                if (!this.associatedBindings.ContainsKey(key))
                {
                    this.associatedBindings.Add(key, new());
                }
            }

            CollectShape(shape, path);
            if (path.Declaration.Parent is SyntaxFormKoto syntax && TryConditionalBlock(syntax, out var block))
            {
                for (var j = 0; j < block.Items.Count; j++)
                {
                    if (block.Items[j] is IsKoto { IsAssociatedConstraint: true, BoundConstraint: { } constraint })
                    {
                        Collect(constraint, path);
                    }
                }
            }

            for (var j = 0; j < shape.Ancestors.Count; j++)
            {
                CollectShape(shape.Ancestors[j].Contract!, path);
            }

            var container = (DeclarationContainerKoto)path.Type.Declaration;
            for (var j = 0; j < container.Members.Count; j++)
            {
                if (container.Members[j] is IsKoto { IsAssociatedConstraint: true, BoundConstraint: { } constraint } clause && SpecifiesContract(clause, path.RootContract))
                {
                    Collect(constraint, path);
                }
            }
        }

        for (var i = 0; i < this.activeConformancePaths.Count; i++)
        {
            var path = this.activeConformancePaths[i];
            var shape = path.Contract.Contract!;
            for (var j = 0; j < shape.AssociatedTypes.Count; j++)
            {
                var associated = shape.AssociatedTypes[j];
                if (this.ResolveAssociated(path, associated) is { } result)
                {
                    path.AssociatedStorage[associated] = result;
                }
            }
        }

        void CollectShape(BoundContract shape, BoundConformancePath path)
        {
            for (var i = 0; i < shape.ClauseStorage.Count; i++)
            {
                if (shape.ClauseStorage[i].BoundConstraint is { } constraint)
                {
                    Collect(constraint, path);
                }
            }
        }

        void Collect(BoundConstraint constraint, BoundConformancePath path)
        {
            if (constraint.Kind == ConstraintKind.And)
            {
                Collect(constraint.Left!, path);
                Collect(constraint.Right!, path);
            }
            else if (constraint is { Kind: ConstraintKind.TypeIdentity, Subject.Kind: BoundTypeKind.AssociatedProjection, RequiredType: { } required } && this.associatedBindings.TryGetValue((path, constraint.Subject.Symbol!), out var binding) && !binding.Candidates.Contains(required))
            {
                binding.Candidates.Add(required);
            }
        }
    }

    private BoundType? ResolveAssociated(BoundConformancePath path, BindingSymbol associated)
    {
        // Refinement paths have exactly the root declaration's D + P environment.
        // Share immutable normalized Types and candidate storage within that root only.
        path = path.RootPath;
        if (!this.associatedBindings.TryGetValue((path, associated), out var binding))
        {
            return null;
        }

        if (binding.State != 0)
        {
            return binding.State == 2 ? binding.Result : null;
        }

        binding.State = 1;
        var scope = path.Scope;
        var self = this.SelfType(path.Type);
        BoundType? result = null;
        var valid = binding.Candidates.Count != 0;
        var iteratorItem = this.IsCompleteAssociated(associated);
        for (var i = 0; i < binding.Candidates.Count; i++)
        {
            var type = this.ContractType(binding.Candidates[i], scope, self);
            valid &= (iteratorItem || this.IsAssociatedCore(type, scope)) && (result is null || ReferenceEquals(result, type));
            result = type;
        }

        binding.State = valid ? (byte)2 : (byte)3;
        binding.Result = valid ? result : null;
        return binding.Result;
    }

    // `qualifier` is the bound reference of a generic Contract's projection (SPEC 8.4.9); it selects that conformance.
    private BoundType? ResolveAssociated(BoundType receiver, BindingSymbol associated, BindingScope scope, BoundType? qualifier = null)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.ConformancePath is { } path && ReferenceEquals(path.Type, receiver.Symbol) &&
                (qualifier is null || ReferenceEquals(this.DeclaringReference(associated, path.Contract.Type ?? qualifier), qualifier)))
            {
                return this.ResolveAssociated(path, associated);
            }
        }

        if (receiver.Symbol is not { } owner ||
            (qualifier is null ? this.ConformanceByDeclaration(owner, associated.Scope.Owner.BoundSymbol!, out _) : this.ConformanceByReference(owner, associated, qualifier)) is not { } identity)
        {
            return null;
        }

        BoundType? available = null;
        BoundType? pending = null;
        var conflicting = false;
        for (var i = 0; i < identity.PathStorage.Count; i++)
        {
            var path = identity.PathStorage[i];
            var condition = this.ProveConformanceConditions(path, receiver, scope);
            if (condition == ConstraintProof.Error)
            {
                return null;
            }

            if (condition == ConstraintProof.Refuted || this.ResolveAssociated(path, associated) is not { } binding)
            {
                continue;
            }

            if (condition == ConstraintProof.Proven)
            {
                if (available is not null && !ReferenceEquals(available, binding))
                {
                    return null;
                }

                available = binding;
            }

            conflicting |= pending is not null && !ReferenceEquals(pending, binding);
            pending = binding;
        }

        // Explicit header metadata may normalize a common binding before witnesses are ready.
        // The separately retained projection obligation must still prove conformance at finalization.
        return available ?? (conflicting ? null : pending);
    }

    private bool IsAssociatedCore(BoundType type, BindingScope scope)
    {
        if (type.Semantics != SemanticsKind.Owner || type.Origin is not null)
        {
            return false;
        }

        if (type.Kind == BoundTypeKind.Parameter)
        {
            if (this.HasSemanticsRole(type, SemanticsMask.Owner, scope))
            {
                return true;
            }

            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Constraints is not { Invalid: false } environment)
                {
                    continue;
                }

                foreach (var fact in environment.Facts)
                {
                    if (fact.Kind == ConstraintKind.TypeIdentity && this.FactStates(fact, type, out var stated) && this.AvailableConstraintFact(environment, fact) && stated is { Kind: not (BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication) } required)
                    {
                        return this.IsAssociatedCore(required, scope);
                    }
                }
            }

            return false;
        }

        if (type.Kind == BoundTypeKind.AssociatedProjection && type.Components[0].Symbol?.Declaration is StructKoto or EnumKoto)
        {
            return false;
        }

        return type.Kind != BoundTypeKind.TargetProjection || this.HasValueRole(type, scope, false);
    }

    // SPEC 8.4.3, 4.6.9, 22.1.2: family definitions with implemented formation checks, Iterator.Item and Indexable.Element
    // denote complete Types. Other associated definitions still have the Core limitation recorded in STATUS.
    private bool IsCompleteAssociated(BindingSymbol associated)
        => this.AssociatedParameters(associated.Declaration).Length != 0 ||
        (associated.Name == "Item" && ReferenceEquals(associated.Scope.Owner, this.Library.Iterator.Declaration)) ||
        (associated.Name == "Element" && ReferenceEquals(associated.Scope.Owner, this.Library.Indexable?.Declaration));

    /// <summary>Substitutes Contract Self and normalizes explicit associated identities without member inference.</summary>
    private BoundType ContractType(BoundType type, BindingScope scope, BoundType? self = null, bool normalize = true)
    {
        type = this.ApplyContractEnvironment(type, scope);
        if (this.activeRequirementContract is { } bound)
        {
            type = this.SubstituteContractReference(type, bound); // SPEC 8.4.2: the requirement of a bound reference.
        }

        if (type.Symbol?.Declaration is ContractKoto && type.Kind is BoundTypeKind.Nominal or BoundTypeKind.Constructed)
        {
            return self ?? EnclosingContractSelf(scope) ?? type;
        }

        // SPEC 8.4.9: substituting Self in `Self.Element` of a bound reference's requirement or conformance keeps that
        // reference (`Indexable<isize>`) as the qualifier of the projection.
        if (type is { Kind: BoundTypeKind.AssociatedProjection, OriginArguments.Count: 0, Components: [{ Kind: BoundTypeKind.Nominal or BoundTypeKind.Constructed, Symbol.Declaration: ContractKoto }] } &&
            this.AssociatedParameters(type.Symbol!.Declaration).Length == 0 &&
            (self ?? EnclosingContractSelf(scope)) is { } replacement && replacement.Symbol?.Declaration is not ContractKoto &&
            (this.activeRequirementContract ?? ReferenceContract(scope))?.Type is { Kind: BoundTypeKind.Constructed } boundReference &&
            this.DeclaringReference(type.Symbol!, boundReference) is var declaring && ReferenceEquals(declaring.Symbol?.Declaration, type.Symbol!.Scope.Owner))
        {
            type = this.InternType(BoundTypeKind.AssociatedProjection, type.Symbol, type.Semantics, [replacement, declaring], type.Length, type.Origin, (BoundOrigin[])type.OriginArguments, type.LengthExpression);
        }

        var count = type.Components.Count;
        var qualified = type.Kind == BoundTypeKind.AssociatedProjection && count == 2;
        var components = count == 0 ? null : this.RentTypes(count);
        try
        {
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                components![i] = qualified && i == 1 ? this.ContractArguments(type.Components[1], scope, self, normalize) : this.ContractType(type.Components[i], scope, self, normalize);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            var result = changed ? this.InternType(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, count), type.Length, type.Origin, (BoundOrigin[])type.OriginArguments, type.LengthExpression) : type;
            if (!normalize || result.Kind is not (BoundTypeKind.AssociatedProjection or BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication) || !this.normalizingAssociated.Add((result, scope)))
            {
                return result;
            }

            try
            {
                if (result.Kind == BoundTypeKind.AssociatedProjection && result.Components[0] is { Symbol.Declaration: StructKoto or EnumKoto } receiver &&
                    this.ResolveAssociated(receiver, result.Symbol!, scope, result.Components.Count == 2 ? result.Components[1] : null) is { } fixedType)
                {
                    fixedType = this.SubstituteStoredOrigins(fixedType, result.Symbol!.Declaration, (BoundOrigin[])result.OriginArguments);
                    // A container substitution can expose another associated projection
                    // (Wrapper<S>.Element -> S.Element). Normalize that identity too;
                    // the active query above still guards recursive specifications.
                    return this.StoredType(fixedType, receiver) is { } stored
                        ? this.ContractType(stored, scope, self) : result;
                }

                for (var current = scope; current is not null; current = current.Parent)
                {
                    if (current.Constraints is not { Invalid: false } environment)
                    {
                        continue;
                    }

                    foreach (var fact in environment.Facts)
                    {
                        if (fact.Kind == ConstraintKind.TypeIdentity && this.FactStates(fact, result, out var required) && this.AvailableConstraintFact(environment, fact) && required is not null)
                        {
                            return this.ContractType(required, scope, self);
                        }
                    }
                }

                return result;
            }
            finally
            {
                this.normalizingAssociated.Remove((result, scope));
            }
        }
        finally
        {
            if (components is not null)
            {
                this.typeScratch.Return(components, clearArray: true);
            }
        }
    }

    // The one conformance of `owner` whose bound reference reaches `qualifier` as the declaring reference of `associated`.
    private BoundConformance? ConformanceByReference(BindingSymbol owner, BindingSymbol associated, BoundType qualifier)
    {
        if (!this.conformancesByType.TryGetValue(owner, out var identities))
        {
            return null;
        }

        BoundConformance? found = null;
        for (var i = 0; i < identities.Count; i++)
        {
            var identity = identities[i];
            if (identity.Paths.Count != 0 && identity.Contract.Type is { } reference && ReferenceEquals(reference.Symbol?.Declaration, associated.Scope.Owner) &&
                ReferenceEquals(reference, qualifier))
            {
                if (found is not null && !ReferenceEquals(found, identity))
                {
                    return null;
                }

                found = identity;
            }
        }

        return found;
    }

    // The bound reference, as a Type, of the Contract that declares `associated`, reached from a reference to it or to a
    // refinement of it (`UniqIndexable<isize>` reaches `Indexable<isize>`).
    private BoundType DeclaringReference(BindingSymbol associated, BoundType reference)
    {
        var owner = associated.Scope.Owner;
        if (ReferenceEquals(reference.Symbol?.Declaration, owner) || reference.Symbol?.Contract is not { } shape)
        {
            return reference;
        }

        for (var i = 0; i < shape.Ancestors.Count; i++)
        {
            if (ReferenceEquals(shape.Ancestors[i].Declaration, owner) && shape.Ancestors[i].Type is { } ancestor)
            {
                return this.StoredType(ancestor, reference) ?? ancestor;
            }
        }

        return reference;
    }

    // A qualifier's Type arguments substituted like any Type; the qualifier itself names its Contract, not Self.
    private BoundType ContractArguments(BoundType qualifier, BindingScope scope, BoundType? self, bool normalize)
    {
        var count = qualifier.Components.Count;
        var arguments = this.RentTypes(count);
        try
        {
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                arguments[i] = this.ContractType(qualifier.Components[i], scope, self, normalize);
                changed |= !ReferenceEquals(arguments[i], qualifier.Components[i]);
            }

            return changed ? this.InternType(qualifier.Kind, qualifier.Symbol, qualifier.Semantics, arguments.AsSpan(0, count), qualifier.Length, qualifier.Origin, (BoundOrigin[])qualifier.OriginArguments, qualifier.LengthExpression) : qualifier;
        }
        finally
        {
            this.typeScratch.Return(arguments, clearArray: true);
        }
    }

    private sealed class AssociatedBinding
    {
        internal BindingScope? FormationScope { get; set; }

        internal List<BoundType> Candidates { get; } = new();

        internal BoundType? Result { get; set; }

        internal byte State { get; set; }
    }
}
