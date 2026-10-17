// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns structural completion: whether a node's evaluation can complete normally and which jumps leave it, from source structure.
// Input: a node, its creator's Never evidence, optionally which nodes the parser supplied, and an invocation's omitted default
// arguments, read through InvocationKoto.BoundCall.DefaultArguments (a Koto forwarding property removed at R7, DECISIONS R5-M9).
// Output: CanComplete, read by Binding's result Types, the HIR Completion column and the old control-flow analysis; the children's
// evaluation order and the transfer union that the flow analyses share.
// A node still being composed (a recursive default) is assumed to complete. Results stay cached until Clear, which a caller calls
// whenever its Never evidence may change. The cache and the transfer sets are keyed by Koto reference: ARCH "Syntax identity" debt,
// due by R7's exit.
// Must not evaluate constants or Scope Exit cleanup, or report diagnostics.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Composes conservative source paths without evaluating constants or Scope Exit cleanup.</summary>
/// <param name="isNever">Whether an evaluated node has Type Never.</param>
/// <param name="isGuess">Whether a node is syntax the parser supplied for missing syntax, assumed here not to complete normally.</param>
internal sealed class StructuralCompletion(Func<Koto, bool> isNever, Func<Koto, bool>? isGuess = null)
{
    private readonly Dictionary<Koto, Completion> cache = new(ReferenceEqualityComparer.Instance);

    private readonly List<HashSet<JumpKoto>> transferPool = new();
    private readonly List<Koto> children = new();
    private Collector? collector;
    private int cursor;

    internal static void CollectEvaluationChildren(Koto node, List<Koto> children, KotoVisitor collector)
    {
        node.VisitChildren(collector);
        if (node.Akind == KotoKind.Equals)
        {
            // Keep lexical visitors unchanged; simple assignment alone evaluates RHS first.
            var last = children.Count - 1;
            (children[last - 1], children[last]) = (children[last], children[last - 1]);
        }
    }

    // HashSet.UnionWith accepts IEnumerable and boxes its source enumerator.
    // Both flow analyses already retain concrete sets; enumerate those directly.
    internal static void UnionTransfers(HashSet<JumpKoto> destination, HashSet<JumpKoto> source)
    {
        if (!ReferenceEquals(destination, source))
        {
            foreach (var transfer in source)
            {
                destination.Add(transfer);
            }
        }
    }

    internal bool CanComplete(Koto node) => this.Visit(node).Normal;

    internal void Clear()
    {
        this.cache.Clear();
        this.cursor = 0;
    }

    // Reaching these runs none of their contents: a function's or an accessor's body runs when called and a Scope Exit block at
    // Scope Exit, which this does not evaluate; Type syntax and a #switch that selected no arm have nothing to run.
    private static bool RunsNoContents(Koto node) => node switch
    {
        FunctionKoto or PropertyAccessorKoto or DeferredBlockKoto => true,
        TypeKoto or CompileTimeSwitchKoto => true,
        _ => false,
    };

    // The forms a jump inside them can target (KotoHelper.ResolveTransferTarget): an exit targets an iteration or a Do (a continue
    // an iteration), a yield a selection. Function and Scope Exit targets run no contents here.
    private static bool IsTransferTarget(Koto node) => node switch
    {
        LoopKoto or WhileKoto or ForKoto or DoKoto => true,
        IfKoto or MatchKoto => true,
        _ => false,
    };

    private HashSet<JumpKoto> Transfers(HashSet<JumpKoto>? a, HashSet<JumpKoto>? b = null, JumpKoto? jump = null)
    {
        if (this.cursor == this.transferPool.Count)
        {
            this.transferPool.Add(new(ReferenceEqualityComparer.Instance));
        }

        var result = this.transferPool[this.cursor++];
        result.Clear();
        if (a is not null)
        {
            UnionTransfers(result, a);
        }

        if (b is not null)
        {
            UnionTransfers(result, b);
        }

        if (jump is not null)
        {
            result.Add(jump);
        }

        return result;
    }

    private Completion Merge(Completion left, Completion right)
        => new(left.Normal || right.Normal, left.Transfers is null && right.Transfers is null ? null : this.Transfers(left.Transfers, right.Transfers));

    private Completion Sequence(IReadOnlyList<Koto> items, int start, int count)
    {
        var result = new Completion(true, null);
        for (var i = start; i < start + count && result.Normal; i++)
        {
            var next = this.Visit(items[i]);
            result = this.Merge(result, next) with { Normal = next.Normal };
        }

        return result;
    }

    private Completion Visit(Koto node)
    {
        if (this.cache.TryGetValue(node, out var cached))
        {
            return cached;
        }

        // Selected defaults can form recursive evaluation dependencies. Until a
        // node completes, conservatively assume completion; semantic flow retains
        // a pending obligation for recursive defaults.
        this.cache[node] = new(true, null);
        var result = this.CloseTransfers(node, this.Compose(node));
        this.cache[node] = result;
        return result;
    }

