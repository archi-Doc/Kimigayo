// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// A selection's record (R7-E3 E3b-1; DECISIONS R7-M75): the candidates a failed selection considered, as the record's facts.
// - Responsibility: the one candidate-fact builder (CandidateFacts) and the adapter row that renders its parties (ReportCandidates).
// - Inputs: a candidate the selection considered and what its trial left: its pair, its packed reason and its refuted clause (where each
//   fact lives is R7-P2's, the record's form this file's).
// - Outputs: per party, in this order: Related(declaration, 3) "candidate"; its pair, Actual then Expected at one site (an unfixed
//   reference's Actual alone); its packed Reason (CandidateFlags, the two receivers a byte each plus one, and above them the receiver
//   parameter's ordinal plus one), absent when zero; its checked part, Checked (the argument part checked against its expected Type
//   whose own resolution there failed, SPEC 10.5.1; CheckedFact, read by CheckedPartText only), absent when none; its refuted clause,
//   Related(clause, 4) "constraint" then its Constraint; the retained implementation, Related(implementation, 6), that keeps its
//   declaring Type as its implementation-side Self (SPEC 8.6.2).
// - Invariants: a selection's record is the subject's facts before the first party, then each party's entries after its Related (role 3
//   or 5) up to the next party Related, in the order its adapter renders them; read by position, never by FactOf or a fixed index; no
//   Count (the adapter counts the parties). The scratch keeps its capacity, so a warm rebind of a rejected call allocates nothing.
// - Prohibitions: no fit, inference or proof runs here (the retained implementation's conformance search reads the paths the proof
//   decided); no message text is recorded.
public sealed partial class Binding
{
    // The facts of the selection-family record being built: a selection's candidates, a rejected candidate's checked part, a constructor
    // check's parties, an unbound slot's Type parameter and waiting argument, a pending judgment's argument and slot, an index key's
    // selection members (RecordKeyAmbiguity); cleared by the recorder after its record (RecordSelection), capacity kept.
    private readonly List<ProblemFact> candidateFacts = [];

    // A candidate's closed reasons, the low byte of its packed Reason.
    [Flags]
    private enum CandidateFlags : byte
    {
        None = 0,
        SharedReceiver = 1, // SPEC 15.1.5: the pair is the receiver's authority.
        CallableSignature = 2, // SPEC 10.5.3, 10.7: the pair is an argument's known call signature and the required one.
        Selected = 4, // SPEC 10.5: the declaration was selected before its callable arguments completed.
        ReferenceSignature = 8, // SPEC 10.5.3: a function reference against a fixed expected call signature.
        UnfixedReference = 16, // SPEC 10.5: a function reference without a fixed expected call signature.
        ErasureIncomparable = 32, // SPEC 10.2.2, 10.7: an erasure and a direct take at one argument.
    }

    // The one candidate-fact builder: appends one party's facts to the scratch in the invariant's order.
    private void CandidateFacts(FunctionKoto function, ReferenceConstraintFailure? refuted, CandidateFlags flags = CandidateFlags.None, (Koto At, BoundType Actual, BoundType? Expected)? pair = null, (SemanticsKind Actual, SemanticsKind Required, int Parameter)? receivers = null, CheckedPartFailure? checkedPart = null)
    {
        var site = this.FactSite(function);
        this.candidateFacts.Add(new(ProblemFactKind.Related, site, 3));
        if (pair is (var at, var actual, var expected))
        {
            var shown = this.FactSite(at);
            this.candidateFacts.Add(new(ProblemFactKind.Actual, shown, this.report.Keep(actual)));
            if (expected is not null)
            {
                this.candidateFacts.Add(new(ProblemFactKind.Expected, shown, this.report.Keep(expected)));
            }
        }

        var reason = (long)flags;
        if (receivers is (var closure, var callable, var parameter))
        {
            reason |= (((long)closure + 1) << 8) | (((long)callable + 1) << 16) | ((long)(parameter + 1) << 24);
        }

        if (reason != 0)
        {
            this.candidateFacts.Add(new(ProblemFactKind.Reason, site, reason));
        }

        if (checkedPart is { } part)
        {
            this.candidateFacts.Add(this.CheckedFact(part));
        }

        if (refuted is { } failure)
        {
            var clause = this.FactSite(failure.Clause);
            this.candidateFacts.Add(new(ProblemFactKind.Related, clause, 4));
            this.candidateFacts.Add(new(ProblemFactKind.Constraint, clause, this.report.Keep(failure.Constraint)));
            if (failure.Conformance is { } conformance && this.RetainedImplementation(conformance) is { } implementation)
            {
                this.candidateFacts.Add(new(ProblemFactKind.Related, this.FactSite(implementation), 6));
            }
        }
    }

