// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Describes a fully evaluated compile-time condition.</summary>
internal enum CompileTimeConditionResult : byte
{
    Error,
    False,
    True,
}

/// <summary>Validates and evaluates conditions in one pass over the prepared Compilation environment.</summary>
internal static class CompileTimeConditionEvaluator
{
    /// <summary>Validates and evaluates a Condition (SPEC 19.3).</summary>
    /// <param name="compilation">The prepared Compilation.</param>
    /// <param name="condition">The Condition.</param>
    /// <param name="cause">For an Error, its cause (G144-23): the first failing part with a record, in source order, or else the first
    /// skip mark of the syntax depth limit (<see cref="ErrorKoto.IsSkip"/>), which has none; otherwise <see langword="null"/>.</param>
    /// <returns>The result.</returns>
    public static CompileTimeConditionResult Evaluate(Compilation compilation, Koto condition, out Koto? cause)
    {
        cause = null;
        return EvaluatePart(compilation, condition, ref cause);
    }

    private static CompileTimeConditionResult EvaluatePart(Compilation compilation, Koto condition, ref Koto? cause)
    {
        if (!TryEvaluateValue(compilation, condition, out var value, ref cause))
        {
            return CompileTimeConditionResult.Error;
        }

        if (value.Kind != BasicValueKind.Bool)
        {
            condition.AddDiagnostic(DiagnosticCode.ConditionMustBeBool_Kd);
            _ = Fail(condition, ref cause);
            return CompileTimeConditionResult.Error;
        }

        return value.Bool ? CompileTimeConditionResult.True : CompileTimeConditionResult.False;
    }

    private static bool TryEvaluateValue(Compilation compilation, Koto node, out BasicValue value, ref Koto? cause)
    {
        value = default;
        if (node is not ErrorKoto && node.CodeContext.RecoveryCause(node) is not null)
        {
            // The parser's guess of a rejected form: its parts are checked on their own, and its value rests on the syntax Error
            // (DIAGNOSTICS.md §4.3).
            node.VisitChildren(new RecoveredParts(compilation));
            return Fail(node, ref cause);
        }

        switch (node)
        {
            case BoolLiteralKoto boolean:
                value = new(boolean.Value);
                return true;

            case StringLiteralKoto text:
                value = new(text.Literal);
                return true;

            case NumberLiteralKoto or UnaryKoto { Akind: KotoKind.PrefixPlus or KotoKind.PrefixMinus }:
                return TryGetInteger(node, out value) || Invalid(node, ref cause);

            case IdentifierNameKoto identifier:
                if (compilation.TryResolveValue(identifier, out value))
                {
                    return true;
                }

                identifier.AddDiagnostic(DiagnosticCode.UnknownCompileTimeName_Kd, identifier.IdentifierName);
                return Fail(identifier, ref cause);

            case ParenthesizedKoto parenthesized:
                return TryEvaluateValue(compilation, parenthesized.Operand, out value, ref cause);

            case UnaryKoto { Akind: KotoKind.Not } not:
                var operand = EvaluatePart(compilation, not.Operand, ref cause);
                value = new(operand == CompileTimeConditionResult.False);
                return operand != CompileTimeConditionResult.Error;

            case BinaryKoto { Akind: KotoKind.And or KotoKind.Or } logical:
                // Validate both operands even when one determines the truth value.
                var left = EvaluatePart(compilation, logical.Left, ref cause);
                var right = EvaluatePart(compilation, logical.Right, ref cause);
                value = new(logical.Akind == KotoKind.And
                    ? left == CompileTimeConditionResult.True && right == CompileTimeConditionResult.True
                    : left == CompileTimeConditionResult.True || right == CompileTimeConditionResult.True);
                return left != CompileTimeConditionResult.Error && right != CompileTimeConditionResult.Error;

            case BinaryKoto { Akind: KotoKind.EqualsEquals or KotoKind.ExclamationEquals } equality:
                var leftValid = TryEvaluateValue(compilation, equality.Left, out var leftValue, ref cause);
                var rightValid = TryEvaluateValue(compilation, equality.Right, out var rightValue, ref cause);
                if (!leftValid || !rightValid)
                {
                    return false;
                }

                if (leftValue.Kind != rightValue.Kind)
                {
                    node.AddDiagnostic(DiagnosticCode.TypeMismatch_Kd);
                    return Fail(node, ref cause);
                }

                value = new(equality.Akind == KotoKind.EqualsEquals ? leftValue == rightValue : leftValue != rightValue);
                return true;

            default:
                return Invalid(node, ref cause);
        }
    }

    private static bool Invalid(Koto node, ref Koto? cause)
    {
        if (node is not ErrorKoto)
        {
            // A condition the parser could not read rests on the syntax Error; any other form is outside the grammar.
            node.AddDiagnostic(DiagnosticCode.InvalidCompileTimeCondition_Kd);
        }

        return Fail(node, ref cause);
    }

    // The cause rule (G144-23): a failing part with a record (this evaluator's, or the parser's at a recovery or an ErrorKoto) takes
    // the cause from a skip mark; otherwise the first failing part, in source order, keeps it. A #switch's Conditions share it.
    internal static bool Fail(Koto part, ref Koto? cause)
    {
        if (cause is null || (cause is ErrorKoto { IsSkip: true } && part is not ErrorKoto { IsSkip: true }))
        {
            cause = part;
        }

        return false;
    }

    private static bool TryGetInteger(Koto node, out BasicValue value)
    {
        if (KotoHelper.SignedNumber(node, out var negative) is { } literal && literal.TryGetIntegerMagnitude(out var magnitude) &&
            magnitude <= (UInt128)long.MaxValue + (negative ? 1U : 0U))
        {
            value = new(negative ? (long)-(Int128)magnitude : (long)magnitude);
            return true;
        }

        value = default;
        return false;
    }

    // A recovered form's parts, each checked on its own; the recovery is the cause.
    private sealed class RecoveredParts(Compilation compilation) : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            Koto? cause = null;
            _ = TryEvaluateValue(compilation, node, out _, ref cause);
        }
    }
}
