// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns structural completion: whether a node's evaluation can complete normally and the targets its Exit and Yield transfers reach
// outward, from source structure.
// Input: a node, its creator's Never evidence, optionally the guesses (which reads the composition cannot know), an invocation's
// omitted default arguments, read through its creator's Binding (Binding.CallOf), and each jump's published target
// (Binding.TransferTarget, BU-101), the one input of a composed form that is not one of its parts.
// Output: CanComplete, read by Binding's result Types and the HIR Completion column.
// A node still being composed (a recursive default) is assumed to complete. Results hold until Clear, which a caller calls whenever its
// Never evidence may change: each instance's query memo is its own working-state family of the node's module's NodeColumns (Memo),
// whose row carries the query that wrote it, so Clear advances the instance's query and a row of another query reads absent (DECISIONS
// BU-220 as R7-N5 B6b amends it). A completion's transfers are values: the SyntaxIds of the targets its Exit and Yield transfers reach
// outward (Outward).
// Invariant: a query composes only nodes of the running pass's modules, never a frozen library node (the library declares no parameter
// default, and GuessedCompletionNodes answers none at a frozen node: KimiLibraryShapeTest's two premise rows).
// Must not evaluate constants or Scope Exit cleanup, or report diagnostics.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Composes conservative source paths without evaluating constants or Scope Exit cleanup.</summary>
/// <param name="binding">The Binding that creates it, whose current plans give an invocation's omitted default arguments.</param>
/// <param name="isNever">Whether an evaluated node has Type Never.</param>
/// <param name="isGuess">Whether a read the composition makes at a node is unknown, assumed then not to complete normally: asked only
/// where it reads the node itself, at a block (its items, which the parser may have supplied) and at an evaluated node (its Never
/// evidence), never at a form it composes from its parts (PLAN G47).</param>
/// <param name="memo">The instance's family in a module's NodeColumns, which holds its query memo.</param>
internal sealed class StructuralCompletion(Binding binding, Func<Koto, bool> isNever, Func<Koto, bool>? isGuess, StructuralCompletion.Memo memo)
{
    // The chains of Outward's target ids: cell i - 1 holds a target and the next cell's index plus 1 (0 ends the chain). A chain grows
    // only at its head and never changes once a completion holds it, so completions share tails; Clear empties the cells when it ends
    // the query whose rows hold their chains.
    private readonly List<(int Target, int Next)> outward = new();
    private readonly List<Koto> children = new();
    private Collector? collector;

    // The query the memo's current rows carry: FirstQuery at creation, advanced by Clear through Binding.NextQuery, which skips 0 (an
    // absent row's number: no query).
    private uint query = Binding.FirstQuery;

    /// <summary>Selects an instance's working-state family of a module's NodeColumns: a node's completion in the query its row carries.
    /// Each of the four creators passes a static lambda naming its family, chosen once at creation (no capture, no allocation); no reader
    /// tests which family an instance holds (DECISIONS BU-220 as R7-N5 B6b amends it).</summary>
    /// <param name="columns">The module's families.</param>
    /// <returns>The instance's column.</returns>
    internal delegate ref StampedColumn<(uint Query, Completion Completion), Binding.PassCounter> Memo(ref NodeColumns columns);

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

    // Reaching these runs none of their contents: a function's or an accessor's body runs when called and a Scope Exit block at
    // Scope Exit, which this does not evaluate; Type syntax and a #switch that selected no arm have nothing to run. The HIR
    // publisher's Keeps reads it too (DECISIONS Z16).
    internal static bool RunsNoContents(Koto node) => node switch
    {
        FunctionKoto or PropertyAccessorKoto or DeferredBlockKoto => true,
        TypeKoto or CompileTimeSwitchKoto => true,
        _ => false,
    };

    // SPEC 12, 13.4, 14: whether a form's evaluation, where it is evaluated, reaches a part on every path through it, the one table of
    // the paths Compose composes: not a part a form selects or repeats (a selection's bodies, SelectionRole.Body, and Patterns; an if's
    // later conditions; a match's guards; a while's and a for's body; a require's failure body; a test's message; `and`'s and `or`'s right
    // operand), nor any part of a form that runs none of its contents there (RunsNoContents); every other form reaches each of its parts,
    // Compose's sequence (Evaluate, Sequence). A form Compose gives a new path adds its row here. The HIR publisher's Keeps reads it
    // (DECISIONS Z16).
    internal static bool OnEveryPath(Koto form, Koto part) => part.SelectionRole == SelectionRole.None && !RunsNoContents(form) && form switch
    {
        IfKoto conditional => ReferenceEquals(part, conditional.Branches[0].Condition),
        MatchKoto match => ReferenceEquals(part, match.Expression),
        WhileKoto loop => ReferenceEquals(part, loop.Condition),
        ForKoto loop => ReferenceEquals(part, loop.Iterable),
        RequireKoto require => ReferenceEquals(part, require.Condition),
        TestVerificationKoto verification => ReferenceEquals(part, verification.Condition),
        BinaryKoto { Akind: KotoKind.And or KotoKind.Or } logical => ReferenceEquals(part, logical.Left),
        _ => true,
    };

    // The forms a jump inside them can target (Binding.TransferTarget): an exit targets an iteration or a Do (a continue an
    // iteration), a yield a selection. Function and Scope Exit targets run no contents here. The HIR publisher's Keeps reads it too.
    internal static bool IsTransferTarget(Koto node) => node switch
    {
        LoopKoto or WhileKoto or ForKoto or DoKoto => true,
        IfKoto or MatchKoto => true,
        _ => false,
    };

