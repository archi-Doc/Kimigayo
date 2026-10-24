// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// Owns retained operator failure facts and their diagnostic projection. Binding supplies Types, the written operator,
// location and any actual requirement/proof; publication renders these facts without deciding applicability.
// The immutable operation key owns the written operator; pass reset/rebinding clears its facts. No inference or proof runs here.
public sealed partial class Binding
{
    // SPEC 12.4.2, 13.3.1: string has no arithmetic; an interpolated literal is the one way to join strings, and a buffer builds text.
    private const string StringOperatorNote = "string has no arithmetic operators; an interpolated literal creates an owning string and borrows the values it embeds (SPEC 12.4.2, 13.3.1)";

    private readonly record struct OperatorOperandFailure(
        BoundType Operand, BoundType Terminal, Koto At, BoundConstraint? Requirement, ConstraintProof? Proof);

    // Only failed operations retain facts. The ordinary Binding pass reset owns their lifetime.
    private Dictionary<Koto, OperatorOperandFailure>? operatorOperands;

    private BoundType? FailOperator(Koto node, Koto at, OperatorApplicability decision)
        => this.FailExplained(
            ref this.operatorOperands,
            node,
            decision.Failure,
            new(decision.Operand!, decision.Terminal!, at, decision.Requirement, decision.Proof),
            decision.Proof == ConstraintProof.Unknown);

    private void ReportOperatorOperand(Koto operation, OperatorOperandFailure fact, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var symbol = operation is BinaryKoto binary ? binary.InfixText.Trim() : ((UnaryKoto)operation).OperatorText;
        var operand = fact.Operand;
        object[]? evidence = null;
        string? requirementNote = null;
        if (fact.Requirement is { } condition && fact.Proof is ConstraintProof.Unknown or ConstraintProof.Refuted)
        {
            var subject = DiagnosticTypeName(condition.Subject!);
            var contract = condition.Contract!.Type is { } applied ? DiagnosticTypeName(applied) : condition.Contract.Name;
            var clause = $"{subject} is {contract}";
            evidence = [subject, symbol, clause];
            var shown = $"{DiagnosticText.Bound(subject).Text} is {DiagnosticText.Bound(contract).Text}";
            requirementNote = fact.Proof == ConstraintProof.Unknown
                ? $"Required condition is not proven: {shown}" : $"Required condition is not satisfied: {shown}";
        }

        if (code is DiagnosticCode.UnprovenConstraint_Kd or DiagnosticCode.UnsatisfiedConstraint_Kd)
        {
            operation.Report(requirement, code, note: requirementNote, evidence: evidence, at: fact.At);
        }
        else if (code == DiagnosticCode.InvalidConstraint_Kd)
        {
            operation.Report(requirement, code, at: fact.At);
        }
        else
        {
            var note = requirementNote;
            if (code == DiagnosticCode.NonNumericOperand_Kd && ReferenceTypes.EndsInString(fact.Terminal))
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
                at: fact.At,
                evidence: code == DiagnosticCode.NonIntegerOperand_Kd ? evidence : null);
        }
    }
}
