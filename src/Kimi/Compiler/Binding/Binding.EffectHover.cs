// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Checking;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1204 // Projection helpers stay in execution order.

public sealed partial class Binding
{
    // Compatibility entry point until the session adopts the common token index.
    internal EffectHover[] CreateCallableEffectHovers() => this.CreateHoverSnapshot().Effects;

    private sealed partial class HoverBuilder
    {
        private HoverInfo? Call(InvocationKoto syntax)
        {
            for (var scope = binding.ConstraintScope(syntax); scope is not null; scope = scope.Parent)
            {
                if (scope.Constraints is { Invalid: true })
                {
                    return null;
                }
            }

            HoverInfo info;
            List<EffectEvidence>? evidence = null;
            StringBuilder? text = null;
            if (syntax.BoundCall is { } call)
            {
                var original = call.Target.Declaration is FunctionKoto specialized && binding.GetSpecializationOriginal(specialized) is { } originalFunction
                    ? originalFunction : call.Target.Declaration;
                info = new([this.Declaration(original)], this.CallUse(call, syntax), TypeIdentity: this.CallIdentity(call));
                if (call.VirtualDispatch is not null && binding.TryGetObjectErasure(syntax, out var erasure))
                {
                    info = info with { TypeIdentity = new("erased receiver", [info.TypeIdentity!, this.TypeIdentity(erasure.Source), this.TypeIdentity(erasure.Target), this.BinderIdentity(erasure.Entry)]) };
                }

                if (original is FunctionKoto requirement && (requirement.IsRequirement || requirement.IsVirtual))
                {
                    evidence = [];
                    text = new();
                    var available = binding.AvailableEffectBounds(requirement, call.ConformingType, binding.ConstraintScope(syntax), evidence, call.RequirementContract);
                    if ((available.Confined && !evidence.Exists(static x => x.Bound.Bound == EffectBoundKind.Confined)) ||
                        (available.Preserves && !evidence.Exists(static x => x.Bound.Bound == EffectBoundKind.PreservesResults)))
                    {
                        return null; // Incomplete conformance evidence must not become a public guarantee.
                    }

                    text.Append("Call: ").AppendLine(CallSpelling(syntax));
                    text.Append(requirement.IsVirtual ? "Public slot: " : "Requirement: ").Append(Owner(requirement)).Append('.').AppendLine(requirement.Name);
                    WriteBounds(text, available);
                }
            }
            else if (syntax.BoundValueCall is { ReceiverType: { Kind: BoundTypeKind.FunctionItem, Symbol.Declaration: FunctionKoto { IsVirtual: true } originalSlot } itemType } virtualItem)
            {
                info = new(
                    [this.Declaration(originalSlot)],
                    "Result: " + this.TypeName(virtualItem.ReturnType) + "\n" + this.VirtualItemDetails(itemType, originalSlot),
                    TypeIdentity: new("virtual Item call", [this.TypeIdentity(itemType), this.TypeIdentity(virtualItem.Signature)]));
                evidence = [];
                text = new();
                var available = binding.AvailableEffectBounds(originalSlot, null, binding.ConstraintScope(syntax), evidence);
                text.Append("Call: ").AppendLine(CallSpelling(syntax));
                text.Append("Public slot: ").AppendLine(Qualified(originalSlot));
                WriteBounds(text, available);
            }
            else if (syntax.BoundValueCall is { } valueCall)
            {
                info = new(
                    [new("Call contract", string.Empty, this.TypeName(valueCall.DeclaredSignature), [], [])],
                    TypeIdentity: new("value call;" + valueCall.ReceiverKind, [this.TypeIdentity(valueCall.Signature), this.TypeIdentity(valueCall.ReceiverType)]));
                if (CallableCore(valueCall.ReceiverType) is { } type && AbstractTypes.IsAbstract(type))
                {
                    evidence = [];
                    text = new();
                    var available = binding.AvailableCallableEffects(type, valueCall.DeclaredSignature, valueCall.ReceiverKind, binding.ConstraintScope(syntax), evidence);
                    text.Append("Call: ").AppendLine(CallSpelling(syntax));
                    text.Append("Callable: ").Append(this.TypeName(type)).Append(" is Callable<");
                    if (valueCall.ReceiverKind != SemanticsKind.Ref)
                    {
                        text.Append(valueCall.ReceiverKind == SemanticsKind.Uniq ? "uniq, " : "owner, ");
                    }

                    text.Append(this.TypeName(valueCall.DeclaredSignature)).AppendLine(">");
                    WriteBounds(text, available);
                }
            }
            else
            {
                return null;
            }

            if (text is null || evidence is null)
            {
                return info;
            }

            // Arrival and hash-table enumeration do not determine provenance order. Keep distinct premises even when their text matches.
            evidence.Sort((a, b) =>
            {
                var order = CompareLocation(a.Context, b.Context);
                if (order == 0)
                {
                    order = CompareLocation(a.Bound, b.Bound);
                }

                if (order == 0)
                {
                    var left = a.Premise?.Contract;
                    var right = b.Premise?.Contract;
                    order = string.CompareOrdinal(left?.Type is { } leftType ? this.TypeName(leftType) : left?.Name, right?.Type is { } rightType ? this.TypeName(rightType) : right?.Name);
                }

                return order;
            });
            var keys = new List<HoverKey>(evidence.Count + 1) { info.TypeIdentity! };
            EffectEvidence? previous = null;
            foreach (var item in evidence)
            {
                if (previous == item)
                {
                    continue;
                }

                previous = item;
                var declaring = (Koto?)DeclaringContract(item.Bound) ?? item.Context;
                text.Append("Declared by: ").AppendLine(Qualified(declaring));
                if (item.Context is FunctionKoto { IsVirtual: true } evidenceSlot)
                {
                    text.Append("Public guarantee: ").Append(Qualified(evidenceSlot)).Append(" effect ").AppendLine(EffectBoundKoto.Spelling(item.Bound.Bound));
                }
                else
                {
                    text.Append("Premise: ");
                    if (item.Clause is IsKoto clause && item.Premise?.Kind == ConstraintKind.Callable)
                    {
                        text.Append(clause.Left).Append(" is ").Append(clause.Right);
                    }
                    else if (item.Premise is { Subject: { } subject, Contract: { } contract })
                    {
                        text.Append(this.TypeName(subject)).Append(" is ").Append(contract.Type is { } applied ? this.TypeName(applied) : Qualified(contract.Declaration));
                    }
                    else
                    {
                        text.Append(this.TypeName(item.Conforming!)).Append(" is ").Append(Qualified(item.Contract!.Declaration));
                    }

                    text.Append(" effect ").Append(EffectBoundKoto.Spelling(item.Bound.Bound)).Append(" (in ").Append(Qualified(item.Context)).AppendLine(")");
                }

                keys.Add(new(
                    "effect;" + item.Bound.Bound,
                    [this.BinderIdentity(item.Bound), this.BinderIdentity(item.Context), this.BinderIdentity(item.Clause),
                    this.ConstraintIdentity(item.Premise), this.TypeIdentity(item.Conforming), this.SymbolIdentity(item.Contract)]));
            }

            var effects = text.ToString().TrimEnd();
            info = info with { Effects = effects, TypeIdentity = new("effect evidence", keys.ToArray()) };
            if (syntax.CodeContext.SourceDocument is { } source)
            {
                this.legacyEffects.Add(new(SourceIdentity.FromPath(source.Path), source.GetSourceRange(syntax.Span), effects));
            }

            return info;
        }