    /// <summary>Gets the work since creation, which <see cref="Clear"/> keeps: the node completions composed and the Outward cells
    /// read or added, a scaling check's measure.</summary>
    internal long Work { get; private set; }

    internal bool CanComplete(Koto node) => this.Visit(node).Normal;

    internal void Clear()
    {
        this.query = Binding.NextQuery(this.query);
        this.outward.Clear();
    }

    // The target a transfer completes (SPEC 14.1): an Exit or a Yield to a form that receives it here; a Continue repeats its target,
    // a Return's and a deferred body's targets run no contents here, and an invalid transfer goes to none.
    private static Koto? Completes(JumpKoto jump)
        => jump is not ContinueKoto && Binding.TransferTarget(jump) is { } target && IsTransferTarget(target) ? target : null;

    private bool Holds(int chain, int target)
    {
        for (; chain != 0; chain = this.outward[chain - 1].Next)
        {
            this.Work++;
            if (this.outward[chain - 1].Target == target)
            {
                return true;
            }
        }

        return false;
    }

    private int With(int chain, int target)
    {
        if (this.Holds(chain, target))
        {
            return chain;
        }

        this.outward.Add((target, chain));
        return this.outward.Count;
    }

    private int Join(int left, int right)
    {
        if (left == 0 || left == right)
        {
            return right;
        }

        var result = left;
        for (var chain = right; chain != 0; chain = this.outward[chain - 1].Next)
        {
            result = this.With(result, this.outward[chain - 1].Target);
        }

        return result;
    }

    // The chain without a held target: the cells above it are re-added over the rest (a chain's ids are unique, so none is held there).
    private int Without(int chain, int target)
    {
        this.Work++;
        var (head, next) = this.outward[chain - 1];
        if (head == target)
        {
            return next;
        }

        this.outward.Add((head, this.Without(next, target)));
        return this.outward.Count;
    }

    private Completion Merge(Completion left, Completion right)
        => new(left.Normal || right.Normal, this.Join(left.Outward, right.Outward));

    private Completion Sequence(IReadOnlyList<Koto> items, int start, int count)
    {
        var result = new Completion(true, 0);
        for (var i = start; i < start + count && result.Normal; i++)
        {
            var next = this.Visit(items[i]);
            result = this.Merge(result, next) with { Normal = next.Normal };
        }

        return result;
    }

    private Completion Visit(Koto node)
    {
        if (node.SyntaxOwner is { } table && memo(ref table.Hir.Families).Get(node.SyntaxId, binding) is var row && row.Query == this.query)
        {
            return row.Completion;
        }

        // Selected defaults can form recursive evaluation dependencies. Until a
        // node completes, conservatively assume completion; semantic flow retains
        // a pending obligation for recursive defaults.
        this.Remember(node, new(true, 0));
        this.Work++;
        var result = this.CloseTransfers(node, this.Compose(node));
        this.Remember(node, result);
        return result;
    }

    private void Remember(Koto node, Completion completion) => memo(ref HirTables.Of(node).Families).Ref(node.SyntaxId, binding) = (this.query, completion);

    // A form's completion from its parts' completions in evaluation order; any other node evaluates its children (Evaluate). The parts
    // each case reaches on every path are OnEveryPath's rows; a case that changes them changes its row.
    private Completion Compose(Koto node)
    {
        Completion result;
        switch (node)
        {
            case Koto when RunsNoContents(node):
                return new(true, 0);
            case LabeledKoto label:
                return this.Visit(label.Target);
            case CodeBlockKoto body:
                return isGuess?.Invoke(body) == true ? new(false, 0) : this.Sequence(body.Items, 0, body.Items.Count);
            case DoKoto scoped:
                return this.Visit(scoped.Body);
            case UnsafeBlockKoto permission:
                return this.Visit(permission.Body);
            case JumpKoto jump:
                result = jump.Expression is { } operand ? this.Visit(operand) : new(true, 0);
                return result.Normal ? new(false, Completes(jump) is { } target ? this.With(result.Outward, target.SyntaxId) : result.Outward) : result;
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
                return isGuess?.Invoke(node) == true ? new(false, 0) : this.Evaluate(node);
        }
    }

    // A branch's body runs only when its condition completes, as do the later branches; the else body, or Unit without one, runs
    // after the last condition.
    private Completion Conditional(IfKoto conditional)
    {
        var result = new Completion(false, 0);
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
            result = this.Merge(result, conditional.ElseBody is { } other ? this.Visit(other) : new(true, 0));
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
                var guard = arm.Guard is { } condition ? this.Visit(condition) : new Completion(true, 0);
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
        if (result.Normal && node is InvocationKoto invocation && binding.CallOf(invocation) is { } call)
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

    // A transfer that goes to the node ends at it: an exit or a yield completes the node normally (Completes keeps no other). Other
    // transfers leave it (SPEC 14.9.3; DECISIONS BU-101).
    private Completion CloseTransfers(Koto node, Completion result)
        => result.Outward != 0 && IsTransferTarget(node) && this.Holds(result.Outward, node.SyntaxId)
            ? new(true, this.Without(result.Outward, node.SyntaxId)) : result;

    // A node's Structural Completion (SPEC 14.9.3): whether it can complete normally, and the targets, by SyntaxId in the node's module,
    // that its Exit and Yield transfers reach outward (a chain of outward; 0 for none). A transfer never leaves its function (SPEC
    // 14.5), and an omitted default argument passes only Normal on, so a chain's targets are ancestors in the node's own module.
    internal readonly record struct Completion(bool Normal, int Outward);

    private sealed class Collector(List<Koto> children) : KotoVisitor
    {
        public override void Visit(Koto node) => children.Add(node);
    }
}
