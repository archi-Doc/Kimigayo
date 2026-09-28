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
    public static CompileTimeConditionResult Evaluate(Compilation compilation, Koto condition)
    {
        if (!TryEvaluateValue(compilation, condition, out var value))
        {
            return CompileTimeConditionResult.Error;
        }

        if (value.Kind != BasicValueKind.Bool)
        {
            condition.AddDiagnostic(DiagnosticCode.ConditionMustBeBool_Kd);
            return CompileTimeConditionResult.Error;
        }

        return value.Bool ? CompileTimeConditionResult.True : CompileTimeConditionResult.False;
    }

    private static bool TryEvaluateValue(Compilation compilation, Koto node, out BasicValue value)
    {
        value = default;
        if (node.AttributeChain is not null)
        {
            return Invalid(node);
        }

        switch (node)
        {
            case BoolLiteralKoto boolean:
                value = new(boolean.Value);
                return true;

            case StringLiteralKoto text:
                value = new(text.Literal);
                return true;

            case NumberLiteralKoto or PrefixPlusKoto or PrefixMinusKoto:
                return TryGetInteger(node, out value) || Invalid(node);

            case IdentifierNameKoto identifier:
                if (compilation.TryResolveValue(identifier, out value))
                {
                    return true;
                }

                identifier.AddDiagnostic(DiagnosticCode.UnknownCompileTimeName_Kd, identifier.IdentifierName);
                return false;

            case ParenthesizedKoto parenthesized:
                return TryEvaluateValue(compilation, parenthesized.Operand, out value);

            case NotKoto not:
                var operand = Evaluate(compilation, not.Operand);
                value = new(operand == CompileTimeConditionResult.False);
                return operand != CompileTimeConditionResult.Error;

            case AndKoto or OrKoto:
                var logical = (BinaryKoto)node;
                // Validate both operands even when one determines the truth value.
                var left = Evaluate(compilation, logical.Left);
                var right = Evaluate(compilation, logical.Right);
                value = new(node is AndKoto
                    ? left == CompileTimeConditionResult.True && right == CompileTimeConditionResult.True
                    : left == CompileTimeConditionResult.True || right == CompileTimeConditionResult.True);
                return left != CompileTimeConditionResult.Error && right != CompileTimeConditionResult.Error;

            case EqualsEqualsKoto or ExclamationEqualsKoto:
                var equality = (BinaryKoto)node;
                var leftValid = TryEvaluateValue(compilation, equality.Left, out var leftValue);
                var rightValid = TryEvaluateValue(compilation, equality.Right, out var rightValue);
                if (!leftValid || !rightValid)
                {
                    return false;
                }

                if (leftValue.Kind != rightValue.Kind)
                {
                    node.AddDiagnostic(DiagnosticCode.TypeMismatch_Kd);
                    return false;
                }

                value = new(node is EqualsEqualsKoto ? leftValue == rightValue : leftValue != rightValue);
                return true;

            default:
                return Invalid(node);
        }
    }

    private static bool Invalid(Koto node)
    {
        node.AddDiagnostic(DiagnosticCode.InvalidCompileTimeCondition_Kd);
        return false;
    }

    private static bool TryGetInteger(Koto node, out BasicValue value)
    {
        var negative = node is PrefixMinusKoto;
        var operand = node is PrefixMinusKoto or PrefixPlusKoto ? ((UnaryKoto)node).Operand : node;
        if (operand is NumberLiteralKoto { AttributeChain: null } literal && literal.TryGetIntegerMagnitude(out var magnitude) &&
            magnitude <= (UInt128)long.MaxValue + (negative ? 1U : 0U))
        {
            value = new(negative ? (long)-(Int128)magnitude : (long)magnitude);
            return true;
        }

        value = default;
        return false;
    }
}
