// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Binding's effect declarations (SPEC 8.11.1, 8.11.4; DIAGNOSTICS, effect items).
// - Responsibility: the verdict of every effect item and the bound tables its valid items fill: each Contract's bounds
//   (EnsureEffectBounds), the bounded Callable clauses (PrepareCallableEffects) and each original virtual slot's bound
//   (PrepareVirtualEffectBounds), with the reads of those tables (DeclaredEffectBound, AvailableEffectBounds).
// - Inputs: the pass's Contract shapes, bound requirement headers, Constraint facts and validated virtual declarations;
//   PrepareEffectBounds runs once per pass after them and before conformances are verified.
// - Outputs: BoundContract.EffectBounds, boundedRequirements, callableEffectClauses, virtualEffectBounds, and one rejection
//   fact per invalid item (EffectBoundRejection), published as InvalidEffectBound_Kd at the item (ReportEffectBound).
// - Invariants: the position of an item in the tree is decided by Misplaced alone, before its position's own checks (the parser
//   reports and drops an item that starts a body item outside a leading region, DECISIONS G223); an item AppliesBound
//   rejects supplies no bound (SPEC 8.11.3); every table is rebuilt per pass in reused storage; a rejection records facts,
//   its text is formed at publication.
// - Prohibitions: no bound is searched for in implementations; no other component decides an effect item in the tree.

