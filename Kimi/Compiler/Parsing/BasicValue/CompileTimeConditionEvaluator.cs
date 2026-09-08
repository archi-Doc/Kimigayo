// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Describes the result of an early compile-time condition evaluation.</summary>
internal enum CompileTimeConditionResult : byte
{
    Error,
    False,
    True,
    Deferred,
}

/// <summary>Evaluates the condition subset available before ordinary binding.</summary>
internal static class CompileTimeConditionEvaluator
{
    private enum ValueResult : byte
    {
        Error,
        Known,
        Deferred,
    }

    public static CompileTimeConditionResult Evaluate(Compilation compilation, Koto condition, out bool requiresBinding)
    {
        requiresBinding = false;
        var result = EvaluateBoolean(compilation, condition, ref requiresBinding);
        if (result == CompileTimeConditionResult.Error)
        {
            condition.AddDiagnostic(DiagnosticCode.ConditionMustBeBool_Kd);
        }

        return result;
    }

    private static CompileTimeConditionResult EvaluateBoolean(Compilation compilation, Koto koto, ref bool requiresBinding)
    {
        if (koto is ParenthesizedKoto parenthesized)
        {
            return EvaluateBoolean(compilation, parenthesized.Operand, ref requiresBinding);
        }

        if (koto is NotKoto not)
        {
            return EvaluateBoolean(compilation, not.Operand, ref requiresBinding) switch
            {
                CompileTimeConditionResult.True => CompileTimeConditionResult.False,
                CompileTimeConditionResult.False => CompileTimeConditionResult.True,
                var result => result,
            };
        }

        if (koto is AndKoto and)
        {
            // Truth may short-circuit, but validation must inspect both operands.
            var left = EvaluateBoolean(compilation, and.Left, ref requiresBinding);
            var right = EvaluateBoolean(compilation, and.Right, ref requiresBinding);
            if (left == CompileTimeConditionResult.Error || right == CompileTimeConditionResult.Error)
            {
                return CompileTimeConditionResult.Error;
            }

            if (left == CompileTimeConditionResult.False || right == CompileTimeConditionResult.False)
            {
                return CompileTimeConditionResult.False;
            }

            return left == CompileTimeConditionResult.Deferred || right == CompileTimeConditionResult.Deferred
                ? CompileTimeConditionResult.Deferred
                : CompileTimeConditionResult.True;
        }

        if (koto is OrKoto or)
        {
            var left = EvaluateBoolean(compilation, or.Left, ref requiresBinding);
            var right = EvaluateBoolean(compilation, or.Right, ref requiresBinding);
            if (left == CompileTimeConditionResult.Error || right == CompileTimeConditionResult.Error)
            {
                return CompileTimeConditionResult.Error;
            }

            if (left == CompileTimeConditionResult.True || right == CompileTimeConditionResult.True)
            {
                return CompileTimeConditionResult.True;
            }

            return left == CompileTimeConditionResult.Deferred || right == CompileTimeConditionResult.Deferred
                ? CompileTimeConditionResult.Deferred
                : CompileTimeConditionResult.False;
        }

        var valueResult = EvaluateValue(compilation, koto, out var value, ref requiresBinding);
        if (valueResult == ValueResult.Deferred)
        {
            return CompileTimeConditionResult.Deferred;
        }

        if (valueResult == ValueResult.Error || value.Kind != BasicValueKind.Bool)
        {
            return CompileTimeConditionResult.Error;
        }

        return value.Bool ? CompileTimeConditionResult.True : CompileTimeConditionResult.False;
    }

    private static ValueResult EvaluateValue(Compilation compilation, Koto koto, out BasicValue value, ref bool requiresBinding)
    {
        switch (koto)
        {
            case BoolLiteralKoto boolean:
                value = new(boolean.Value);
                return ValueResult.Known;

            case NumberLiteralKoto number when number.TryGetBasicValue(out value):
                return ValueResult.Known;

            case StringLiteralKoto text:
                value = new(text.Literal);
                return ValueResult.Known;

            case IdentifierNameKoto identifier:
                if (compilation.TryResolveValue(identifier, out value))
                {
                    return ValueResult.Known;
                }

                value = default;
                requiresBinding = true;
                return ValueResult.Deferred;

            case ParenthesizedKoto parenthesized:
                return EvaluateValue(compilation, parenthesized.Operand, out value, ref requiresBinding);

            case EqualsEqualsKoto equals:
                return EvaluateEquality(compilation, equals, false, out value, ref requiresBinding);

            case ExclamationEqualsKoto notEquals:
                return EvaluateEquality(compilation, notEquals, true, out value, ref requiresBinding);

            case NotKoto or AndKoto or OrKoto:
                var booleanResult = EvaluateBoolean(compilation, koto, ref requiresBinding);
                value = booleanResult switch
                {
                    CompileTimeConditionResult.True => new BasicValue(true),
                    CompileTimeConditionResult.False => new BasicValue(false),
                    _ => default,
                };
                return booleanResult switch
                {
                    CompileTimeConditionResult.True or CompileTimeConditionResult.False => ValueResult.Known,
                    CompileTimeConditionResult.Deferred => ValueResult.Deferred,
                    _ => ValueResult.Error,
                };

            default:
                value = default;
                requiresBinding = true;
                return ValueResult.Deferred;
        }
    }

    private static ValueResult EvaluateEquality(
        Compilation compilation,
        BinaryKoto binary,
        bool negate,
        out BasicValue value,
        ref bool requiresBinding)
    {
        var leftResult = EvaluateValue(compilation, binary.Left, out var left, ref requiresBinding);
        var rightResult = EvaluateValue(compilation, binary.Right, out var right, ref requiresBinding);
        if (leftResult == ValueResult.Error || rightResult == ValueResult.Error)
        {
            value = default;
            return ValueResult.Error;
        }

        if (leftResult == ValueResult.Deferred || rightResult == ValueResult.Deferred)
        {
            value = default;
            return ValueResult.Deferred;
        }

        if (left.Kind != right.Kind)
        {
            binary.AddDiagnostic(DiagnosticCode.TypeMismatch_Kd);
            value = default;
            return ValueResult.Error;
        }

        var equal = left == right;
        value = new(negate ? !equal : equal);
        return ValueResult.Known;
    }
}
