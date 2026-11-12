// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Determines syntactic transfer targets and contexts independently of type inference and reachability.</summary>
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

            if (parent is FunctionKoto function && IsParameterDefault(function, child))
            {
                return null; // Defaults cannot transfer into the declaration's enclosing execution.
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

    // SPEC 14.7, 14.8: whether the child is a branch's or an arm's Body of its parent, the part the selection attached it as.
    internal static bool IsSelectionBody(Koto parent, Koto child)
        => child.SelectionRole == SelectionRole.Body && ReferenceEquals(child.Parent, parent);

    internal static bool IsParameterDefault(FunctionKoto function, Koto expression)
    {
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (ReferenceEquals(function.Parameters[i].DefaultValue, expression))
            {
                return true;
            }
        }

        return false;
    }

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
}
