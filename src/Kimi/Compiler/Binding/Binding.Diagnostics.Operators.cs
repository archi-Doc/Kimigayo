// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// Owns the retained operator-operand failure and its diagnostic projection. Binding supplies the failed operand Type;
// publication uses the existing code, requirement and operation to render the same reason, Note and location.
// The Binding pass clears this store with its other diagnostic facts. No inference or proof runs in this adapter.
public sealed partial class Binding
{
    // SPEC 12.4.2, 13.3.1: string has no arithmetic; an interpolated literal is the one way to join strings, and a buffer builds text.
    private const string StringOperatorNote = "string has no arithmetic operators; an interpolated literal creates an owning string and borrows the values it embeds (SPEC 12.4.2, 13.3.1)";

    // SPEC 13.2, 13.3.1: the operand Type an operator rejected, or the count Type a shift rejected, recorded only when the check fails.
    private Dictionary<Koto, BoundType>? operatorOperands;

    /// <summary>Fails an operation whose operand Type has no such operator, or a shift whose count is not an integer Type
    /// (SPEC 13.2, 13.3.1), recording that Type.</summary>
    /// <param name="node">The operation.</param>
    /// <param name="operand">The operand Type as compared, or the count Type.</param>
    /// <param name="failure"><see cref="BindingFailure.NonNumericOperand"/>, <see cref="BindingFailure.NonIntegerOperand"/> or
    /// <see cref="BindingFailure.InvalidShiftCount"/>.</param>
    /// <returns><see langword="null"/>.</returns>
    private BoundType? FailOperand(Koto node, BoundType operand, BindingFailure failure)
        => this.FailExplained(ref this.operatorOperands, node, failure, operand);

    // SPEC 13.2, 13.3.1: the operand Type and the operator are the facts. A string operand is told that interpolation joins strings;
    // a shift count is located at the count, and a wrapping count is told to leave Wrapping<U> through U.
    private void ReportOperatorOperand(Koto operation, BoundType operand, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var symbol = operation is BinaryKoto binary ? binary.InfixText.Trim() : ((UnaryKoto)operation).OperatorText;
        if (code == DiagnosticCode.NonNumericOperand_Kd)
        {
            var text = ReferenceTypes.EndsInString(operand);
            operation.Report(requirement, code, DiagnosticTypeName(operand), symbol, note: text ? StringOperatorNote : null);
        }
        else if (code == DiagnosticCode.NonIntegerOperand_Kd)
        {
            operation.Report(requirement, code, DiagnosticTypeName(operand), symbol);
        }
        else
        {
            // SPEC 13.5.4: a numeric conversion leaves Wrapping<U> to U itself, which is always exact.
            var integer = operand.IsWrappingInteger ? DiagnosticTypeName(operand.Underlying) : null;
            operation.Report(
                requirement,
                code,
                DiagnosticTypeName(operand),
                symbol,
                note: integer is not null ? "A wrapping integer Type is never a shift count (SPEC 13.3.1)" : null,
                at: ((BinaryKoto)operation).Right);
        }
    }
}
