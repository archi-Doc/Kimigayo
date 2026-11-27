// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// SPEC 8.3, 15.6.1, 23.1: a Callable Constraint whose proof is Unknown only in the Origin part of its whole-contract comparison: the
// clause, the proposition its proof judged (`T is Callable<S>`, T the Type that binds F), and the first member of the comparison that
// fails with its relation, located at the value whose Type binds F (the contract's At); the selection's carrier until its record
// (RecordCallableConstraint).
internal readonly record struct CallableConstraintFact(IsKoto Clause, BoundConstraint Proposition, OriginContractFact Contract);

public sealed partial class Binding
{
    // SPEC 8.10, 8.3, 15.6.1, 23.1: the Reason names the Type bound to F, the clause, the failing member and the relation with both ends
    // as Origin displays; the clause is related as `constraint`, an omitted end at its Type occurrence and a closure's call receiver at
    // the anonymous function's header. The record's facts (RecordCallableConstraint): the clause, the judged proposition and the
    // trial's contract, whose At is the record's location.
    private void ReportCallableConstraint(Koto node, DiagnosticCollection? store, int index, in BindingProblem problem)
    {
        var facts = this.report.Facts(index);
        var clause = (IsKoto)this.compilation.SyntaxOf(facts[0].Site);
        var subject = this.report.Constraint(facts[1]).Subject!;
        var contract = this.report.Origin(facts[2]).Contract!.Value;
        var receiver = ReceiverAtom(contract.Longer) ? (FunctionKoto)contract.Longer.Binder! : null;
        var longer = receiver is not null ? new DiagnosticOrigin("closure", "call receiver") : OriginDisplay(contract.Longer, null);
        var shorter = OriginDisplay(contract.Shorter, null);
        var related = new List<(string Role, Koto At, string? Label)>(3) { ("constraint", clause, null) };
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

        var text = clause.CodeContext.SourceDocument is { } document && clause.Span.Start + clause.Span.Length <= document.SourceText.Length
            ? document.SourceText.Substring(clause.Span.Start, clause.Span.Length) : "the Callable Constraint";
        var evidence = new object?[] { DiagnosticTypeName(subject), DiagnosticText.Bound(text).Text, contract.Member, contract.Equality ? "==" : "outlives", longer, shorter };
        var relation = $"{contract.Member} requires {Phrase(longer)} {(contract.Equality ? "==" : "outlives")} {Phrase(shorter)}";

        if (receiver is not null)
        {
            // SPEC 8.10: a Callable result cannot borrow the hidden environment receiver of the closure.
            var header = SourceSpan.FromBounds(receiver.Span.Start, Math.Max(receiver.Span.Start, receiver.HeaderEnd));
            node.ReportIn(
                store,
                problem.Requirement,
                problem.Code,
                note: $"{text} is not proven: {relation}, and a Callable result cannot borrow the closure's hidden environment receiver (SPEC 8.10)",
                evidence: evidence,
                related: [.. related],
                relatedSpans: [("origin", receiver, header, null)],
                at: contract.At,
                condition: problem.Condition);
            return;
        }

        node.ReportIn(
            store,
            problem.Requirement,
            problem.Code,
            note: $"{text} compares whole contracts and is not proven: {relation}; its Origin part is judged from the premises alone (SPEC 8.10, 10.7, 15.6.1)",
            evidence: evidence,
            related: [.. related],
            at: contract.At,
            condition: problem.Condition);

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
    private CallableConstraintFact? CallableOriginFailure(InvocationKoto call, FunctionKoto function, ReadOnlySpan<int> mapping)
    {
        var fact = this.trialUnknown;
        this.trialUnknown = null;
        if (!this.OwnFrame(call, fact?.Epoch))
        {
            return null;
        }

        if (fact is not { Clause: { BoundConstraint.Subject: { Kind: BoundTypeKind.Parameter, Symbol: { } slot } } clause } || !fact.Failure.IsCondition)
        {
            return null;
        }

        var at = ArgumentBinding(call, function, mapping, slot) ?? call;
        return new(clause, fact.Proposition, this.ContractFact(at, fact.Failure, call));

        static Koto? ArgumentBinding(InvocationKoto call, FunctionKoto function, ReadOnlySpan<int> mapping, BindingSymbol slot)
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

    // SPEC 8.3, 15.6.1, 23.1 (R7-E4 E4e): a Callable Constraint Unknown only in its Origin part, recorded at `use` through the constraint
    // family's recorder (RecordConstraint: UnprovenConstraint_Kd) with a clause record's facts, the clause (Related, role 0, "constraint")
    // and the judged proposition (Constraint), then the trial's contract (OriginRecord, sited at its At).
    private BoundType? RecordCallableConstraint(Koto use, in CallableConstraintFact fact, bool unresolved)
    {
        var site = this.FactSite(fact.Clause);
        ReadOnlySpan<ProblemFact> facts =
        [
            new(ProblemFactKind.Related, site, 0),
            new(ProblemFactKind.Constraint, site, this.report.Keep(fact.Proposition)),
            new(ProblemFactKind.OriginRecord, this.FactSite(fact.Contract.At), this.report.Keep(new OriginRecord(null, fact.Contract))),
        ];
        return this.RecordConstraint(use, ConstraintProof.Unknown, facts, unresolved);
    }
}
