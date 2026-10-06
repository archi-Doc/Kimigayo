// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly List<IsKoto> callableEffectClauses = new();
    private readonly Dictionary<Koto, CallableEffectViolation> callableEffectViolations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Koto, (IsKoto Clause, EffectBoundKoto Bound, FunctionKoto Requirement)> callablePremiseFailures = new(ReferenceEqualityComparer.Instance);
    private EffectSummary? callableEffectSummary;

    /// <summary>Gets explicit bounds available on a Callable subject and signature under the local premises.</summary>
    /// <param name="type">The callable Type, without its acquisition layer.</param>
    /// <param name="signature">The selected public signature.</param>
    /// <param name="receiver">The selected receiver acquisition.</param>
    /// <param name="at">The call or binding whose premises apply.</param>
    /// <returns>The bounds available from matching clauses with an equal or stronger receiver.</returns>
    internal (bool Confined, bool Preserves) AvailableCallableEffects(BoundType type, BoundType signature, SemanticsKind receiver, Koto at)
        => this.AvailableCallableEffects(type, signature, receiver, this.ConstraintScope(at));

    /// <summary>Discharges selected-use bounds after ownership has supplied cleanup plans.</summary>
    /// <param name="rejected">The located failed arguments and Type formations.</param>
    internal void ValidateCallableEffects(List<Koto> rejected)
    {
        this.callableEffectViolations.Clear();
        if (this.callableEffectClauses.Count == 0)
        {
            return;
        }

        this.callableEffectSummary?.BeginPass();
        for (var i = 0; i < this.nodes.Count; i++)
        {
            var node = this.nodes[i];
            if (node is InvocationKoto { BoundCall: { Target.Declaration: FunctionKoto function } call })
            {
                this.CheckCallableEffectUses(node, function.TypeConstraints, function, call.TypeArguments, call.DeclaringType, call, call.LengthArguments);
            }

            if (node.BoundType is { Kind: BoundTypeKind.FunctionItem, Symbol.Declaration: FunctionKoto referenced } item &&
                node.Parent?.BoundType != item && node is not FunctionKoto)
            {
                var slots = referenced.GenericArguments.Count;
                if (item.Components.Count >= ItemTypeArgumentCount(referenced))
                {
                    var arguments = this.typeScratch.Rent(slots);
                    try
                    {
                        CopyItemArguments(item, referenced, arguments);
                        this.CheckCallableEffectUses(node, referenced.TypeConstraints, referenced, arguments.AsSpan(0, slots), ItemDeclaringType(item, referenced), null, item.LengthArguments);
                    }
                    finally
                    {
                        this.typeScratch.Return(arguments, clearArray: true);
                    }
                }
            }

            if (node.BoundType is { Kind: BoundTypeKind.Constructed, Symbol.Declaration: DeclarationContainerKoto container } type &&
                node.Parent?.BoundType != type)
            {
                this.CheckCallableEffectUses(node, container.ConstraintNodes, container, (BoundType[])type.Components, null, null);
            }
        }

        foreach (var pair in this.callableEffectViolations)
        {
            rejected.Add(pair.Key);
        }
    }

    /// <summary>Reports a selected callable argument's failed bound using its declaration and first effect as evidence.</summary>
    /// <param name="use">The located argument or Type formation.</param>
    /// <param name="requirement">The Ownership diagnostic requirement.</param>
    /// <returns>Whether a stored violation was reported.</returns>
    internal bool ReportCallableEffectViolation(Koto use, Kimi.Diagnostics.DiagnosticRequirement requirement)
    {
        if (!this.callableEffectViolations.TryGetValue(use, out var violation))
        {
            return false;
        }

        var spelling = EffectBoundKoto.Spelling(violation.Bound.Bound);
        var cause = violation.Kind switch
        {
            EffectViolation.MutableStatic => "a mutable static access",
            EffectViolation.ExternalOperation => "an external operation",
            EffectViolation.ForeignCall => "a foreign function call",
            EffectViolation.StaticPointer => "a raw pointer read from an immutable static",
            EffectViolation.IntegerPointer => "a pointer made from an integer",
            EffectViolation.ResultLoan => "an access to a Loan an earlier result may keep",
            EffectViolation.MissingCallablePremise => "the enclosing premise does not declare the bound",
            EffectViolation.ErasedCallable => "a common Function Type whose erasure keeps no bounds",
            EffectViolation.UnknownDestruction => "destruction with unknown effects",
            EffectViolation.UnboundedRequirement => "a requirement call without the required guarantee",
            _ => "a call with unknown effects",
        };
        var related = violation.Node is { } effect
            ? new (string Role, Koto At, string? Label)[] { ("bound", violation.Clause, "the bounded Callable Constraint"), ("effect", effect, "the violating effect") }
            : [("bound", (Koto)violation.Clause, (string?)"the bounded Callable Constraint")];
        var advice = violation.Kind switch
        {
            EffectViolation.MissingCallablePremise => "Declare the required effect bound on the enclosing Callable premise",
            EffectViolation.ErasedCallable => "Keep the concrete Function Item or Closure Type when passing the callable; conversion to a common Function Type erases its effect guarantees",
            _ => "Pass mutable state as an argument or capture; remove an editable bound only if its body can be verified without that guarantee",
        };
        use.Report(
            requirement,
            DiagnosticCode.UnsatisfiedEffectBound_Kd,
            evidence: [$"{spelling}: {cause}"],
            note: "Effect bounds are checked after selection; a failed bound never selects another overload",
            advice: advice,
            related: related);
        return true;
    }

    private static bool ReceiverCovers(SemanticsMask premise, SemanticsKind receiver)
        => premise == SemanticsMask.Owner || receiver == SemanticsKind.Ref || (premise == SemanticsMask.Uniq && receiver == SemanticsKind.Uniq);

    private static BoundType? CallableCore(BoundType? type)
        => type is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } ? type.Components[0] : type;

    // Bounds are attached to clauses, never to interned propositions or applicability facts (SPEC 8.4.10.7).
    private void PrepareCallableEffects()
    {
        this.callableEffectClauses.Clear();
        this.callablePremiseFailures.Clear();
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is not IsKoto { EffectBounds.Count: > 0 } clause)
            {
                continue;
            }

            this.callableEffectClauses.Add(clause);
            var kinds = 0;
            for (var e = 0; e < clause.EffectBounds.Count; e++)
            {
                var effect = clause.EffectBounds[e];
                EffectRejection? rejection = null;
                if (clause.Right is not GenericsKoto || clause.BoundConstraint?.Kind != ConstraintKind.Callable || effect.IsSpecification)
                {
                    rejection = EffectRejection.CallableForm;
                }
                else if (clause.IsAssociatedConstraint || clause.Parent is not (FunctionKoto or StructKoto or EnumKoto))
                {
                    rejection = EffectRejection.CallablePosition;
                }
                else if ((kinds & (1 << (int)effect.Bound)) != 0)
                {
                    rejection = EffectRejection.CallableDuplicate;
                }
                else if (effect.Bound == EffectBoundKind.PreservesResults && clause.BoundConstraint.Mask == SemanticsMask.Owner)
                {
                    rejection = EffectRejection.CallableReceiver;
                }

                kinds |= 1 << (int)effect.Bound;
                if (rejection is { } invalid)
                {
                    this.RejectEffectBound(effect, new(invalid, Requirement: clause));
                }

                this.BindEffectBound(effect);
            }
        }
    }

    private bool ReportCallablePremiseFailure(Koto use, Kimi.Diagnostics.DiagnosticRequirement diagnostic)
    {
        if (!this.callablePremiseFailures.TryGetValue(use, out var failure))
        {
            return false;
        }

        var spelling = EffectBoundKoto.Spelling(failure.Bound.Bound);
        use.Report(
            diagnostic,
            DiagnosticCode.IncompatibleContractImplementation_Kd,
            evidence: [$"the implementation requires {spelling}, but the premises of requirement {failure.Requirement.Name} do not declare it"],
            note: "An implementation must be callable under its requirement's declared premises",
            advice: $"Remove {spelling} from the implementation's Callable Constraint and verify its body without that guarantee, or declare it in the requirement's Callable premise",
            related: [("bound", failure.Clause, "the implementation's bounded Callable Constraint"), ("requirement", failure.Requirement, "the requirement whose premises lack the bound")]);
        return true;
    }

    private (bool Confined, bool Preserves) AvailableCallableEffects(BoundType type, BoundType signature, SemanticsKind receiver, BindingScope scope, List<EffectEvidence>? evidence = null)
    {
        var confined = false;
        var preserves = false;
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { Invalid: false } environment)
            {
                continue;
            }

            for (var i = 0; i < this.callableEffectClauses.Count; i++)
            {
                var clause = this.callableEffectClauses[i];
                if (!ReferenceEquals(clause.Parent, current.Owner) || clause.BoundConstraint is not { Kind: ConstraintKind.Callable } fact ||
                    !this.AvailableConstraintFact(environment, fact) ||
                    !ReferenceEquals(this.CallableContractType(fact.Subject!, scope), this.CallableContractType(type, scope)) ||
                    !SameCallableSignature(this.CallableContractType(fact.RequiredType!, scope), this.CallableContractType(signature, scope)) ||
                    !ReceiverCovers(fact.Mask, receiver))
                {
                    continue;
                }

                for (var e = 0; e < clause.EffectBounds.Count; e++)
                {
                    var effect = clause.EffectBounds[e];
                    if (this.effectBoundRejections?.ContainsKey(effect) == true || IsRecovery(effect, out _))
                    {
                        continue;
                    }

                    confined |= effect.Bound == EffectBoundKind.Confined;
                    preserves |= effect.Bound == EffectBoundKind.PreservesResults;
                    evidence?.Add(new(effect, current.Owner, fact, clause));
                }
            }
        }

        return (confined, preserves);
    }

    private void CheckCallableEffectUses(Koto use, IReadOnlyList<Koto> clauses, Koto binder, ReadOnlySpan<BoundType?> arguments, BoundType? declaring, BoundCall? call, ReadOnlySpan<BoundLength?> lengths = default)
    {
        var scope = this.ConstraintScope(use);
        for (var i = 0; i < clauses.Count; i++)
        {
            if (clauses[i] is not IsKoto { EffectBounds.Count: > 0, BoundConstraint: { Kind: ConstraintKind.Callable } fact } clause)
            {
                continue;
            }

            var substituted = this.SubstituteConstraint(fact, binder, arguments, lengths);
            if (declaring?.Symbol?.Declaration is { } owner)
            {
                substituted = this.SubstituteConstraint(substituted, owner, (BoundType[])declaring.Components);
            }

            substituted = this.ContractConstraint(substituted, scope, null);
            var subject = substituted.Subject!;
            var receiver = substituted.Mask == SemanticsMask.Ref ? SemanticsKind.Ref : substituted.Mask == SemanticsMask.Uniq ? SemanticsKind.Uniq : SemanticsKind.Owner;
            var at = use;
            var written = KotoHelper.UnwrapParentheses(use is InvocationKoto invocation ? invocation.Method : use);
            if (written is GenericsKoto generic && fact.Subject?.Symbol is { Slot: var slot } &&
                (uint)slot < (uint)generic.TypeArguments.Count && generic.TypeArguments[slot].BoundType is { } argumentType &&
                ReferenceEquals(this.ContractType(argumentType, scope), subject))
            {
                at = generic.TypeArguments[slot];
            }

            if (call is not null)
            {
                for (var a = 0; a < call.ArgumentOperations.Length; a++)
                {
                    var argument = call.ArgumentOperations[a];
                    if (argument.Source is { } source && CallableCore(argument.ParameterType) is { } parameter &&
                        ReferenceEquals(parameter, subject))
                    {
                        at = source;
                        break;
                    }
                }
            }

            for (var e = 0; e < clause.EffectBounds.Count; e++)
            {
                var bound = clause.EffectBounds[e];
                if (this.effectBoundRejections?.ContainsKey(bound) == true || IsRecovery(bound, out _))
                {
                    continue;
                }

                if (this.CallableEffectFailure(subject, substituted.RequiredType!, receiver, bound.Bound, use) is { } failure)
                {
                    this.callableEffectViolations.TryAdd(at, new(clause, bound, failure.Kind, failure.Node));
                }
            }
        }
    }

    private (EffectViolation Kind, Koto? Node)? CallableEffectFailure(BoundType type, BoundType signature, SemanticsKind receiver, EffectBoundKind bound, Koto use)
    {
        type = CallableCore(type)!;
        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection)
        {
            var available = this.AvailableCallableEffects(type, signature, receiver, use);
            return (bound == EffectBoundKind.Confined ? available.Confined : available.Preserves) ? null : (EffectViolation.MissingCallablePremise, null);
        }

        if (type.Kind == BoundTypeKind.Function)
        {
            return (EffectViolation.ErasedCallable, null);
        }

        if (type.Symbol is not { Declaration: FunctionKoto function } symbol)
        {
            return (EffectViolation.UnclassifiedCall, null);
        }

        if (function.IsRequirement)
        {
            return (EffectViolation.UnboundedRequirement, function);
        }

        var summary = this.callableEffectSummary ??= new(this);
        return summary.Check(bound == EffectBoundKind.Confined, bound == EffectBoundKind.PreservesResults, symbol, this.ConstraintScope(use), true, type, receiver)
            ? null : (summary.Violation, summary.ViolationNode);
    }

    private readonly record struct CallableEffectViolation(IsKoto Clause, EffectBoundKoto Bound, EffectViolation Kind, Koto? Node);
}