        private static int CompareLocation(Koto a, Koto b)
        {
            var result = string.CompareOrdinal(a.Kotonoha.Url, b.Kotonoha.Url);
            if (result == 0)
            {
                result = string.CompareOrdinal(a.CodeContext.SourceDocument?.Path, b.CodeContext.SourceDocument?.Path);
            }

            return result != 0 ? result : a.Span.Start.CompareTo(b.Span.Start);
        }

        private static string CallSpelling(InvocationKoto syntax)
        {
            if (syntax.Span.Length > HoverLimits.Input)
            {
                throw new HoverLimitException("Hover call description input limit exceeded");
            }

            return syntax.CodeContext.SourceDocument?.SourceText.Substring(syntax.Span.Start, syntax.Span.Length) ?? "selected call";
        }

        private static string Qualified(Koto declaration)
        {
            var owner = Owner(declaration);
            var name = declaration.BoundSymbol?.Name ?? "the enclosing declaration";
            return owner.Length == 0 ? name : owner + "." + name;
        }

        private static void WriteBounds(StringBuilder text, (bool Confined, bool Preserves) available)
        {
            if (available.Confined)
            {
                text.AppendLine("Available bound: confined");
            }

            if (available.Preserves)
            {
                text.AppendLine("Available bound: preserves results");
            }

            if (!available.Confined && !available.Preserves)
            {
                text.AppendLine("Available bound: none");
            }
        }

        private HoverKey ConstraintIdentity(BoundConstraint? fact)
        {
            using var guard = this.budget.Enter();
            if (fact is null)
            {
                return HoverKey.Missing;
            }

            if (!this.identities.TryGetValue(fact, out var key))
            {
                key = new(
                    $"constraint;{fact.Kind};{fact.Mask}",
                    [this.TypeIdentity(fact.Subject), this.TypeIdentity(fact.RequiredType), this.SymbolIdentity(fact.Contract),
                    this.ConstraintIdentity(fact.Left), this.ConstraintIdentity(fact.Right)]);
                this.identities.Add(fact, key);
            }

            return key;
        }
    }
}
