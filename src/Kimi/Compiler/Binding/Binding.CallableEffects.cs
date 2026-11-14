// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly List<CallableEffectUse> callableEffectUses = new(); // Each selected use of a bounded Callable clause, once per pass.
    private readonly Dictionary<Koto, (IsKoto Clause, FunctionKoto Requirement)> callablePremiseFailures = new(ReferenceEqualityComparer.Instance);

    internal static BoundType? CallableCore(BoundType? type)
        => type is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } ? type.Components[0] : type;

    /// <summary>Gets explicit bounds available on a Callable subject and signature under the local premises.</summary>
    /// <param name="type">The callable Type, without its acquisition layer.</param>
    /// <param name="signature">The selected public signature.</param>
    /// <param name="receiver">The selected receiver acquisition.</param>
    /// <param name="at">The call or binding whose premises apply.</param>
    /// <returns>Whether confined is available from matching clauses with an equal or stronger receiver.</returns>
    internal bool AvailableCallableEffects(BoundType type, BoundType signature, SemanticsKind receiver, Koto at)
        => this.AvailableCallableEffects(type, signature, receiver, this.ConstraintScope(at));

    internal bool AvailableCallableEffects(BoundType type, BoundType signature, SemanticsKind receiver, BindingScope scope, List<EffectEvidence>? evidence = null)
    {
        // SPEC 8.11.4: the evidence is a premise on the Type with the signature, an equal or stronger receiver and the bound: the
        // Type's available Callable rows whose Source is a written premise (a Contract-wide clause carries no bound list), each
        // with the bounds its clause declares in the scope stating it (ContractSolver's FirstRows).
        var confined = false;
        var required = this.CallableContractType(signature, scope);
        var start = this.premiseRows.Count;
        this.FirstRows(this.CallableContractType(type, scope), scope, this.premiseRows);
        for (var r = start; r < this.premiseRows.Count; r++)
        {
            if (this.premiseRows[r] is not { Available: true, Clause: { Kind: ConstraintKind.Callable } fact, Raw: { } raw, Stated: { } stated } ||
                !SameCallableSignature(this.CallableContractType(fact.RequiredType!, scope), required) || !ReceiverCovers(fact.Mask, receiver))
            {
                continue;
            }

            for (var i = 0; i < this.callableEffectClauses.Count; i++)
            {
                var clause = this.callableEffectClauses[i];
                if (!ReferenceEquals(clause.Parent, stated.Owner) || !ReferenceEquals(clause.BoundConstraint, raw))
                {
                    continue;
                }

                for (var e = 0; e < clause.EffectBounds.Count; e++)
                {
                    var effect = clause.EffectBounds[e];
                    if (!this.AppliesBound(effect))
                    {
                        continue;
                    }

                    confined = true;
                    evidence?.Add(new(effect, stated.Owner, raw, clause));
                }
            }
        }

        this.premiseRows.RemoveRange(start, this.premiseRows.Count - start);
        return confined;
    }

    private static bool ReceiverCovers(SemanticsMask premise, SemanticsKind receiver)
        => premise == SemanticsMask.Owner || receiver == SemanticsKind.Ref || (premise == SemanticsMask.Uniq && receiver == SemanticsKind.Uniq);

    private bool ReportCallablePremiseFailure(Koto use, Kimi.Diagnostics.DiagnosticRequirement diagnostic)
    {
        if (!this.callablePremiseFailures.TryGetValue(use, out var failure))
        {
            return false;
        }

        use.Report(
            diagnostic,
            DiagnosticCode.IncompatibleContractImplementation_Kd,
            evidence: [$"the implementation requires {Constants.ConfinedKeyword}, but the premises of requirement {failure.Requirement.Name} " +
                "do not declare it"],
            note: "An implementation must be callable under its requirement's declared premises",
            related: [("bound", failure.Clause, "the implementation's bounded Callable Constraint"), ("requirement", failure.Requirement, "the requirement whose premises lack the bound")]);
        return true;
    }

    // SPEC 8.11.4: the selected uses of bounded Callable clauses: each selected call, function reference and Type formation, located at
    // its argument or Type argument, with its substituted subject, signature and receiver; enumerated once at the end of the Binding pass.
    private void CollectCallableEffectUses()
    {
        this.callableEffectUses.Clear();
        for (var i = 0; i < this.nodes.Count && this.callableEffectClauses.Count != 0; i++)
        {
            var node = this.nodes[i];
            if (node is InvocationKoto { BoundCall: { } call } && this.Library.PresentedTarget(call.Target).Declaration is FunctionKoto function)
            {
                this.CheckCallableEffectUses(node, function.TypeConstraints, SubstitutionOf(function, call), call);
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
                        this.CheckCallableEffectUses(node, referenced.TypeConstraints, SubstitutionOf(referenced, ItemDeclaringType(item, referenced), arguments.AsSpan(0, slots), item.LengthArguments), null);
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
                this.CheckCallableEffectUses(node, container.ConstraintNodes, new CallSubstitution(container, (BoundType[])type.Components, origins: (BoundOrigin[])type.OriginArguments), null);
            }
        }
    }

    private void CheckCallableEffectUses(Koto use, IReadOnlyList<Koto> clauses, in CallSubstitution substitution, CallPlan? call)
    {
        var scope = this.ConstraintScope(use);
        for (var i = 0; i < clauses.Count; i++)
        {
            if (clauses[i] is not IsKoto { EffectBounds.Count: > 0, BoundConstraint: { Kind: ConstraintKind.Callable } fact } clause)
            {
                continue;
            }

            var substituted = this.SubstituteConstraint(fact, substitution);

            // SPEC 8.6.4: a selected requirement call's clause reads the Contract's parameters at its reference, as AvailableEffectBounds does.
            substituted = this.ContractConstraint(substituted, scope, null, reference: call?.RequirementContract);
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

            this.callableEffectUses.Add(new(at, use, clause, subject, substituted.RequiredType!, receiver));
        }
    }

    // A selected use of a bounded Callable clause: where a failure is located, the use whose premises apply, the clause, and its substituted
    // subject, signature and receiver.
    private readonly record struct CallableEffectUse(Koto At, Koto Use, IsKoto Clause, BoundType Subject, BoundType Signature, SemanticsKind Receiver);
}
