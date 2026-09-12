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
            if (parent is DeferredBlockKoto deferred && child == deferred.Body)
            {
                return jump is ExitKoto { Label: null } ? deferred : null;
            }

            if (IsFunctionBody(parent, child))
            {
                return jump is ReturnKoto && parent is not FunctionKoto { IsGenerated: true } ? parent : null;
            }

            if (parent is LabeledKoto labeled && child == labeled.Target && jump.Label == labeled.Label && IsInsideLabeledBody(jump, labeled))
            {
                return jump switch
                {
                    ExitKoto when labeled.Target is ForKoto or WhileKoto or LoopKoto or DoKoto => labeled.Target,
                    YieldKoto when labeled.Target is IfKoto or MatchKoto => labeled.Target,
                    ContinueKoto when labeled.Target is ForKoto or WhileKoto or LoopKoto => labeled.Target,
                    _ => null,
                };
            }

            if (IsIterationBody(parent, child))
            {
                if (jump.Label is null && jump is ExitKoto or ContinueKoto)
                {
                    return parent;
                }
            }

            if (jump is YieldKoto { Label: null } && IsSelectionBody(parent, child))
            {
                return parent;
            }
        }

        return null;
    }

    /// <summary>Classifies a Labeled Block before reachability is considered.</summary>
    /// <param name="labeled">The attached label and its Block.</param>
    /// <returns>Whether explicit self-targeted results are required.</returns>
    public static bool IsResultRequiringLabeledBlock(LabeledKoto labeled)
        => labeled.Target is DoKoto && IsValueContext(labeled);

    /// <summary>Tests lexical unsafe permission without inheriting it across function bodies.</summary>
    /// <param name="node">The operation to inspect.</param>
    /// <returns>Whether an enclosing Unsafe Block grants permission.</returns>
    public static bool IsUnsafeContext(Koto node)
    {
        Koto child = node;
        for (var parent = child.Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            if (IsFunctionBody(parent, child))
            {
                return false;
            }

            if (parent is UnsafeBlockKoto)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether a selection's result is used, without consulting its bodies.</summary>
    /// <param name="selection">The attached selection.</param>
    /// <returns>Whether the selection is in Value Context.</returns>
    public static bool IsResultRequiringSelection(Koto selection)
        => selection is IfKoto or MatchKoto && IsValueContext(selection);

    /// <summary>Determines whether a function's fixed return type discards a single-item value.</summary>
    /// <param name="boundary">The function or accessor.</param>
    /// <returns>Whether the return type is already Unit.</returns>
    public static bool DiscardsFunctionBody(Koto boundary) => boundary switch
    {
        FunctionKoto f => f.IsGenerated || f.IsConstructor || f.IsDestructor ||
            (f.ReturnType is null && !f.IsAnonymous) || IsUnitType(f.ReturnType) ||
            ReferenceEquals(f.BoundSymbol?.Type, BoundType.Unit),
        PropertyAccessorKoto a => a.AccessorKind == PropertyAccessorKind.Set || IsUnitType(a.ReturnType) ||
            (a.ReturnType is null && a.Parent is PropertyKoto property && IsUnitType(property.TypeKoto)),
        _ => false,
    };

    /// <summary>Determines whether a position uses its value; expected Unit alone does not discard it.</summary>
    /// <param name="expression">The expression in its attached syntax tree.</param>
    /// <returns>Whether the expression is in Value Context.</returns>
    public static bool IsValueContext(Koto expression)
    {
        switch (expression.Parent)
        {
            case null:
                return false;
            case CodeBlockKoto block:
                return block.TrailingExpression == expression && IsValueContext(block);
            case FunctionKoto function:
                return function.ExpressionBody == expression && !DiscardsFunctionBody(function);
            case PropertyAccessorKoto accessor:
                return accessor.Body == expression && expression is not CodeBlockKoto && !DiscardsFunctionBody(accessor);
            case LabeledKoto labeled:
                return IsValueContext(labeled);
            case ParenthesizedKoto parentheses:
                return IsValueContext(parentheses);
            case IfKoto conditional:
                return !IsSelectionBody(conditional, expression) || IsValueContext(conditional);
            case MatchKoto match:
                return !IsSelectionBody(match, expression) || IsValueContext(match);
            case DoKoto scoped:
                return IsValueContext(scoped);
            case RequireKoto require:
                return require.Condition == expression;
            case ForKoto loop:
                return loop.Iterable == expression;
            case WhileKoto loop:
                return loop.Condition == expression;
            case LoopKoto:
            case BlockStatementKoto:
                return false;
            default:
                return true;
        }
    }

    internal static bool IsInsideLabeledBody(Koto node, LabeledKoto labeled)
    {
        Koto child = node;
        for (var parent = child.Parent; parent is not null && parent != labeled; child = parent, parent = parent.Parent)
        {
            if (parent == labeled.Target)
            {
                return IsIterationBody(parent, child) || IsSelectionBody(parent, child) ||
                    (parent is DoKoto scoped && child == scoped.Body);
            }
        }

        return false;
    }

    internal static Koto UnwrapParentheses(Koto node)
    {
        while (node is ParenthesizedKoto parentheses)
        {
            node = parentheses.Operand;
        }

        return node;
    }

    internal static bool IsBodyExpression(Koto body) => body is ExpressionKoto and not CodeBlockKoto or FunctionKoto { IsAnonymous: true };

    private static bool IsUnitType(Koto? type)
        => type is TupleTypeKoto { ElementNodes.Count: 0 } || ReferenceEquals(type?.BoundType, BoundType.Unit) ||
            (type is ParenthesizedTypeKoto p && IsUnitType(p.Type));

    private static bool IsFunctionBody(Koto parent, Koto child)
        => (parent is FunctionKoto function && (child == function.Body || child == function.ExpressionBody)) ||
            (parent is PropertyAccessorKoto accessor && child == accessor.Body);

    private static bool IsIterationBody(Koto parent, Koto child) => parent switch
    {
        ForKoto f => child == f.Body,
        WhileKoto w => child == w.Body,
        LoopKoto l => child == l.Body,
        _ => false,
    };

    private static bool IsSelectionBody(Koto parent, Koto child)
    {
        if (parent is IfKoto conditional)
        {
            if (conditional.ElseBody == child)
            {
                return true;
            }

            for (var index = 0; index < conditional.Branches.Count; index++)
            {
                var branch = conditional.Branches[index];
                if (branch.Body == child)
                {
                    return true;
                }
            }
        }
        else if (parent is MatchKoto match)
        {
            for (var index = 0; index < match.Arms.Count; index++)
            {
                var arm = match.Arms[index];
                if (arm.Body == child)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
