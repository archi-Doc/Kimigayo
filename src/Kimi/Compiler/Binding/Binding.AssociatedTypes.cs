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
    private bool SpecifiesContract(IsKoto clause, BindingSymbol contract)
    {
        var head = AssociatedHead(clause);
        head = head is OriginApplicationKoto applied ? UnwrapTransparentType(applied.Type) : head;
        if (head is not MemberAccessKoto member || this.ContractReferenceOf(member.Left) is not { } reference || !IsBoundContractReference(reference))
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

        reference = context is null ? reference : this.SubstituteContractReference(reference, context, out _);
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

            if (type.Symbol is { } symbol && this.ConformancesOf(symbol) is { } list)
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
        }

        // The receiver's lookup, published by the path's walk; when it failed, its record is the path's (one, SPEC 23.1.2) and the
        // projection forms nothing from it.
        var receiverPath = this.NamePath(receiver, scope);
        if (receiverPath.End is PathEnd.QualificationStop or PathEnd.Ambiguous)
        {
            return null;
        }

        var receiverSymbol = receiverPath.Symbol;
        if (receiverSymbol?.Kind == BindingSymbolKind.Container || (receiverSymbol?.Declaration is ContractKoto && TypeSpelling(receiver) != "Self"))
        {
            return null;
        }

        var type = this.BindType(receiver, scope);
        if (type is null || type.Semantics != SemanticsKind.Owner)
        {
            return null;
        }

        if (this.ProjectsThroughItsOwnClause(syntax, type, scope))
        {
            return this.Fail(syntax, BindingFailure.Cycle, true);
        }

        var associated = this.FindAssociated(type, scope, name, qualifier, syntax, out var reference);
        if (associated is null)
        {
            // G621: before the conformance declarations register, an unqualified name over a declared Type has no candidates yet (they
            // come from the conformance table; a qualified one searches its Contract); the read is judged again once they register
            // (VerifyEarlyProjections).
            if (!this.conformancesRegistered && type.Symbol?.Declaration is StructKoto or EnumKoto)
            {
                this.earlyProjections.Add((syntax, type, scope, name, qualifier));
            }

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

    // DECISIONS BU-61 (SPEC 23.1.2): the projection use that formed `type` and is judged under `scope`'s own premises: the first use
    // found walking `scope`'s chain inward-out (the scopes whose premises the solver reads, Supply) before a scope that adds premises
    // without forming it; null otherwise. Each use is keyed by its Type and the scope its conformance check (ValidateConformances, after
    // the bodies) judges it in, ConstraintScope. Premises only grow inward, so a use found before such a scope is judged as `scope`
    // would judge it, and one beyond it may be rejected under fewer premises than `scope` has. A check that fails only because its
    // Type is such a projection rests on that use, which publication judges (AddPendingPrerequisite).
    private Koto? ProjectionUseOf(BoundType type, BindingScope scope)
    {
        for (BindingScope? current = scope; current is not null; current = current.Parent)
        {
            if (this.projectionForms.TryGetValue((type, current), out var use))
            {
                return use;
            }

            if (current.Constraints is { } premises && (premises.Invalid || premises.Facts.Count != 0))
            {
                return null;
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
        if (syntax.Left is MemberAccessKoto left && this.ProjectionQualifier(left, scope, out receiver) is { Kind: BindingSymbolKind.Container } prefix && this.scopes.TryGetValue(prefix.Declaration, out var members) && TypeSpelling(syntax.Right) is { } name && MemberTypes(members, name) is { } target && this.Accessible(target, scope))
        {
            syntax.Right.BoundSymbol = target;
            Complete(syntax.Right, BoundType.Unit);
            Complete(syntax.Left, BoundType.Unit);
            return target;
        }

        var selection = this.TypeName(syntax.Right, scope);
        var referenceSyntax = syntax.Right;
        while (referenceSyntax is ParenthesizedTypeKoto groupedReference)
        {
            referenceSyntax = groupedReference.Type;
        }

        receiver = syntax.Left;
        if (selection is null || (selection.Kind != BindingSymbolKind.Container && selection.Declaration is not ContractKoto))
        {
            return null;
        }

        var symbol = this.QualifierReference(referenceSyntax, selection, scope);
        if (symbol is not null)
        {
            CompleteQualifier(syntax.Right, selection, symbol);
        }

        return symbol;
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
        head = applied is null ? head : UnwrapTransparentType(applied.Type);
        var name = head;
        BindingSymbol? qualifier = null;
        if (head is MemberAccessKoto member)
        {
            name = member.Right;

            // SPEC 8.6.1: `Indexable<isize>.Element` names the associated Type of that bound reference's conformance.
            if (this.TypeName(member.Left, scope) is not { Declaration: ContractKoto } selection || this.QualifierReference(member.Left, selection, scope) is not { } bound)
            {
                this.Fail(clause, BindingFailure.InvalidAssociatedType);
                return;
            }

            qualifier = bound;
            CompleteQualifier(member.Left, selection, bound);
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
        for (var i = 0; i < this.impls.Paths.Count; i++)
        {
            var path = this.impls.Paths[i];
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
                if (container.Members[j] is IsKoto { IsAssociatedConstraint: true, BoundConstraint: { } constraint } clause && this.SpecifiesContract(clause, path.RootContract))
                {
                    Collect(constraint, path);
                }
            }
        }

        for (var i = 0; i < this.impls.Paths.Count; i++)
        {
            var path = this.impls.Paths[i];
            var shape = path.Contract.Contract!;
            for (var j = 0; j < shape.AssociatedTypes.Count; j++)
            {
                var associated = shape.AssociatedTypes[j];
                // The conformance is the site of a Type its associated binding would form past the height limit (Binding.TypeHeight.cs).
                var frame = this.BeginConsultation(path.Declaration);
                var resolved = this.ResolveAssociated(path, associated, out _);
                this.EndConsultation(path.Declaration, frame);
                if (resolved is { } result && !this.PendingAssociatedType(result))
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
                required = context is null ? required : this.SubstituteContractReference(required, context, out _);
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
        if (this.RefusedConformance(path))
        {
            pending = false;
            return null;
        }

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
        var self = this.ConformanceSelf(path);
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
