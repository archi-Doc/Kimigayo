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

    // The associated Type `name` of `type`, its declaring reference out; an ambiguity is recorded at `use` (SPEC 8.5.2).
    private BindingSymbol? FindAssociated(BoundType type, BindingScope scope, string name, BindingSymbol? qualifier, Koto use, out BindingSymbol? reference)
    {
        var search = this.SearchAssociated(type, scope, name, qualifier);
        reference = search.Reference;
        if (search.Alternative is { } second)
        {
            this.FailExplained(ref this.associatedAmbiguities, use, BindingFailure.Ambiguous, (new(search.Found!, search.Reference!), second, this.ambiguousAssociatedMatches!.Count));
            return null;
        }

        return search.Found;
    }

    // The search alone, recording nothing (a walk's question, 9.7.3's evidence): the candidate Contracts are the qualifier's, else the
    // Type's own Contract, the scope's Contract facts on it and its conformances; Source is the conformance that supplied the match.
    private AssociatedSearch SearchAssociated(BoundType type, BindingScope scope, string name, BindingSymbol? qualifier)
    {
        BindingSymbol? found = null;
        BindingSymbol? foundReference = null;
        BoundRequirement? alternative = null;
        BoundConformance? source = null;
        BoundConformance? searching = null;
        this.ambiguousAssociatedMatches?.Clear();
        if (qualifier?.Contract is { } qualified)
        {
            Search(qualified);
        }
        else
        {
            if ((type.Symbol?.SelfOf ?? type.Symbol)?.Contract is { } own)
            {
                Search(own);
            }

            // SPEC 8.3.2, 8.5.2: the Contracts the premises supply for the Type (every one of Enumerate's rows: the use is judged).
            var start = this.premiseRows.Count;
            this.PremiseRows(type, scope, this.premiseRows);
            for (var i = start; i < this.premiseRows.Count; i++)
            {
                if (this.premiseRows[i].Symbol.Contract is { } shape)
                {
                    Search(shape);
                }
            }

            this.premiseRows.RemoveRange(start, this.premiseRows.Count - start);

            // SPEC 9.7.3: a conformance is a candidate as the receiver sees it, its bound reference at the receiver's arguments, the
            // terms a premise's reference is in, so one conformance reached both ways is one candidate (PLAN G798).
            if (type.Symbol is { } symbol && this.ConformancesOf(symbol) is { } list)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    searching = list[i];
                    Search(this.SubstituteRequirementContract(list[i].Contract, type).Contract!);
                }
            }
        }

        return new(found, foundReference, alternative, source);

        void Search(BoundContract shape)
        {
            for (var i = 0; i < shape.AssociatedTypes.Count; i++)
            {
                var identity = shape.AssociatedTypes[i];
                var associated = identity.Symbol;
                if (associated.Name == name)
                {
                    var candidate = identity.Contract;
                    // SPEC 8.5.1, 8.2.3: paths to one declaration whose references are one in the view count once.
                    if (found is not null && (!ReferenceEquals(found, associated) || !this.SameContractReference(foundReference!, candidate, scope)))
                    {
                        alternative ??= identity;
                        (this.ambiguousAssociatedMatches ??= new()).Add(new(found, foundReference!));
                        this.ambiguousAssociatedMatches.Add(identity);
                    }
                    else
                    {
                        found = associated;
                        foundReference = candidate;
                        source ??= searching;
                    }
                }
            }
        }
    }

    // SPEC 9.7.3, 8.5.2, 8.8.2: a nested Type named through a Core qualifier also resolves as an associated Type when the qualifier's
    // applied Type has one of that name with conformance evidence: a premise, or a conformance with a declared path without premises,
    // holds for every binding (8.8.1); a conditional one only where its condition is proven for the written arguments ("Refuted: No
    // conformance from this path"; before the Contract headers are ready every proof is Unknown: PLAN G728's remainder). Judged where the
    // qualifier is applied (9.7.1 step 3), the one former of every nested Type's reference; the path's selection stands.
    private bool AlsoAssociated(BoundType qualifier, string name, MemberAccessKoto path, BindingScope scope)
    {
        var search = this.SearchAssociated(qualifier, scope, name, null);
        if (search.Found is not { } associated)
        {
            this.NoteEarlyProjection(path, qualifier, scope, name, null);
            return false;
        }

        if (search.Alternative is not null || search.Source is not { } source)
        {
            return true;
        }

        for (var i = 0; i < source.Paths.Count; i++)
        {
            if (source.Paths[i].Premises is null)
            {
                return true;
            }
        }

        var evidence = search.Reference ?? associated.Scope.Owner.BoundSymbol!;
        return this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, qualifier, contract: evidence)), scope) == ConstraintProof.Proven;
    }

    // G621: before the conformance declarations register, an unqualified name over a declared Type has no associated candidates yet (they
    // come from the conformance table; a qualified one searches its Contract): a projection that found none and a nested Type judged
    // without them are judged again once they register (VerifyEarlyProjections).
    private void NoteEarlyProjection(MemberAccessKoto use, BoundType type, BindingScope scope, string name, BindingSymbol? qualifier)
    {
        if (!this.conformancesRegistered && type.Symbol?.Declaration is StructKoto or EnumKoto)
        {
            this.earlyProjections.Add((use, type, scope, name, qualifier));
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
        if (receiver is MemberAccessKoto { Right: ParenthesizedTypeKoto } qualified)
        {
            // F2 (SPEC 23.1.2): a `.(C)` whose Contract reference failed recorded that failure, and the projection rests on it, forming
            // nothing; one naming no Contract is the path's failing step, missing there.
            if (this.ProjectionQualifier(qualified, scope) is not { } contract)
            {
                return WrittenReference(qualified.Right) is { BindingFailure: not BindingFailure.None } ? null : this.FailMissing(qualified, scope, BindingFailure.MissingType);
            }

            qualifier = contract;
            receiver = qualified.Left;
        }

        // The receiver's lookup, published by the path's walk; when it failed, its record is the path's (one, SPEC 23.1.2) and the
        // projection forms nothing from it.
        var receiverPath = this.NamePath(receiver, scope);
        if (receiverPath.End is PathEnd.QualificationStop or PathEnd.Ambiguous)
        {
            return null;
        }

        var receiverSymbol = receiverPath.Symbol;
        if (receiverSymbol?.Kind == BindingSymbolKind.Container || receiverSymbol is { IsContract: true })
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
            this.NoteEarlyProjection(syntax, type, scope, name, qualifier);

            // F3 (PLAN G724): a Name after a projection that names a member Type of the projection's normal form (SPEC 9.6) is a selection
            // not implemented yet: a located Unsupported_Kd, never a missing name. One the normal form does not declare stays missing.
            return receiverPath.End == PathEnd.Projection && this.LookupTypeMember(type, name, scope, typeRole: true).Member is not null
                ? this.Fail(syntax, BindingFailure.Unsupported, true) : null;
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
        syntax.BoundSymbol = associated;
        syntax.Right.BoundSymbol = associated;
        Complete(syntax.Right, projection);
        if (!ReferenceEquals(receiver, syntax.Left))
        {
            Complete(syntax.Left, type);
        }

        return this.NormalizedProjection(projection, scope, applyingOrigins);
    }

    // SPEC 8.5: a family with Origin parameters names a Type only once its Origin arguments are applied.
    private bool UnappliedFamily(Koto declaration, bool applyingOrigins) => !applyingOrigins && this.AssociatedParameters(declaration).Length != 0;

    // A projection is normalized through the available Constraints, except while Constraint Types are bound or before the
    // Origin arguments that the caller applies.
    private BoundType NormalizedProjection(BoundType projection, BindingScope scope, bool applyingOrigins)
        => this.bindingConstraintTypes || applyingOrigins ? projection : this.ContractType(projection, scope);

    // One associated search (SearchAssociated): the match, its declaring reference, a second distinct match and the match's conformance.
    private readonly record struct AssociatedSearch(BindingSymbol? Found, BindingSymbol? Reference, BoundRequirement? Alternative, BoundConformance? Source);

    // SPEC 8.5.2: only the suffix `.(C).` names a projection's Contract (its `C` in the Contract Reference row); `T.C.X` is an ordinary
    // path (9.6, 9.7.3), never read as `T.(C).X`.
    private BindingSymbol? ProjectionQualifier(MemberAccessKoto syntax, BindingScope scope)
    {
        if (syntax.Right is not ParenthesizedTypeKoto || this.TypeName(syntax.Right, scope) is not { IsContract: true } selection ||
            WrittenReference(syntax.Right) is not { } referenceSyntax)
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

    // SPEC 8.5.1, 8.5.3: an associated specification of any owner, a struct's, an enum's, a conditional block's or a Contract's: the one
    // reader of `associate AssociatedTypeName is R` past the parser (which rejected every other head, Parser.IsAssociatedTypeName). A
    // Contract's specification is no Constraint clause, but its bound requirement is a premise of the Contract (8.5.3: a condition on the
    // conforming Self), so it enters the Contract's environment, failed or not, as BuildContract keeps it with the Contract's clauses.
    private void BindAssociatedSpecification(IsKoto clause, BindingScope scope, BindingSymbol? owner = null)
    {
        owner ??= scope.Owner.BoundSymbol!;
        if (IsRecovery(clause, out _) || IsRecovery(clause.Left, out _))
        {
            // The head failed (SPEC F.6.2): the specification is judged no further, and its requirement keeps its own records (23.1.2).
            this.Fail(clause, BindingFailure.InvalidConstraint);
            this.BindRequirement(clause.Right, BoundType.Unit, false, this.NodeScope(clause, scope));
            clause.BoundConstraint = owner.Declaration is ContractKoto ? this.InternConstraint(new(ConstraintKind.Error)) : null;
            return;
        }

        var requirement = this.BindSpecification(clause, scope, owner);
        if (requirement is not null)
        {
            clause.BoundConstraint = requirement;
            Complete(clause, BoundType.Boolean);
        }

        if (owner.Declaration is ContractKoto)
        {
            clause.BoundConstraint ??= this.InternConstraint(new(ConstraintKind.Error));
            var environment = scope.Constraints ??= new();
            if (this.AddConstraintFact(environment, clause.BoundConstraint))
            {
                environment.Fail(clause); // The clause failed, or holds a recovery.
            }

            if (requirement is null)
            {
                this.AddPrerequisite(scope.Owner, clause);
            }
        }
    }

    private BoundConstraint? BindSpecification(IsKoto clause, BindingScope scope, BindingSymbol owner)
    {
        if (clause.FormationType is not null)
        {
            this.Fail(clause, BindingFailure.InvalidConstraint); // SPEC 8.5: a specification fixes its Type; only a requirement may end with a formation Type.
            return null;
        }

        var projection = this.BindAssociatedName(clause, scope, owner, out var pending);
        if (pending)
        {
            // The requirement binds against no subject and keeps its own records, as under a pending subject (BindConstraint).
            var open = this.BindRequirement(clause.Right, BoundType.Unit, false, this.NodeScope(clause, scope));
            return open.Kind == ConstraintKind.Error ? open : this.InternConstraint(new(ConstraintKind.Unresolved));
        }

        if (projection is not { Symbol: { } associated })
        {
            return null;
        }

        var parameters = this.AssociatedParameters(associated.Declaration);
        var requirement = this.BindRequirement(clause.Right, projection, false, this.NodeScope(clause, scope));
        if (parameters.Length != 0 && HasUnsupportedAssociatedIdentity(requirement))
        {
            this.Fail(clause, BindingFailure.Unsupported, true);
            return null;
        }

        return parameters.Length == 0 ? requirement : this.CanonicalAssociatedOrigins(requirement, clause, parameters);
    }

    // SPEC 8.5.1 (Qualification), 8.5.3, 8.5.4: the associated Type that a specification's AssociatedTypeName names, as the projection of
    // its owner's Self, for every owner kind (BindAssociatedSpecification: a struct's, an enum's, a conditional block's, a Contract's). A
    // selector (`C.X`, `(C).X`, `::C.X`) is looked up once in its own row (UseRow) and names, with its Type arguments (QualifierReference
    // on the reference WrittenReference reads), a direct conformance or an ancestor of the owner, judged on the selector
    // itself (SelectorQualifies); the Name is found among that Contract's associated Types, an unqualified Name among all available ones;
    // the Origin parameters repeat the count. Null after the clause's failure, or with pending set and the clause unfailed when its
    // selector formed no reference for a missing name (PendingReference).
    private BoundType? BindAssociatedName(IsKoto clause, BindingScope scope, BindingSymbol owner, out bool pending)
    {
        pending = false;
        var self = this.DeclarationSelf(owner);
        var head = AssociatedHead(clause);
        var applied = head as OriginApplicationKoto;
        head = applied is null ? head : UnwrapTransparentType(applied.Type);
        var name = head;
        BindingSymbol? qualifier = null;
        if (head is MemberAccessKoto member)
        {
            name = member.Right;
            var selection = this.TypeName(member.Left, scope);
            if (selection is not { IsContract: true } || WrittenReference(member.Left) is not { } written ||
                this.QualifierReference(written, selection, scope) is not { } bound)
            {
                pending = this.PendingReference(member.Left, selection, scope);
                return pending ? null : this.Fail(clause, BindingFailure.InvalidAssociatedType);
            }

            if (!this.SelectorQualifies(owner, self, bound))
            {
                return this.Fail(clause, BindingFailure.InvalidAssociatedType);
            }

            qualifier = bound;
            CompleteQualifier(member.Left, selection, bound);
        }

        BindingSymbol? reference = null;
        var associated = TypeSpelling(name!) is { } spelling ? this.FindAssociated(self, scope, spelling, qualifier, clause, out reference) : null;
        var ambiguous = false;
        if (associated is null || (owner.Declaration is not ContractKoto &&
            (this.ConformanceByDeclaration(self.Symbol!, reference ?? qualifier ?? associated.Scope.Owner.BoundSymbol!, out ambiguous) is not { } conformance || conformance.Paths.Count == 0)))
        {
            return this.Fail(clause, ambiguous ? BindingFailure.Ambiguous : BindingFailure.InvalidAssociatedType);
        }

        var parameters = this.AssociatedParameters(associated.Declaration);
        if (parameters.Length != (applied?.ArgumentNodes.Count ?? 0))
        {
            return this.Fail(clause, BindingFailure.InvalidAssociatedType);
        }

        var projection = this.types.Intern(BoundTypeKind.AssociatedProjection, associated, SemanticsKind.Owner, [self, this.ProjectionContract(associated, reference ?? qualifier)], originArguments: parameters);
        clause.BoundSymbol = associated;
        clause.Left.BoundSymbol = associated;
        name!.BoundSymbol = associated;
        Complete(name, projection);
        Complete(clause.Left, projection);
        if (head is MemberAccessKoto qualified && !ReferenceEquals(qualified, clause.Left))
        {
            qualified.BoundSymbol = associated;
            Complete(qualified, projection); // A qualifier with Type arguments or Origin parameters keeps the head inside a wrapper.
        }

        if (applied is not null)
        {
            applied.BoundSymbol = associated;
            Complete(applied.Type, projection);
            Complete(applied, projection);
        }

        return projection;
    }

    // SPEC 8.5.1 Qualification, 8.4.3: the Contract a selector names is a direct conformance or an ancestor of the owner. A Contract's
    // conformances are itself and its ancestors, and it is not its own ancestor, so it qualifies a proper ancestor; a struct's or an enum's
    // are the conformances it declares and their ancestors (8.6.2).
    private bool SelectorQualifies(BindingSymbol owner, BoundType self, BindingSymbol contract)
        => owner.Declaration is ContractKoto
            ? !ReferenceEquals(contract, owner) && IsRefinement(owner, contract)
            : this.ConformanceByDeclaration(self.Symbol!, contract, out _) is { Paths.Count: > 0 };

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
