// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BoundConformancePath Path, BoundRequirement Associated), AssociatedBinding> associatedBindings = new();
    private readonly HashSet<(BoundType Type, BindingScope Scope)> normalizingAssociated = new();
    private HashSet<BoundRequirement>? ambiguousAssociatedMatches;
    private Dictionary<Koto, (BoundRequirement First, BoundRequirement Second, int Count)>? associatedAmbiguities;

    // Whether the pass's declaration evidence of conformance bindings is incomplete: before PrepareAssociatedInference has collected it,
    // or while an inference batch collects (a state-3 verdict then is provisional; PrepareAssociatedInference resolves every binding again).
    private bool AssociatedEvidencePending => !this.associatedInferenceReady || this.collectingAssociated is not null;

    // SPEC 8.4: whether a Type is the Self of a Contract, a Type parameter standing for any conforming Type. It is distinct
    // from every reference to the Contract itself, such as the declaring Contract of a projection.
    internal static bool IsContractSelf(BoundType? type) => type is { Kind: BoundTypeKind.Parameter, Symbol.SelfOf: not null };

    // SPEC 8.4: the dedicated Self Type parameter of a Contract. It has no generic slot, so no substitution of the Contract's
    // own Type parameters replaces it; ContractType substitutes it by the conforming Type.
    internal BoundType ContractSelfType(BindingSymbol contract)
    {
        if (contract.ContractSelf is { } existing)
        {
            return existing;
        }

        var symbol = new BindingSymbol("Self", BindingSymbolKind.TypeParameter, contract.Declaration, this.scopes.GetValueOrDefault(contract.Declaration) ?? contract.Scope) { SelfOf = contract, Slot = -1 };
        symbol.WholeType = symbol.Type = new("Self", BoundTypeKind.Parameter, symbol);
        return contract.ContractSelf = symbol.Type;
    }

    // SPEC 8.6.1: a specification qualified by a bound Contract reference (`Indexable<isize>.Element`) belongs to the
    // conformance of that reference or of a refinement of it, not to a sibling reference of the same declaration.
    private static bool SpecifiesContract(IsKoto clause, BindingSymbol contract)
    {
        var head = AssociatedHead(clause);
        head = head is OriginApplicationKoto applied ? UnwrapAssociatedHead(applied.Type) : head;
        if (head is not MemberAccessKoto { Left.BoundSymbol: { } reference } || !IsBoundContractReference(reference))
        {
            return true;
        }

        return ReferenceEquals(contract, reference) || contract.Contract?.Ancestors.Contains(reference) == true;
    }

    // The Type that `Self` denotes inside a declaration: a Contract's Self parameter, or the declared Type applied to its own
    // Type parameters.
    private BoundType DeclarationSelf(BindingSymbol owner) => owner.Declaration is ContractKoto ? this.ContractSelfType(owner) : this.SelfType(owner);

    private BoundType? EnclosingContractSelf(BindingScope scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Owner is ContractKoto)
            {
                return this.ContractSelfType(current.Owner.BoundSymbol!);
            }
        }

        return null;
    }

    // SPEC 8.6.1: every projection names the Contract reference that declares its associated Type, as its second component:
    // a bound reference (`Indexable<isize>`), or a Contract applied to its own Type parameters (`Indexable<Key>` inside its
    // declaration), reached from the reference or refinement that supplied the name, or from the declaring Contract.
    private BoundType ProjectionContract(BindingSymbol associated, BindingSymbol? reference)
    {
        var supplied = reference ?? associated.Scope.Owner.BoundSymbol!;
        return IsBoundContractReference(supplied) ? supplied.Type! : this.SelfType(supplied);
    }

    // Projection syntax and the effective shape share one identity. A declaration applied to its own slots denotes
    // that declaration's requirement; every other normalized argument list retains its interned bound reference.
    private BoundRequirement? AssociatedIdentity(BoundType? projection, BindingSymbol? context = null)
    {
        if (projection is not { Kind: BoundTypeKind.AssociatedProjection, Symbol: { } associated, Components: [_, var reference] })
        {
            return null;
        }

        reference = context is null ? reference : this.SubstituteContractReference(reference, context);
        var declaration = reference.Symbol!;
        return new(associated, ReferenceEquals(reference, this.SelfType(declaration)) ? declaration : this.BoundContractReference(reference));
    }

    private BindingSymbol? FindAssociated(BoundType type, BindingScope scope, string name, BindingSymbol? qualifier, Koto use)
        => this.FindAssociated(type, scope, name, qualifier, use, out _);

    private BindingSymbol? FindAssociated(BoundType type, BindingScope scope, string name, BindingSymbol? qualifier, Koto use, out BindingSymbol? reference)
    {
        BindingSymbol? found = null;
        BindingSymbol? foundReference = null;
        BoundRequirement? alternative = null;
        this.ambiguousAssociatedMatches?.Clear();
        var contextualQualifier = qualifier is not null && !IsBoundContractReference(qualifier) && qualifier.Schema is { GenericSlots.Count: > 0 } or { Origins.Count: > 0 };
        if (qualifier?.Contract is { } qualified && !contextualQualifier)
        {
            Search(qualified);
        }
        else
        {
            if ((type.Symbol?.SelfOf ?? type.Symbol)?.Contract is { } own)
            {
                SearchAvailable(own);
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
                        SearchAvailable(this.AppliedAssociatedContract(fact, type).Contract!);
                    }
                }
            }

            if (type.Symbol is { } symbol && this.conformancesByType.TryGetValue(symbol, out var list))
            {
                for (var i = 0; i < list.Count; i++)
                {
                    SearchAvailable(list[i].Contract.Contract!);
                }
            }
        }

        reference = foundReference;
        if (alternative is { } second)
        {
            this.FailExplained(ref this.associatedAmbiguities, use, BindingFailure.Ambiguous, (new(found!, foundReference!), second, this.ambiguousAssociatedMatches!.Count));
            return null;
        }

        return found;

        void SearchAvailable(BoundContract shape)
        {
            if (!contextualQualifier || ReferenceEquals(shape.Symbol.Declaration, qualifier!.Declaration))
            {
                Search(shape);
            }

            if (contextualQualifier)
            {
                for (var i = 0; i < shape.Ancestors.Count; i++)
                {
                    if (ReferenceEquals(shape.Ancestors[i].Declaration, qualifier!.Declaration))
                    {
                        Search(shape.Ancestors[i].Contract!);
                    }
                }
            }
        }

        void Search(BoundContract shape)
        {
            for (var i = 0; i < shape.AssociatedTypes.Count; i++)
            {
                var identity = shape.AssociatedTypes[i];
                var associated = identity.Symbol;
                if (associated.Name == name)
                {
                    var candidate = identity.Contract;
                    if (found is not null && (!ReferenceEquals(found, associated) || !ReferenceEquals(foundReference, candidate)))
                    {
                        alternative ??= identity;
                        (this.ambiguousAssociatedMatches ??= new()).Add(new(found, foundReference!));
                        this.ambiguousAssociatedMatches.Add(identity);
                    }
                    else
                    {
                        found = associated;
                        foundReference = candidate;
                    }
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
                return this.Fail(syntax, BindingFailure.InvalidAssociatedType);
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
            return this.Fail(syntax, BindingFailure.InvalidAssociatedType);
        }

        var evidence = qualifier ?? reference ?? associated.Scope.Owner.BoundSymbol!;
        this.projectionUses.Add((syntax, type, evidence));
        if (!this.bindingConstraintTypes && this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: evidence)), scope) == ConstraintProof.Proven)
        {
            // Expand only a referenced receiver's proved contract. This avoids eagerly generating
            // an unbounded chain for recursive associated requirements such as E is SelfContract.
            this.AddContractPremises(evidence.Contract!, type, scope);
        }

        // SPEC 8.6.1: an associated Type is identified by its receiver and its declaring Contract reference.
        var projection = this.types.Intern(BoundTypeKind.AssociatedProjection, associated, SemanticsKind.Owner, [type, this.ProjectionContract(associated, reference ?? qualifier)]);
        this.projectionForms.TryAdd((projection, this.ConstraintScope(syntax)), syntax);
        syntax.BoundSymbol = associated;
        syntax.Right.BoundSymbol = associated;
        Complete(syntax.Right, projection);
        if (!ReferenceEquals(receiver, syntax.Left))
        {
            Complete(syntax.Left, type);
        }

        return this.NormalizedProjection(projection, scope, applyingOrigins);
    }

    // DECISIONS BU-61 (SPEC 23.1.2): the projection use that formed `type` and is judged in the innermost scope of `scope`'s chain, the
    // scopes whose premises the solver reads (Supply); null when no use on that chain formed it. Each use is keyed by its Type and the
    // scope its conformance check (ValidateConformances, after the bodies) judges it in, ConstraintScope; the innermost wins, since an
    // inner scope's premises can validate what an outer one rejects. A check that fails only because its Type is such a projection rests
    // on that use, which publication judges (AddPendingPrerequisite).
    private Koto? ProjectionUseOf(BoundType type, BindingScope scope)
    {
        for (BindingScope? current = scope; current is not null; current = current.Parent)
        {
            if (this.projectionForms.TryGetValue((type, current), out var use))
            {
                return use;
            }
        }

        return null;
    }

    // SPEC 8.5: a family with Origin parameters names a Type only once its Origin arguments are applied.
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
            this.Fail(clause, BindingFailure.InvalidConstraint); // SPEC 8.5: a specification fixes its Type; only a requirement may end with a formation Type.
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
                // SPEC 8.6.1: `Indexable<isize>.Element` names the associated Type of that bound reference's conformance.
                qualifier = this.BindContractReference(member.Left, qualifier, scope);
            }

            if (qualifier?.Declaration is not ContractKoto)
            {
                this.Fail(clause, BindingFailure.InvalidAssociatedType);
                return;
            }

            member.Left.BoundSymbol = qualifier;
            Complete(member.Left, BoundType.Unit);
        }

        BindingSymbol? reference = null;
        var associated = TypeSpelling(name!) is { } spelling ? this.FindAssociated(self, scope, spelling, qualifier, clause, out reference) : null;
        var ambiguous = false;
        if (associated is null || this.ConformanceByDeclaration(self.Symbol!, reference ?? qualifier ?? associated.Scope.Owner.BoundSymbol!, out ambiguous) is not { } conformance || conformance.Paths.Count == 0)
        {
            this.Fail(clause, ambiguous ? BindingFailure.Ambiguous : BindingFailure.InvalidAssociatedType);
            return;
        }

        var parameters = this.AssociatedParameters(associated.Declaration);
        if (parameters.Length != (applied?.ArgumentNodes.Count ?? 0))
        {
            this.Fail(clause, BindingFailure.InvalidAssociatedType);
            return;
        }

        var projection = this.types.Intern(BoundTypeKind.AssociatedProjection, associated, SemanticsKind.Owner, [self, this.ProjectionContract(associated, reference)], originArguments: parameters);
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
            this.Fail(clause, BindingFailure.Unsupported, true);
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
            if (!ReferenceEquals(path.RootPath, path) || path.InheritedFrom is not null)
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
                if (this.ResolveAssociated(path, associated, out _) is { } result && !this.PendingAssociatedType(result))
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
                    Collect(constraint, path, shape.Symbol);
                }
            }
        }

        void Collect(BoundConstraint constraint, BoundConformancePath path, BindingSymbol? context = null)
        {
            if (constraint.Kind == ConstraintKind.And)
            {
                Collect(constraint.Left!, path, context);
                Collect(constraint.Right!, path, context);
            }
            else if (constraint is { Kind: ConstraintKind.TypeIdentity, RequiredType: { } required } &&
                this.AssociatedIdentity(constraint.Subject, context) is { } identity && this.associatedBindings.TryGetValue((path, identity), out var binding))
            {
                required = context is null ? required : this.SubstituteContractReference(required, context);
                if (!binding.Candidates.Contains(required))
                {
                    binding.Candidates.Add(required);
                }
            }
        }
    }

    // A conformance path's binding of `associated`, or null: `pending` while its evidence is incomplete (AssociatedEvidencePending, or the
    // binding revisited while it is being resolved), else none, final (DECISIONS BU-50): nothing specifies the binding, Binding rejected
    // it (state 3), or an inherited path's base gives none, its base clause substitutes none or its sources disagree.
    private BoundType? ResolveAssociated(BoundConformancePath path, BoundRequirement associated, out bool pending)
    {
        pending = this.AssociatedEvidencePending;
        if (path.InheritedFrom is not null)
        {
            BoundType? inheritedResult = null;
            for (var s = 0; s < path.InheritedSourceCount; s++)
            {
                var source = path.InheritedSource(s);
                foreach (var original in source.Contract.Contract!.AssociatedStorage)
                {
                    if (original.Symbol != associated.Symbol || this.SubstituteRequirementContract(original.Contract, path.InheritedBase!) != associated.Contract)
                    {
                        continue;
                    }

                    if (this.ResolveAssociated(source, original, out pending) is not { } inherited ||
                        this.StoredType(inherited, path.InheritedBase!) is not { } current ||
                        (inheritedResult is not null && !this.SameCompleteType(inheritedResult, current)))
                    {
                        return null;
                    }

                    inheritedResult = current;
                }
            }

            return inheritedResult;
        }

        // Refinement paths have exactly the root declaration's D + P environment.
        // Share immutable normalized Types and candidate storage within that root only.
        path = path.RootPath;
        if (!this.associatedBindings.TryGetValue((path, associated), out var binding))
        {
            return null;
        }

        if (binding.Candidates.Count == 0)
        {
            if (this.associatedInferenceReady && this.associatedInference.TryGetValue(path.Type, out var batch))
            {
                this.InferAssociatedTypes(batch);
                return batch.State == 2 && !ReferenceEquals(batch, this.collectingAssociated) && !binding.InferenceConflict ? binding.Inferred : null;
            }

            return null; // Pending declaration evidence is not a cached negative result (`pending`); once complete, nothing specifies it.
        }

        if (binding.State != 0)
        {
            binding.State = binding.State == 1 ? (byte)4 : binding.State; // Revisited while it is being resolved.
            pending |= binding.State == 4;
            return binding.State == 2 ? binding.Result : null;
        }

        binding.State = 1;
        var scope = path.Scope;
        var self = this.SelfType(path.Type);
        BoundType? result = null;
        var valid = binding.Candidates.Count != 0;
        for (var i = 0; i < binding.Candidates.Count; i++)
        {
            var type = this.ContractType(binding.Candidates[i], scope, self);
            valid &= result is null || this.SameCompleteType(result, type);
            result = type;
        }

        // SPEC 8.3.3 Recursion (DECISIONS BU-50): a binding revisited while it is being resolved supplies no evidence to itself, so a
        // Type that stays pending through that revisit (`G<T> { Item is G<G<T>>.Item }`, `A.Item is A.Item`, a cycle through another
        // binding) names no Type: the binding gives none, and its declaration's records stay those its proof gives.
        valid &= binding.State != 4 || result is null || !this.PendingAssociatedType(result);
        binding.State = valid ? (byte)2 : (byte)3;
        binding.Result = valid ? result : null;
        return binding.Result;
    }

    // SPEC 8.6.1 (DECISIONS BU-50): what a projection names before its subject substitutes it, asked in the owner declaration's scope, one
    // outcome for every subject. Finality is independence of scope: a Type found there is the answer, since the declaration's premises are
    // each instance's obligations (a builtin arithmetic output or a fixed array's iterator Type, complete, no Subject; else the subject's
    // conformance binding, ResolveAssociated, an inherited conformance's under the base clause's arguments, in the subject declaration's
    // terms with the associated Type's Origin parameters bound to the projection's arguments, for the caller to instantiate under the
    // Subject: as a view, ContractTypeCore, or as a kept part, CompleteProjections); no Type is pending while the pass's evidence is
    // incomplete or the projection has an abstract part (a use scope may carry a premise the declaration's lacks: `T is PrimitiveInteger`
    // for `Wrapping<T>.Output`), else none, final, whatever the reason: the projection names no Type, and the records are the
    // declarations' and the use's (SPEC 23.1.2). No subject-kind test decides pending or none.
    private ProjectionOutcome ProjectionSpecification(BoundType projection, BindingScope scope)
    {
        if ((this.BuiltinArithmeticOutput(projection, scope) ?? this.FixedArrayIteratorType(projection)) is { } builtin)
        {
            return new(builtin, null, false);
        }

        var subject = projection.Components[0];
        var qualifier = projection.Components.Count == 2 ? projection.Components[1] : null;
        if (this.ResolveAssociated(subject, projection.Symbol!, scope, qualifier, out var pending) is not { } binding)
        {
            return new(null, null, pending || subject.HasAbstractPart || qualifier is { HasAbstractPart: true }); // A projection is itself abstract.
        }

        return new(this.SubstituteStoredOrigins(binding, projection.Symbol!.Declaration, (BoundOrigin[])projection.OriginArguments), subject, false);
    }

    // `qualifier` is the declaring Contract reference of the projection (SPEC 8.6.1); a bound reference of a Contract with Type
    // arguments selects that conformance, and a Contract without them has one conformance per Type. `pending`: a null result is not final
    // (as the path's ResolveAssociated); once the pass's evidence is complete, a Type without the conformance gives none. The paths whose
    // conditions the receiver meets must agree (PathsAgree), else none.
    private BoundType? ResolveAssociated(BoundType receiver, BindingSymbol associated, BindingScope scope, BoundType? qualifier, out bool pending)
    {
        pending = this.AssociatedEvidencePending;
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.ConformancePath is { } path && ReferenceEquals(path.Type, receiver.Symbol) &&
                this.AssociatedInPath(path, receiver, associated, qualifier, scope) is { } selected)
            {
                return this.ResolveAssociated(path, selected, out pending);
            }
        }

        if (receiver.Symbol is not { } owner ||
            (qualifier is not { Kind: BoundTypeKind.Constructed } ? this.ConformanceByDeclaration(owner, associated.Scope.Owner.BoundSymbol!, out _)
                : this.ConformanceByReference(owner, receiver, associated, qualifier, scope)) is not { } identity)
        {
            return null;
        }

        BoundType? available = null;
        BoundType? unproven = null;
        var conflicting = false;
        for (var i = 0; i < identity.PathStorage.Count; i++)
        {
            var path = identity.PathStorage[i];
            var condition = this.ProveConformanceConditions(path, receiver, scope);
            if (condition == ConstraintProof.Error)
            {
                return null;
            }

            if (condition == ConstraintProof.Refuted || this.AssociatedInPath(path, receiver, associated, qualifier, scope) is not { } selected)
            {
                continue;
            }

            if (this.ResolveAssociated(path, selected, out var bindingPending) is not { } binding)
            {
                pending |= bindingPending;
                continue;
            }

            if (condition == ConstraintProof.Proven)
            {
                if (available is not null && !this.PathsAgree(available, binding, receiver, scope))
                {
                    return null;
                }

                available = binding;
            }

            conflicting |= unproven is not null && !this.PathsAgree(unproven, binding, receiver, scope);
            unproven = binding;
        }

        // Explicit header metadata may normalize a common binding before witnesses are ready.
        // The separately retained projection obligation must still prove conformance at finalization.
        return available ?? (conflicting ? null : unproven);
    }

    // SPEC 8.9.2 (DECISIONS BU-50, revised by bu55): two conformance paths of a receiver agree on an associated Type when their bindings are
    // one Type as written or as the receiver's Types. Each binding is in the subject declaration's terms, normalized under its own path's
    // conditions only: OwningIterator's direct `Self is LendingIterator` binds `I.(LendingIterator).LentItem(step)` and its refinement path
    // through `Self is Iterator when I is Iterator` binds `I.Item` (Iterator's `LendingIterator.LentItem(step) is Item`), both i32 under
    // OwningIterator<Counter>. The instances are judged by SameConformanceType, as the declaration's agreement check judges two paths.
    private bool PathsAgree(BoundType left, BoundType right, BoundType receiver, BindingScope scope)
        => this.SameCompleteType(left, right) ||
            (this.StoredType(left, receiver) is { } x && this.StoredType(right, receiver) is { } y && this.SameConformanceType(x, y, scope));

    // The one conformance of `owner` whose bound reference, instantiated for `receiver` (`Indexable<K>` of `Keyed<isize>`),
    // names `qualifier` in `scope` (NamesReference) as the declaring reference of `associated`.
    private BoundConformance? ConformanceByReference(BindingSymbol owner, BoundType receiver, BindingSymbol associated, BoundType qualifier, BindingScope scope)
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
                (ReferenceEquals(reference, qualifier) || this.NamesReference(reference, receiver, qualifier, scope)))
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

    private BoundRequirement? AssociatedInPath(
        BoundConformancePath path, BoundType receiver, BindingSymbol associated, BoundType? qualifier, BindingScope scope)
    {
        BoundRequirement? found = null;
        foreach (var identity in path.Contract.Contract!.AssociatedStorage)
        {
            if (ReferenceEquals(identity.Symbol, associated))
            {
                var reference = this.ProjectionContract(associated, identity.Contract);
                if (qualifier is null || ReferenceEquals(reference, qualifier) || this.NamesReference(reference, receiver, qualifier, scope))
                {
                    if (found is not null)
                    {
                        return null;
                    }

                    found = identity;
                }
            }
        }

        return found;
    }

    // ProjectionSpecification's one outcome (DECISIONS BU-50): the Type, with the Subject it is instantiated under (none for a builtin's
    // complete Type); else Pending (nothing decides it yet) or none.
    private readonly record struct ProjectionOutcome(BoundType? Type, BoundType? Subject, bool Pending);

    private sealed class AssociatedBinding
    {
        internal BindingScope? FormationScope { get; set; }

        internal List<BoundType> Candidates { get; } = new();

        internal BoundType? Result { get; set; }

        internal BoundType? Inferred { get; set; }

        internal FunctionKoto? InferenceSource { get; set; }

        internal bool InferenceConflict { get; set; }

        internal InferenceProblem Problem { get; set; }

        internal FunctionKoto? ProblemSource { get; set; }

        internal Koto? ProblemDetail { get; set; }

        internal List<AssociatedEvidence> EvidenceSources { get; } = new();

        internal byte State { get; set; }
    }
}
