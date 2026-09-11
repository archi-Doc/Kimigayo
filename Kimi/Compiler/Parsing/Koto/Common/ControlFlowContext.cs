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

            if (IsIterationBody(parent, child))
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

            if (jump is YieldKoto && IsSelectionBody(parent, child))
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
        => labeled.Target is CodeBlockKoto &&
            (IsValueContext(labeled) || TransferSearch.ContainsResult(labeled.Target));

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

    /// <summary>Classifies selections using context, explicit body forms, and lexically targeted yields.</summary>
    /// <param name="selection">An attached if or match node.</param>
    /// <returns>Whether the selection requires a result.</returns>
    public static bool IsResultRequiringSelection(Koto selection)
    {
        switch (selection)
        {
            case IfKoto conditional:
                if (conditional.ElseBody?.IsExpressionBody == true)
                {
                    return true;
                }

                foreach (var branch in conditional.Branches)
                {
                    if (branch.Body.IsExpressionBody)
                    {
                        return true;
                    }
                }

                break;
            case MatchKoto match:
                foreach (var arm in match.Arms)
                {
                    if (arm.Body is not CodeBlockKoto)
                    {
                        return true;
                    }
                }

                break;
            default:
                return false;
        }

        return IsValueContext(selection) || TransferSearch.ContainsYield(selection);
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
                if (expression is not CodeBlockKoto blockBody || blockBody.IsExpressionBody)
                {
                    return true;
                }

                foreach (var branch in conditional.Branches)
                {
                    if (branch.Condition == expression)
                    {
                        return true;
                    }
                }

                return false;
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
            case BlockStatementKoto:
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

    internal static Koto UnwrapParentheses(Koto node)
    {
        while (node is ParenthesizedKoto parentheses)
        {
            node = parentheses.Operand;
        }

        return node;
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

    private static bool IsSelectionBody(Koto parent, Koto child)
    {
        if (parent is IfKoto conditional)
        {
            if (conditional.ElseBody == child)
            {
                return true;
            }

            foreach (var branch in conditional.Branches)
            {
                if (branch.Body == child)
                {
                    return true;
                }
            }
        }
        else if (parent is MatchKoto match)
        {
            foreach (var arm in match.Arms)
            {
                if (arm.Body == child)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Searches for transfers delivering a result to one target, without child iterators or closures.</summary>
    private sealed class TransferSearch : KotoVisitor
    {
        [ThreadStatic]
        private static TransferSearch? cached;

        private Koto target = null!;
        private bool exitResults;
        private bool found;

        /// <summary>Determines whether a yield targets the selection.</summary>
        /// <param name="selection">The if or match node.</param>
        /// <returns>Whether a targeted yield exists.</returns>
        public static bool ContainsYield(Koto selection)
        {
            var search = Rent(selection, false);
            selection.VisitChildren(search); // The selection's own bodies are searched.
            return Return(search);
        }

        /// <summary>Determines whether an exit with a result operand targets the Labeled Block.</summary>
        /// <param name="block">The labeled block.</param>
        /// <returns>Whether a targeted exit result exists.</returns>
        public static bool ContainsResult(Koto block)
        {
            var search = Rent(block, true);
            search.Visit(block);
            return Return(search);
        }

        public override void Visit(Koto node)
        {
            if (this.found)
            {
                return;
            }

            if (this.exitResults)
            {
                if (node is ExitKoto { Expression: not null } exit && ResolveTransferTarget(exit) == this.target)
                {
                    this.found = true;
                    return;
                }

                if (node is CompileTimeMatchKoto or DeferredBlockKoto or FunctionKoto or PropertyAccessorKoto)
                {
                    return;
                }
            }
            else
            {
                if (node is YieldKoto yield && ResolveTransferTarget(yield) == this.target)
                {
                    this.found = true;
                    return;
                }

                // Deferred directives must be selected before their syntax participates.
                if (node is CompileTimeMatchKoto or DeferredBlockKoto)
                {
                    return;
                }

                // A yield inside a nested selection, iteration, or function body always resolves to that
                // boundary or nothing, so those bodies cannot deliver a result to the searched selection.
                if (node.Parent is { } parent && parent != this.target &&
                    (IsSelectionBody(parent, node) || IsIterationBody(parent, node) || IsFunctionBody(parent, node)))
                {
                    return;
                }
            }

            node.VisitChildren(this);
        }

        private static TransferSearch Rent(Koto target, bool exitResults)
        {
            // A nested search on the same thread (not expected) receives its own instance.
            var search = cached ?? new();
            cached = null;
            search.target = target;
            search.exitResults = exitResults;
            search.found = false;
            return search;
        }

        private static bool Return(TransferSearch search)
        {
            var found = search.found;
            search.target = null!;
            cached = search;
            return found;
        }
    }
}
