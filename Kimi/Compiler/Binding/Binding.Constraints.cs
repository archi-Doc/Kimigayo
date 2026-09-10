// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<ConstraintKey, BoundConstraint> constraints = new();

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

    private static bool DependentType(BoundType type)
    {
        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.AssociatedProjection || type.LengthExpression is not null || (type.Origin is not null && type.Origin.Kind != OriginKind.Static))
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
            if (DependentType(type.Components[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InvalidConstraintType(BoundType type)
    {
        if (type.Symbol?.Declaration.BindingState == BindingState.Invalid)
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

    private ConstraintProof JudgeConstraintAtom(BoundConstraint proposition, BindingScope scope)
    {
        if (InvalidConstraintType(proposition.Subject!) || (proposition.RequiredType is { } required && InvalidConstraintType(required)) || proposition.Contract?.Declaration.BindingState == BindingState.Invalid)
        {
            return ConstraintProof.Error;
        }

        if (proposition.Kind == ConstraintKind.TypeIdentity)
        {
            if (ReferenceEquals(proposition.Subject, proposition.RequiredType))
            {
                return ConstraintProof.Proven;
            }

            return DependentType(proposition.Subject!) || DependentType(proposition.RequiredType!) ? ConstraintProof.Unknown : ConstraintProof.Refuted;
        }

        if (proposition.Kind == ConstraintKind.Semantics)
        {
            if (proposition.Subject!.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication)
            {
                return ConstraintProof.Unknown;
            }

            return proposition.Mask.Contains(proposition.Subject.Semantics) ? ConstraintProof.Proven : ConstraintProof.Refuted;
        }

        if (proposition.Contract is { Intrinsic: IntrinsicKind.Copy or IntrinsicKind.Owned } intrinsic)
        {
            return this.RequestCapability(proposition.Subject!, intrinsic, scope);
        }

        // Registration is not verified conformance. In particular, absence is not refutation.
        return ConstraintProof.Unknown;
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
        => value.Kind == ConstraintKind.Not ? value.Left! : this.InternConstraint(new(ConstraintKind.Not, left: value));

    private BindingScope ConstraintScope(Koto node)
    {
        for (Koto? current = node; current is not null; current = current.Parent)
        {
            if (this.scopes.TryGetValue(current, out var scope))
            {
                return this.NodeScope(node, scope);
            }
        }

        return this.rootScope;
    }

    private void BindConstraints()
    {
        // Bind all clauses before any signature needs their projections. No proof runs in this pass.
        for (var i = 0; i < this.nodes.Count; i++)
        {
            var node = this.nodes[i];
            if (node is FunctionKoto function)
            {
                for (var j = 0; j < function.TypeConstraints.Count; j++)
                {
                    this.BindConstraint((IsKoto)function.TypeConstraints[j], this.scopes[function]);
                }
            }
            else if (node is DeclarationContainerKoto container)
            {
                if (container is ContractKoto && (container.GenericParameterNodes.Count != 0 || container.OriginNames.Count != 0))
                {
                    Fail(container, BindingFailure.InvalidConstraint);
                }

                for (var j = 0; j < container.ConstraintNodes.Count; j++)
                {
                    this.BindConstraint(container.ConstraintNodes[j], this.scopes[container]);
                }
            }
        }

        this.BindCopyDeclarations();
    }

    private void ValidateConstraintEnvironments()
    {
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
                    environment.Invalid = true;
                    Fail(scope.Owner, BindingFailure.InvalidConstraint);
                    break;
                }
            }
        }
    }

    private void BindConstraint(IsKoto clause, BindingScope scope)
    {
        var symbol = this.TypeName(clause.Left, scope, false);
        var semantics = symbol?.Kind == BindingSymbolKind.SemanticsParameter;
        var subject = semantics ? symbol!.Pair!.WholeType : symbol?.Kind == BindingSymbolKind.SemanticsTarget ? symbol.Type : this.BindType(clause.Left, scope);
        clause.Left.BoundSymbol = symbol;
        Complete(clause.Left, subject);
        var requirement = this.BindRequirement(clause.Right, subject, semantics, scope);
        var input = symbol?.Kind is BindingSymbolKind.TypeParameter or BindingSymbolKind.SemanticsTarget or BindingSymbolKind.SemanticsParameter;
        var validSubject = subject is not null && (scope.Owner is FunctionKoto ? input && ReferenceEquals(symbol!.Scope, scope) : input || (clause.Left is IdentifierNameKoto { IdentifierName: "Self" } && scope.Owner is DeclarationContainerKoto and not GroupKoto));
        if (!validSubject || clause.IsAssociatedConstraint)
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
                        node.BoundSymbol = target;
                        result = target.Intrinsic == IntrinsicKind.Callable ? this.InternConstraint(new(ConstraintKind.Error)) : this.InternConstraint(new(ConstraintKind.Contract, subject, contract: target));
                    }
                    else
                    {
                        var type = this.BindType(node, scope);
                        result = type is null ? this.InternConstraint(new(ConstraintKind.Error)) : this.InternConstraint(new(ConstraintKind.TypeIdentity, subject, type));
                    }
                }

                break;
        }

        if (result.Kind == ConstraintKind.Error)
        {
            Fail(node, BindingFailure.InvalidConstraint);
        }
        else if (node.BoundType is null)
        {
            Complete(node, BoundType.Boolean);
        }

        return result;
    }

    private void AddConstraintFact(ConstraintEnvironment environment, BoundConstraint fact)
    {
        if (!environment.Facts.Add(fact))
        {
            return;
        }

        if (fact.Kind == ConstraintKind.Error)
        {
            environment.Invalid = true;
        }
        else if (fact.Kind == ConstraintKind.And)
        {
            this.AddConstraintFact(environment, fact.Left!);
            this.AddConstraintFact(environment, fact.Right!);
        }
    }

    private ConstraintProof ProveConstraint(BoundConstraint proposition, BindingScope scope)
    {
        var positive = false;
        var negative = false;
        var negation = this.NegateConstraint(proposition);
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

            positive |= environment.Facts.Contains(proposition);
            negative |= environment.Facts.Contains(negation);
        }

        // Always validate both operands, even when an exact compound assumption is available.
        var structural = proposition.Kind switch
        {
            ConstraintKind.Error => ConstraintProof.Error,
            ConstraintKind.Not => NegateProof(this.ProveConstraint(proposition.Left!, scope)),
            ConstraintKind.And => CombineProof(this.ProveConstraint(proposition.Left!, scope), this.ProveConstraint(proposition.Right!, scope), true),
            ConstraintKind.Or => CombineProof(this.ProveConstraint(proposition.Left!, scope), this.ProveConstraint(proposition.Right!, scope), false),
            _ => this.JudgeConstraintAtom(proposition, scope),
        };
        positive |= structural == ConstraintProof.Proven;
        negative |= structural == ConstraintProof.Refuted;
        return structural == ConstraintProof.Error || (positive && negative) ? ConstraintProof.Error : positive ? ConstraintProof.Proven : negative ? ConstraintProof.Refuted : ConstraintProof.Unknown;
    }

    private BoundConstraint SubstituteConstraint(BoundConstraint constraint, Koto binder, ReadOnlySpan<BoundType?> arguments)
    {
        if (constraint.Kind == ConstraintKind.Not)
        {
            return this.NegateConstraint(this.SubstituteConstraint(constraint.Left!, binder, arguments));
        }

        if (constraint.Kind is ConstraintKind.And or ConstraintKind.Or)
        {
            return this.InternConstraint(new(constraint.Kind, left: this.SubstituteConstraint(constraint.Left!, binder, arguments), right: this.SubstituteConstraint(constraint.Right!, binder, arguments)));
        }

        if (constraint.Subject is null)
        {
            return constraint;
        }

        var subject = this.SubstituteType(constraint.Subject, binder, arguments);
        var required = constraint.RequiredType is null ? null : this.SubstituteType(constraint.RequiredType, binder, arguments);
        return subject is null || (constraint.RequiredType is not null && required is null) ? this.InternConstraint(new(ConstraintKind.Error)) : this.InternConstraint(new(constraint.Kind, subject, required, constraint.Contract, constraint.Mask));
    }

    private ConstraintProof CheckConstraints(IReadOnlyList<Koto> clauses, Koto binder, ReadOnlySpan<BoundType?> arguments, BindingScope scope)
    {
        var result = ConstraintProof.Proven;
        for (var i = 0; i < clauses.Count; i++)
        {
            var constraint = (IsKoto)clauses[i];
            if (binder is DeclarationContainerKoto && constraint.Left.BoundSymbol?.Kind is not (BindingSymbolKind.TypeParameter or BindingSymbolKind.SemanticsTarget or BindingSymbolKind.SemanticsParameter))
            {
                continue;
            }

            if (constraint.BoundConstraint is not { } bound)
            {
                result = ConstraintProof.Error;
                continue;
            }

            result = CombineProof(result, this.ProveConstraint(this.SubstituteConstraint(bound, binder, arguments), scope), true);
        }

        return result;
    }
}