    // A form's completion from its parts' completions in evaluation order; any other node evaluates its children (Evaluate).
    private Completion Compose(Koto node)
    {
        Completion result;
        switch (node)
        {
            case Koto when isGuess?.Invoke(node) == true:
                return new(false, null);
            case Koto when RunsNoContents(node):
                return new(true, null);
            case LabeledKoto label:
                return this.Visit(label.Target);
            case CodeBlockKoto body:
                return this.Sequence(body.Items, 0, body.Items.Count);
            case DoKoto scoped:
                return this.Visit(scoped.Body);
            case UnsafeBlockKoto permission:
                return this.Visit(permission.Body);
            case JumpKoto jump:
                result = jump.Expression is { } operand ? this.Visit(operand) : new(true, null);
                return result.Normal ? new(false, this.Transfers(result.Transfers, jump: jump)) : result;
            case IfKoto conditional:
                return this.Conditional(conditional);
            case MatchKoto match:
                return this.Match(match);
            case LoopKoto loop:
                return this.Visit(loop.Body) with { Normal = false };
            case WhileKoto loop:
                result = this.Visit(loop.Condition);
                return result.Normal ? this.Merge(result, this.Visit(loop.Body) with { Normal = false }) : result;
            case ForKoto loop:
                result = this.Visit(loop.Iterable);
                return result.Normal ? this.Merge(result, this.Visit(loop.Body) with { Normal = false }) : result;
            case DiscardKoto discard:
                return this.Visit(discard.Operand);
            case RequireKoto require:
                result = this.Visit(require.Condition);
                return result.Normal ? this.Merge(result, this.Visit(require.ElseBody) with { Normal = false }) : result;
            case TestVerificationKoto verification:
                result = this.Visit(verification.Condition);
                return result.Normal && verification.Message is { } message ? this.Merge(result, this.Visit(message) with { Normal = false }) : result;
            case IsKoto { IsRuntimeTest: true } test:
                return this.Visit(test.Left);
            case BinaryKoto { Akind: KotoKind.And or KotoKind.Or } logical:
                result = this.Visit(logical.Left);
                return result.Normal ? this.Merge(result, this.Visit(logical.Right)) : result;
            default:
                return this.Evaluate(node);
        }
    }

    // A branch's body runs only when its condition completes, as do the later branches; the else body, or Unit without one, runs
    // after the last condition.
    private Completion Conditional(IfKoto conditional)
    {
        var result = new Completion(false, null);
        var next = true;
        for (var i = 0; i < conditional.Branches.Count; i++)
        {
            var branch = conditional.Branches[i];
            if (!next)
            {
                break;
            }

            var condition = this.Visit(branch.Condition);
            result = this.Merge(result, condition with { Normal = false });
            next = condition.Normal;
            if (next)
            {
                result = this.Merge(result, this.Visit(branch.Body));
            }
        }

        if (next)
        {
            result = this.Merge(result, conditional.ElseBody is { } other ? this.Visit(other) : new(true, null));
        }

        return result;
    }

    // The subject runs first; an arm's body runs only when its guard, if any, completes, and the match completes through a body.
    private Completion Match(MatchKoto match)
    {
        var result = this.Visit(match.Expression);
        if (result.Normal)
        {
            result = result with { Normal = false };
            for (var i = 0; i < match.Arms.Count; i++)
            {
                var arm = match.Arms[i];
                var guard = arm.Guard is { } condition ? this.Visit(condition) : new Completion(true, null);
                var branch = guard.Normal ? this.Merge(guard with { Normal = false }, this.Visit(arm.Body)) : guard;
                result = this.Merge(result, branch);
            }
        }

        return result;
    }

    // Any other node evaluates its children in evaluation order, then an invocation's omitted default arguments; with Never
    // evidence it does not complete.
    private Completion Evaluate(Koto node)
    {
        var start = this.children.Count;
        CollectEvaluationChildren(node, this.children, this.collector ??= new(this.children));
        var count = this.children.Count - start;
        var result = this.Sequence(this.children, start, count);
        this.children.RemoveRange(start, count);
        if (result.Normal && node is InvocationKoto { BoundCall: { } call })
        {
            foreach (var omitted in call.DefaultArguments)
            {
                result = result with { Normal = this.Visit(omitted.Expression).Normal };
                if (!result.Normal)
                {
                    break;
                }
            }
        }

        if (isNever(node))
        {
            result = result with { Normal = false };
        }

        return result;
    }

    // A jump that targets the node ends at it: an exit or a yield completes the node normally, a continue repeats it. Other jumps
    // leave it.
    private Completion CloseTransfers(Koto node, Completion result)
    {
        if (result.Transfers is null || !IsTransferTarget(node))
        {
            return result;
        }

        var normal = result.Normal;
        var outward = this.Transfers(null);
        foreach (var jump in result.Transfers)
        {
            if (KotoHelper.ResolveTransferTarget(jump) == node)
            {
                normal |= jump is not ContinueKoto;
            }
            else
            {
                outward.Add(jump);
            }
        }

        return new(normal, outward);
    }

    private readonly record struct Completion(bool Normal, HashSet<JumpKoto>? Transfers);

    private sealed class Collector(List<Koto> children) : KotoVisitor
    {
        public override void Visit(Koto node) => children.Add(node);
    }
}