using System.Diagnostics;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 8.11.3: every bound declared in this pass, for requirement calls whose conforming Type is concrete.
    private readonly List<BoundEffectBound> boundedRequirements = [];

    // The confined bound each original virtual function declares, or null (virtual-dispatch 6).
    private readonly Dictionary<FunctionKoto, EffectBoundKoto?> virtualEffectBounds = new(ReferenceEqualityComparer.Instance);

    // SPEC 8.11.4: the Constraint clauses of this pass that carry an effect list.
    private readonly List<IsKoto> callableEffectClauses = new();

    // The Contracts of this pass, recorded when their shapes are built, so the bound tables need no second scan of the nodes.
    private readonly List<BoundContract> contractShapes = [];

    // SPEC 8.11.1: the cause of each rejected effect item, recorded only when one fails. The store is reused across passes,
    // so a warm rebind allocates nothing.
    private Dictionary<EffectBoundKoto, EffectBoundRejection>? effectBoundRejections;
    private EffectSyntaxCompleter? effectSyntax;

    // SPEC 8.11.1: why an effect item declares no bound.
    private enum EffectRejection : byte
    {
        SpecificationOutsideContract,
        ClauseOutsideRequirement,
        OutsideContract,
        NotContract,
        NotReference,
        OwnContract,
        NotAncestor,
        AmbiguousAncestor,
        NoRequirement,
        OverloadedRequirement,
        Duplicate,
        CallableForm,
        CallablePosition,
    }

    // The Contract whose bound an effect item declares: the Contract itself, or the Contract of the requirement whose
    // Constraint region holds it.
    internal static ContractKoto? DeclaringContract(EffectBoundKoto effect)
        => effect.Parent switch
        {
            ContractKoto contract => contract,
            FunctionKoto { IsRequirement: true, Parent: ContractKoto contract } => contract,
            _ => null,
        };

    /// <summary>
    /// Gets the bound that a Contract or one of its ancestors declares for a requirement (SPEC 8.11.1): <c>T is C</c>
    /// guarantees exactly these.
    /// </summary>
    /// <param name="shape">The Contract.</param>
    /// <param name="requirement">The bound Requirement Identity.</param>
    /// <returns>The declaring effect item, or <see langword="null"/>.</returns>
    internal EffectBoundKoto? DeclaredEffectBound(BoundContract shape, BoundRequirement requirement)
    {
        this.EnsureEffectBounds(shape);
        if (OwnEffectBound(shape, requirement) is { } own)
        {
            return own;
        }

        for (var i = 0; i < shape.Ancestors.Count; i++)
        {
            if (shape.Ancestors[i].Contract is { } ancestor && OwnEffectBound(ancestor, requirement) is { } inherited)
            {
                return inherited;
            }
        }

        return null;
    }

    /// <summary>
    /// SPEC 8.11.3: the bounds available to a requirement call: those that the Contracts its premises prove for the conforming
    /// Type declare for the Requirement Identity, through constraint facts in scope or a concrete Type's conformance. Bounds
    /// are never searched for in implementations.
    /// </summary>
    /// <param name="requirement">The Requirement Identity.</param>
    /// <param name="conforming">The conforming Type of the call.</param>
    /// <param name="scope">The scope whose premises hold at the call.</param>
    /// <param name="evidence">Optional complete provenance for an editor projection; ordinary proofs allocate none.</param>
    /// <param name="reference">The requirement's exact declaring Contract reference; virtual functions have none.</param>
    /// <returns>Whether confined is available.</returns>
    internal bool AvailableEffectBounds(FunctionKoto requirement, BoundType? conforming, BindingScope scope, List<EffectEvidence>? evidence = null, BindingSymbol? reference = null)
    {
        if (requirement.IsVirtual)
        {
            if ((!this.IsRunning && this.Result == default) || !this.virtualEffectBounds.TryGetValue(requirement, out var declared) || declared is null)
            {
                return false;
            }

            evidence?.Add(new(declared, requirement));
            return true;
        }

        var bounded = false;
        for (var i = 0; !bounded && i < this.boundedRequirements.Count; i++)
        {
            bounded = ReferenceEquals(this.boundedRequirements[i].Requirement.Declaration, requirement);
        }

        if (!bounded || conforming is null)
        {
            return false;
        }

        var confined = false;
        var identity = new BoundRequirement(requirement.BoundSymbol!, reference ?? requirement.BoundSymbol!.Scope.Owner.BoundSymbol!);
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { Invalid: false } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (fact.Kind == ConstraintKind.Contract && ReferenceEquals(fact.Subject, conforming) && fact.Contract?.Contract is { } shape &&
                    this.AvailableConstraintFact(environment, fact))
                {
                    Add(shape, current.Owner, fact);
                }
            }
        }

        if (conforming.Symbol is { Kind: BindingSymbolKind.Type } type && this.conformancesByType.TryGetValue(type, out var conformances))
        {
            // Use the available bound conformance paths, including conditional and constructed references.
            for (var i = 0; i < conformances.Count; i++)
            {
                var conformance = conformances[i];
                for (var p = 0; p < conformance.Paths.Count; p++)
                {
                    var path = conformance.Paths[p];
                    if (path.IsVerified && this.ProveConformanceConditions(path, conforming, scope) == ConstraintProof.Proven)
                    {
                        Add(this.SubstituteRequirementContract(conformance.Contract, conforming).Contract!, type.Declaration, null, path.Declaration);
                    }
                }
            }
        }

        return confined;

        void Add(BoundContract shape, Koto context, BoundConstraint? premise, Koto? clause = null)
        {
            confined |= this.DeclaredEffectBound(shape, identity) is not null;
            if (evidence is not null)
            {
                Capture(shape);
                foreach (var ancestor in shape.Ancestors)
                {
                    if (ancestor.Contract is { } inherited)
                    {
                        Capture(inherited);
                    }
                }
            }

            void Capture(BoundContract declaring)
            {
                foreach (var entry in declaring.EffectBounds)
                {
                    if (entry.Identity == identity)
                    {
                        evidence!.Add(new(entry.Declaration, context, premise, clause, clause is null ? null : conforming, clause is null ? null : shape.Symbol));
                    }
                }
            }
        }
    }

    /// <summary>Gets the bounds available to a requirement call under the premises in scope at a node (SPEC 8.11.3).</summary>
    /// <param name="requirement">The Requirement Identity.</param>
    /// <param name="conforming">The conforming Type of the call.</param>
    /// <param name="at">The call.</param>
    /// <returns>Whether confined is available.</returns>
    internal bool AvailableEffectBounds(FunctionKoto requirement, BoundType? conforming, Koto at)
        => this.AvailableEffectBounds(requirement, conforming, this.ConstraintScope(at), reference: (at as InvocationKoto)?.BoundCall?.RequirementContract);

    private static EffectBoundKoto? OwnEffectBound(BoundContract shape, BoundRequirement requirement)
    {
        var bounds = shape.EffectBoundStorage;
        for (var i = 0; i < bounds.Count; i++)
        {
            if (bounds[i].Identity == requirement)
            {
                return bounds[i].Declaration;
            }
        }

        return null;
    }

    // SPEC 8.11.1, F.6: the one rule for where an effect item in the tree stands. A specification is a Contract item; a clause stands in a
    // requirement's Constraint region, a Callable Constraint's effect list or an original virtual function's leading region,
    // whose own checks follow; any other item declares no bound.
    private static EffectRejection? Misplaced(EffectBoundKoto effect) => effect.Parent switch
    {
        ContractKoto => effect.IsSpecification ? null : EffectRejection.ClauseOutsideRequirement,
        FunctionKoto { IsRequirement: true, Parent: ContractKoto } or IsKoto or FunctionKoto { IsVirtual: true } =>
            effect.IsSpecification ? EffectRejection.SpecificationOutsideContract : null,
        _ => EffectRejection.OutsideContract,
    };

    // SPEC 8.11.1: the bounds of every Contract, checked once per pass after the requirement headers are bound and before
    // conformances are verified against them.
    private void PrepareEffectBounds()
    {
        this.effectBoundRejections?.Clear();
        this.boundedRequirements.Clear();
        for (var i = 0; i < this.contractShapes.Count; i++)
        {
            this.EnsureEffectBounds(this.contractShapes[i]);
        }

        this.callablePremiseFailures.Clear();
        this.PrepareCallableEffects();
        this.PrepareVirtualEffectBounds();
    }

    // A Contract's ancestors are checked first, so a bound already declared above is known (SPEC 8.11.1).
    private void EnsureEffectBounds(BoundContract shape)
    {
        if (shape.EffectState != 0 || shape.Symbol.Declaration is not ContractKoto contract)
        {
            return;
        }

        shape.EffectState = 1;
        shape.HasEffectBounds = false;
        if (IsBoundContractReference(shape.Symbol))
        {
            var original = contract.BoundSymbol!.Contract!;
            this.EnsureEffectBounds(original);
            for (var i = 0; i < original.EffectBounds.Count; i++)
            {
                var effect = original.EffectBounds[i];
                var reference = ReferenceEquals(effect.Identity.Contract, original.Symbol) ? shape.Symbol : this.SubstituteRequirementContract(effect.Identity.Contract, shape.Symbol.Type!);
                shape.EffectBoundStorage.Add(effect with { Identity = new(effect.Requirement, reference) });
            }

            shape.HasEffectBounds = original.HasEffectBounds;
            for (var i = 0; i < shape.Ancestors.Count; i++)
            {
                this.EnsureEffectBounds(shape.Ancestors[i].Contract!);
            }

            shape.EffectState = 2;
            return;
        }

        for (var i = 0; i < shape.Ancestors.Count; i++)
        {
            if (shape.Ancestors[i].Contract is { } ancestor)
            {
                this.EnsureEffectBounds(ancestor);
                shape.HasEffectBounds |= ancestor.HasEffectBounds;
            }
        }

        if (!this.scopes.TryGetValue(contract, out var scope))
        {
            shape.EffectState = 2; // An intrinsic Contract, such as Copy, declares no effect item.
            return;
        }

        for (var m = 0; m < contract.Members.Count; m++)
        {
            if (contract.Members[m] is FunctionKoto { IsRequirement: true, BoundSymbol: { } requirement } function)
            {
                for (var e = 0; e < function.EffectBounds.Count; e++)
                {
                    if (this.AppliesBound(function.EffectBounds[e]))
                    {
                        this.DeclareEffectBound(shape, function.EffectBounds[e], new(requirement, shape.Symbol));
                    }
                }
            }
            else if (contract.Members[m] is EffectBoundKoto effect && this.AppliesBound(effect) && this.EffectTarget(shape, effect, scope) is { } target)
            {
                this.DeclareEffectBound(shape, effect, target);
            }
        }

        shape.EffectState = 2;
    }

    // Bounds are attached to clauses, never to interned propositions or applicability facts (SPEC 8.11.4).
    private void PrepareCallableEffects()
    {
        this.callableEffectClauses.Clear();
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is not IsKoto { EffectBounds.Count: > 0 } clause)
            {
                continue;
            }

            this.callableEffectClauses.Add(clause);
            EffectBoundKoto? confined = null;
            for (var e = 0; e < clause.EffectBounds.Count; e++)
            {
                var effect = clause.EffectBounds[e];
                if (!this.AppliesBound(effect))
                {
                    // A misplaced item or a recovery has its own cause and supplies no bound, so it is no earlier one (SPEC 8.11.3).
                    this.BindEffectBound(effect);
                    continue;
                }

                EffectRejection? rejection = null;
                if (clause.Right is not GenericsKoto || clause.BoundConstraint?.Kind != ConstraintKind.Callable)
                {
                    rejection = EffectRejection.CallableForm;
                }
                else if (clause.IsAssociatedConstraint || clause.Parent is not (FunctionKoto or StructKoto or EnumKoto))
                {
                    rejection = EffectRejection.CallablePosition;
                }

                if (rejection is { } invalid)
                {
                    this.RejectEffectBound(effect, new(invalid, Target: clause));
                }
                else if (this.ValidateDeclaredEffectBound(effect, clause, confined))
                {
                    confined = effect; // SPEC 8.11.4: confined appears at most once in one Constraint.
                }

                this.BindEffectBound(effect);
            }
        }
    }

    // SPEC 8.11.1: the confined bound of each valid original virtual function, its slot's public guarantee.
    private void PrepareVirtualEffectBounds()
    {
        foreach (var function in this.virtualDeclarations)
        {
            if (!function.IsVirtual || function.BindingFailure != BindingFailure.None)
            {
                continue;
            }

            EffectBoundKoto? confined = null;
            for (var i = 0; i < function.EffectBounds.Count; i++)
            {
                var effect = function.EffectBounds[i];
                if (this.AppliesBound(effect) && this.ValidateDeclaredEffectBound(effect, function, confined))
                {
                    confined = effect;
                }
            }

            this.virtualEffectBounds[function] = confined;
        }
    }

    private void DeclareEffectBound(BoundContract shape, EffectBoundKoto effect, BoundRequirement requirement)
    {
        // SPEC 8.11.1: a bound declared twice in one Contract is an error; restating an ancestor's bound forms one guarantee.
        if (!this.ValidateDeclaredEffectBound(effect, requirement.Symbol.Declaration, OwnEffectBound(shape, requirement)))
        {
            return;
        }

        shape.EffectBoundStorage.Add(new(requirement, effect));
        shape.HasEffectBounds = true;
        this.boundedRequirements.Add(new(requirement, effect));
    }

    // SPEC 8.11.1, 8.11.4: the one duplicate rule of every bound position; the target is the function requirement, the original
    // virtual function or the Callable Constraint the item bounds, and the earlier item its valid bound there.
    private bool ValidateDeclaredEffectBound(EffectBoundKoto effect, Koto target, EffectBoundKoto? earlier)
    {
        if (earlier is not null)
        {
            this.RejectEffectBound(effect, new(EffectRejection.Duplicate, Target: target, Earlier: earlier));
            return false;
        }

        return true;
    }

    // SPEC 8.11.1: the selector names an ancestor of the Contract, and Name exactly one function requirement of that ancestor.
    private BoundRequirement? EffectTarget(BoundContract shape, EffectBoundKoto effect, BindingScope scope)
    {
        var selector = effect.Selector!;
        var syntax = selector;
        while (syntax is ParenthesizedKoto or ParenthesizedTypeKoto || syntax is TypeSemanticsKoto { IsTransparentWrapper: true, Type: not null })
        {
            syntax = syntax switch
            {
                ParenthesizedKoto grouped => grouped.Operand,
                ParenthesizedTypeKoto grouped => grouped.Type,
                TypeSemanticsKoto wrapper => wrapper.Type!,
                _ => syntax,
            };
        }

        if (this.TypeName(syntax, scope, false) is not { Declaration: ContractKoto declaration } named)
        {
            this.RejectEffectBound(effect, new(EffectRejection.NotContract));
            return null;
        }

        BoundType? reference = null;
        if (GenericParentApplication(syntax) is { } generic)
        {
            var arguments = this.BindTypeList(generic.TypeArguments, scope, this.TypeContext(generic, scope).Nested, BoundTypeKind.Constructed, named);
            if (arguments is null)
            {
                this.RejectEffectBound(effect, new(EffectRejection.NotReference, Symbol: named));
                return null;
            }

            reference = this.types.Intern(BoundTypeKind.Constructed, named, SemanticsKind.Owner, (BoundType[])arguments.Components);
        }

        BindingSymbol? ancestor = null;
        var matches = 0;
        for (var i = 0; i < shape.Ancestors.Count; i++)
        {
            var candidate = shape.Ancestors[i];
            if (ReferenceEquals(candidate.Declaration, declaration) && (reference is null || this.SameConformanceType(candidate.Type, reference, scope)))
            {
                ancestor = candidate;
                matches++;
            }
        }

        if (ancestor is null || matches > 1)
        {
            var kind = matches > 1 ? EffectRejection.AmbiguousAncestor : ReferenceEquals(declaration, shape.Symbol.Declaration) ? EffectRejection.OwnContract : EffectRejection.NotAncestor;
            this.RejectEffectBound(effect, new(kind, Symbol: matches > 1 ? named : shape.Symbol, Count: matches));
            return null;
        }

        selector.BoundSymbol = ancestor;
        var name = effect.Name is IdentifierNameKoto identifier ? identifier.IdentifierName : null;
        BoundRequirement? target = null;
        var found = 0;
        var requirements = ancestor.Contract!.Requirements;
        for (var i = 0; i < requirements.Count; i++)
        {
            if (requirements[i].Symbol.Declaration is FunctionKoto && requirements[i].Symbol.Name == name)
            {
                target = requirements[i];
                found++;
            }
        }

        if (target is null || found > 1)
        {
            this.RejectEffectBound(effect, new(target is null ? EffectRejection.NoRequirement : EffectRejection.OverloadedRequirement, Target: target?.Symbol.Declaration, Symbol: ancestor, Count: found));
            return null;
        }

        effect.Name!.BoundSymbol = target.Value.Symbol;
        return target;
    }

    private void RejectEffectBound(EffectBoundKoto effect, EffectBoundRejection rejection)
        => (this.effectBoundRejections ??= new(ReferenceEqualityComparer.Instance))[effect] = rejection;

    // SPEC 8.11.3: whether an effect item supplies its bound. A misplaced item, one a declaration check rejected and a recovery,
    // whose checks rest on its syntax Error, supply none; every reader of an item asks this first.
    private bool AppliesBound(EffectBoundKoto bound)
        => Misplaced(bound) is null && this.effectBoundRejections?.ContainsKey(bound) != true && !IsRecovery(bound, out _);

    private BoundType? BindEffectBound(EffectBoundKoto effect)
    {
        if (effect.Selector is { } selector)
        {
            // The selector and Name designate declarations; their failures are the effect item's (SPEC 8.11.1).
            this.effectSyntax ??= new();
            this.effectSyntax.Visit(selector);
            this.effectSyntax.Visit(effect.Name!);
        }

        if (effect.CodeContext.RecoveryCause(effect) is not null)
        {
            return Complete(effect, null);
        }

        if (Misplaced(effect) is { } position)
        {
            this.RejectEffectBound(effect, new(position));
        }

        return this.effectBoundRejections?.ContainsKey(effect) == true ? this.Fail(effect, BindingFailure.InvalidEffectBound) : Complete(effect, BoundType.Unit);
    }

    // SPEC 8.11.1: the Reason names the cause, the Note its path and the related locations the bounded declaration (a function
    // requirement, an original virtual slot or the Constraint of a Callable effect list) and an earlier bound. The text is formed
    // here, only for published records.
    private void ReportEffectBound(EffectBoundKoto effect, DiagnosticRequirement requirement)
    {
        if (this.effectBoundRejections?.TryGetValue(effect, out var rejection) != true)
        {
            effect.Report(requirement, DiagnosticCode.InvalidEffectBound_Kd);
            return;
        }

        var selector = effect.Selector?.ToString();
        var name = (rejection.Target as FunctionKoto)?.Name ?? effect.Name?.ToString();
        var spelling = Constants.ConfinedKeyword;
        var (cause, note) = rejection.Kind switch
        {
            EffectRejection.SpecificationOutsideContract => ("an effect specification outside a Contract's items",
                "An effect specification is a Contract item and bounds a requirement the Contract inherits"),
            EffectRejection.ClauseOutsideRequirement => ("an effect clause outside a requirement", "An effect clause stands in the Constraint region of the requirement it bounds"),
            EffectRejection.OutsideContract => ("an effect item outside a Contract, original virtual function or Callable Constraint", $"A Contract, original virtual function or Callable Constraint declares bounds; {EffectOwner(effect)} declares no bound of its own, and an implementation can neither add nor remove its public contract's bound"),
            EffectRejection.CallableForm => ("the effect list is not under a single Callable Constraint", "Write other requirements in separate clauses; parentheses and combined requirements accept no effect list"),
            EffectRejection.CallablePosition => ("a Callable effect bound in an excluded position", "Callable bounds belong to function, struct, enum or function-requirement Constraint regions"),
            EffectRejection.NotContract => ($"{selector} names no Contract", "An effect specification names a requirement of an ancestor through the ancestor's Contract selector"),
            EffectRejection.NotReference => ($"{selector} is not a reference to {rejection.Symbol!.Name}", $"The Type arguments of {selector} do not form a reference to {rejection.Symbol!.Name}"),
            EffectRejection.OwnContract => ($"{selector} is this Contract", "A Contract bounds its own requirement with an effect clause in that requirement's Constraint region"),
            EffectRejection.NotAncestor => ($"{selector} is not an ancestor of {rejection.Symbol!.Name}", "An effect specification bounds a requirement that the Contract inherits"),
            EffectRejection.AmbiguousAncestor => ($"{selector} names {rejection.Count} ancestors", $"The Contract refines {rejection.Count} references of {rejection.Symbol!.Name}"),
            EffectRejection.NoRequirement => ($"{rejection.Symbol!.Name} has no function requirement {name}", null),
            EffectRejection.OverloadedRequirement => ($"{name} names {rejection.Count} function requirements of {rejection.Symbol!.Name}", "An effect specification bounds exactly one function requirement"),
            EffectRejection.Duplicate => rejection.Target switch
            {
                IsKoto => ($"the Callable Constraint already declares {spelling}", $"A Callable Constraint declares {spelling} at most once"),
                FunctionKoto { IsVirtual: true } => ($"{name} already has {spelling}",
                    $"An original virtual function declares {spelling} for its slot at most once"),
                _ => ($"{name} already has {spelling}", $"{DeclaringContract(rejection.Earlier!)?.BoundSymbol?.Name} already declares {spelling} for {name}; " +
                    "each declaration supplies a bound once, though a Contract may restate a bound of an ancestor"),
            },
            _ => throw new UnreachableException(),
        };

        var related = rejection.Target is null && rejection.Earlier is null ? null : new (string Role, Koto At, string? Label)[(rejection.Target is null ? 0 : 1) + (rejection.Earlier is null ? 0 : 1)];
        if (rejection.Target is { } target)
        {
            related![0] = target switch
            {
                IsKoto => ("constraint", target, "the Constraint of the effect list"),
                FunctionKoto { IsVirtual: true } => ("declaration", target, "the original virtual slot"),
                _ => ("requirement", target, "the bounded requirement"),
            };
        }

        if (rejection.Earlier is { } earlier)
        {
            related![^1] = ("bound", earlier, "the bound declared earlier");
        }

        effect.Report(requirement, DiagnosticCode.InvalidEffectBound_Kd, note: note, evidence: [cause], related: related);

        static string EffectOwner(EffectBoundKoto effect)
            => effect.Parent switch
            {
                FunctionKoto { IsAnonymous: true } => "a function literal",
                FunctionKoto function => $"the function {function.Name}",
                CodeBlockKoto { Parent: SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } } => "a conditional conformance",
                _ => effect.Parent?.BoundSymbol?.Name ?? "this declaration",
            };
    }

    // Completes the selector and Name syntax of an effect item, which Binding never evaluates.
    private sealed class EffectSyntaxCompleter : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            if (node.BindingState != BindingState.Resolved)
            {
                Complete(node, BoundType.Unit);
            }

            node.VisitChildren(this);
        }
    }

    // Target: the declaration the item bounds (a function requirement, an original virtual function or a Callable Constraint).
    private readonly record struct EffectBoundRejection(EffectRejection Kind, Koto? Target = null, EffectBoundKoto? Earlier = null, BindingSymbol? Symbol = null, int Count = 0);
}

// One premise or public guarantee behind an available effect bound, collected only for Hover provenance.
internal readonly record struct EffectEvidence(EffectBoundKoto Bound, Koto Context, BoundConstraint? Premise = null, Koto? Clause = null, BoundType? Conforming = null, BindingSymbol? Contract = null);
