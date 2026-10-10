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
        if (node is not ErrorKoto && node.CodeContext.RecoveryCause(node) is not null)
        {
            // The parser's guess of a rejected form: its parts are checked on their own, and its value rests on the syntax Error
            // (DIAGNOSTICS.md §4.3).
            foreach (var part in node.ChildNodes)
            {
                if (part is not AttributeKoto)
                {
                    _ = TryEvaluateValue(compilation, part, out _);
                }
            }

            return false;
        }

        if (Parser.HasWrittenAttribute(node.AttributeChain))
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

            case NumberLiteralKoto or UnaryKoto { Akind: KotoKind.PrefixPlus or KotoKind.PrefixMinus }:
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

            case UnaryKoto { Akind: KotoKind.Not } not:
                var operand = Evaluate(compilation, not.Operand);
                value = new(operand == CompileTimeConditionResult.False);
                return operand != CompileTimeConditionResult.Error;

            case BinaryKoto { Akind: KotoKind.And or KotoKind.Or } logical:
                // Validate both operands even when one determines the truth value.
                var left = Evaluate(compilation, logical.Left);
                var right = Evaluate(compilation, logical.Right);
                value = new(logical.Akind == KotoKind.And
                    ? left == CompileTimeConditionResult.True && right == CompileTimeConditionResult.True
                    : left == CompileTimeConditionResult.True || right == CompileTimeConditionResult.True);
                return left != CompileTimeConditionResult.Error && right != CompileTimeConditionResult.Error;

            case BinaryKoto { Akind: KotoKind.EqualsEquals or KotoKind.ExclamationEquals } equality:
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

                value = new(equality.Akind == KotoKind.EqualsEquals ? leftValue == rightValue : leftValue != rightValue);
                return true;

            default:
                return Invalid(node);
        }
    }

    private static bool Invalid(Koto node)
    {
        if (node is not ErrorKoto)
        {
            // A condition the parser could not read rests on the syntax Error; any other form is outside the grammar.
            node.AddDiagnostic(DiagnosticCode.InvalidCompileTimeCondition_Kd);
        }

        return false;
    }

    private static bool TryGetInteger(Koto node, out BasicValue value)
    {
        if (KotoHelper.SignedNumber(node, out var negative) is { AttributeChain: null } literal && literal.TryGetIntegerMagnitude(out var magnitude) &&
            magnitude <= (UInt128)long.MaxValue + (negative ? 1U : 0U))
        {
            value = new(negative ? (long)-(Int128)magnitude : (long)magnitude);
            return true;
        }

        value = default;
        return false;
    }
}
