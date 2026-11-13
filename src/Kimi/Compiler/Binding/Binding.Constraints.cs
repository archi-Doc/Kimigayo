// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<ConstraintKey, BoundConstraint> constraints = new(KimiLibrarySize.KeyedCapacity(KimiLibrarySize.Stated.Constraints));

    private readonly Dictionary<IsKoto, bool> deferredConstraints = new(ReferenceEqualityComparer.Instance);
    private ConstraintSyntaxVisitor? constraintSyntaxVisitor;

    /// <summary>Queries a bound proposition using the lexical input constraints at a syntax node.</summary>
    /// <param name="constraint">The proposition produced by this compilation's Binding.</param>
    /// <param name="context">The use site supplying lexical assumptions.</param>
    /// <returns>The four-valued proof result; Unknown is never permission to proceed.</returns>
    public ConstraintProof Prove(BoundConstraint constraint, Koto context)
        => this.ProveConstraint(constraint, this.ConstraintScope(context));

    /// <summary>Substitutes declaration slots and queries the result in a caller's lexical environment.</summary>
    /// <param name="constraint">The bound declaration constraint.</param>
    /// <param name="binder">The declaration owning the substituted slots.</param>
    /// <param name="arguments">Complete type arguments in slot order.</param>
    /// <param name="context">The caller's syntax node.</param>
    /// <returns>The proof result after identity-preserving substitution.</returns>
    public ConstraintProof Prove(BoundConstraint constraint, Koto binder, ReadOnlySpan<BoundType?> arguments, Koto context)
        => this.ProveConstraint(this.SubstituteConstraint(constraint, new CallSubstitution(binder, arguments)), this.ConstraintScope(context));

    // The scope a node's names and premises are judged in: its nearest enclosing scope, where a match arm's body is in its pattern's
    // scope (SPEC 14.8.2) and a standard witness bridge has its conformance path's premises (SPEC 11.9.2).
    internal BindingScope ConstraintScope(Koto node)
    {
        Koto? child = null;
        for (Koto? current = node; current is not null; child = current, current = current.Parent)
        {
            if (current is FunctionKoto { IsPropertyWitness: true, BoundSymbol.Scope: { } bridge })
            {
                return bridge; // SPEC 11.9.2: a standard witness bridge's premises are its conformance path's (PropertyWitnessFunction).
            }

            if ((current is MatchKoto match && child is not null ? this.ArmScope(match, child) : null) is { } armScope)
            {
                return this.NodeScope(node, armScope);
            }

            if (this.scopes.TryGetValue(current, out var scope))
            {
                return this.NodeScope(node, scope);
            }
        }

        return this.ModuleScope(node);
    }

    private static ConstraintProof NegateProof(ConstraintProof value) => value switch
    {
        ConstraintProof.Proven => ConstraintProof.Refuted,
        ConstraintProof.Refuted => ConstraintProof.Proven,
        _ => value,
    };

    private static ConstraintProof CombineProof(ConstraintProof left, ConstraintProof right, bool conjunction)
    {
        if (left == ConstraintProof.Error || right == ConstraintProof.Error)
        {
            return ConstraintProof.Error;
        }

        var decisive = conjunction ? ConstraintProof.Refuted : ConstraintProof.Proven;
        var both = conjunction ? ConstraintProof.Proven : ConstraintProof.Refuted;
        return left == decisive || right == decisive ? decisive : left == both && right == both ? both : ConstraintProof.Unknown;
    }

    private static bool IsSelfConstraint(IsKoto clause)
        => clause.Left is IdentifierNameKoto { IdentifierName: "Self" };

    // SPEC 7.5, 8.5.2: the base of a subject's projection chain (`T` of `T.(C).X.(D).Y`), below transparent wrappers; the subject itself
    // when it is no projection. Its published symbol is the root of a subject that formed no Type.
    private static Koto SubjectBase(Koto subject)
    {
        var node = UnwrapTransparentType(subject)!;
        while (node is MemberAccessKoto projection)
        {
            node = UnwrapTransparentType(projection.Left)!;
        }

        return node;
    }

    private static bool IsSelfRequirement(BoundConstraint constraint)
        => (constraint.Contract is { } contract && IntrinsicContract.Of(contract.Intrinsic).SelfRequirement) ||
        (constraint.Kind == ConstraintKind.Not && constraint.Left?.Contract?.Intrinsic == IntrinsicKind.ObjectPayload);

    // SPEC 14.8.2: the scope of the arm of `match` whose body is `body`, its pattern's; null when `body` is no arm's body.
    private BindingScope? ArmScope(MatchKoto match, Koto body)
        => KotoHelper.IsSelectionBody(match, body) ? this.scopes.GetValueOrDefault(match.Arms[body.SelectionArm].Pattern) : null;

    // Whether a Type depends on a generic input; a Contract's Self counts only with `contractSelf`, since a Contract clause on
    // Self is an obligation of every conformer rather than a closed proposition. An existence walk: each distinct part visited once
    // (DECISIONS BU-19, BU-55).
    private bool DependentType(BoundType type, bool unresolvedProjection = true, bool contractSelf = false, bool includeOrigins = true)
    {
        var query = this.visitedParts.Begin(this.storageVersion);
        return Dependent(type);

        bool Dependent(BoundType part)
        {
            if ((part.Kind == BoundTypeKind.Parameter && (contractSelf || !IsContractSelf(part))) ||
                part.Kind is BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.SemanticsAdaptation ||
                (unresolvedProjection && part.Kind == BoundTypeKind.AssociatedProjection) || part.LengthExpression is not null ||
                (includeOrigins && part.Origin is not null && part.Origin.Kind != OriginKind.Static))
            {
                return true;
            }

            foreach (var length in part.LengthArguments)
            {
                if (length is { IsConstant: false })
                {
                    return true;
                }
            }

            for (var i = 0; i < part.OriginArguments.Count; i++)
            {
                if (includeOrigins && part.OriginArguments[i].Kind != OriginKind.Static)
                {
                    return true;
                }
            }

            for (var i = 0; i < part.Components.Count; i++)
            {
                if (this.FirstVisit(part.Components[i], query) && Dependent(part.Components[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private bool IsDependentConstraint(IsKoto clause)
        => !IsSelfConstraint(clause) && clause.BoundConstraint is { } constraint && this.DependentConstraint(constraint);

    private bool DependentConstraint(BoundConstraint constraint, bool contractSelf = false)
        => (constraint.Subject is { } subject && this.DependentType(subject, unresolvedProjection: false, contractSelf)) ||
        (constraint.RequiredType is { } required && this.DependentType(required, unresolvedProjection: false, contractSelf)) ||
        (constraint.Left is { } left && this.DependentConstraint(left, contractSelf)) ||
        (constraint.Right is { } right && this.DependentConstraint(right, contractSelf));

    private ConstraintProof JudgeConstraintAtom(BoundConstraint proposition, BindingScope scope)
    {
        var contexts = this.OperandContexts(proposition);
        if (contexts.Invalid)
        {
            return ConstraintProof.Error;
        }

        if (contexts.Unresolved)
        {
            return ConstraintProof.Unknown;
        }

        if (proposition.Kind == ConstraintKind.TypeIdentity)
        {
            if (this.SameCompleteType(proposition.Subject!, proposition.RequiredType!))
            {
                return ConstraintProof.Proven;
            }

            return this.DependentType(proposition.Subject!) || this.DependentType(proposition.RequiredType!)
                ? ConstraintProof.Unknown : ConstraintProof.Refuted;
        }

        if (proposition.Kind == ConstraintKind.Callable)
        {
            var subject = proposition.Subject!;
            var closure = BoundClosure.Of(subject);
            var signature = this.ClosureSignature(subject) ?? this.FunctionItemSignature(subject) ?? (subject.Kind == BoundTypeKind.Function ? subject : null);
            if (signature is null)
            {
                return this.DependentType(subject) ? ConstraintProof.Unknown : ConstraintProof.Refuted;
            }

            var receiver = closure?.Receiver ?? SemanticsKind.Ref;
            if (!this.permitClosureReceivers && !CallableReceiverFits(receiver, proposition.Mask))
            {
                return ConstraintProof.Refuted;
            }

            // SPEC 15.6.1, 8.3: once the structural part of the whole-contract comparison holds, its Origin part is Proven or Unknown,
            // never Refuted (CallableOriginFailure explains it). A required signature over an open region is decided by the per-call
            // stand-in instead (SPEC 15.3.6, PerCallCallables). A Function Item's Origin conditions, its clauses and result premises, are
            // proven from the required contract and belong to that Origin part (SPEC 15.3.7).
            // The Unknown's comparison failure joins the proof as its U fact (design-r2 Amendment 4).
            var failure = default(ComparisonFailure);
            if (subject.Kind == BoundTypeKind.FunctionItem ? this.ItemContractFits(subject, signature, proposition.RequiredType!, scope.Owner, out failure)
                : this.CallableSignatureFits(signature, proposition.RequiredType!, SignatureOwner(subject), scope.Owner, out failure))
            {
                return ConstraintProof.Proven;
            }

            if (!ReferenceTypes.StorageMatches(proposition.RequiredType, signature) || HasOpenOrigin(proposition.RequiredType!))
            {
                return ConstraintProof.Refuted;
            }

            this.ScopeReads.Unknown ??= new(subject, proposition.RequiredType!, failure) { Epoch = this.frameEpoch };
            return ConstraintProof.Unknown;
        }

        if (proposition.Kind == ConstraintKind.Semantics)
        {
            var subject = proposition.Subject!;
            if (subject.IsAbstract)
            {
                // SPEC 8.3: a requirement on a Semantics binding is decided by containment of its admitted set.
                var admitted = this.ResultSemantics(subject, scope);
                return admitted == SemanticsMask.None ? ConstraintProof.Error :
                    (admitted & ~proposition.Mask) == 0 ? ConstraintProof.Proven :
                    (admitted & proposition.Mask) == 0 ? ConstraintProof.Refuted : ConstraintProof.Unknown;
            }

            return proposition.Mask.Contains(subject.Semantics) ? ConstraintProof.Proven : ConstraintProof.Refuted;
        }

        if (proposition.Contract is { } intrinsic && IntrinsicContract.Of(intrinsic.Intrinsic).Judged)
        {
            return this.RequestCapability(proposition.Subject!, intrinsic, scope);
        }

        // Registration is not verified conformance. In particular, absence is not refutation.
        return proposition.Contract is { } contract ? this.ProveConformance(proposition.Subject!, contract, scope) : ConstraintProof.Unknown;
    }

    private BoundConstraint InternConstraint(ConstraintKey key)
    {
        if (!this.constraints.TryGetValue(key, out var result))
        {
            result = new(key, this.ContainsAssociatedProjection(key.Subject) || this.ContainsAssociatedProjection(key.RequiredType));
            this.constraints.Add(key, result);
        }

        return result;
    }

    private BoundConstraint NegateConstraint(BoundConstraint value)
        => value.Kind == ConstraintKind.Not ? value.Left! : value.Negation ??= this.InternConstraint(new(ConstraintKind.Not, left: value));

    private void BindConstraints()
    {
        this.ResetSourceStates();
        this.deferredConstraints.Clear();
        this.bindingConstraintTypes = true;
        // Collect ordinary facts, then complete outer Type/Contract constraints, then P.
        // Dependent function constraints may use P only through their declaration scope.
        for (var pass = 0; pass < 3; pass++)
        {
            if (pass == 2)
            {
                this.BindConditionalDeclarations(true);
            }

            for (var i = 0; i < this.nodes.Count; i++)
            {
                var node = this.nodes[i];
                if (node is FunctionKoto function && pass != 1)
                {
                    for (var j = 0; j < function.TypeConstraints.Count; j++)
                    {
                        var clause = (IsKoto)function.TypeConstraints[j];
                        if (IsRecovery(clause, out _) || this.DeferredConstraint(clause, this.scopes[function]) == (pass != 0))
                        {
                            this.BindConstraint(clause, this.scopes[function]);
                        }
                    }
                }
                else if (node is DeclarationContainerKoto container && pass != 2)
                {
                    // SPEC 8.4, Appendix D: a Contract declares Type parameters; Contract-owned Origin parameters are not introduced.
                    if (container is ContractKoto && container.OriginNames.Count != 0)
                    {
                        this.Fail(container, BindingFailure.InvalidConstraint);
                    }

                    for (var j = 0; j < container.ConstraintNodes.Count; j++)
                    {
                        var clause = container.ConstraintNodes[j];
                        if (IsRecovery(clause, out _) || this.DeferredConstraint(clause, this.scopes[container]) == (pass != 0))
                        {
                            this.BindConstraint(clause, this.scopes[container]);
                        }
                    }
                }
            }

            if (pass == 0)
            {
                this.RegisterConformances();
                this.BindCopyDeclarations();
                this.BindConditionalDeclarations(false);
                this.RegisterInheritedConformances();
            }
        }

        this.BindConditionalAssociatedSpecifications();

        // SPEC 8.5.1, 8.5.3: every owner's specifications are its members (Parser.ParseAssociatedTypeItem), a Contract's included.
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is StructKoto or EnumKoto or ContractKoto)
            {
                var owner = (DeclarationContainerKoto)this.nodes[i];
                for (var j = 0; j < owner.Members.Count; j++)
                {
                    if (owner.Members[j] is IsKoto { IsAssociatedConstraint: true } clause)
                    {
                        this.BindAssociatedSpecification(clause, this.scopes[owner]);
                    }
                }
            }
        }

        this.ExpandContractPremises();
        this.PublishSourceStates();
        this.PrepareAssociatedBindings();
        this.bindingConstraintTypes = false;
        this.ValidateConstraintProjectionAccess();
    }

    // A clause waits for the conformances when it names an associated projection (SPEC 8.5, 15.3.1), except a conformance declaration
    // (DeclaresConformance): RegisterConformances registers it after pass 0, so it binds in pass 0 (conformancesRegistered). Each clause is
    // decided once per Bind run, before any Constraint binds: the query's TypeName lookups publish import and qualifier facts as every
    // lookup does, and a lookup through a base clause not ready yet is an early read (TypeName's note) that VerifyEarlyReads evaluates
    // again, running the declarations once more on a different answer, so the decision cannot disagree with the final names
    // (Binding.BaseLayers).
    private bool DeferredConstraint(IsKoto clause, BindingScope scope)
    {
        if (!this.deferredConstraints.TryGetValue(clause, out var deferred))
        {
            deferred = !DeclaresConformance(clause) && (clause.IsAssociatedConstraint || this.ContainsProjection(clause.Left, scope) || this.ContainsProjection(clause.Right, scope));
            this.deferredConstraints.Add(clause, deferred);
        }

        return deferred;
    }

    // SPEC 8.5, 15.3.1: whether a requirement names an associated projection anywhere: a PathSuffix that names no declared Type, under
    // the requirement's operators (a group, not, and, or: BindRequirement's) or in any Type component a Type node publishes. It decides by
    // name lookup over the written Type's components, never by the top node's shape.
    private bool ContainsProjection(Koto node, BindingScope scope)
    {
        switch (node)
        {
            // Each PathSuffix is asked in its position's row (UseRow), the row its Type former reads (R7-N1e2-1R).
            case MemberAccessKoto when this.TypeName(node, scope) is null:
                return true;
            case ParenthesizedKoto group:
                return this.ContainsProjection(group.Operand, scope);
            case UnaryKoto { Akind: KotoKind.Not } not:
                return this.ContainsProjection(not.Operand, scope);
            case BinaryKoto { Akind: KotoKind.And or KotoKind.Or } binary:
                return this.ContainsProjection(binary.Left, scope) || this.ContainsProjection(binary.Right, scope);
        }

        for (var i = 0; i < node.TypeComponentCount; i++)
        {
            if (this.ContainsProjection(node.TypeComponent(i), scope))
            {
                return true;
            }
        }

        return false;
    }

    // A later run judges the source environments only: the Kimi library's environments are fixed and valid, and the first run
    // already judged them.
    private bool ValidateConstraintEnvironments(BindingMode mode, bool sourcesOnly = false)
    {
        var changed = false;
        foreach (var scope in this.scopes.Values)
        {
            if (scope.Constraints is not { } environment || (sourcesOnly && this.IsLibraryDeclaration(scope.Owner)))
            {
                continue;
            }

            if (!environment.Invalid && !this.ValidateArithmeticDirections(scope, environment))
            {
                environment.Fail(scope.Owner); // ValidateArithmeticDirections failed the owner.
                changed = true;
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                // The environment's judgment is its owner's, the node its record names: the proof runs in the owner's frame.
                this.failedCause = null;
                var frame = this.BeginConsultation(scope.Owner);
                var proof = this.ProveConstraint(fact, scope);
                if (proof != ConstraintProof.Error)
                {
                    this.DropConsultation(frame);
                    continue;
                }

                var previous = scope.Owner.BindingState;
                changed |= !environment.Invalid;
                environment.Fail(scope.Owner);
                // Other input facts can have independent contradictions. Record a
                // sole missing-name cause only when this is the entire environment.
                this.RequireConstraintInFrame(scope.Owner, proof, mode, frame, environment.Facts.Count == 1 ? this.FindConformanceDiagnosticCause(scope.Owner, fact) : null);
                changed |= previous != scope.Owner.BindingState;
                break;
            }
        }

        return changed;
    }

    private void BindConstraint(IsKoto clause, BindingScope scope)
    {
        if (IsRecovery(clause, out _))
        {
            // Every entry path, including conditional premises, depends on the clause's complete syntax.
            clause.BoundConstraint = this.InternConstraint(new(ConstraintKind.Error));
            this.Fail(clause, BindingFailure.InvalidConstraint);
            if (IsRecovery(clause.Left, out _))
            {
                // A rejected subject (SPEC F.6) supplies none, and the requirement's own records stay (SPEC 23.1.2), as a failed selector's below.
                this.BindRequirement(clause.Right, BoundType.Unit, false, this.NodeScope(clause, scope));
            }

            return;
        }

        if (clause.FormationType is not null && !IsAssociatedRequirement(clause))
        {
            this.Fail(clause, BindingFailure.InvalidConstraint); // SPEC 8.5: only a requirement may end with a formation Type.
            return;
        }

        var bindingScope = this.NodeScope(clause, scope);
        var subjectProjections = this.projectionUses.Count; // The projection uses the subject's binding registers: from here to subjectEnd.
        var symbol = this.TypeName(clause.Left, bindingScope); // The subject's one lookup (Requirement, SPEC 8.2.1).
        var semantics = symbol?.Kind == BindingSymbolKind.SemanticsParameter;
        var subject = semantics ? symbol!.Pair!.WholeType : symbol?.Kind == BindingSymbolKind.SemanticsTarget ? symbol.Type : this.BindType(clause.Left, bindingScope);
        clause.Left.BoundSymbol = symbol ?? clause.Left.BoundSymbol;
        Complete(clause.Left, subject);
        var subjectEnd = this.projectionUses.Count; // The requirement's own projection uses come after it and stay its own (SPEC 23.1.2).
        // SPEC 23.1.2: a subject that formed no Type because of a failure in it, its own or a part's (the published HasFailureCauses),
        // supplies none: the requirement binds against no subject, and a rejecting verdict rests on the subject.
        var failedSubject = subject is null &&
            (clause.BindingFailure != BindingFailure.None || clause.Left.BindingState == BindingState.Invalid || this.HasFailureCauses(clause.Left));
        // Closed subjects are valid in a struct's, an enum's and, Self apart, a Contract's region.
        var closedScope = scope.Owner is StructKoto or EnumKoto || (scope.Owner is ContractKoto && !IsSelfConstraint(clause));
        // A subject that formed nothing for a missing name is pending when a later source can complete it into a subject valid here,
        // as a formed one is below: its root (RootedInput) is the base its projection chain published (SPEC 7.5, 8.8.1: `T.(D).Item`,
        // D missing), and whether it is closed is not known yet, so any subject is pending where closed subjects are valid. A root no
        // source can make an input (`U is Copy` in a function) is decided now.
        var pendingRoot = subject is null ? SubjectBase(clause.Left).BoundSymbol : null;
        var unresolvedSubject = subject is null && this.HasUnresolvedConstraintSyntax(clause.Left) &&
            (RootedInput(pendingRoot is { IsGenericInput: true }, pendingRoot) || closedScope);
        // A failed selector supplies no subject. Check the independent requirement syntax, then discard the provisional
        // proposition; its valid Type or Contract must not become another failure merely because the selector failed.
        var requirement = this.BindRequirement(clause.Right, unresolvedSubject || failedSubject ? BoundType.Unit : subject, semantics, bindingScope);
        if (AssociatedHead(clause) is OriginApplicationKoto && HasUnsupportedAssociatedIdentity(requirement))
        {
            this.Fail(clause, BindingFailure.Unsupported, true);
            return;
        }

        if (AssociatedHead(clause) is OriginApplicationKoto && subject?.Symbol is { Kind: BindingSymbolKind.AssociatedType } associated)
        {
            requirement = this.CanonicalAssociatedOrigins(requirement, clause, this.AssociatedParameters(associated.Declaration));
        }

        if (unresolvedSubject && requirement.Kind != ConstraintKind.Error)
        {
            // Validate the available requirement syntax without inventing a subject identity.
            requirement = this.InternConstraint(new(ConstraintKind.Unresolved));
        }

        var root = subject;
        while (root?.Kind == BoundTypeKind.AssociatedProjection)
        {
            root = root.Components[0];
        }

        // Only propositions dependent on generic inputs or the conforming Self are premises.
        // Closed propositions are independently checked declaration obligations.
        // SPEC 8.7.3, 8.7: inside a Contract, `Self is K` for the table's Self requirements and `Self is not ObjectPayload` are
        // Self-dependent implementation requirements: premises for the Contract's own signatures, derived for every Type
        // that the Contract is available for, and obligations of every conformer.
        var contractSelfClause = scope.Owner is ContractKoto && IsSelfConstraint(clause) && IsSelfRequirement(requirement);
        var input = symbol is { IsGenericInput: true } || (subject?.Kind == BoundTypeKind.AssociatedProjection && root?.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection);
        input |= scope.Owner is StructKoto or EnumKoto && !IsSelfConstraint(clause) && this.DependentConstraint(requirement);
        input |= contractSelfClause;
        var closedSubject = closedScope && subject is not null && !this.DependentType(subject, unresolvedProjection: false);
        var validSubject = subject is not null && (RootedInput(input, root?.Symbol ?? symbol) || closedSubject ||
            (IsSelfConstraint(clause) && scope.Owner is DeclarationContainerKoto and not (GroupKoto or ContractKoto)));
        if (!validSubject && !unresolvedSubject)
        {
            requirement = this.InternConstraint(new(ConstraintKind.Error));
            if (failedSubject)
            {
                // SPEC 23.1.2: the clause's verdict reads its failed subject directly; the subject's record is the one.
                this.AddPrerequisite(clause, clause.Left);
            }
            else
            {
                // A projection of the formed subject is proved in the environment this verdict makes invalid: it rests on the clause.
                for (var i = subjectProjections; i < subjectEnd; i++)
                {
                    this.AddPendingPrerequisite(this.projectionUses[i].Use, clause);
                }
            }

            this.Fail(clause, BindingFailure.InvalidConstraint);
        }

        clause.BoundConstraint = requirement;
        Complete(clause, BoundType.Boolean);
        if (input || requirement.Kind == ConstraintKind.Error)
        {
            var environment = scope.Constraints ??= new();
            if (this.AddConstraintFact(environment, requirement))
            {
                environment.Fail(clause); // The clause failed, or holds a recovery.
            }

            if (requirement.Kind == ConstraintKind.Error && clause.BindingFailure != BindingFailure.None)
            {
                this.AddPrerequisite(scope.Owner, clause);
            }
        }

        // A subject's root valid here, the one statement for a formed subject and a pending one: an input, and (SPEC 7.5) in a function
        // its own parameter or one of its declaring Type, the Container whose scope the member is declared in (Indexing; a conditional
        // block's member included), never a local function's enclosing function. A closed subject is the other valid subject, where
        // closed subjects are valid (closedScope: never a function).
        bool RootedInput(bool input, BindingSymbol? rootSymbol)
            => input && (scope.Owner is not FunctionKoto member || ReferenceEquals(rootSymbol?.Scope, scope) ||
                (rootSymbol?.Scope is { Owner: DeclarationContainerKoto } declaring && ReferenceEquals(declaring, member.BoundSymbol?.Scope)));

        // Self clauses remain implementation obligations, never assumptions.
    }

    // Adds a fact and its conjuncts; true when one of them is an Error. BindConstraints and BindAssociatedSpecification name their clause
    // as the environment's failed node. The witness scope (ContractMatching), an inherited path's scope (InheritedConformances) and a Contract's premises
    // (ContractSolver) can add one too, a failed clause's Error passed through or a null substitution, and name no node, so such an
    // environment stays valid (PLAN G651's open item).
    private bool AddConstraintFact(ConstraintEnvironment environment, BoundConstraint fact, BindingSymbol? source = null)
    {
        if (fact.HasUnresolved)
        {
            return false;
        }

        environment.Add(fact, source);
        if (fact.Kind == ConstraintKind.And)
        {
            return this.AddConstraintFact(environment, fact.Left!, source) | this.AddConstraintFact(environment, fact.Right!, source);
        }

        return fact.Kind == ConstraintKind.Error;
    }

    private ConstraintProof ProveProposition(BoundConstraint proposition, BindingScope scope)
    {
        var normalized = this.NormalizeProofConstraint(proposition, scope, out _);
        var evidence = this.Supply(normalized, this.ViewOf(scope));
        if (evidence.Invalid is { } invalid)
        {
            return this.InvalidEnvironment(invalid);
        }

        var positive = evidence.Positive;
        var negative = evidence.Negative;

        // Always validate both operands, even when an exact compound assumption is available.
        var structural = proposition.Kind switch
        {
            ConstraintKind.Error => ConstraintProof.Error,
            ConstraintKind.Unresolved => ConstraintProof.Unknown,
            ConstraintKind.Not => NegateProof(this.ProveConstraint(proposition.Left!, scope)),
            ConstraintKind.And => CombineProof(this.ProveConstraint(proposition.Left!, scope), this.ProveConstraint(proposition.Right!, scope), true),
            ConstraintKind.Or => CombineProof(this.ProveConstraint(proposition.Left!, scope), this.ProveConstraint(proposition.Right!, scope), false),
            _ => !ReferenceEquals(proposition, normalized) && this.JudgeConstraintAtom(proposition, scope) == ConstraintProof.Error ? ConstraintProof.Error : this.JudgeConstraintAtom(normalized, scope),
        };
        if (structural != ConstraintProof.Error && normalized.Kind is ConstraintKind.And or ConstraintKind.Or or ConstraintKind.Not &&
            ClauseSubject(normalized) is { } whole && SemanticsPremise(normalized, whole))
        {
            // Universal case evidence must judge the whole set: neither disjunct alone need cover every case.
            structural = this.JudgeConstraintAtom(this.InternConstraint(new(ConstraintKind.Semantics, whole, mask: SemanticsSet(normalized))), scope);
        }

        positive |= structural == ConstraintProof.Proven;
        negative |= structural == ConstraintProof.Refuted;
        if (structural != ConstraintProof.Error && (positive || negative) && this.OperandContexts(proposition).Unresolved)
        {
            return ConstraintProof.Unknown;
        }

        return structural == ConstraintProof.Error || (positive && negative) ? ConstraintProof.Error : positive ? ConstraintProof.Proven : negative ? ConstraintProof.Refuted : ConstraintProof.Unknown;
    }

    // W1 (ContractSolver's Completion): Complete with nothing to normalize, else the meet of the parts'; a Callable fact's required
    // Type (CallableContractType) is Provisional.
    private BoundConstraint NormalizeProofConstraint(BoundConstraint constraint, BindingScope scope, out Completion completion)
    {
        completion = Completion.Complete;
        if (!constraint.HasAssociatedProjection && !HasParameterIdentity(scope))
        {
            return constraint;
        }

        if (constraint.Kind == ConstraintKind.Not)
        {
            return this.NegateConstraint(this.NormalizeProofConstraint(constraint.Left!, scope, out completion));
        }

        if (constraint.Kind is ConstraintKind.And or ConstraintKind.Or)
        {
            var left = this.NormalizeProofConstraint(constraint.Left!, scope, out var a);
            var right = this.NormalizeProofConstraint(constraint.Right!, scope, out var b);
            completion = Meet(a, b);
            return ReferenceEquals(left, constraint.Left) && ReferenceEquals(right, constraint.Right) ? constraint : this.InternConstraint(new(constraint.Kind, left: left, right: right));
        }

        if (constraint.Subject is null)
        {
            return constraint;
        }

        var subject = this.NormalizeProofType(constraint.Subject, scope, out completion);
        var required = constraint.RequiredType;
        if (required is not null)
        {
            var part = Completion.Provisional;
            required = constraint.Kind == ConstraintKind.Callable ? this.CallableContractType(required, scope) : this.NormalizeProofType(required, scope, out part);
            completion = Meet(completion, part);
        }

        return ReferenceEquals(subject, constraint.Subject) && ReferenceEquals(required, constraint.RequiredType) ? constraint : this.InternConstraint(new(constraint.Kind, subject, required, constraint.Contract, constraint.Mask));
    }

    private BoundType NormalizeProofType(BoundType type, BindingScope scope, out Completion completion)
    {
        completion = Completion.Complete;
        if (type.ContainsParameter && HasParameterIdentity(scope))
        {
            type = this.SubstituteIdentity(type, scope, parametersOnly: true, out completion);
        }

        if (type.Kind == BoundTypeKind.AssociatedProjection)
        {
            var projected = this.ContractTypeAnswer(type, scope);
            completion = Meet(completion, projected.Completion);
            return projected.Image;
        }

        // Keep symbolic subjects symbolic: share facts within a parameter equivalence class, but do not turn a
        // concrete-Type assumption into an extra proof/refutation rule. Associated identities are reduced as before.
        var count = type.Components.Count;
        if (count == 0)
        {
            return type;
        }

        var components = this.RentTypes(count);
        try
        {
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                components[i] = this.NormalizeProofType(type.Components[i], scope, out var part);
                completion = Meet(completion, part);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            return changed ? this.types.Rebuild(type, components.AsSpan(0, count), type.Origin, (BoundOrigin[])type.OriginArguments) : type;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    private bool HasUnresolvedConstraintSyntax(Koto node)
    {
        if (this.capabilityMode != BindingMode.Provisional)
        {
            return false;
        }

        this.constraintSyntaxVisitor ??= new();
        this.constraintSyntaxVisitor.Missing = false;
        this.constraintSyntaxVisitor.Invalid = false;
        this.constraintSyntaxVisitor.Visit(node);
        return this.constraintSyntaxVisitor.Missing && !this.constraintSyntaxVisitor.Invalid;
    }

    private sealed class ConstraintSyntaxVisitor : KotoVisitor
    {
        internal bool Missing { get; set; }

        internal bool Invalid { get; set; }

        public override void Visit(Koto node)
        {
            // A missing name is pending whatever the role excluded (SPEC 9.2, 9.5): a declaration that is no candidate decides nothing.
            this.Missing |= node.BindingState == BindingState.Unresolved && node.BindingFailure is BindingFailure.MissingType or BindingFailure.MissingName;
            this.Invalid |= node.BindingState == BindingState.Invalid;
            node.VisitChildren(this);
        }
    }
}
