// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// SPEC 8.7, 15.6.1, 23.3.6.5: a Callable Constraint whose proof is Unknown only in the Origin part of its whole-contract comparison: the
// value whose Type binds F, the clause, that Type, and the first member of the comparison that fails with its relation.
internal readonly record struct CallableConstraintFact(Koto At, IsKoto Clause, BoundType Subject, OriginContractFact Contract);

public sealed partial class Binding
{
    private Dictionary<Koto, CallableConstraintFact>? callableConstraints;

    // SPEC 8.6, 8.7, 15.6.1, 23.3.6.5: the Reason names the Type bound to F, the clause, the failing member and the relation with both ends
    // as Origin displays; the clause is related as `constraint`, an omitted end at its Type occurrence and a closure's call receiver at
    // the anonymous function's header. Advice only describes a repair.
    private static void ReportCallableConstraint(Koto node, CallableConstraintFact fact, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var contract = fact.Contract;
        var receiver = contract.Longer is { Kind: OriginKind.Projection, Binder: FunctionKoto { IsAnonymous: true } closure } environment && environment.Slot <= EnvironmentSlot(0)
            ? closure : null;
        var longer = receiver is not null ? new DiagnosticOrigin("closure", "call receiver") : OriginDisplay(contract.Longer, null);
        var shorter = OriginDisplay(contract.Shorter, null);
        var related = new List<(string Role, Koto At, string? Label)>(3) { ("constraint", fact.Clause, null) };
        if (longer.Kind == "omitted" && OmittedAt(contract.Longer) is { } longerAt)
        {
            related.Add(("origin", longerAt, null));
        }

        if (shorter.Kind == "omitted" && OmittedAt(contract.Shorter) is { } shorterAt)
        {
            related.Add(("origin", shorterAt, null));
        }

        if (longer.Kind == "borrow" && contract.Longer.Binder is VariableKoto borrowed)
        {
            related.Add(("origin", borrowed.NameKoto, null)); // The Place whose Borrow supplies the finite end.
        }

        var clause = fact.Clause.CodeContext.SourceDocument is { } document && fact.Clause.Span.Start + fact.Clause.Span.Length <= document.SourceText.Length
            ? document.SourceText.Substring(fact.Clause.Span.Start, fact.Clause.Span.Length) : "the Callable Constraint";
        var evidence = new object?[] { DiagnosticTypeName(fact.Subject), DiagnosticText.Bound(clause).Text, contract.Member, contract.Equality ? "==" : "outlives", longer, shorter };
        var relation = $"{contract.Member} requires {Phrase(longer)} {(contract.Equality ? "==" : "outlives")} {Phrase(shorter)}";

        // SPEC 8.6: a Callable signature writes `during` on none of its parameters and not on its result, so the Advice never suggests
        // annotating it; it names only an implementation that fits, whose result keeps the Type the signature requires.
        var example = contract.Shorter.Kind == OriginKind.Static ? ", such as a borrow of static storage"
            : contract.Shorter is { Kind: OriginKind.Input } && shorter.Kind == "omitted" ? ", such as a borrow of its input" : string.Empty;
        if (receiver is not null)
        {
            // SPEC 8.6: a Callable result cannot borrow the hidden environment receiver of the closure.
            var header = SourceSpan.FromBounds(receiver.Span.Start, Math.Max(receiver.Span.Start, receiver.HeaderEnd));
            node.Report(
                requirement,
                code,
                note: $"{clause} is not proven: {relation}, and a Callable result cannot borrow the closure's hidden environment receiver (SPEC 8.6)",
                evidence: evidence,
                related: [.. related],
                relatedSpans: [("origin", receiver, header, null)],
                advice: $"Return a borrow that outlives {Phrase(shorter)} and that the closure's environment does not own{example}",
                at: fact.At);
            return;
        }

        // SPEC 15.3.7: an Item's condition is proven from the Callable signature; writing the input that supplies its longer end over the
        // shorter one proves it.
        var condition = contract.Member == "the result's well-formedness" || contract.Member.StartsWith("the clause '", StringComparison.Ordinal);
        var without = contract.Member == "the result's well-formedness" ? "whose result Type needs no such relation" : "without that clause";
        var advice = condition ? $"Pass an implementation {without}"
            : contract.Member != "the result" ? $"Pass an implementation whose {contract.Member[4..]} accepts any borrow, as an input written without an Origin does"
            : $"Pass an implementation whose result outlives {Phrase(shorter)}{example}";
        node.Report(
            requirement,
            code,
            note: $"{clause} compares whole contracts and is not proven: {relation}; its Origin part is judged from the premises alone (SPEC 8.6, 10.7, 15.6.1)",
            evidence: evidence,
            related: [.. related],
            advice: advice,
            at: fact.At);

        static string Phrase(DiagnosticOrigin origin) => origin.Kind switch
        {
            "borrow" => "the borrow " + origin.Text,
            "omitted" => "the omitted Origin of " + origin.Text,
            "closure" => "the closure's " + origin.Text,
            _ => origin.Text,
        };
    }

