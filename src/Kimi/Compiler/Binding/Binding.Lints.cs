// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the HIR lints (ARCH target pipeline P2-P5s; DECISIONS PD31, R5-M24): Binding's checks that never fail a node, one row
// per code in the lint table below, judged once per Final pass by one walk of the source modules' evaluated syntax, the last
// step of Bind(Final); and GuessedCompletion, the one guess rule of completion checks.
// Input: the Final pass's bound tree and facts: Types and adaptations, call plans, Copy proofs, InfersUnit, structural
// completion (ResultStructure), FailureCauses and the parser's recoveries.
// Output: the pass's lint records (node, row, completion root), cleared by ResetPass and published by ReportDiagnostics at a
// Final pass as Binding records: requirement Binding with BindingFailure.None and the row as condition, so a lint never shares
// a check key with Binding's own warnings (condition 0). A row that reads Binding's result at a node Binding failed, or rests
// on a guessed completion, records nothing when its code is a warning and is derived from those causes when it is an Error.
// Invariants: never fails a node; at one node the discard rows keep only the first (SPEC 17.5's priority); a warm rebind
// allocates nothing (duplicates are resolved by sorting at report time, never by a Koto-keyed set).
// Must not re-run inference or a proof to explain a record, lint the library's bodies or record from a Provisional pass.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The lint table (SPEC 17.5, 14.9.3, 22.10.4): each row's code, whether the row reads Binding's result at its node (Derived)
    // and whether it reads a completion the parser may have guessed (Guessed). A row's number is its records' condition; rows
    // 1-4 are the discard checks in SPEC 17.5's priority order.
    private static readonly (DiagnosticCode Code, bool Derived, bool Guessed)[] LintTable =
    [
        default,
        (DiagnosticCode.DiscardedTail_Kd, Derived: true, Guessed: true),
        (DiagnosticCode.DiscardedResult_Kd, Derived: true, Guessed: false),
        (DiagnosticCode.UnusedTrySuccess_Kd, Derived: true, Guessed: false),
        (DiagnosticCode.DiscardedValue_Kd, Derived: true, Guessed: false),
        (DiagnosticCode.OwningWriteArgument_Kd, Derived: true, Guessed: false),
        (DiagnosticCode.StaticWhileTrue_Kd, Derived: false, Guessed: false),
    ];

    private readonly List<(Koto Node, LintRow Row, Koto? Root)> lints = new();
    private LintWalk? lintWalk;

    // Created at the first guess query after a recovery or in an incomplete pass: completion that assumes guessed nodes never
    // complete, and the causes of the guesses it met.
    private StructuralCompletion? guessedCompletion;
    private List<DiagnosticKey>? guessCauses;

    // The rows of the lint table.
    private enum LintRow : byte
    {
        None,
        DiscardedTail,
        DiscardedResult,
        UnusedTrySuccess,
        DiscardedValue,
        OwningWriteArgument,
        StaticWhileTrue,
    }

    /// <summary>Gets the causes a completion check rests on when the syntax from a node up to a root completes normally only through
    /// nodes whose completion Binding did not establish: a body the parser supplied for a missing one (docs/dev/DIAGNOSTICS.md §4.4),
    /// whose cause is its syntax Error, or an unresolved expression, whose causes are its Binding causes. Written, resolved syntax
    /// reaching the same completion keeps the check direct.</summary>
    /// <param name="node">The checked syntax.</param>
    /// <param name="root">The syntax whose completion, down to the node, decided the check: the node itself, or a discarded
    /// tail's descent root.</param>
    /// <returns>The causes, or null when the check does not rest on a guess.</returns>
    internal DiagnosticKey[]? GuessedCompletion(Koto node, Koto root)
    {
        if (!node.CodeContext.HasRecoveries && this.Result.IsComplete)
        {
            return null;
        }

        var structural = this.ResultStructure();
        var guessed = this.guessedCompletion ??= new(this.ResultNeverEvidence, this.IsGuess);
        var causes = this.guessCauses ??= new();
        guessed.Clear();
        causes.Clear();
        var rests = false;
        for (Koto? part = node; part is not null && !rests; part = part == root ? null : part.Parent)
        {
            rests = structural.CanComplete(part) && !guessed.CanComplete(part);
        }

        return rests && causes.Count > 0 ? [.. causes] : null;
    }

    // A single-item body: the item of a body introduced by =>, a require failure body or a match arm's body.
    private static bool IsBodyItem(Koto node) => node.Parent switch
    {
        CodeBlockKoto block => block.IsExpressionBody,
        RequireKoto require => require.ElseBody == node,
        MatchKoto match => KotoHelper.IsSelectionBody(match, node),
        _ => false,
    };

    // A Type whose value a discard loses: neither Unit nor Never.
    private static bool HasValue(BoundType? type) => type is not null && !ReferenceEquals(type, BoundType.Unit) && !ReferenceEquals(type, BoundType.Never);

    // SPEC 17.5.3: a name of a local or a parameter, whose read has no effect.
    private static bool IsStableName(Koto node)
        => KotoHelper.UnwrapParentheses(node) is IdentifierNameKoto { BoundSymbol.Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter };

    // SPEC 17.5.2: an iteration's value comes from its exits, never from a tail it discards.
    private static bool IsIteration(Koto node) => node is LoopKoto or ForKoto or WhileKoto;

    // SPEC 17.3: `try` propagates a Result's error only to a function whose result is a Result of the same error Type.
    private static bool FailureTargetFits(Koto node)
    {
        if (node.BoundType is not { Kind: BoundTypeKind.Constructed, Components.Count: 2 } result ||
            result.Symbol?.LibraryDeclaration != KimiDeclarationId.Result)
        {
            return false;
        }

        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is FunctionKoto function)
            {
                return function.BoundSymbol?.Type is { Kind: BoundTypeKind.Constructed, Components.Count: 2 } target &&
                    target.Symbol?.LibraryDeclaration == KimiDeclarationId.Result &&
                    (ReferenceEquals(target.Components[1], result.Components[1]) || Equals(target.Components[1], result.Components[1]));
            }

            if (parent is PropertyAccessorKoto or DeferredBlockKoto)
            {
                return false;
            }
        }

        return false;
    }

    // SPEC 17.5.1, 23.1.3: a discarded Result or try success value that is a direct item of an indented body offers `_ = ` before it
    // and, for a Result whose error Type the enclosing function's failure return target takes (SPEC 17.3), `_ = try ` first; `_ =`
    // puts the expression in Value Context (SPEC 14.2.4) and changes no scope, so no condition is relevant.
    private static DiagnosticRepairFact[]? DiscardRepairs(Koto node, LintRow row)
    {
        if (row is not (LintRow.DiscardedResult or LintRow.UnusedTrySuccess) || node.Parent is not CodeBlockKoto { IsExpressionBody: false })
        {
            return null;
        }

        var start = new SourceSpan(node.Span.Start, 0);
        var discard = new DiagnosticRepairFact(RepairKind.ExplicitDiscard, null, [node.Edit(start, "_ = ")], RepairConditionSet.None);
        return row == LintRow.DiscardedResult && FailureTargetFits(node)
            ? [new(RepairKind.PropagateFailure, null, [node.Edit(start, "_ = try ")], RepairConditionSet.None), discard]
            : [discard];
    }

    // One node's records in sequence, a discarded tail's innermost root first; the comparison is total, so the order is stable.
    private static int CompareLints((Koto Node, LintRow Row, Koto? Root) x, (Koto Node, LintRow Row, Koto? Root) y)
    {
        var order = x.Node.SyntaxOwner!.Module.CompareTo(y.Node.SyntaxOwner!.Module);
        if (order == 0)
        {
            order = x.Node.SyntaxId.CompareTo(y.Node.SyntaxId);
        }

        if (order == 0)
        {
            order = ((byte)x.Row).CompareTo((byte)y.Row);
        }

        return order != 0 ? order : (y.Root?.SyntaxId ?? 0).CompareTo(x.Root?.SyntaxId ?? 0);
    }

    // The last step of a Final pass: one walk of each source module (the library is not linted).
    private void WalkLints()
    {
        var walk = this.lintWalk ??= new(this);
        foreach (var module in this.compilation.SourceModules)
        {
            walk.Visit(module.RootKoto);
        }
    }

    // Publishes the pass's records: of one node's discard rows only the first, and of a row recorded twice the innermost root's.
    private void ReportLints()
    {
        this.lints.Sort(CompareLints);
        var requirement = DiagnosticRequirement.Binding(BindingFailure.None);
        for (var i = 0; i < this.lints.Count; i++)
        {
            var (node, row, root) = this.lints[i];
            if (i > 0 && this.lints[i - 1].Node == node && (this.lints[i - 1].Row == row || row <= LintRow.DiscardedValue))
            {
                continue;
            }

            var (code, derived, guessed) = LintTable[(int)row];
            var causes = derived ? this.FailureCauses(node) : null;
            causes ??= guessed ? this.GuessedCompletion(node, root ?? node) : null;
            if (causes is null)
            {
                node.Report(requirement, code, condition: (ushort)row, repairs: DiscardRepairs(node, row));
            }
            else if (DiagnosticEntries.TryGet(code, out var entry) && entry.Severity == DiagnosticSeverity.Error)
            {
                node.ReportDerived(requirement, causes, (ushort)row);
            }
        }
    }

    // A node whose completion Binding did not establish, assumed not to complete: a body the parser supplied for a missing one,
    // the empty block of an indented body or the Error of a single-item body (other Errors stand for operands), or an unresolved
    // expression. Records its causes.
    private bool IsGuess(Koto node)
    {
        if ((node is CodeBlockKoto || (node is ErrorKoto && IsBodyItem(node))) && node.CodeContext.RecoveryCause(node) is { } supplied)
        {
            this.guessCauses!.Add(supplied);
            return true;
        }

        if (node.BindingState == BindingState.Resolved || node is not ExpressionKoto || node is ErrorKoto || node.CodeContext.RecoveryCause(node) is not null)
        {
            return false;
        }

        this.guessCauses!.AddRange(this.FailureCauses(node) ?? [DiagnosticKey.Unresolved]);
        return true;
    }

    // SPEC 10.2: the Type a resolved expression supplies, its one adaptation's, else its own; null while Binding has not resolved it.
    private BoundType? SuppliedType(Koto node)
    {
        if (node.BindingState != BindingState.Resolved)
        {
            return null;
        }

        return this.adaptations.TryGetValue(node, out var adaptation) ? adaptation.Type : node.ErasedFunctionType ?? node.BoundType;
    }

    // The rows judged at one evaluated node: while true (SPEC 14.9.3), an owning write argument (SPEC 17.5, 22.10.4) and the discard
    // rows of an expression in Discard Context (SPEC 14.2.4), recorded at the expression inside its parentheses.
    private void LintNode(Koto node)
    {
        if (node is WhileKoto loop && KotoHelper.UnwrapParentheses(loop.Condition) is BoolLiteralKoto { Value: true })
        {
            this.lints.Add((node, LintRow.StaticWhileTrue, null));
        }
        else if (node is InvocationKoto call && CallPlan.Of(call) is { Target.CompilerFunction: CompilerFunctionKind.WriterWrite } write)
        {
            for (var i = 0; i < call.ArgumentNodes.Count; i++)
            {
                if (write.ArgumentToParameter[i] == 1 && call.ArgumentNodes[i] is InterpolatedStringKoto literal)
                {
                    this.lints.Add((literal, LintRow.OwningWriteArgument, null));
                }
            }
        }

        if (node is ExpressionKoto && node.Parent is not ParenthesizedKoto && !KotoHelper.IsValueContext(node))
        {
            var value = KotoHelper.UnwrapParentheses(node);
            var row = this.DiscardRow(value);
            if (row != LintRow.None)
            {
                this.lints.Add((value, row, null));
            }
        }
    }

    // SPEC 17.5.1, 17.5.3: a discarded Kimi Result, a try success value that is neither Unit nor Never, or an effect-free value.
    private LintRow DiscardRow(Koto value)
    {
        if (value is { BindingState: BindingState.Resolved, BoundType: { Kind: BoundTypeKind.Constructed } type } && type.Symbol == this.Library.Result)
        {
            return LintRow.DiscardedResult;
        }

        if (value is TryKoto && HasValue(this.SuppliedType(value)))
        {
            return LintRow.UnusedTrySuccess;
        }

        return this.IsEffectFree(value) ? LintRow.DiscardedValue : LintRow.None;
    }

    // SPEC 17.5.3: a value built only from literals and stable names of a Scalar Type, `!` and built-in comparisons, a runtime Type
    // test of a stable name, Tuples, and Case constructions whose Type is proven Copy, so their destruction is not observable.
    private bool IsEffectFree(Koto node)
    {
        node = KotoHelper.UnwrapParentheses(node);
        if (node is IsKoto { IsRuntimeTest: true } test)
        {
            return test is { BindingState: BindingState.Resolved, BoundRuntimeTest: not null } && IsStableName(test.Left);
        }

        if (node is TupleLiteralKoto tuple)
        {
            return this.AreEffectFree(tuple.Elements);
        }

        if (this.TryGetEnumConstruction(node, out _))
        {
            return node is { BindingState: BindingState.Resolved, BoundType: { } type } && this.ProveCopy(type, node) == ConstraintProof.Proven &&
                (node is not InvocationKoto call || this.AreEffectFree(call.ArgumentNodes));
        }

        return ScalarTypes.Supports(this.SuppliedType(node)) && node switch
        {
            NumberLiteralKoto or BoolLiteralKoto or CharLiteralKoto => true,
            IdentifierNameKoto => IsStableName(node),
            UnaryKoto { Akind: KotoKind.Not } unary => this.IsEffectFree(unary.Operand),
            BinaryKoto { Akind: >= KotoKind.LessThan and <= KotoKind.GreaterThanEquals or KotoKind.EqualsEquals or KotoKind.ExclamationEquals } comparison =>
                this.IsEffectFree(comparison.Left) && this.IsEffectFree(comparison.Right),
            _ => false,
        };
    }

    private bool AreEffectFree(IReadOnlyList<Koto> operands)
    {
        for (var i = 0; i < operands.Count; i++)
        {
            if (!this.IsEffectFree(operands[i]))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 17.5.2: a do or a selection whose Unit result Binding inferred, rather than a declaration, a construct rule or an expected
    // Type fixing it, records a non-Unit tail it discards, with the root of the descent.
    private void LintTail(Koto node)
    {
        if (node is DoKoto or IfKoto or MatchKoto and not TryKoto && this.InfersUnit(node))
        {
            var root = node is DoKoto scoped ? scoped.Body : node;
            this.LintUnitTail(root, root, this.ResultStructure());
        }
    }

    // Descends from the root through grouping, labels and the bodies of if, match and do that complete normally.
    private void LintUnitTail(Koto body, Koto root, StructuralCompletion structural)
    {
        if (!structural.CanComplete(body))
        {
            return;
        }

        switch (KotoHelper.UnwrapParentheses(body))
        {
            case CodeBlockKoto block:
                if (block.Items.Count > 0)
                {
                    this.LintUnitTail(block.Items[^1], root, structural);
                }

                break;
            case LabeledKoto label:
                this.LintUnitTail(label.Target, root, structural);
                break;
            case DoKoto scoped:
                this.LintUnitTail(scoped.Body, root, structural);
                break;
            case IfKoto conditional:
                for (var i = 0; i < conditional.Branches.Count; i++)
                {
                    this.LintUnitTail(conditional.Branches[i].Body, root, structural);
                }

                if (conditional.ElseBody is { } other)
                {
                    this.LintUnitTail(other, root, structural);
                }

                break;
            case MatchKoto match when match is not TryKoto:
                for (var i = 0; i < match.Arms.Count; i++)
                {
                    this.LintUnitTail(match.Arms[i].Body, root, structural);
                }

                break;
            case var tail when tail is ExpressionKoto and not (UnitLiteralKoto or JumpKoto) && !IsIteration(tail) && HasValue(this.SuppliedType(tail)):
                this.lints.Add((tail, LintRow.DiscardedTail, root));
                break;
        }
    }

    // The walk of one module's evaluated syntax in source order: Types, attributes, Origin clauses and effect items are not
    // evaluated, nor are a function's signature and a for loop's bindings. An excluded test is not linted, nor is a function part
    // (a default, the base initializer or the body) holding a #switch that selected no arm (SPEC 19.3).
    private sealed class LintWalk(Binding binding) : KotoVisitor
    {
        // Whether the function part being walked holds a #switch that selected no arm.
        private bool unselected;

        public override void Visit(Koto node)
        {
            switch (node)
            {
                case TypeKoto or AttributeKoto or OriginRelationKoto or EffectBoundKoto:
                    return;
                case CompileTimeSwitchKoto:
                    this.unselected = true;
                    return;
                case FunctionKoto function:
                    this.VisitFunction(function);
                    return;
                case PropertyAccessorKoto accessor:
                    this.VisitPart(accessor.Body);
                    return;
                case ForKoto loop:
                    binding.LintNode(loop);
                    this.Visit(loop.Iterable);
                    this.Visit(loop.Body);
                    return;
            }

            binding.LintNode(node);
            node.VisitChildren(this);
            binding.LintTail(node);
        }

        private void VisitFunction(FunctionKoto function)
        {
            if (TestDefinition.Marker(function) is not null && !TestDefinition.IsIncluded(function))
            {
                return;
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                this.VisitPart(function.Parameters[i].DefaultValue);
            }

            this.VisitPart(function.BaseInitializer);
            this.VisitPart((Koto?)function.Body ?? function.ExpressionBody);
        }

        // A function part: its records are dropped when it holds a #switch that selected no arm.
        private void VisitPart(Koto? part)
        {
            if (part is null)
            {
                return;
            }

            var start = binding.lints.Count;
            var outer = this.unselected;
            this.unselected = false;
            this.Visit(part);
            if (this.unselected)
            {
                binding.lints.RemoveRange(start, binding.lints.Count - start);
            }

            this.unselected = outer;
        }
    }
}
