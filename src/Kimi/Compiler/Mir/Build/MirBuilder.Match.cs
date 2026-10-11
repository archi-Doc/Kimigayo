// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Matches (SPEC 14.8, 15.1.6, plan 5.6.3): the Subject acquired once into its Place, then the arms in source order as a decision chain, each
// its Pattern's tests, its guard, its bindings and its body; no decision tree, no pruning by Pattern containment and no constant folding come
// before the analyses (SPEC 14.9.2), and where no arm matches is unreachable.
internal sealed partial class MirBuilder
{
    private readonly List<MirPlace> selected = []; // The Place each Pattern position of the matches being built selects, before its follows.

    // SPEC 14.8, 17.2.4: a match, or a try with its synthesized arms, a target for a yield. The Subject and the tests' temporaries end
    // when the match does, after the selected arm's scope (SPEC 15.1.6); a link whose Pattern tests nothing and has no guard still branches,
    // on a constant, to the next one, so that even a catch-all can fail for the analyses.
    private void Match(MirPlace destination, MatchKoto match)
    {
        if (BoundMatch.Of(match) is not { IsCurrent: true } plan || plan.Invalid || plan.Pending)
        {
            this.Opaque(match, destination); // Binding rejected a Pattern of it.
            return;
        }

        var scope = this.OpenScope(false);
        var subject = this.Temp(plan.SubjectBorrow ?? TypeOf(match.Expression), match.Expression, MirLocalRole.Subject);
        this.Assign(subject, match.Expression);
        var (after, start) = (this.Open(match, destination), this.selected.Count);
        CollectionsMarshal.SetCount(this.selected, start + plan.Positions.Count);
        for (var i = 0; i < plan.Arms.Count; i++)
        {
            var (arm, next, outer) = (plan.Arms[i], this.body.AddBlock(), this.scope);
            var guard = arm.Syntax.Guard;
            if (!this.Pattern(plan, arm.Pattern, subject, start, next) && guard is null)
            {
                this.Pass(MirOperand.Const(this.body.Const(new(MirConstKind.Bool, BoundType.Boolean, 1))), 1, next, arm.Syntax.Pattern);
            }

            if (guard is not null)
            {
                var (rejected, accepted) = (this.body.AddBlock(), this.body.AddBlock());
                this.scope = (ushort)this.body.AddScope(outer, MirScopeKind.Guard, SourceRef.Of(guard));
                var test = this.Test(guard, rejected, accepted, plan, arm.Pattern, start);
                this.Enter(rejected, test);
                this.body.Terminate(rejected, MirTerminator.Goto(next, this.Info(guard)));
                this.Enter(accepted, test);
            }

            this.scope = (ushort)this.body.AddScope(outer, MirScopeKind.Lexical, SourceRef.Of(arm.Syntax.Pattern));
            var bindings = this.temporaries = this.OpenScope(true);
            this.Bind(plan, arm.Pattern, start);
            this.Result(destination, arm.Syntax.Body, arm.Syntax.Body);
            this.CloseScope(bindings);
            this.scope = outer;
            this.Join(after, arm.Syntax.Body);
            this.current = next;
        }

        this.body.Terminate(this.current, MirTerminator.Unreachable(this.Info(match)));
        CollectionsMarshal.SetCount(this.selected, start);
        this.Close(after);
        this.CloseScope(scope);
    }

    // SPEC 14.8.1: a position's tests on the Place it selects, after its implicit follows: a Literal's comparison of the value (a string's
    // inspected through a shared borrow, SPEC 13.4), a Case's discriminant, then its payloads or a Tuple's elements, outside inward and left
    // to right; a mismatch goes on to the next arm. Whether it tested anything.
    private bool Pattern(BoundMatch plan, int index, MirPlace place, int start, int next)
    {
        var (position, tested) = (plan.Positions[index], false);
        this.selected[start + index] = place;
        place = this.Follow(place, position.ImplicitFollows);
        if (position.Kind == BoundPatternKind.Literal)
        {
            var (literal, value, operand) = (position.Literal, this.Temp(BoundType.Boolean, position.Source), MirOperand.Copy(place));
            if (literal.Kind == PatternLiteralKind.String)
            {
                var text = compilation.Types.Reference(SemanticsKind.Ref, BoundType.String);
                operand = this.Temporary(text, MirRvalue.Ref(MirRefKind.Shared, place, text), position.Source);
            }

            var (operands, magnitude) = (this.body.AddOperand(operand), (Int128)literal.Magnitude);
            this.body.AddOperand(MirOperand.Const(this.body.Const(literal.Kind switch
            {
                PatternLiteralKind.String => new(MirConstKind.String, BoundType.String, Value: literal.Text),
                PatternLiteralKind.Boolean => new(MirConstKind.Bool, BoundType.Boolean, magnitude),
                _ => new(MirConstKind.Integer, this.body.TypeOf(place)!, literal.Negative ? -magnitude : magnitude),
            })));
            this.Set(value, MirRvalue.Binary(MirBinaryOp.Eq, operands, true), position.Source);
            this.Pass(MirOperand.Copy(value), 1, next, position.Source);
            tested = true;
        }
        else if (position is { Kind: BoundPatternKind.Case, Case.Ordinal: var ordinal })
        {
            var discriminant = this.Temp(BoundType.ISize, position.Source);
            this.Set(discriminant, MirRvalue.Discriminant(place), position.Source);
            this.Pass(MirOperand.Copy(discriminant), ordinal, next, position.Source);
            (place, tested) = (this.body.Project(place, MirProjectionKind.Downcast, ordinal), true);
        }

        for (var child = index + 1; child < position.End; child = plan.Positions[child].End)
        {
            tested |= this.Pattern(plan, child, this.body.Project(place, MirProjectionKind.Field, plan.Positions[child].Element), start, next);
        }

        return tested;
    }

    // A test's branch: on in a new block where the value is the expected one, else to the next arm.
    private void Pass(in MirOperand value, long expected, int next, Koto source)
    {
        var pass = this.body.AddBlock();
        this.body.Terminate(this.current, MirTerminator.SwitchInt(value, this.body.AddCase(expected, pass), 1, next, this.Info(source)));
        this.current = pass;
    }

    // SPEC 15.1.6: the selected arm's body locals, left to right, from the Places its binding positions selected, each as its Adjust
    // acquires it: Copied, moved, by case, or borrowed in the mode its path grants; a for loop's are its iteration bindings (SPEC 14.6.1).
    private void Bind(BoundMatch plan, int pattern, int start, ForKoto? loop = null)
    {
        for (var i = pattern; i < plan.Positions[pattern].End; i++)
        {
            if (plan.Positions[i] is { BodySymbol: { } symbol, Source: var binding })
            {
                var (type, adjust, place) = (symbol.Type ?? BoundType.Unit, HirTables.AdjustOf(binding), this.selected[start + i]);
                var mutable = binding is SyntaxFormKoto { IsMutablePattern: true } || (binding is IdentifierNameKoto name && loop?.IsMutableSlot(name) == true);
                var local = this.Live(this.body.AddLocal(new(type, MirLocalKind.Var, this.Info(binding, true), mutable, symbol.Name)), this.lexical);
                var value = adjust.Adaptation == HirAdaptation.None ? MirRvalue.Use(this.body.AddOperand(this.Acquire(adjust, place, null, false, binding)))
                    : MirRvalue.Ref(adjust.Adaptation == HirAdaptation.ExclusiveBorrow ? MirRefKind.Uniq : MirRefKind.Shared, place, type);
                this.Set(new(local), value, binding);
                this.locals[symbol] = local;
            }
        }
    }
}
