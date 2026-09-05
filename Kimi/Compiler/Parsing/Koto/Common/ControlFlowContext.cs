// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Determines syntactic value contexts independently of type inference and reachability.</summary>
public static partial class KotoHelper
{
    /// <summary>Resolves a transfer's lexical target without inspecting operand types or reachability.</summary>
    /// <param name="jump">The attached transfer expression.</param>
    /// <returns>The target, or null if lookup is blocked or the named target has the wrong kind.</returns>
    public static Koto? ResolveTransferTarget(JumpKoto jump)
    {
        Koto child = jump;
        for (var parent = child.Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            if ((parent is FunctionKoto function && (child == function.Body || child == function.ExpressionBody)) ||
                (parent is PropertyAccessorKoto accessor && child == accessor.Body))
            {
                return jump is ReturnKoto ? parent : null;
            }

            if (parent is LabeledKoto labeled && child == labeled.Target && jump.Label == labeled.Label && IsInsideLabeledBody(jump, labeled))
            {
                return jump switch
                {
                    ExitKoto => labeled.Target,
                    ContinueKoto when labeled.Target is ForKoto or WhileKoto or LoopKoto => labeled.Target,
                    _ => null,
                };
            }

            var iterationBody = parent switch
            {
                ForKoto f => f.Body,
                WhileKoto w => w.Body,
                LoopKoto l => l.Body,
                _ => null,
            };
            if (child == iterationBody)
            {
                if (jump is YieldKoto)
                {
                    return null;
                }

                if (jump.Label is null && jump is ExitKoto or ContinueKoto)
                {
                    return parent;
                }
            }

            if (jump is YieldKoto &&
                ((parent is IfKoto conditional && (conditional.ElseBody == child || conditional.Branches.Any(x => x.Body == child))) ||
                 (parent is MatchKoto match && match.Arms.Any(x => x.Body == child))))
            {
                return parent;
            }
        }

        return null;
    }

    /// <summary>Classifies selections using context, explicit body forms, and lexically targeted yields.</summary>
    /// <param name="selection">An attached if or match node.</param>
    /// <returns>Whether the selection requires a result.</returns>
    public static bool IsResultRequiringSelection(Koto selection)
    {
        if (selection is not (IfKoto or MatchKoto))
        {
            return false;
        }

        if (IsValueContext(selection) ||
            (selection is IfKoto conditional && (conditional.Branches.Any(x => x.Body.IsExpressionBody) || conditional.ElseBody?.IsExpressionBody == true)) ||
            (selection is MatchKoto match && match.Arms.Any(x => x.Body is not CodeBlockKoto)))
        {
            return true;
        }

        return ContainsYield(selection);

        bool ContainsYield(Koto node)
        {
            if (node is YieldKoto jump && ResolveTransferTarget(jump) == selection)
            {
                return true;
            }

            // Deferred directives must be selected before their syntax participates.
            if (node is CompileTimeIfKoto or CompileTimeCaseGroupKoto)
            {
                return false;
            }

            return node.ChildNodes.Any(ContainsYield);
        }
    }

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
                return expression is not CodeBlockKoto blockBody || blockBody.IsExpressionBody ||
                    conditional.Branches.Any(x => x.Condition == expression);
            case MatchKoto match:
                if (match.Expression == expression)
                {
                    return true;
                }

                foreach (var arm in match.Arms)
                {
                    if (arm.Body == expression)
                    {
                        return expression is not CodeBlockKoto;
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

    internal static bool IsInsideLabeledBody(Koto node, LabeledKoto labeled)
    {
        var body = labeled.Target switch
        {
            ForKoto f => f.Body,
            WhileKoto w => w.Body,
            LoopKoto l => l.Body,
            CodeBlockKoto block => block,
            _ => null,
        };
        for (var parent = node.Parent; parent is not null && parent != labeled; parent = parent.Parent)
        {
            if (parent == body)
            {
                return true;
            }
        }

        return false;
    }
}
