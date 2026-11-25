// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Each selected use of a bounded Callable clause, once per pass: the running pass's part (PassPart, EnterPass).
    private List<CallableEffectUse> callableEffectUses;

    // SPEC 3.4.3, 7.7.3, 8.10: the Callable a callee value designates, without its acquisition layers: below every ref and uniq layer
    // (ComparisonReferent), where selection continues, and below a concrete handle's or view's layer, whose View Target selection stops at;
    // any other Type is its own (a raw pointer is no layer selection follows, SPEC 3.4.1).
    internal static BoundType? CallableCore(BoundType? type)
    {
        var terminal = type is null ? null : ComparisonReferent(type);
        return terminal is { Kind: BoundTypeKind.Semantics, Components: [var view] } && IsObjectSemantics(terminal.Semantics) ? view : terminal;
    }

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
        // with the union of the bounds of the clauses of its fact's Statements in its environment (ContractSolver's FirstRows;
        // ConstraintEnvironment.States: the in-place one and each recorded, as an override's inherited premise's, SPEC 6.4.2 step 3).
        // The evidence names the clause's declaration and written premise.
        var confined = false;
        var required = this.CallableContractType(signature, scope);
        var start = this.premiseRows.Count;
        var classes = this.FirstRows(this.CallableContractType(type, scope), scope, this.premiseRows);
        for (var r = start; r < this.premiseRows.Count; r++)
        {
            if (this.premiseRows[r] is not { Available: true, Clause: { Kind: ConstraintKind.Callable } fact, Raw: { } raw, Stated: { } stated } ||
                !this.SameCallableSignature(this.CallableContractType(fact.RequiredType!, scope), required) || !ReceiverCovers(fact.Mask, receiver))
            {
                continue;
            }

            var clauses = this.selectableEffectClauses;
            for (var i = 0; i < clauses.Count; i++)
            {
                if (clauses[i] is not { Parent: { } owner, BoundConstraint: { } written } clause ||
                    !stated.Constraints!.States(raw, stated.Owner, owner, written))
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
                    evidence?.Add(new(effect, owner, written, clause));
                }
            }
        }

        this.premiseRows.RemoveRange(start, this.premiseRows.Count - start);
        if (!confined)
        {
            // e2-7 R3.3: false over cut rows is a negative answer, read by the open site frame's node (QuerySite's carrier).
            this.ReadIdentityLimit(this.consultationNode, scope, classes);
        }

        return confined;
    }

    private static bool ReceiverCovers(SemanticsMask premise, SemanticsKind receiver)
        => premise == SemanticsMask.Owner || receiver == SemanticsKind.Ref || (premise == SemanticsMask.Uniq && receiver == SemanticsKind.Uniq);

    // SPEC 8.11.4: an implementation's bounded Callable clause its requirement's premises do not declare, from its facts: the clause
    // (Related, role 0) and the requirement (role 1), whose name is read from its declaration.
    private void ReportCallablePremise(Koto use, Kimi.Diagnostics.DiagnosticCollection? store, int index, in BindingProblem problem)
    {
        var facts = this.report.Facts(index);
        var requirement = this.compilation.SyntaxOf(facts[1].Site);
        use.ReportIn(
            store,
            problem.Requirement,
            problem.Code,
            evidence: [$"the implementation requires {Constants.ConfinedKeyword}, but the premises of requirement {requirement.BoundSymbol!.Name} " +
                "do not declare it"],
            related: [("bound", this.compilation.SyntaxOf(facts[0].Site), "the implementation's bounded Callable Constraint"), ("requirement", requirement, "the requirement whose premises lack the bound")],
            condition: problem.Condition);
    }

    // SPEC 8.11.4: the selected uses of bounded Callable clauses: each selected call, function reference and Type formation, located at
    // its argument or Type argument, with its substituted subject, signature and receiver; enumerated once at the end of the Binding pass.
    private void CollectCallableEffectUses()
    {
        this.callableEffectUses.Clear();
        // A use may select a library callee's bounded clause, so the test reads the clauses the pass may select (review-r1a B2), as
        // AvailableCallableEffects does; the uses collected are the pass's own (its nodes).
        for (var i = 0; i < this.nodes.Count && this.selectableEffectClauses.Count != 0; i++)
        {
            var node = this.nodes[i];
            if (node is InvocationKoto invocation && this.CallOf(invocation) is { } call && this.Library.PresentedTarget(call.Target).Declaration is FunctionKoto function)
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
    internal readonly record struct CallableEffectUse(Koto At, Koto Use, IsKoto Clause, BoundType Subject, BoundType Signature, SemanticsKind Receiver);
}