    // SPEC 8.7, 15.6.1: the first Callable Constraint of a candidate whose proof is Unknown because only the Origin part of its whole-contract
    // comparison fails, explained at the argument whose Type binds F; null when no such clause is found.
    private CallableConstraintFact? CallableOriginFailure(InvocationKoto call, FunctionKoto function, BoundType?[] slots, BoundLength?[] lengths, int[] mapping, BindingScope scope, BoundType? self, BoundType? declaringType)
    {
        for (var i = 0; i < function.TypeConstraints.Count; i++)
        {
            if (function.TypeConstraints[i] is not IsKoto { BoundConstraint: { Kind: ConstraintKind.Callable, Subject: { Kind: BoundTypeKind.Parameter, Symbol: { } slot } } bound } clause)
            {
                continue;
            }

            var substituted = this.SubstituteConstraint(bound, function, slots.AsSpan(0, function.GenericArguments.Count), lengths.AsSpan(0, function.GenericArguments.Count));
            if (declaringType?.Symbol?.Declaration is { } owner)
            {
                substituted = this.SubstituteConstraint(substituted, owner, (BoundType[])declaringType.Components);
            }

            substituted = this.ContractConstraint(substituted, scope, self);
            if (substituted is not { Kind: ConstraintKind.Callable, Subject: { } subject, RequiredType: { } required } || this.ProveConstraint(substituted, scope) != ConstraintProof.Unknown)
            {
                continue;
            }

            var signature = (subject.Kind == BoundTypeKind.Closure ? this.ClosureSignature(subject) : null) ?? this.FunctionItemSignature(subject) ?? (subject.Kind == BoundTypeKind.Function ? subject : null);
            var at = ArgumentBinding(call, function, mapping, slot) ?? call;
            // SPEC 15.3.7: with matching members, a Function Item's failing condition (a clause or a result premise) is the member.
            if (signature is not null && (this.ConversionContractFailure(signature, required, SignatureOwner(subject), at, call) ??
                (subject.Kind == BoundTypeKind.FunctionItem && SignatureOwner(subject) is FunctionKoto item ? this.ConditionContractFailure(item, signature, required, at, call) : null)) is { } contract)
            {
                return new(at, clause, subject, contract);
            }
        }

        return null;

        static Koto? ArgumentBinding(InvocationKoto call, FunctionKoto function, int[] mapping, BindingSymbol slot)
        {
            for (var a = 0; a < call.ArgumentNodes.Count; a++)
            {
                var pattern = function.Parameters[mapping[a]].Type.BoundType;
                var slotType = pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } ? pattern.Components[0] : pattern;
                if (slotType is { Kind: BoundTypeKind.Parameter } && ReferenceEquals(slotType.Symbol, slot))
                {
                    return call.ArgumentNodes[a];
                }
            }

            return null;
        }
    }
}