    // SPEC 10.1, 10.4, 23.3: a failed selection's parties from its facts (CandidateFacts), each related as a candidate and labelled by the
    // facts it carries, its refuted clause and retained implementation related after every candidate; the Note from the first party that
    // names one, by the precedence the labels read.
    private void ReportCandidates(Koto node, DiagnosticCollection? store, int index, in BindingProblem problem)
    {
        var facts = this.report.Facts(index);
        var ambiguous = (BindingFailure)problem.Requirement.Kind == BindingFailure.Ambiguous;
        var (parties, extra) = (0, 0);
        foreach (ref readonly var fact in facts)
        {
            (parties, extra) = fact is { Kind: ProblemFactKind.Related, Value: 3 } ? (parties + 1, extra) : fact.Kind == ProblemFactKind.Related ? (parties, extra + 1) : (parties, extra);
        }

        var candidates = new (string Role, Koto At, string? Label)[parties + extra];
        var next = parties;
        string? shapeNote = ambiguous ? "No candidate is better than every other remaining candidate under the argument, parameter Type, generic and default ranking rules. Anonymous bodies, captures and waiting function references do not select an overload" : null;
        for (int c = 0, i = 0; c < parties; c++)
        {
            var function = (FunctionKoto)this.compilation.SyntaxOf(facts[i++].Site);
            var (actual, expected, reason) = ((BoundType?)null, (BoundType?)null, 0L);
            var (clause, constraint, implementation, checkedPart) = ((IsKoto?)null, (BoundConstraint?)null, (Koto?)null, (string?)null);
            for (; i < facts.Length && facts[i] is not { Kind: ProblemFactKind.Related, Value: 3 }; i++)
            {
                switch (facts[i])
                {
                    case { Kind: ProblemFactKind.Actual }:
                        actual = this.report.Value(facts[i]);
                        break;
                    case { Kind: ProblemFactKind.Expected }:
                        expected = this.report.Value(facts[i]);
                        break;
                    case { Kind: ProblemFactKind.Reason }:
                        reason = facts[i].Value;
                        break;
                    case { Kind: ProblemFactKind.Checked }:
                        checkedPart = this.CheckedPartText(facts[i]);
                        break;
                    case { Kind: ProblemFactKind.Related, Value: 4 }:
                        clause = (IsKoto)this.compilation.SyntaxOf(facts[i].Site);
                        break;
                    case { Kind: ProblemFactKind.Constraint }:
                        constraint = this.report.Constraint(facts[i]);
                        break;
                    case { Kind: ProblemFactKind.Related, Value: 6 }:
                        implementation = this.compilation.SyntaxOf(facts[i].Site);
                        break;
                }
            }

            var flags = (CandidateFlags)(byte)reason;
            var (closureReceiver, callableReceiver, receiverParameter) = ((int)(byte)(reason >> 8) - 1, (int)(byte)(reason >> 16) - 1, (int)(reason >> 24) - 1);
            var label = function.Name;
            if (flags.HasFlag(CandidateFlags.ReferenceSignature) && ambiguous)
            {
                shapeNote = "No function reference candidate is better than every other fitting candidate under the parameter Type and generic ranking rules. Results do not rank candidates";
            }

            if (flags.HasFlag(CandidateFlags.UnfixedReference))
            {
                // SPEC 10.5: without a fixed expected call signature, an overload set is not a value.
                shapeNote = "A function reference without a fixed expected call signature is a value only when exactly one candidate remains and its Type parameters are bound";
                candidates[c] = ("candidate", function, actual is { } signature ? $"{label}: callable signature {DiagnosticText.Bound(DiagnosticTypeName(signature), 48).Text}" : label);
                continue;
            }

            if (flags.HasFlag(CandidateFlags.ErasureIncomparable) && ambiguous)
            {
                shapeNote = ErasureAmbiguityNote;
            }

            if (flags.HasFlag(CandidateFlags.Selected))
            {
                shapeNote = "This declaration was selected before completing its callable arguments; its argument constraints failed. Another overload is not selected";
                if (closureReceiver >= 0 && callableReceiver >= 0)
                {
                    label = $"{function.Name}: closure requires {((SemanticsKind)closureReceiver).ToString().ToLowerInvariant()}; Callable requires {((SemanticsKind)callableReceiver).ToString().ToLowerInvariant()}";
                    shapeNote = $"{label}. {shapeNote}";
                }
            }
            else if (closureReceiver >= 0 && callableReceiver >= 0 && (uint)receiverParameter < (uint)function.Parameters.Count)
            {
                // SPEC 7.7.3, 8.10: the closure argument's minimum call receiver is the candidate's only refuted condition (TryCandidate);
                // the receiver Note reads that parameter's own syntax.
                var parameter = function.Parameters[receiverParameter];
                var pattern = parameter.Type.BoundType;
                var slot = (pattern is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } ? pattern.Components[0] : pattern)?.Symbol?.Name ?? "F";
                label = $"{function.Name}: closure requires {((SemanticsKind)closureReceiver).ToString().ToLowerInvariant()}; Callable requires {((SemanticsKind)callableReceiver).ToString().ToLowerInvariant()}";
                shapeNote ??= $"{label}. The closure argument of {parameter.InternalName} needs {CallReceiverText((SemanticsKind)closureReceiver)}, and the Callable Constraint of {slot} permits {PermittedCallsText((SemanticsKind)callableReceiver)} (SPEC 7.7.3, 8.10)";
            }

            if (actual is not null && expected is not null)
            {
                var (shownActual, shownExpected) = DiagnosticText.BoundPair(DiagnosticTypeName(actual), DiagnosticTypeName(expected));
                label = flags.HasFlag(CandidateFlags.ReferenceSignature) && ambiguous ? $"{function.Name}: callable signature {shownActual.Text}"
                    : flags.HasFlag(CandidateFlags.CallableSignature) ? $"{function.Name}: callable signature is {shownActual.Text}; requires {shownExpected.Text}"
                    : flags.HasFlag(CandidateFlags.SharedReceiver) ? $"{function.Name}: receiver has {shownActual.Text}; requires {shownExpected.Text}"
                    : $"{function.Name}: argument has {shownActual.Text}; parameter requires {shownExpected.Text}";

                // Keep the compared Types even when the related-location limit omits this candidate.
                shapeNote ??= flags.HasFlag(CandidateFlags.ReferenceSignature) ? $"The function reference requires the fixed call signature {shownExpected.Text}; no candidate applies"
                    : flags.HasFlag(CandidateFlags.CallableSignature) ? $"The argument's known call signature is {shownActual.Text}; the candidate requires {shownExpected.Text} from the supplied Type evidence"
                    : flags.HasFlag(CandidateFlags.SharedReceiver) ? $"{SharedObjectAuthorityNote}. Receiver: {DiagnosticText.Bound(DiagnosticTypeName(actual), 48).Text}; required: {DiagnosticText.Bound(DiagnosticTypeName(expected), 48).Text}"
                    : $"The range argument has {shownActual.Text}; a candidate parameter requires {shownExpected.Text}";
            }

            if (checkedPart is not null)
            {
                label = $"{function.Name}: {checkedPart}";
            }

            if (clause is not null && constraint is not null)
            {
                var subject = ClauseSubject(constraint) is { } type ? DiagnosticTypeName(type) : "the supplied bindings";
                var requirer = ForeignClauseOwner(clause, function) is { } owner ? $"{function.Name}: {owner}" : function.Name;
                var conditionNote = $"{requirer} requires {clause}; this condition is refuted for {subject}";
                string? retained = null;
                if (implementation?.BoundSymbol is { } kept)
                {
                    // SPEC 8.6.2: the retained implementation keeps its declaring Type as Self.
                    var declaring = kept.Scope.Owner.BoundSymbol!.Name;
                    retained = $"{declaring}.{kept.Name}";
                    conditionNote += $". The retained implementation {retained} keeps {declaring} as Self, so its input signature does not match Self = {subject}. Only a borrowed receiver may project to a base subobject; other inputs and owning receivers do not convert";
                }

                label = conditionNote;
                shapeNote ??= conditionNote;
                candidates[next++] = ("constraint", clause, conditionNote);
                if (retained is not null)
                {
                    candidates[next++] = ("retained implementation", implementation!, retained);
                }
            }

            candidates[c] = ("candidate", function, label);
        }

