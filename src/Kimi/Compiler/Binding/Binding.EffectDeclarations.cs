// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

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

    // The Contracts of this pass, recorded when their shapes are built, so the bound tables need no second scan of the nodes.
    private readonly List<BoundContract> contractShapes = [];

    // SPEC 8.11.1: the first violating effect of each conformance a bound rejects, keyed by its Self clause; recorded only
    // when one is rejected and reused across passes.
    private Dictionary<Koto, EffectViolationRecord>? effectViolations;

    // SPEC 8.11.1: the cause of each rejected effect item, recorded only when one fails. The store is reused across passes,
    // so a warm rebind allocates nothing.
    private Dictionary<EffectBoundKoto, EffectBoundRejection>? effectBoundRejections;
    private EffectSyntaxCompleter? effectSyntax;

    /// <summary>SPEC 8.11.1: the kind of the first effect of an implementation that violates a bound.</summary>
    internal enum EffectViolation : byte
    {
        /// <summary>No violation.</summary>
        None,

        /// <summary>An access to a mutable static Field, which confined excludes.</summary>
        MutableStatic,

        /// <summary>A standard operation on state outside the program, such as Console output, which confined excludes.</summary>
        ExternalOperation,

        /// <summary>A call of a foreign function, an environment effect that confined excludes (SPEC 22.6).</summary>
        ForeignCall,

        /// <summary>A read of a raw pointer from an immutable static, which obtains environment authority.</summary>
        StaticPointer,

        /// <summary>A conversion of an integer to a pointer, which obtains environment authority.</summary>
        IntegerPointer,

        /// <summary>A call whose effects cannot be classified, treated as a conflict.</summary>
        UnclassifiedCall,

        /// <summary>A requirement call that no available bound covers, treated as a conflict.</summary>
        UnboundedRequirement,

        /// <summary>A dynamic virtual call whose public slot does not guarantee the required bound.</summary>
        UnboundedVirtual,

        /// <summary>A destruction whose effects cannot be classified, treated as a conflict.</summary>
        UnknownDestruction,

        /// <summary>An abstract callable has no premise declaring the required bound.</summary>
        MissingCallablePremise,

        /// <summary>An erased Function Type keeps no effect bound.</summary>
        ErasedCallable,

        /// <summary>An effect this implementation cannot classify yet: the check fails as an implementation limit (Unsupported_Kd).</summary>
        Limit,
    }

    // SPEC 8.11.1: why an effect item declares no bound.
    private enum EffectRejection : byte
    {
        SpecificationInRequirement,
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
        CallableDuplicate,
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

    /// <summary>
    /// SPEC 8.11.1: reports a conformance that a bound rejects at the first violating effect in the implementation's own body.
    /// The Reason names the violation, the bound and its declaring Contract; the Note tells a definite violation from an
    /// unknown effect counted as a conflict; the related locations give the effect, the conformance and the bound.
    /// </summary>
    /// <param name="use">The Self clause of the conformance.</param>
    /// <param name="requirement">The failed requirement of the record.</param>
    /// <param name="code">The code to report.</param>
    /// <param name="instanceContext">The concrete generic context, when reporting an instance.</param>
    /// <param name="instanceSite">The source call that requested the instance.</param>
    /// <returns><see langword="true"/> when a violation was recorded for the conformance and reported.</returns>
    internal bool ReportEffectViolation(Koto use, DiagnosticRequirement requirement, DiagnosticCode code, string? instanceContext = null, Koto? instanceSite = null)
    {
        if (this.effectViolations?.TryGetValue(use, out var violation) != true)
        {
            return false;
        }

        var name = violation.Requirement.Name;
        var item = this.DeclaredEffectBound(violation.Contract, violation.Identity);
        var contract = item is null ? violation.Contract.Symbol.Name : DeclaringContract(item)?.BoundSymbol?.Name ?? violation.Contract.Symbol.Name;
        var spelling = Constants.ConfinedKeyword;

        // SPEC 8.11.1: an environment effect is named by the operation that obtained the authority (SPEC 8.11.2).
        var effect = EffectCause(violation.Kind);
        var definite = violation.Kind is EffectViolation.MutableStatic or EffectViolation.ExternalOperation or EffectViolation.ForeignCall or
            EffectViolation.StaticPointer or EffectViolation.IntegerPointer;

        var note = definite
            ? $"{name} must satisfy {spelling}, declared by {contract}, and this effect violates it"
            : $"{name} must satisfy {spelling}, declared by {contract}; this effect cannot be classified, so verification treats it as a conflict (SPEC 8.6)";
        if (instanceContext is not null)
        {
            note += "; " + instanceContext;
        }

        var count = (violation.Node is { } node && !ReferenceEquals(node, violation.Site) ? 1 : 0) + 1 + (item is null ? 0 : 1) + (instanceSite is null ? 0 : 1);
        var related = new (string Role, Koto At, string? Label)[count];
        var next = 0;
        if (violation.Node is { } effectNode && !ReferenceEquals(effectNode, violation.Site))
        {
            related[next++] = ("effect", effectNode, "the violating effect");
        }

        related[next++] = ("conformance", use, "the conformance checked against the bound");
        if (item is not null)
        {
            related[next++] = ("bound", item, "the bound");
        }

        if (instanceSite is not null)
        {
            related[next] = ("instantiation", instanceSite, "the call requesting this instance");
        }

        use.Report(requirement, code, note: note, at: violation.Site, evidence: [$"{effect}, which {spelling} excludes"], related: related);
        return true;
    }

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
                    var clause = function.EffectBounds[e];
                    if (clause.CodeContext.RecoveryCause(clause) is not null)
                    {
                        continue;
                    }

                    if (clause.IsSpecification)
                    {
                        this.RejectEffectBound(clause, new(EffectRejection.SpecificationInRequirement, Requirement: function));
                    }
                    else
                    {
                        this.DeclareEffectBound(shape, clause, new(requirement, shape.Symbol));
                    }
                }
            }
            else if (contract.Members[m] is EffectBoundKoto effect && effect.CodeContext.RecoveryCause(effect) is null)
            {
                if (!effect.IsSpecification)
                {
                    this.RejectEffectBound(effect, new(EffectRejection.ClauseOutsideRequirement));
                }
                else if (this.EffectTarget(shape, effect, scope) is { } target)
                {
                    this.DeclareEffectBound(shape, effect, target);
                }
            }
        }

        shape.EffectState = 2;
    }

    private void DeclareEffectBound(BoundContract shape, EffectBoundKoto effect, BoundRequirement requirement)
    {
        // SPEC 8.11.1: a bound declared twice in one Contract is an error; restating an ancestor's bound forms one guarantee.
        if (!this.ValidateDeclaredEffectBound(effect, requirement.Symbol, OwnEffectBound(shape, requirement)))
        {
            return;
        }

        shape.EffectBoundStorage.Add(new(requirement, effect));
        shape.HasEffectBounds = true;
        this.boundedRequirements.Add(new(requirement, effect));
    }

    private bool ValidateDeclaredEffectBound(EffectBoundKoto effect, BindingSymbol requirement, EffectBoundKoto? earlier)
    {
        if (earlier is not null)
        {
            this.RejectEffectBound(effect, new(EffectRejection.Duplicate, Requirement: requirement.Declaration, Earlier: earlier));
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
            if (ReferenceEquals(candidate.Declaration, declaration) && (reference is null || ReferenceEquals(candidate.Type, reference)))
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
            this.RejectEffectBound(effect, new(target is null ? EffectRejection.NoRequirement : EffectRejection.OverloadedRequirement, Requirement: target?.Symbol.Declaration, Symbol: ancestor, Count: found));
            return null;
        }

        effect.Name!.BoundSymbol = target.Value.Symbol;
        return target;
    }

    private void RejectEffectBound(EffectBoundKoto effect, EffectBoundRejection rejection)
        => (this.effectBoundRejections ??= new(ReferenceEqualityComparer.Instance))[effect] = rejection;

    // SPEC 8.11.1: an effect item declares a bound of its Contract. A rejected bound word leaves a recovery whose checks
    // rest on the syntax Error; an item outside a Contract declares nothing.
    // Whether an effect bound applies: one the declaration checks rejected, or a recovery, declares nothing.
    private bool AppliesBound(EffectBoundKoto bound) => this.effectBoundRejections?.ContainsKey(bound) != true && !IsRecovery(bound, out _);

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

        if (DeclaringContract(effect) is null && effect.Parent is not IsKoto and not FunctionKoto { IsVirtual: true })
        {
            this.RejectEffectBound(effect, new(EffectRejection.OutsideContract));
        }

        return this.effectBoundRejections?.ContainsKey(effect) == true ? this.Fail(effect, BindingFailure.InvalidEffectBound) : Complete(effect, BoundType.Unit);
    }

    // SPEC 8.11.1: the Reason names the cause, the Note its path and the related locations the target requirement and an
    // earlier bound. The text is formed here, only for published records.
    private void ReportEffectBound(EffectBoundKoto effect, DiagnosticRequirement requirement)
    {
        if (this.effectBoundRejections?.TryGetValue(effect, out var rejection) != true)
        {
            effect.Report(requirement, DiagnosticCode.InvalidEffectBound_Kd);
            return;
        }

        var selector = effect.Selector?.ToString();
        var name = (rejection.Requirement as FunctionKoto)?.Name ?? effect.Name?.ToString();
        var spelling = Constants.ConfinedKeyword;
        var earlierOwner = rejection.Earlier is { } previous ? DeclaringContract(previous)?.BoundSymbol?.Name ?? (previous.Parent as FunctionKoto)?.Name : null;
        var (cause, note) = rejection.Kind switch
        {
            EffectRejection.SpecificationInRequirement => ("an effect specification in a requirement", "An effect specification names an inherited requirement and is a Contract item; a requirement's own bound is an effect clause"),
            EffectRejection.ClauseOutsideRequirement => ("an effect clause outside a requirement", "An effect clause stands in the Constraint region of the requirement it bounds"),
            EffectRejection.OutsideContract => ("an effect item outside a Contract, original virtual function or Callable Constraint", $"A Contract, original virtual function or Callable Constraint declares bounds; {EffectOwner(effect)} declares no bound of its own, and an implementation can neither add nor remove its public contract's bound"),
            EffectRejection.CallableForm => ("the effect list is not under a single Callable Constraint", "Write other requirements in separate clauses; parentheses and combined requirements accept no effect list"),
            EffectRejection.CallablePosition => ("a Callable effect bound in an excluded position", "Callable bounds belong to function, struct, enum or function-requirement Constraint regions"),
            EffectRejection.CallableDuplicate => ($"the Callable Constraint already declares {spelling}", "Each effect bound kind appears once in one Callable Constraint"),
            EffectRejection.NotContract => ($"{selector} names no Contract", "An effect specification names a requirement of an ancestor through the ancestor's Contract selector"),
            EffectRejection.NotReference => ($"{selector} is not a reference to {rejection.Symbol!.Name}", $"The Type arguments of {selector} do not form a reference to {rejection.Symbol!.Name}"),
            EffectRejection.OwnContract => ($"{selector} is this Contract", "A Contract bounds its own requirement with an effect clause in that requirement's Constraint region"),
            EffectRejection.NotAncestor => ($"{selector} is not an ancestor of {rejection.Symbol!.Name}", "An effect specification bounds a requirement that the Contract inherits"),
            EffectRejection.AmbiguousAncestor => ($"{selector} names {rejection.Count} ancestors", $"The Contract refines {rejection.Count} references of {rejection.Symbol!.Name}"),
            EffectRejection.NoRequirement => ($"{rejection.Symbol!.Name} has no function requirement {name}", null),
            EffectRejection.OverloadedRequirement => ($"{name} names {rejection.Count} function requirements of {rejection.Symbol!.Name}", "An effect specification bounds exactly one function requirement"),
            EffectRejection.Duplicate => ($"{name} already has {spelling}", $"{earlierOwner} already declares {spelling} for {name}; each declaration supplies a bound once, though a Contract may restate a bound of an ancestor"),
            _ => throw new UnreachableException(),
        };

        var related = rejection.Requirement is null && rejection.Earlier is null ? null : new (string Role, Koto At, string? Label)[(rejection.Requirement is null ? 0 : 1) + (rejection.Earlier is null ? 0 : 1)];
        if (rejection.Requirement is { } target)
        {
            related![0] = ("requirement", target, "the bounded requirement");
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

    // Share cause text across Contract, Callable and virtual implementation diagnostics.
    private static string EffectCause(EffectViolation kind, bool callable = false) => kind switch
    {
        EffectViolation.MutableStatic => "a mutable static access",
        EffectViolation.ExternalOperation => "an external operation",
        EffectViolation.ForeignCall => "a foreign function call",
        EffectViolation.StaticPointer => "a raw pointer read from an immutable static",
        EffectViolation.IntegerPointer => "a pointer made from an integer",
        EffectViolation.UnboundedRequirement => callable ? "a requirement call without the required guarantee" : "a requirement call with unknown effects",
        EffectViolation.UnboundedVirtual => "a virtual call without the required public effect guarantee",
        EffectViolation.UnknownDestruction => callable ? "destruction with unknown effects" : "a destruction with unknown effects",
        EffectViolation.MissingCallablePremise => "the enclosing premise does not declare the bound",
        EffectViolation.ErasedCallable => "a common Function Type whose erasure keeps no bounds",
        _ => "a call with unknown effects",
    };

    // The violating effect and the public bound it must satisfy; text is formed only during publication.
    private readonly record struct EffectViolationRecord(EffectViolation Kind, Koto? Site, Koto? Node, FunctionKoto Requirement, BoundContract Contract, BoundRequirement Identity);

    private readonly record struct EffectBoundRejection(EffectRejection Kind, Koto? Requirement = null, EffectBoundKoto? Earlier = null, BindingSymbol? Symbol = null, int Count = 0);
}

// One premise or public guarantee behind an available effect bound, collected only for Hover provenance.
internal readonly record struct EffectEvidence(EffectBoundKoto Bound, Koto Context, BoundConstraint? Premise = null, Koto? Clause = null, BoundType? Conforming = null, BindingSymbol? Contract = null);
