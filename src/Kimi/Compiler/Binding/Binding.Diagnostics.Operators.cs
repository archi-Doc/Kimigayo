// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// Owns an operator failure's record and its diagnostic projection. A failed decision supplies Types, the location and any actual requirement;
// its fields are the problem's facts (Binding.Problems), and publication renders them without deciding applicability.
// The immutable operation key owns the written operator. No inference or proof runs here.
public sealed partial class Binding
{
    // SPEC 12.4.2, 13.3.1: string has no arithmetic; an interpolated literal is the one way to join strings, and a buffer builds text.
    private const string StringOperatorNote = "string has no arithmetic operators; an interpolated literal creates an owning string and borrows the values it embeds (SPEC 12.4.2, 13.3.1)";

    // The facts: the operand and its judged Type, and the proposition of a proof that is Unknown or Refuted. An Unknown proof is the
    // UnprovenConstraint failure, so the code tells the two.
    private BoundType? FailOperator(Koto node, Koto at, OperatorApplicability decision)
    {
        var site = this.FactSite(at);
        var judged = decision.Proof is ConstraintProof.Unknown or ConstraintProof.Refuted ? decision.Requirement : null;
        ReadOnlySpan<ProblemFact> facts =
        [
            new(ProblemFactKind.Operand, site, this.report.Keep(decision.Operand!)),
            new(ProblemFactKind.Terminal, site, this.report.Keep(decision.Terminal!)),
            judged is null ? default : new(ProblemFactKind.Constraint, site, this.report.Keep(judged)),
        ];
        return this.Record(node, decision.Failure, judged is null ? facts[..2] : facts, unresolved: decision.Proof == ConstraintProof.Unknown);
    }

    private void ReportOperatorOperand(Koto operation, int problem, in BindingProblem record)
    {
        var (requirement, code) = (record.Requirement, record.Code);
        var shown = this.FactOf(problem, ProblemFactKind.Operand)!.Value;
        var at = this.compilation.SyntaxOf(shown.Site);
        var symbol = operation is BinaryKoto binary ? binary.InfixText.Trim() : ((UnaryKoto)operation).OperatorText;
        var operand = this.report.Value(shown);
        object[]? evidence = null;
        string? requirementNote = null;
        if (this.FactOf(problem, ProblemFactKind.Constraint) is { } proposition)
        {
            var condition = this.report.Constraint(proposition);
            var subject = DiagnosticTypeName(condition.Subject!);
            var contract = condition.Contract!.Type is { } applied ? DiagnosticTypeName(applied) : condition.Contract.Name;
            var clause = $"{subject} is {contract}";
            evidence = [subject, symbol, clause];
            var text = $"{DiagnosticText.Bound(subject).Text} is {DiagnosticText.Bound(contract).Text}";
            requirementNote = code == DiagnosticCode.UnprovenConstraint_Kd
                ? $"Required condition is not proven: {text}" : $"Required condition is not satisfied: {text}";
        }

        if (code is DiagnosticCode.UnprovenConstraint_Kd or DiagnosticCode.UnsatisfiedConstraint_Kd)
        {
            operation.Report(requirement, code, note: requirementNote, evidence: evidence, at: at);
        }
        else if (code == DiagnosticCode.InvalidConstraint_Kd)
        {
            operation.Report(requirement, code, at: at);
        }
        else
        {
            var note = requirementNote;
            var terminal = this.FactOf(problem, ProblemFactKind.Terminal)!.Value;
            if (code == DiagnosticCode.NonNumericOperand_Kd && ReferenceTypes.EndsInString(this.report.Value(terminal)))
            {
                note = StringOperatorNote;
            }
            else if (code == DiagnosticCode.InvalidShiftCount_Kd && operand.IsWrappingInteger)
            {
                note = "A wrapping integer Type is never a shift count (SPEC 13.3.1)";
            }

            operation.Report(
                requirement,
                code,
                DiagnosticTypeName(operand),
                symbol,
                note: note,
                at: at,
                evidence: code == DiagnosticCode.NonIntegerOperand_Kd ? evidence : null);
        }
    }
}
