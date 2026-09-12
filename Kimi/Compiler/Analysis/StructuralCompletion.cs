// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Composes conservative source paths without evaluating constants or Scope Exit cleanup.</summary>
internal sealed class StructuralCompletion(Func<Koto, bool> isNever)
{
    private readonly Dictionary<Koto, Completion> cache = new(ReferenceEqualityComparer.Instance);

    internal bool CanComplete(Koto node) => this.Visit(node).Normal;

    private readonly List<HashSet<JumpKoto>> transferPool = new();
    private readonly List<Koto> children = new();
    private Collector? collector;
    private int cursor;

    internal void Clear()
    {
        this.cache.Clear();
        this.cursor = 0;
    }

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
            result.UnionWith(a);
        }

        if (b is not null)
        {
            result.UnionWith(b);
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

        Completion result;
        switch (node)
        {
            case FunctionKoto or PropertyAccessorKoto or DeferredBlockKoto or TypeKoto or CompileTimeMatchKoto:
                result = new(true, null);
                break;
            case LabeledKoto label:
                result = this.Visit(label.Target);
                break;
            case CodeBlockKoto body:
                result = this.Sequence(body.Items, 0, body.Items.Count);
                break;
            case DoKoto scoped:
                result = this.Visit(scoped.Body);
                break;
            case UnsafeBlockKoto permission:
                result = this.Visit(permission.Body);
                break;
            case JumpKoto jump:
                result = jump.Expression is { } operand ? this.Visit(operand) : new(true, null);
                if (result.Normal)
                {
                    result = new(false, this.Transfers(result.Transfers, jump: jump));
                }

                break;
            case IfKoto conditional:
                result = new(false, null);
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

                break;
            case MatchKoto match:
                result = this.Visit(match.Expression);
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

                break;
            case LoopKoto loop:
                result = this.Visit(loop.Body) with { Normal = false };
                break;
            case WhileKoto loop:
                result = this.Visit(loop.Condition);
                if (result.Normal)
                {
                    result = this.Merge(result, this.Visit(loop.Body) with { Normal = false });
                }

                break;
            case ForKoto loop:
                result = this.Visit(loop.Iterable);
                if (result.Normal)
                {
                    result = this.Merge(result, this.Visit(loop.Body) with { Normal = false });
                }

                break;
            case RequireKoto require:
                result = this.Visit(require.Condition);
                if (result.Normal)
                {
                    result = this.Merge(result, this.Visit(require.ElseBody) with { Normal = false });
                }

                break;
            case AndKoto or OrKoto:
                var logical = (BinaryKoto)node;
                result = this.Visit(logical.Left);
                if (result.Normal)
                {
                    result = this.Merge(result, this.Visit(logical.Right));
                }

                break;
            default:
                var start = this.children.Count;
                node.VisitChildren(this.collector ??= new(this.children));
                var count = this.children.Count - start;
                result = this.Sequence(this.children, start, count);
                this.children.RemoveRange(start, count);
                if (isNever(node))
                {
                    result = result with { Normal = false };
                }

                break;
        }

        if (result.Transfers is not null && node is IfKoto or MatchKoto or DoKoto or LoopKoto or WhileKoto or ForKoto)
        {
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

            result = new(normal, outward);
        }

        this.cache[node] = result;
        return result;
    }

    private readonly record struct Completion(bool Normal, HashSet<JumpKoto>? Transfers);

    private sealed class Collector(List<Koto> children) : KotoVisitor
    {
        public override void Visit(Koto node) => children.Add(node);
    }
}
