// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 8.4.10.4: every bound declared in this pass, for requirement calls whose conforming Type is concrete.
    private readonly List<BoundEffectBound> boundedRequirements = [];

    // The Contracts of this pass, recorded when their shapes are built, so the bound tables need no second scan of the nodes.
    private readonly List<BoundContract> contractShapes = [];

    // SPEC 8.4.10.6: the first violating effect of each conformance a bound rejects, keyed by its Self clause; recorded only
    // when one is rejected and reused across passes.
    private Dictionary<Koto, EffectViolationRecord>? effectViolations;

    // SPEC 8.4.10.6: the cause of each rejected effect item, recorded only when one fails. The store is reused across passes,
    // so a warm rebind allocates nothing.
    private Dictionary<EffectBoundKoto, EffectBoundRejection>? effectBoundRejections;
    private EffectSyntaxCompleter? effectSyntax;

    /// <summary>SPEC 8.4.10.6: the kind of the first effect of an implementation that violates a bound.</summary>
    internal enum EffectViolation : byte
    {
        /// <summary>No violation.</summary>
        None,

        /// <summary>An access to a mutable static Field, which confined excludes.</summary>
        MutableStatic,

        /// <summary>A standard operation on state outside the program, such as Console output, which confined excludes.</summary>
        ExternalOperation,

        /// <summary>A call of a foreign function, an environment effect that confined excludes (SPEC 22.3.1).</summary>
        ForeignCall,

        /// <summary>A read of a raw pointer from an immutable static, which obtains environment authority.</summary>
        StaticPointer,

        /// <summary>A conversion of an integer to a pointer, which obtains environment authority.</summary>
        IntegerPointer,

        /// <summary>An access that may conflict with a Loan an earlier result keeps, which preserves results excludes.</summary>
        ResultLoan,

        /// <summary>A call whose effects cannot be classified, treated as a conflict.</summary>
        UnclassifiedCall,

        /// <summary>A requirement call that no available bound covers, treated as a conflict.</summary>
        UnboundedRequirement,

        /// <summary>An access whose Loans cannot be classified, treated as a conflict.</summary>
        UnclassifiedAccess,

        /// <summary>A destruction whose effects cannot be classified, treated as a conflict.</summary>
        UnknownDestruction,
    }

    /// <summary>SPEC 8.4.10.5: why the Loans of earlier results were not excluded for a requirement call.</summary>
    internal enum DelegationFailure : byte
    {
        /// <summary>The Loans were excluded, or exclusion was not tried.</summary>
        None,

        /// <summary>The call is not made on a value reached through a Field path of self in the implementation's own body.</summary>
        CallSite,

        /// <summary>The body replaces, swaps or moves a value on the path to the called value.</summary>
        Replaced,

        /// <summary>An earlier result may keep the Loans of an argument.</summary>
        Argument,

        /// <summary>An earlier result may come from another requirement.</summary>
        OtherRequirement,

        /// <summary>An earlier result may come from the same requirement on another value.</summary>
        OtherValue,

        /// <summary>An earlier result may come from a source that cannot be traced to a call.</summary>
        Untraced,
    }

    // SPEC 8.4.10.6: why an effect item declares no bound.
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
        NoBorrowedReceiver,
        DependentResult,
    }

    /// <summary>
    /// Gets the bound that a Contract or one of its ancestors declares for a requirement (SPEC 8.4.10.1): <c>T is C</c>
    /// guarantees exactly these.
    /// </summary>
    /// <param name="shape">The Contract.</param>
    /// <param name="requirement">The Requirement Identity: the requirement's declaration.</param>
    /// <param name="bound">The bound.</param>
    /// <returns>The declaring effect item, or <see langword="null"/>.</returns>
    internal EffectBoundKoto? DeclaredEffectBound(BoundContract shape, FunctionKoto requirement, EffectBoundKind bound)
    {
        if (OwnEffectBound(shape, requirement, bound) is { } own)
        {
            return own;
        }

        for (var i = 0; i < shape.Ancestors.Count; i++)
        {
            if (shape.Ancestors[i].Declaration.BoundSymbol?.Contract is { } ancestor && OwnEffectBound(ancestor, requirement, bound) is { } inherited)
            {
                return inherited;
            }
        }

        return null;
    }

    /// <summary>
    /// SPEC 8.4.10.4: the bounds available to a requirement call: those that the Contracts its premises prove for the conforming
    /// Type declare for the Requirement Identity, through constraint facts in scope or a concrete Type's conformance. Bounds
    /// are never searched for in implementations.
    /// </summary>
    /// <param name="requirement">The Requirement Identity.</param>
    /// <param name="conforming">The conforming Type of the call.</param>
    /// <param name="scope">The scope whose premises hold at the call.</param>
    /// <returns>Whether confined and preserves results are available.</returns>
    internal (bool Confined, bool Preserves) AvailableEffectBounds(FunctionKoto requirement, BoundType? conforming, BindingScope scope)
    {
        var bounded = false;
        for (var i = 0; !bounded && i < this.boundedRequirements.Count; i++)
        {
            bounded = ReferenceEquals(this.boundedRequirements[i].Requirement.Declaration, requirement);
        }

        if (!bounded || conforming is null)
        {
            return default;
        }

        var confined = false;
        var preserves = false;
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { Invalid: false } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (fact.Kind == ConstraintKind.Contract && ReferenceEquals(fact.Subject, conforming) && fact.Contract?.Declaration.BoundSymbol?.Contract is { HasEffectBounds: true } shape &&
                    this.AvailableConstraintFact(environment, fact))
                {
                    Add(shape);
                }
            }
        }

        if (conforming.Symbol is { Kind: BindingSymbolKind.Type } type)
        {
            // A concrete Type proves each Contract it conforms to.
            for (var i = 0; i < this.boundedRequirements.Count; i++)
            {
                var entry = this.boundedRequirements[i];
                if (ReferenceEquals(entry.Requirement.Declaration, requirement) && DeclaringContract(entry.Declaration)?.BoundSymbol is { } contract &&
                    this.ConformanceByDeclaration(type, contract, out _) is not null)
                {
                    confined |= entry.Bound == EffectBoundKind.Confined;
                    preserves |= entry.Bound == EffectBoundKind.PreservesResults;
                }
            }
        }

        return (confined, preserves);

        void Add(BoundContract shape)
        {
            confined |= this.DeclaredEffectBound(shape, requirement, EffectBoundKind.Confined) is not null;
            preserves |= this.DeclaredEffectBound(shape, requirement, EffectBoundKind.PreservesResults) is not null;
        }
    }

    /// <summary>Gets the bounds available to a requirement call under the premises in scope at a node (SPEC 8.4.10.4).</summary>
    /// <param name="requirement">The Requirement Identity.</param>
    /// <param name="conforming">The conforming Type of the call.</param>
    /// <param name="at">The call.</param>
    /// <returns>Whether confined and preserves results are available.</returns>
    internal (bool Confined, bool Preserves) AvailableEffectBounds(FunctionKoto requirement, BoundType? conforming, Koto at)
        => this.AvailableEffectBounds(requirement, conforming, this.ConstraintScope(at));

    /// <summary>
    /// SPEC 8.4.10.6: reports a conformance that a bound rejects at the first violating effect in the implementation's own body.
    /// The Reason names the violation, the bound and its declaring Contract; the Note tells a definite violation from an
    /// unknown effect counted as a conflict; the related locations give the effect, the conformance and the bound.
    /// </summary>
    /// <param name="use">The Self clause of the conformance.</param>
    /// <param name="requirement">The failed requirement of the record.</param>
    /// <param name="code">The code to report.</param>
    /// <returns><see langword="true"/> when a violation was recorded for the conformance and reported.</returns>
    internal bool ReportEffectViolation(Koto use, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        if (this.effectViolations?.TryGetValue(use, out var violation) != true)
        {
            return false;
        }

        var name = violation.Requirement.Name;
        var bound = violation.Kind switch
        {
            EffectViolation.MutableStatic or EffectViolation.ExternalOperation or EffectViolation.ForeignCall or EffectViolation.StaticPointer or EffectViolation.IntegerPointer => EffectBoundKind.Confined,
            EffectViolation.ResultLoan => EffectBoundKind.PreservesResults,
            _ => violation.Preserves ? EffectBoundKind.PreservesResults : EffectBoundKind.Confined,
        };
        var item = this.DeclaredEffectBound(violation.Contract, violation.Requirement, bound);
        var contract = item is null ? violation.Contract.Symbol.Name : DeclaringContract(item)?.BoundSymbol?.Name ?? violation.Contract.Symbol.Name;
        var spelling = EffectBoundKoto.Spelling(bound);

        // SPEC 8.4.10.6: an environment effect is named by the operation that obtained the authority (SPEC 8.4.10.2).
        var effect = violation.Kind switch
        {
            EffectViolation.MutableStatic => "a mutable static access",
            EffectViolation.ExternalOperation => "an external operation",
            EffectViolation.ForeignCall => "a foreign function call",
            EffectViolation.StaticPointer => "a raw pointer read from an immutable static",
            EffectViolation.IntegerPointer => "a pointer made from an integer",
            EffectViolation.ResultLoan => "an access to a Loan the result may keep",
            EffectViolation.UnboundedRequirement => "a requirement call with unknown effects",
            EffectViolation.UnclassifiedAccess => "an access with unknown Loans",
            EffectViolation.UnknownDestruction => "a destruction with unknown effects",
            _ => "a call with unknown effects",
        };
        var definite = violation.Kind is EffectViolation.MutableStatic or EffectViolation.ExternalOperation or EffectViolation.ForeignCall or
            EffectViolation.StaticPointer or EffectViolation.IntegerPointer or EffectViolation.ResultLoan;

        // SPEC 8.4.10.5: why the Loans of earlier results were not excluded, with the source or replacement as a related location.
        var (delegation, label) = violation.Delegation switch
        {
            DelegationFailure.CallSite => ("; the Loans of earlier results are not excluded, because the call is not made on a value reached through a Field path of self in the implementation's own body (SPEC 8.4.10.5)", null),
            DelegationFailure.Replaced => ("; the Loans of earlier results are not excluded, because the body replaces, swaps or moves a value on the path to the called value (SPEC 8.4.10.5)", "the replaced value on the path"),
            DelegationFailure.Argument => ("; the Loans of earlier results are not excluded, because an earlier result may keep the Loans of an argument (SPEC 8.4.10.5)", "the argument an earlier result may keep"),
            DelegationFailure.OtherRequirement => ("; the Loans of earlier results are not excluded, because an earlier result may come from another requirement (SPEC 8.4.10.5)", "the other source of a result"),
            DelegationFailure.OtherValue => ("; the Loans of earlier results are not excluded, because an earlier result may come from the same requirement on another value (SPEC 8.4.10.5)", "the other source of a result"),
            DelegationFailure.Untraced => ("; the Loans of earlier results are not excluded, because the source of a result cannot be traced to a call on a Field path of self (SPEC 8.4.10.5)", "the untraced source of a result"),
            _ => (string.Empty, (string?)null),
        };
        var note = (definite
            ? $"{name} must satisfy {spelling}, declared by {contract}, and this effect violates it"
            : $"{name} must satisfy {spelling}, declared by {contract}; this effect cannot be classified, so verification treats it as a conflict (SPEC 8.4.5)") + delegation;
        var editable = item is not null && !ReferenceEquals(item.CodeContext.Kotonoha, this.Library.Kotonoha);
        var advice = (bound == EffectBoundKind.Confined
            ? "Keep the state in a Field of self or pass it as a parameter, so the implementation uses only authority from its inputs"
            : "Avoid accesses that may reach a Loan the result keeps, or return only results of the same requirement on one value reached through a Field path of self") +
            (editable ? $". If no caller relies on the guarantee, {contract} may instead declare no bound, which affects the callers that do" : string.Empty);
        var source = label is not null && violation.DelegationNode is { } delegationNode && !ReferenceEquals(delegationNode, violation.Site) ? delegationNode : null;
        var count = (violation.Node is { } node && !ReferenceEquals(node, violation.Site) ? 1 : 0) + (source is null ? 0 : 1) + 1 + (item is null ? 0 : 1);
        var related = new (string Role, Koto At, string? Label)[count];
        var next = 0;
        if (violation.Node is { } effectNode && !ReferenceEquals(effectNode, violation.Site))
        {
            related[next++] = ("effect", effectNode, "the violating effect");
        }

        if (source is not null)
        {
            related[next++] = ("source", source, label);
        }

        related[next++] = ("conformance", use, "the conformance checked against the bound");
        if (item is not null)
        {
            related[next] = ("bound", item, "the bound");
        }

        use.Report(requirement, code, note: note, at: violation.Site, evidence: [$"{effect}, which {spelling} excludes"], advice: advice, related: related);
        return true;
    }

    // The Contract whose bound an effect item declares: the Contract itself, or the Contract of the requirement whose
    // Constraint region holds it.
    private static ContractKoto? DeclaringContract(EffectBoundKoto effect)
        => effect.Parent switch
        {
            ContractKoto contract => contract,
            FunctionKoto { IsRequirement: true, Parent: ContractKoto contract } => contract,
            _ => null,
        };

    private static EffectBoundKoto? OwnEffectBound(BoundContract shape, FunctionKoto requirement, EffectBoundKind bound)
    {
        var bounds = shape.EffectBoundStorage;
        for (var i = 0; i < bounds.Count; i++)
        {
            if (bounds[i].Bound == bound && ReferenceEquals(bounds[i].Requirement.Declaration, requirement))
            {
                return bounds[i].Declaration;
            }
        }

        return null;
    }

    // SPEC 15.4.3: the atom of an Origin that may denote the receiver borrow: the receiver's own Origin, the receiver input that
    // an omitted Origin stands for, or such an atom within a meet.
    private static BoundOrigin? ReceiverAtom(BoundOrigin origin, FunctionKoto function, int receiver, BoundOrigin? receiverOrigin)
    {
        if (origin.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (ReceiverAtom(origin.Operands[i], function, receiver, receiverOrigin) is { } atom)
                {
                    return atom;
                }
            }

            return null;
        }

        return (origin.Kind == OriginKind.Input && ReferenceEquals(origin.Binder, function) && origin.InputIndex == receiver) ||
            (receiverOrigin is not null && (ReferenceEquals(origin, receiverOrigin) ||
                (origin.Kind == receiverOrigin.Kind && ReferenceEquals(origin.Binder, receiverOrigin.Binder) && origin.Slot == receiverOrigin.Slot &&
                    origin.InputIndex == receiverOrigin.InputIndex && origin.Name == receiverOrigin.Name)))
            ? origin : null;
    }

    // SPEC 8.4.10.3: the outermost part of a canonical result Type that may depend on the receiver borrow, with the atom it
    // names. An omitted borrow-layer Origin, with no atom yet or completed as the receiver input, defaults to the meet of
    // every direct borrowed input, the receiver included (SPEC 15.4.3).
    private static BoundType? ReceiverDependentPart(BoundType type, FunctionKoto function, int receiver, BoundOrigin? receiverOrigin, out BoundOrigin? atom)
    {
        atom = null;
        if (type.Origin is null && (ReferenceTypes.IsBorrow(type) || ObjectTypes.IsBorrow(type)))
        {
            return type;
        }

        if (type.Origin is { } origin && (atom = ReceiverAtom(origin, function, receiver, receiverOrigin)) is not null)
        {
            return type;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if ((atom = ReceiverAtom(type.OriginArguments[i], function, receiver, receiverOrigin)) is not null)
            {
                return type;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (ReceiverDependentPart(type.Components[i], function, receiver, receiverOrigin, out atom) is { } part)
            {
                return part;
            }
        }

        return null;
    }

    // SPEC 8.4.10.1: the bounds of every Contract, checked once per pass after the requirement headers are bound and before
    // conformances are verified against them.
    private void PrepareEffectBounds()
    {
        this.effectBoundRejections?.Clear();
        this.boundedRequirements.Clear();
        for (var i = 0; i < this.contractShapes.Count; i++)
        {
            this.EnsureEffectBounds(this.contractShapes[i]);
        }
    }

    // A Contract's ancestors are checked first, so a bound already declared above is known (SPEC 8.4.10.1).
    private void EnsureEffectBounds(BoundContract shape)
    {
        if (shape.EffectState != 0 || shape.Symbol.Declaration is not ContractKoto contract)
        {
            return;
        }

        shape.EffectState = 1;
        shape.HasEffectBounds = false;
        for (var i = 0; i < shape.Ancestors.Count; i++)
        {
            if (shape.Ancestors[i].Declaration.BoundSymbol?.Contract is { } ancestor)
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
                        this.DeclareEffectBound(shape, clause, requirement, scope);
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
                    this.DeclareEffectBound(shape, effect, target, scope);
                }
            }
        }

        shape.EffectState = 2;
    }

    private void DeclareEffectBound(BoundContract shape, EffectBoundKoto effect, BindingSymbol requirement, BindingScope scope)
    {
        // SPEC 8.4.10.1: a bound declared twice in one Contract is an error; restating an ancestor's bound forms one guarantee.
        var function = (FunctionKoto)requirement.Declaration;
        if (OwnEffectBound(shape, function, effect.Bound) is { } earlier)
        {
            this.RejectEffectBound(effect, new(EffectRejection.Duplicate, Requirement: function, Earlier: earlier));
            return;
        }

        if (effect.Bound == EffectBoundKind.PreservesResults && this.PreservesResultsRejection(requirement, scope) is { } rejection)
        {
            this.RejectEffectBound(effect, rejection);
            return;
        }

        shape.EffectBoundStorage.Add(new(requirement, effect.Bound, effect));
        shape.HasEffectBounds = true;
        this.boundedRequirements.Add(new(requirement, effect.Bound, effect));
    }

    // SPEC 8.4.10.1: the selector names an ancestor of the Contract, and Name exactly one function requirement of that ancestor.
    private BindingSymbol? EffectTarget(BoundContract shape, EffectBoundKoto effect, BindingScope scope)
    {
        var selector = effect.Selector!;
        var syntax = selector is ParenthesizedKoto grouped ? grouped.Operand : selector;
        if (this.TypeName(syntax, scope, false) is not { Declaration: ContractKoto declaration } named)
        {
            this.RejectEffectBound(effect, new(EffectRejection.NotContract));
            return null;
        }

        BoundType? reference = null;
        if (GenericParentApplication(syntax) is { } generic)
        {
            var arguments = this.BindTypeList(generic, generic.TypeArguments, scope, this.TypeContext(generic, scope).Nested, BoundTypeKind.Constructed, named);
            if (arguments is null)
            {
                this.RejectEffectBound(effect, new(EffectRejection.NotReference, Symbol: named));
                return null;
            }

            reference = this.InternType(BoundTypeKind.Constructed, named, SemanticsKind.Owner, (BoundType[])arguments.Components);
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
        BindingSymbol? target = null;
        var found = 0;
        var requirements = ancestor.Contract!.Requirements;
        for (var i = 0; i < requirements.Count; i++)
        {
            if (requirements[i].Declaration is FunctionKoto && requirements[i].Name == name)
            {
                target = requirements[i];
                found++;
            }
        }

        if (target is null || found > 1)
        {
            this.RejectEffectBound(effect, new(target is null ? EffectRejection.NoRequirement : EffectRejection.OverloadedRequirement, Requirement: target?.Declaration, Symbol: ancestor, Count: found));
            return null;
        }

        effect.Name!.BoundSymbol = target;
        return target;
    }

    // SPEC 8.4.10.3: preserves results needs a borrowed receiver and a result that depends neither on the receiver Loan nor
    // on the receiver's Storage under the declaring Contract's premises. One judgement on the canonical result Type.
    private EffectBoundRejection? PreservesResultsRejection(BindingSymbol requirement, BindingScope scope)
    {
        var function = (FunctionKoto)requirement.Declaration;
        var receiver = function.BoundSymbol?.ReceiverIndex ?? -1;
        if ((uint)receiver >= (uint)function.Parameters.Count ||
            function.Parameters[receiver].Type.BoundType is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq } receiverType)
        {
            return new(EffectRejection.NoBorrowedReceiver, Requirement: function);
        }

        if ((requirement.Type ?? function.BoundSymbol!.Type) is not { } declared)
        {
            return null; // The signature's own failure explains it.
        }

        var result = this.ContractType(declared, scope);
        return ReceiverDependentPart(result, function, receiver, receiverType.Origin, out var atom) is { } part
            ? new(EffectRejection.DependentResult, Requirement: function, Part: part, Result: result, Atom: atom)
            : null;
    }

    private void RejectEffectBound(EffectBoundKoto effect, EffectBoundRejection rejection)
        => (this.effectBoundRejections ??= new(ReferenceEqualityComparer.Instance))[effect] = rejection;

    // SPEC 8.4.10.1: an effect item declares a bound of its Contract. A rejected bound word leaves a recovery whose checks
    // rest on the syntax Error; an item outside a Contract declares nothing.
    private BoundType? BindEffectBound(EffectBoundKoto effect)
    {
        if (effect.Selector is { } selector)
        {
            // The selector and Name designate declarations; their failures are the effect item's (SPEC 8.4.10.6).
            this.effectSyntax ??= new();
            this.effectSyntax.Visit(selector);
            this.effectSyntax.Visit(effect.Name!);
        }

        if (effect.CodeContext.RecoveryCause(effect) is not null)
        {
            return Complete(effect, null);
        }

        if (DeclaringContract(effect) is null)
        {
            this.RejectEffectBound(effect, new(EffectRejection.OutsideContract));
        }

        return this.effectBoundRejections?.ContainsKey(effect) == true ? this.Fail(effect, BindingFailure.InvalidEffectBound) : Complete(effect, BoundType.Unit);
    }

    // SPEC 8.4.10.6: the Reason names the cause, the Note its path and the related locations the target requirement and an
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
        var spelling = EffectBoundKoto.Spelling(effect.Bound);
        var (cause, note, advice) = rejection.Kind switch
        {
            EffectRejection.SpecificationInRequirement => ("an effect specification in a requirement", "An effect specification names an inherited requirement and is a Contract item; a requirement's own bound is an effect clause", $"Write effect {spelling} to bound {name} itself"),
            EffectRejection.ClauseOutsideRequirement => ("an effect clause outside a requirement", "An effect clause stands in the Constraint region of the requirement it bounds", "Indent the clause under its requirement, or name an inherited requirement as effect Contract.name"),
            EffectRejection.OutsideContract => ("an effect item outside a Contract", $"Only a Contract declares bounds; {EffectOwner(effect)} accepts no effect item, and an implementation can neither add nor remove one", null),
            EffectRejection.NotContract => ($"{selector} names no Contract", "An effect specification names a requirement of an ancestor through the ancestor's Contract selector", null),
            EffectRejection.NotReference => ($"{selector} is not a reference to {rejection.Symbol!.Name}", $"The Type arguments of {selector} do not form a reference to {rejection.Symbol!.Name}", null),
            EffectRejection.OwnContract => ($"{selector} is this Contract", "A Contract bounds its own requirement with an effect clause in that requirement's Constraint region", "Write the bound as an effect clause in the Constraint region of the requirement"),
            EffectRejection.NotAncestor => ($"{selector} is not an ancestor of {rejection.Symbol!.Name}", "An effect specification bounds a requirement that the Contract inherits", null),
            EffectRejection.AmbiguousAncestor => ($"{selector} names {rejection.Count} ancestors", $"The Contract refines {rejection.Count} references of {rejection.Symbol!.Name}", $"Give the Type arguments of the intended reference, as ({rejection.Symbol!.Name}<...>).name"),
            EffectRejection.NoRequirement => ($"{rejection.Symbol!.Name} has no function requirement {name}", null, null),
            EffectRejection.OverloadedRequirement => ($"{name} names {rejection.Count} function requirements of {rejection.Symbol!.Name}", "An effect specification bounds exactly one function requirement", null),
            EffectRejection.Duplicate => ($"{name} already has {spelling}", $"{DeclaringContract(rejection.Earlier!)?.BoundSymbol?.Name} already declares {spelling} for {name}; a Contract declares each bound of a requirement once, though it may restate a bound of an ancestor", "Remove the repeated effect item"),
            EffectRejection.NoBorrowedReceiver => ($"{name} has no borrowed receiver", "preserves results needs a borrowed receiver, ref/Self or uniq/Self", null),
            _ => DependentResult(rejection),
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

        effect.Report(requirement, DiagnosticCode.InvalidEffectBound_Kd, note: note, evidence: [cause], advice: advice, related: related);

        // The dependent part, and its path: a receiver input atom is what an omitted Origin becomes, so it is explained as the
        // omission (SPEC 15.4.3). No repair is offered, because the dependency is the meaning of the API.
        static (string Cause, string? Note, string? Advice) DependentResult(in EffectBoundRejection rejection)
        {
            var shown = DiagnosticTypeName(rejection.Part!);
            var note = rejection.Atom is null or { Kind: OriginKind.Input }
                ? $"The omitted Origin of {shown} defaults to the meet of every direct borrowed input, the receiver included (SPEC 15.4.3); the result is {DiagnosticTypeName(rejection.Result!)}"
                : $"{shown} names the receiver's Origin {rejection.Atom.Name}; the result is {DiagnosticTypeName(rejection.Result!)} under the premises of the declaring Contract";
            return ($"{shown} may depend on the receiver borrow", note, null);
        }

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

    // The facts of a rejected effect item; its text is formed only when the record is published.
    // The violating effect of a rejected conformance: its kind, the own-body syntax reaching it, its node, why delegation did not
    // apply, the requirement and the bounds checked.
    private readonly record struct EffectViolationRecord(EffectViolation Kind, Koto? Site, Koto? Node, DelegationFailure Delegation, Koto? DelegationNode, FunctionKoto Requirement, BoundContract Contract, bool Confined, bool Preserves);

    private readonly record struct EffectBoundRejection(EffectRejection Kind, Koto? Requirement = null, EffectBoundKoto? Earlier = null, BindingSymbol? Symbol = null, int Count = 0, BoundType? Part = null, BoundType? Result = null, BoundOrigin? Atom = null);
}
