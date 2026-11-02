// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// SPEC 8.3, 15.6.1, 23.1: a Callable Constraint whose proof is Unknown only in the Origin part of its whole-contract comparison: the
// value whose Type binds F, the clause, that Type, and the first member of the comparison that fails with its relation.
internal readonly record struct CallableConstraintFact(Koto At, IsKoto Clause, BoundType Subject, OriginContractFact Contract);

public sealed partial class Binding
{
    private Dictionary<Koto, CallableConstraintFact>? callableConstraints;

    // SPEC 8.10, 8.3, 15.6.1, 23.1: the Reason names the Type bound to F, the clause, the failing member and the relation with both ends
    // as Origin displays; the clause is related as `constraint`, an omitted end at its Type occurrence and a closure's call receiver at
    // the anonymous function's header.
    private static void ReportCallableConstraint(Koto node, CallableConstraintFact fact, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var contract = fact.Contract;
        var receiver = ReceiverAtom(contract.Longer) ? (FunctionKoto)contract.Longer.Binder! : null;
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

        if (receiver is not null)
        {
            // SPEC 8.10: a Callable result cannot borrow the hidden environment receiver of the closure.
            var header = SourceSpan.FromBounds(receiver.Span.Start, Math.Max(receiver.Span.Start, receiver.HeaderEnd));
            node.Report(
                requirement,
                code,
                note: $"{clause} is not proven: {relation}, and a Callable result cannot borrow the closure's hidden environment receiver (SPEC 8.10)",
                evidence: evidence,
                related: [.. related],
                relatedSpans: [("origin", receiver, header, null)],
                at: fact.At);
            return;
        }

        node.Report(
            requirement,
            code,
            note: $"{clause} compares whole contracts and is not proven: {relation}; its Origin part is judged from the premises alone (SPEC 8.10, 10.7, 15.6.1)",
            evidence: evidence,
            related: [.. related],
            at: fact.At);

        static string Phrase(DiagnosticOrigin origin) => origin.Kind switch
        {
            "borrow" => "the borrow " + origin.Text,
            "omitted" => "the omitted Origin of " + origin.Text,
            "closure" => "the closure's " + origin.Text,
            _ => origin.Text,
        };
    }

    // SPEC 8.3, 15.6.1: the Callable Constraint of a Pending candidate whose proof is Unknown because only the Origin part of its
    // whole-contract comparison fails, explained at the argument whose Type binds F, from its own trial's U fact (design-r2 Amendments 4
    // and 5): nothing is proven or compared again. SPEC 15.3.7: with matching members, a Function Item's failing condition (a clause or a
    // result premise) is the member. Null when the trial left no such fact (a normal state); a foreign fact is a broken contract.
    private CallableConstraintFact? CallableOriginFailure(InvocationKoto call, FunctionKoto function, int[] mapping)
    {
        var fact = this.trialUnknown;
        this.trialUnknown = null;
        if (fact is not null && fact.Epoch != this.frameEpoch)
        {
            this.Fail(call, BindingFailure.Internal);
            return null;
        }

        if (fact is not { Clause: { BoundConstraint.Subject: { Kind: BoundTypeKind.Parameter, Symbol: { } slot } } clause } || !fact.Failure.IsCondition)
        {
            return null;
        }

        var at = ArgumentBinding(call, function, mapping, slot) ?? call;
        return new(at, clause, fact.Subject, this.ContractFact(at, fact.Failure, call));

        static Koto? ArgumentBinding(InvocationKoto call, FunctionKoto function, int[] mapping, BindingSymbol slot)
        {
            for (var a = 0; a < call.ArgumentNodes.Count; a++)
            {
                if (function.Parameters[mapping[a]].Type.BoundType is { } pattern &&
                    ReferenceEquals(CallableSlotType(pattern, CallSlotOwner(function))?.Symbol, slot))
                {
                    return call.ArgumentNodes[a];
                }
            }

            return null;
        }
    }
}