        // A synthesized formatting write spans its whole literal; its failure is located at the value it writes, so the writes of one
        // literal are distinct problems at distinct locations (SPEC 23.3, 23.1).
        var at = node is InvocationKoto { Method: FormattingKoto or GenericsKoto { Identifier: FormattingKoto }, ArgumentNodes: [_, var value] } ? value : null;
        node.ReportIn(store, problem.Requirement, problem.Code, evidence: [parties], related: candidates, note: shapeNote, at: at, condition: problem.Condition);
    }

    // SPEC 10.5.1 (PLAN G924 AF-3b): a candidate party's checked part as its Checked fact, written by both party builders (CandidateFacts,
    // FailCallableCandidates); the Value's packing is BindingReport's (KeepChecked).
    private ProblemFact CheckedFact(in CheckedPartFailure failure)
        => new(ProblemFactKind.Checked, this.FactSite(failure.Part), this.report.KeepChecked(failure.Expected, failure.Outcome == CallSelection.Ambiguous));

    // SPEC 10.5.1 (PLAN G924 AF-3b): a rejected candidate's or Callable premise candidate's reason from its Checked fact, the one reader
    // both adapters call (ReportCandidates, ReportCallableCandidates): the Type, the part and its own resolution's outcome there.
    private string CheckedPartText(in ProblemFact fact)
    {
        var (expected, ambiguous) = this.report.Checked(fact);
        return $"at {DiagnosticText.Bound(DiagnosticTypeName(expected), 48).Text}, {DiagnosticText.Bound(this.compilation.SyntaxOf(fact.Site).ToString(), 48).Text} " +
            (ambiguous ? "is ambiguous" : "has no applicable candidate");
    }
}
