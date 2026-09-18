// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<ConstraintKey, BoundConstraint> constraints = new();
    private readonly HashSet<BindingSymbol> contractPremiseQueries = new(ReferenceEqualityComparer.Instance);
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
        => this.ProveConstraint(this.SubstituteConstraint(constraint, binder, arguments), this.ConstraintScope(context));

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

    private static bool DependentType(BoundType type, bool unresolvedProjection = true, bool contractSelf = false)
    {
        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication || (unresolvedProjection && type.Kind == BoundTypeKind.AssociatedProjection) || (contractSelf && type.Symbol?.Declaration is ContractKoto) || type.LengthExpression is not null || (type.Origin is not null && type.Origin.Kind != OriginKind.Static))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (type.OriginArguments[i].Kind != OriginKind.Static)
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (DependentType(type.Components[i], unresolvedProjection, contractSelf))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSelfConstraint(IsKoto clause)
    {
        var subject = clause.Left;
        while (subject is ParenthesizedTypeKoto parentheses)
        {
            subject = parentheses.Type;
        }

        return subject is IdentifierNameKoto { IdentifierName: "Self" } or TypeSemanticsKoto { Type: null, Identifier: "Self", OriginName: null, OriginExpression: null, OriginArguments: null };
    }

    private static bool IsDependentConstraint(IsKoto clause)
        => !IsSelfConstraint(clause) && clause.BoundConstraint is { } constraint && DependentConstraint(constraint);

    private static bool DependentConstraint(BoundConstraint constraint, bool contractSelf = false)
        => (constraint.Subject is { } subject && DependentType(subject, unresolvedProjection: false, contractSelf)) ||
        (constraint.RequiredType is { } required && DependentType(required, unresolvedProjection: false, contractSelf)) ||
        (constraint.Left is { } left && DependentConstraint(left, contractSelf)) ||
        (constraint.Right is { } right && DependentConstraint(right, contractSelf));

    private static bool InvalidConstraintType(BoundType type)
    {
        if (type.Symbol is { } symbol && InvalidDeclarationContext(symbol.Declaration))
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (InvalidConstraintType(type.Components[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UnresolvedConstraintType(BoundType type)
    {
        if (type.Symbol is { } symbol && UnresolvedTypeDeclarationContext(symbol.Declaration))
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (UnresolvedConstraintType(type.Components[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UnresolvedConstraintOperand(BoundConstraint constraint)
        => (constraint.Subject is { } subject && UnresolvedConstraintType(subject)) ||
        (constraint.RequiredType is { } required && UnresolvedConstraintType(required)) ||
        (constraint.Contract is { } contract && UnresolvedTypeDeclarationContext(contract.Declaration));

    private bool AvailableContractPremise(BindingSymbol contract)
    {
        // Ancestors is already the deduplicated transitive closure. Recurse only through
        // actual closed obligations, not through that flattened refinement list again.
        var ancestors = contract.Contract?.Ancestors;
        for (var i = -1; i < (ancestors?.Count ?? 0); i++)
        {
            var source = i < 0 ? contract : ancestors![i];
            if (InvalidDeclarationContext(source.Declaration) || UnresolvedTypeDeclarationContext(source.Declaration) || !this.contractPremiseQueries.Add(source))
            {
                return false;
            }

            try
            {
                if (this.CheckClosedDeclarationConstraints((DeclarationContainerKoto)source.Declaration) != ConstraintProof.Proven)
                {
                    return false;
                }
            }
            finally
            {
                this.contractPremiseQueries.Remove(source);
            }
        }

        return true;
    }

    private bool AvailableConstraintFact(ConstraintEnvironment environment, BoundConstraint fact)
    {
        if (!environment.Facts.Contains(fact))
        {
            return false;
        }

        if (environment.DirectFacts.Contains(fact))
        {
            return true;
        }

        for (var i = 0; i < environment.DerivedFacts.Count; i++)
        {
            var derived = environment.DerivedFacts[i];
            if (ReferenceEquals(derived.Fact, fact) && this.AvailableContractPremise(derived.Source))
            {
                return true;
            }
        }

        return false;
    }

    private ConstraintProof JudgeConstraintAtom(BoundConstraint proposition, BindingScope scope)
    {
        if (InvalidConstraintType(proposition.Subject!) || (proposition.RequiredType is { } required && InvalidConstraintType(required)) || (proposition.Contract is { } contractSymbol && InvalidDeclarationContext(contractSymbol.Declaration)))
        {
            return ConstraintProof.Error;
        }

        if (UnresolvedConstraintOperand(proposition))
        {
            return ConstraintProof.Unknown;
        }

        if (proposition.Kind == ConstraintKind.TypeIdentity)
        {
            if (ReferenceEquals(proposition.Subject, proposition.RequiredType))
            {
                return ConstraintProof.Proven;
            }

            return DependentType(proposition.Subject!) || DependentType(proposition.RequiredType!) ? ConstraintProof.Unknown : ConstraintProof.Refuted;
        }

        if (proposition.Kind == ConstraintKind.Callable)
        {
            var subject = proposition.Subject!;
            var closure = subject.Kind == BoundTypeKind.Closure ? (subject.Symbol?.Declaration as FunctionKoto)?.BoundClosure : null;
            var signature = closure?.Signature ?? (subject.Kind == BoundTypeKind.Function ? subject : null);
            if (signature is null)
            {
                return DependentType(subject) ? ConstraintProof.Unknown : ConstraintProof.Refuted;
            }

            var receiver = closure?.Receiver ?? SemanticsKind.Ref;
            return CallableSignatureFits(signature, proposition.RequiredType!) &&
                (receiver == SemanticsKind.Ref || proposition.Mask == SemanticsMask.Owner || (receiver == SemanticsKind.Uniq && proposition.Mask == SemanticsMask.Uniq))
                ? ConstraintProof.Proven : ConstraintProof.Refuted;
        }

        if (proposition.Kind == ConstraintKind.Semantics)
        {
            if (proposition.Subject!.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication)
            {
                return ConstraintProof.Unknown;
            }

            return proposition.Mask.Contains(proposition.Subject.Semantics) ? ConstraintProof.Proven : ConstraintProof.Refuted;
        }

        if (proposition.Contract is { Intrinsic: IntrinsicKind.Copy or IntrinsicKind.Owned or IntrinsicKind.Sealed } intrinsic)
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
            result = new(key);
            this.constraints.Add(key, result);
        }

        return result;
    }

    private BoundConstraint NegateConstraint(BoundConstraint value)
        => value.Kind == ConstraintKind.Not ? value.Left! : value.Negation ??= this.InternConstraint(new(ConstraintKind.Not, left: value));

    private BindingScope ConstraintScope(Koto node)
    {
        for (Koto? current = node; current is not null; current = current.Parent)
        {
            if (this.scopes.TryGetValue(current, out var scope))
            {
                return this.NodeScope(node, scope);
            }
        }

        return this.ModuleScope(node);
    }

    private void BindConstraints()
    {
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
                        if (this.DeferredConstraint(clause, this.scopes[function]) == (pass != 0))
                        {
                            this.BindConstraint(clause, this.scopes[function]);
                        }
                    }
                }
                else if (node is DeclarationContainerKoto container && pass != 2)
                {
                    if (container is ContractKoto && (container.GenericParameterNodes.Count != 0 || container.OriginNames.Count != 0))
                    {
                        Fail(container, BindingFailure.InvalidConstraint);
                    }

                    for (var j = 0; j < container.ConstraintNodes.Count; j++)
                    {
                        var clause = container.ConstraintNodes[j];
                        if (this.DeferredConstraint(clause, this.scopes[container]) == (pass != 0))
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
            }
        }

        this.BindConditionalAssociatedSpecifications();

        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is IsKoto { IsAssociatedConstraint: true, Parent: StructKoto or EnumKoto } clause)
            {
                this.BindAssociatedSpecification(clause, this.scopes[clause.Parent!]);
            }
        }

        this.ExpandContractPremises();
        this.PrepareAssociatedBindings();
        this.bindingConstraintTypes = false;
        this.ValidateConstraintProjectionAccess();
    }

    private bool DeferredConstraint(IsKoto clause, BindingScope scope)
        => clause.IsAssociatedConstraint || clause.Left is MemberAccessKoto || this.ContainsProjection(clause.Right, scope);

    private bool ContainsProjection(Koto node, BindingScope scope)
        => node is MemberAccessKoto ? this.TypeName(node, scope, false) is null : (node is UnaryKoto unary && this.ContainsProjection(unary.Operand, scope)) || (node is BinaryKoto binary && (this.ContainsProjection(binary.Left, scope) || this.ContainsProjection(binary.Right, scope)));

    private bool ValidateConstraintEnvironments()
    {
        var changed = false;
        foreach (var scope in this.scopes.Values)
        {
            if (scope.Constraints is not { } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (this.ProveConstraint(fact, scope) == ConstraintProof.Error)
                {
                    var previous = scope.Owner.BindingState;
                    changed |= !environment.Invalid;
                    environment.Invalid = true;
                    // Other input facts can have independent contradictions. Record a
                    // sole missing-name cause only when this is the entire environment.
                    this.FailConstraint(scope.Owner, environment.Facts.Count == 1 ? this.FindConformanceDiagnosticCause(scope.Owner, fact) : null);
                    changed |= previous != scope.Owner.BindingState;
                    break;
                }
            }
        }

        return changed;
    }

    private void BindConstraint(IsKoto clause, BindingScope scope)
    {
        var symbol = this.TypeName(clause.Left, scope, false);
        var semantics = symbol?.Kind == BindingSymbolKind.SemanticsParameter;
        var subject = semantics ? symbol!.Pair!.WholeType : symbol?.Kind == BindingSymbolKind.SemanticsTarget ? symbol.Type : this.BindType(clause.Left, scope);
        clause.Left.BoundSymbol = symbol ?? clause.Left.BoundSymbol;
        Complete(clause.Left, subject);
        var unresolvedSubject = subject is null && scope.Owner is StructKoto or EnumKoto or ContractKoto && this.HasUnresolvedConstraintSyntax(clause.Left, scope);
        var requirement = this.BindRequirement(clause.Right, unresolvedSubject ? BoundType.Unit : subject, semantics, scope);
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
        var input = symbol?.Kind is BindingSymbolKind.TypeParameter or BindingSymbolKind.SemanticsTarget or BindingSymbolKind.SemanticsParameter || (subject?.Kind == BoundTypeKind.AssociatedProjection && (root?.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection || (scope.Owner is ContractKoto && root?.Symbol?.Declaration is ContractKoto)));
        input |= scope.Owner is StructKoto or EnumKoto && !IsSelfConstraint(clause) && DependentConstraint(requirement);
        var closedSubject = (scope.Owner is StructKoto or EnumKoto || (scope.Owner is ContractKoto && !IsSelfConstraint(clause))) && subject is not null && !DependentType(subject, unresolvedProjection: false);
        var validSubject = subject is not null && (scope.Owner is FunctionKoto ? input && ReferenceEquals((root?.Symbol ?? symbol)?.Scope, scope) : input || closedSubject || (IsSelfConstraint(clause) && scope.Owner is DeclarationContainerKoto and not (GroupKoto or ContractKoto)));
        if ((!validSubject && !unresolvedSubject) || (clause.IsAssociatedConstraint && scope.Owner is not ContractKoto))
        {
            requirement = this.InternConstraint(new(ConstraintKind.Error));
            Fail(clause, BindingFailure.InvalidConstraint);
        }

        clause.BoundConstraint = requirement;
        Complete(clause, BoundType.Boolean);
        if (input || requirement.Kind == ConstraintKind.Error)
        {
            var environment = scope.Constraints ??= new();
            this.AddConstraintFact(environment, requirement);
        }

        // Self clauses remain implementation obligations, never assumptions.
    }

    private BoundConstraint BindRequirement(Koto node, BoundType? subject, bool semantics, BindingScope scope)
    {
        BoundConstraint result;
        switch (node)
        {
            case ParenthesizedKoto parentheses:
                result = this.BindRequirement(parentheses.Operand, subject, semantics, scope);
                break;
            case NotKoto not:
                result = this.NegateConstraint(this.BindRequirement(not.Operand, subject, semantics, scope));
                break;
            case AndKoto or OrKoto:
                var binary = (BinaryKoto)node;
                var left = this.BindRequirement(binary.Left, subject, semantics, scope);
                var right = this.BindRequirement(binary.Right, subject, semantics, scope);
                result = this.InternConstraint(new(node is AndKoto ? ConstraintKind.And : ConstraintKind.Or, left: left, right: right));
                break;
            default:
                if (subject is null)
                {
                    result = this.InternConstraint(new(ConstraintKind.Error));
                }
                else if (semantics)
                {
                    var name = node is IdentifierNameKoto identifier ? identifier.IdentifierName : node is TypeSemanticsKoto { Type: null } typeName ? typeName.Identifier : string.Empty;
                    result = SemanticsMaskHelper.TryParse(name, out var mask)
                        ? this.InternConstraint(new(ConstraintKind.Semantics, subject, mask: mask))
                        : this.InternConstraint(new(ConstraintKind.Error));
                }
                else
                {
                    var target = this.TypeName(node, scope, false);
                    if (target?.Declaration is ContractKoto)
                    {
                        if (target.Intrinsic == IntrinsicKind.Callable)
                        {
                            result = this.BindCallableRequirement(node, subject, scope, target);
                            break;
                        }

                        target = this.BindContractReference(node, target, scope);
                        if (target is null)
                        {
                            return this.InternConstraint(new(ConstraintKind.Error));
                        }

                        node.BoundSymbol = target;
                        result = target.Intrinsic == IntrinsicKind.Callable ? this.InternConstraint(new(ConstraintKind.Error)) : this.InternConstraint(new(ConstraintKind.Contract, subject, contract: target));
                    }
                    else
                    {
                        var type = this.BindType(node, scope);
                        result = type is null ? this.InternConstraint(new(this.HasUnresolvedConstraintSyntax(node, scope) ? ConstraintKind.Unresolved : ConstraintKind.Error)) : this.InternConstraint(new(ConstraintKind.TypeIdentity, subject, type));
                    }
                }

                break;
        }

        if (result.Kind == ConstraintKind.Error)
        {
            Fail(node, BindingFailure.InvalidConstraint);
        }
        else if (result.HasUnresolved && node.BindingState != BindingState.Invalid)
        {
            node.BindingState = BindingState.Unresolved;
        }
        else if (node.BoundType is null)
        {
            Complete(node, BoundType.Boolean);
        }

        return result;
    }

    private void AddConstraintFact(ConstraintEnvironment environment, BoundConstraint fact, BindingSymbol? source = null)
    {
        if (fact.HasUnresolved)
        {
            return;
        }

        if (source is null)
        {
            environment.DirectFacts.Add(fact);
        }
        else if (!environment.DerivedFacts.Contains((fact, source)))
        {
            environment.DerivedFacts.Add((fact, source));
        }

        var added = environment.Facts.Add(fact);
        if (!added && fact.Kind != ConstraintKind.And)
        {
            return;
        }

        environment.HasAssociatedProjection |= fact.HasAssociatedProjection;
        if (fact.Kind == ConstraintKind.Error)
        {
            environment.Invalid = true;
        }
        else if (fact.Kind == ConstraintKind.And)
        {
            this.AddConstraintFact(environment, fact.Left!, source);
            this.AddConstraintFact(environment, fact.Right!, source);
        }
    }

    private ConstraintProof ProveConstraint(BoundConstraint proposition, BindingScope scope)
    {
        var positive = false;
        var negative = false;
        var normalized = this.NormalizeProofConstraint(proposition, scope);
        var negation = this.NegateConstraint(normalized);
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { } environment)
            {
                continue;
            }

            if (environment.Invalid)
            {
                return ConstraintProof.Error;
            }

            positive |= this.AvailableConstraintFact(environment, normalized);
            negative |= this.AvailableConstraintFact(environment, negation);
            if (environment.HasAssociatedProjection)
            {
                foreach (var fact in environment.Facts)
                {
                    if (!this.AvailableConstraintFact(environment, fact))
                    {
                        continue;
                    }

                    var identity = this.NormalizeProofConstraint(fact, scope);
                    positive |= ReferenceEquals(identity, normalized);
                    negative |= ReferenceEquals(identity, negation);
                }
            }
        }

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
        positive |= structural == ConstraintProof.Proven;
        negative |= structural == ConstraintProof.Refuted;
        if (structural != ConstraintProof.Error && (positive || negative) && UnresolvedConstraintOperand(proposition))
        {
            return ConstraintProof.Unknown;
        }

        return structural == ConstraintProof.Error || (positive && negative) ? ConstraintProof.Error : positive ? ConstraintProof.Proven : negative ? ConstraintProof.Refuted : ConstraintProof.Unknown;
    }

    private BoundConstraint NormalizeProofConstraint(BoundConstraint constraint, BindingScope scope)
    {
        if (!constraint.HasAssociatedProjection)
        {
            return constraint;
        }

        if (constraint.Kind == ConstraintKind.Not)
        {
            return this.NegateConstraint(this.NormalizeProofConstraint(constraint.Left!, scope));
        }

        if (constraint.Kind is ConstraintKind.And or ConstraintKind.Or)
        {
            var left = this.NormalizeProofConstraint(constraint.Left!, scope);
            var right = this.NormalizeProofConstraint(constraint.Right!, scope);
            return ReferenceEquals(left, constraint.Left) && ReferenceEquals(right, constraint.Right) ? constraint : this.InternConstraint(new(constraint.Kind, left: left, right: right));
        }

        if (constraint.Subject is null)
        {
            return constraint;
        }

        var subject = this.NormalizeProofType(constraint.Subject, scope);
        var required = constraint.RequiredType is { } type ? this.NormalizeProofType(type, scope) : null;
        return ReferenceEquals(subject, constraint.Subject) && ReferenceEquals(required, constraint.RequiredType) ? constraint : this.InternConstraint(new(constraint.Kind, subject, required, constraint.Contract, constraint.Mask));
    }

    private BoundType NormalizeProofType(BoundType type, BindingScope scope)
    {
        if (type.Kind == BoundTypeKind.AssociatedProjection)
        {
            return this.ContractType(type, scope);
        }

        // Keep a symbolic subject's identity: an exact assumption is not a choice of one
        // satisfying Type. Only associated identities (including nested ones) are reduced.
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
                components[i] = this.NormalizeProofType(type.Components[i], scope);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            return changed ? this.InternType(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, count), type.Length, type.Origin, (BoundOrigin[])type.OriginArguments, type.LengthExpression) : type;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    private BoundConstraint SubstituteConstraint(BoundConstraint constraint, Koto binder, ReadOnlySpan<BoundType?> arguments, ReadOnlySpan<BoundLength?> lengths = default)
    {
        if (constraint.Kind == ConstraintKind.Not)
        {
            return this.NegateConstraint(this.SubstituteConstraint(constraint.Left!, binder, arguments, lengths));
        }

        if (constraint.Kind is ConstraintKind.And or ConstraintKind.Or)
        {
            return this.InternConstraint(new(constraint.Kind, left: this.SubstituteConstraint(constraint.Left!, binder, arguments, lengths), right: this.SubstituteConstraint(constraint.Right!, binder, arguments, lengths)));
        }

        if (constraint.Subject is null)
        {
            return constraint;
        }

        var subject = this.SubstituteType(constraint.Subject, binder, arguments, lengths);
        var required = constraint.RequiredType is null ? null : this.SubstituteType(constraint.RequiredType, binder, arguments, lengths);
        var contract = constraint.Contract;
        if (contract?.Type is { } reference && !ReferenceEquals(reference.Symbol, contract) && this.SubstituteType(reference, binder, arguments, lengths) is { } substituted)
        {
            contract = this.BoundContractReference(substituted);
        }

        return subject is null || (constraint.RequiredType is not null && required is null) ? this.InternConstraint(new(ConstraintKind.Error)) : this.InternConstraint(new(constraint.Kind, subject, required, contract, constraint.Mask));
    }

    private ConstraintProof CheckConstraints(IReadOnlyList<Koto> clauses, Koto binder, ReadOnlySpan<BoundType?> arguments, BindingScope scope, BoundType? self = null, BoundType? declaringType = null, ReadOnlySpan<BoundLength?> lengths = default)
    {
        var result = ConstraintProof.Proven;
        for (var i = 0; i < clauses.Count; i++)
        {
            var constraint = (IsKoto)clauses[i];
            if (binder is DeclarationContainerKoto && constraint.Left.BoundSymbol?.Kind is not (BindingSymbolKind.TypeParameter or BindingSymbolKind.SemanticsTarget or BindingSymbolKind.SemanticsParameter) && constraint.Left.BoundType?.Kind != BoundTypeKind.AssociatedProjection && !IsDependentConstraint(constraint))
            {
                continue;
            }

            if (constraint.BoundConstraint is not { } bound)
            {
                result = ConstraintProof.Error;
                continue;
            }

            var substituted = this.SubstituteConstraint(bound, binder, arguments, lengths);
            if (declaringType?.Symbol?.Declaration is { } owner)
            {
                substituted = this.SubstituteConstraint(substituted, owner, (BoundType[])declaringType.Components);
            }

            // Associated identities also need normalization for calls without a receiver.
            var proof = this.ProveConstraint(this.ContractConstraint(substituted, scope, self), scope);
            result = CombineProof(result, bound.HasUnresolved && proof == ConstraintProof.Proven ? ConstraintProof.Unknown : proof, true);
        }

        return result;
    }

    private bool HasUnresolvedConstraintSyntax(Koto node, BindingScope scope)
    {
        if (this.capabilityMode != BindingMode.Provisional)
        {
            return false;
        }

        this.constraintSyntaxVisitor ??= new(this);
        this.constraintSyntaxVisitor.Scope = scope;
        this.constraintSyntaxVisitor.Missing = false;
        this.constraintSyntaxVisitor.Invalid = false;
        this.constraintSyntaxVisitor.Visit(node);
        return this.constraintSyntaxVisitor.Missing && !this.constraintSyntaxVisitor.Invalid;
    }

    private sealed class ConstraintSyntaxVisitor(Binding binding) : KotoVisitor
    {
        internal BindingScope Scope { get; set; } = null!;

        internal bool Missing { get; set; }

        internal bool Invalid { get; set; }

        public override void Visit(Koto node)
        {
            var missing = node.BindingState == BindingState.Unresolved && node.BindingFailure is BindingFailure.MissingType or BindingFailure.MissingName;
            this.Missing |= missing;
            this.Invalid |= node.BindingState == BindingState.Invalid;
            // Core lookup excludes groups, but an available group is a definite non-Type.
            this.Invalid |= missing && binding.TypeName(node, this.Scope, false)?.Kind == BindingSymbolKind.Container;
            node.VisitChildren(this);
        }
    }
}
