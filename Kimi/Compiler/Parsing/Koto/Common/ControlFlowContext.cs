// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Determines syntactic value contexts independently of type inference and reachability.</summary>
public static partial class KotoHelper
{
    /// <summary>Determines whether an expression occupies a position that uses its value.</summary>
    /// <param name="expression">The expression in an attached syntax tree.</param>
    /// <returns>Whether the expression is in value context.</returns>
    public static bool IsValueContext(Koto expression)
    {
        switch (expression.Parent)
        {
            case null:
                return false;
            case CodeBlockKoto block:
                return block.TrailingExpression == expression;
            case FunctionKoto function:
                return function.ExpressionBody == expression;
            case PropertyAccessorKoto accessor:
                return accessor.Body == expression && expression is not CodeBlockKoto;
            case LabeledKoto labeled:
                return expression is not CodeBlockKoto && IsValueContext(labeled);
            case ParenthesizedKoto parentheses:
                return IsValueContext(parentheses);
            case IfKoto conditional:
                return expression is not CodeBlockKoto || IsValueContext(conditional);
            case MatchKoto match:
                if (match.Expression == expression)
                {
                    return true;
                }

                foreach (var arm in match.Arms)
                {
                    if (arm.Body == expression)
                    {
                        return !arm.HasTrailingSemicolon && IsValueContext(match);
                    }
                }

                return false;
            case ForKoto loop:
                return loop.Iterable == expression;
            case WhileKoto loop:
                return loop.Condition == expression;
            case LoopKoto:
                return false;
            default:
                return true; // Initializers, arguments, and ordinary operands.
        }
    }
}
