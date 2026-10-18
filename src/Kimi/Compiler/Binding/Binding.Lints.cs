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
// LintErrors counts the Error records for the old analysis, which rejects the program with them until H10.
// Invariants: never fails a node; at one node the discard rows keep only the first (SPEC 17.5's priority); a warm rebind
// allocates nothing (duplicates are resolved by sorting at report time, never by a Koto-keyed set).
// Must not re-run inference or a proof to explain a record, lint the library's bodies or record from a Provisional pass.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The lint table (SPEC 17.5, 14.9.3, 22.10.4, 14.3): each row's code, whether the row reads Binding's result at its node
    // (Derived) and whether it reads a completion the parser may have guessed (Guessed). A row's number is its records' condition;
    // rows 1-4 are the discard checks in SPEC 17.5's priority order.
    private static readonly (DiagnosticCode Code, bool Derived, bool Guessed)[] LintTable =
    [
        default,
        (DiagnosticCode.DiscardedTail_Kd, Derived: true, Guessed: true),
        (DiagnosticCode.DiscardedResult_Kd, Derived: true, Guessed: false),
        (DiagnosticCode.UnusedTrySuccess_Kd, Derived: true, Guessed: false),
        (DiagnosticCode.DiscardedValue_Kd, Derived: true, Guessed: false),
        (DiagnosticCode.OwningWriteArgument_Kd, Derived: true, Guessed: false),
        (DiagnosticCode.StaticWhileTrue_Kd, Derived: false, Guessed: false),
        (DiagnosticCode.UnsafeBlockRequired_Kd, Derived: false, Guessed: false),
        (DiagnosticCode.UnnecessaryUnsafeBlock_Kd, Derived: true, Guessed: false),
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
        UnsafeBlockRequired,
        UnnecessaryUnsafeBlock,
    }

    /// <summary>Gets the number of the pass's lint records whose code is an Error, which the old analysis counts with its own errors
    /// until H10.</summary>
    internal int LintErrors
    {
        get
        {
            var count = 0;
            foreach (var lint in this.lints)
            {
                count += IsError(lint.Row) ? 1 : 0;
            }

            return count;
        }
    }

    /// <summary>Gets the pass's lint records whose code is an Error, in their record order, which tests list with the old path's
    /// records (LegacyRecords) until H10.</summary>
    internal IEnumerable<(Koto Node, DiagnosticCode Code)> LintErrorRecords
        => this.lints.Where(static x => IsError(x.Row)).Select(static x => (x.Node, LintTable[(int)x.Row].Code));

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

    // Whether a row's code is an Error.
    private static bool IsError(LintRow row) => DiagnosticEntries.TryGet(LintTable[(int)row].Code, out var entry) && entry.Severity == DiagnosticSeverity.Error;

    // A direct record's message argument, primary location and repairs (SPEC 23.1.3): a discard's, and an unused Unsafe Block's, which
    // is located at its keyword (SPEC 14.3).
    private static (object? Argument, SourceSpan? Span, DiagnosticRepairFact[]? Repairs) LintFacts(Koto node, LintRow row) => row switch
    {
        LintRow.DiscardedResult or LintRow.UnusedTrySuccess => (null, null, DiscardRepairs(node, row)),
        LintRow.UnnecessaryUnsafeBlock => (null, new SourceSpan(node.Span.Start, Constants.UnsafeKeyword.Length), RemoveUnsafeRepairs((UnsafeBlockKoto)node)),
        _ => default,
    };

    // SPEC 17.5.1, 23.1.3: a discarded Result or try success value that is a direct item of an indented body offers `_ = ` before it
    // and, for a Result whose error Type the enclosing function's failure return target takes (SPEC 17.3), `_ = try ` first; `_ =`
    // puts the expression in Value Context (SPEC 14.2.4) and changes no scope, so no condition is relevant.
    private static DiagnosticRepairFact[]? DiscardRepairs(Koto node, LintRow row)
    {
        if (node.Parent is not CodeBlockKoto { IsExpressionBody: false })
        {
            return null;
        }

        var start = new SourceSpan(node.Span.Start, 0);
        var discard = new DiagnosticRepairFact(RepairKind.ExplicitDiscard, null, [node.Edit(start, "_ = ")], RepairConditionSet.None);
        return row == LintRow.DiscardedResult && FailureTargetFits(node)
            ? [new(RepairKind.PropagateFailure, null, [node.Edit(start, "_ = try ")], RepairConditionSet.None), discard]
            : [discard];
    }

    // SPEC 14.3, 23.1.3: removing an unused Unsafe Block keeps its statements' meaning only when its Body, an independent scope (SPEC
    // 14.2.1), declares no Name and registers no defer (Structure); a statement of an indented body whose lines allow the edit offers it.
    private static DiagnosticRepairFact[]? RemoveUnsafeRepairs(UnsafeBlockKoto block)
    {
        if (block.Parent is not CodeBlockKoto { IsExpressionBody: false })
        {
            return null;
        }

        for (var i = 0; !block.Body.IsExpressionBody && i < block.Body.Items.Count; i++)
        {
            if (block.Body.Items[i] is FieldKoto or FunctionKoto or DeferredBlockKoto or DeclarationContainerKoto)
            {
                return null;
            }
        }

        return RemoveUnsafeEdits(block) is { } edits ? [new(RepairKind.RemoveUnsafe, null, edits, RepairConditionSet.Structure)] : null;
    }

    // The edits that remove an Unsafe Block statement from an indented body: delete `unsafe => ` before an inline Body on the same
    // line, or delete the unsafe line and one indentation level of every line of an indented Body. No candidate when the unsafe line
    // holds other text, a Body line lacks a full level of space indentation, or a string literal spans lines (its content would change).
    private static DiagnosticEditFact[]? RemoveUnsafeEdits(UnsafeBlockKoto block)
    {
        if (block.CodeContext.SourceDocument is not { } document)
        {
            return null;
        }

        var text = document.SourceText.AsSpan();
        var body = block.Body;
        var keywordEnd = block.Span.Start + Constants.UnsafeKeyword.Length;
        var line = document.GetPosition(block.Span.Start).Line;
        var lineStart = document.LineStarts[line];
        if (!text[lineStart..block.Span.Start].IsWhiteSpace())
        {
            return null;
        }

        if (body.IsExpressionBody)
        {
            var bodyStart = body.Span.Start;
            return document.GetPosition(bodyStart).Line == line && text[keywordEnd..bodyStart].Trim().SequenceEqual("=>")
                ? [block.Edit(SourceSpan.FromBounds(block.Span.Start, bodyStart), string.Empty)] : null;
        }

        var lineEnd = lineStart + document.GetLineSpan(line).Length;
        var lastLine = document.GetPosition(Math.Max(body.Span.Start, body.Span.End - 1)).Line;
        if (!text[keywordEnd..lineEnd].IsWhiteSpace() || lastLine <= line || line + 1 >= document.LineCount || SpansLines(body, document))
        {
            return null;
        }

        var edits = new List<DiagnosticEditFact>(lastLine - line + 1)
        {
            block.Edit(SourceSpan.FromBounds(lineStart, document.LineStarts[line + 1]), string.Empty),
        };
        for (var current = line + 1; current <= lastLine; current++)
        {
            var content = document.GetLineSpan(current);
            if (content.IsWhiteSpace())
            {
                continue;
            }

            if (content.Length < Constants.IndentationSpaces || content[..Constants.IndentationSpaces].ContainsAnyExcept(' '))
            {
                return null;
            }

            edits.Add(block.Edit(new(document.LineStarts[current], Constants.IndentationSpaces), string.Empty));
        }

        return edits.ToArray();
    }

    // Whether a string literal inside the node spans lines; dedenting its continuation lines would change its content.
    private static bool SpansLines(Koto node, SourceDocument document)
    {
        var finder = new MultiLineLiteralFinder(document);
        finder.Visit(node);
        return finder.Found;
    }

    // SPEC 5, 4.3.4, 7.6: whether an operation needs an unsafe context: forming a raw Place, pointer arithmetic, noinit, or one of the
    // node's calls whose selected target is an unsafe function (the requirement is checked after selection); null while Binding has not
    // resolved an expression, whose need is then unknown.
    private static bool? RequiresUnsafe(Koto node)
    {
        if (node.BindingState != BindingState.Resolved)
        {
            return node is ExpressionKoto or NoInitKoto ? null : false;
        }

        if (node is DereferenceKoto or NoInitKoto || (node is BinaryKoto binary && ReferenceTypes.IsPointer(binary.Left.BoundType) &&
            (binary is IndexKoto || binary.Akind is KotoKind.Plus or KotoKind.Minus or KotoKind.PlusEquals or KotoKind.MinusEquals)))
        {
            return true;
        }

        for (var i = 0; CallPlan.Of(node, i) is { } call; i++)
        {
            if (call.Kind != CalleeKind.Value && call.Target.Declaration is FunctionKoto { Modifier: var modifier } && (modifier & ModifierKind.Unsafe) != 0)
            {
                return true;
            }
        }

        return false;
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
                var (argument, span, repairs) = LintFacts(node, row);
                node.Report(requirement, code, argument, condition: (ushort)row, span: span, repairs: repairs);
            }
            else if (IsError(row))
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

        // SPEC 14.3: whether an operation in the innermost Unsafe Block being walked used its permission, and whether one whose need
        // is unknown, such as an unresolved operation, may have.
        private bool unsafeUsed;
        private bool unsafeUncertain;

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
                case UnsafeBlockKoto block:
                    this.VisitUnsafe(block);
                    return;
                case ForKoto loop:
                    this.LintUnsafe(loop);
                    binding.LintNode(loop);
                    this.Visit(loop.Iterable);
                    this.Visit(loop.Body);
                    return;
            }

            this.LintUnsafe(node);
            binding.LintNode(node);
            node.VisitChildren(this);
            binding.LintTail(node);
        }

        // SPEC 5, 7.6, 14.3: an operation that needs an unsafe context outside one records row 7; inside one, it uses the innermost
        // Unsafe Block's permission, which an operation whose need is unknown may also use.
        private void LintUnsafe(Koto node)
        {
            switch (RequiresUnsafe(node))
            {
                case true when !KotoHelper.IsUnsafeContext(node):
                    binding.lints.Add((node, LintRow.UnsafeBlockRequired, null));
                    break;
                case true:
                    this.unsafeUsed = true;
                    break;
                case null:
                    this.unsafeUncertain = true;
                    break;
            }
        }

        // SPEC 14.3: a block whose permission no operation of its Body used, nor may have used, records row 8; an unknown need also
        // counts for the enclosing block.
        private void VisitUnsafe(UnsafeBlockKoto block)
        {
            var (used, uncertain) = (this.unsafeUsed, this.unsafeUncertain);
            (this.unsafeUsed, this.unsafeUncertain) = (false, false);
            this.Visit(block.Body);
            if (!this.unsafeUsed && !this.unsafeUncertain)
            {
                binding.lints.Add((block, LintRow.UnnecessaryUnsafeBlock, null));
            }

            (this.unsafeUsed, this.unsafeUncertain) = (used, uncertain || this.unsafeUncertain);
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

    // Finds a string literal that spans lines.
    private sealed class MultiLineLiteralFinder(SourceDocument document) : KotoVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(Koto node)
        {
            if (this.Found)
            {
                return;
            }

            if (node is StringLiteralKoto or InterpolatedStringKoto && document.SourceText.AsSpan(node.Span.Start, node.Span.Length).Contains('\n'))
            {
                this.Found = true;
                return;
            }

            base.Visit(node);
        }
    }
}
