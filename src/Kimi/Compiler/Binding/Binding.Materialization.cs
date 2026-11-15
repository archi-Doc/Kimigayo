// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Materialization (SPEC 3.6, 10.2 row 2, 12.3.1, 14.9.2; PLAN G585, G924 rule B; the owner's O-201 (1) (a), (2) (a), O-333 R2 (a) and
// R6 (b)): the owner temporary that a value at a `ref/U` position is, materialized once and shared-borrowed.
// - Responsibility: the one rule of every position (an argument's fit, BindAndAdaptNode at any expected Type, the evidence walk, a value
//   call's solve) deciding whether a part at `ref/U` is such a temporary, and of which Type: U, the part's expected Type there
//   (ExpectedTypeOf).
// - Inputs: the part, the position's Type and the part's scope (a join's survey).
// - Outputs: Materialization, U, or null where the part is no such temporary.
// - Invariants: a value is an owner value of a literal form: a Tuple literal with elements, an array literal or fill, a mixed range or
//   a Dictionary literal (IsAggregateArgument), an untyped literal or a literal-only expression (12.3); or a value join, an unbound
//   selection, `do` or `loop` with such a source at any depth (its survey's Literal or Value) or one its call classifies by its
//   aggregate or waiting results (PartKind.Join), whose value is its selected source's, every source fitted to U (one temporary at the
//   join's position; a temporary inside a source stays the source's, 14.2.1). A join of Places and references only is no value. The
//   part is fitted to U as at an expected U, so a misfit is its own record at U; a call's fit tests it (FitLiteral, FitComposite). A
//   form that delivers its operand (a label, an expression body) passes the position to it.
// - Prohibitions: no fit, binding or record here; no second copy of the form list.
public sealed partial class Binding
{
    // The Type U of the owner temporary that `part` at `type` is (the contract above); null at any other Type or part.
    private BoundType? Materialization(Koto part, BoundType? type, BindingScope scope)
    {
        var core = KotoHelper.UnwrapParentheses(part);
        if (type is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref } || DeliveredOperand(core) is not null)
        {
            return null;
        }

        return IsAggregateArgument(core) || core is DictionaryLiteralKoto || IsUnfittedLiteral(core) || (ControlFlowExpression(core) is { } join &&
            (this.KindOf(core) == PartKind.Join || (core.BoundType is null && this.SurveyResult(join, scope) is { Literal: true } or { Value: true })))
            ? ExpectedTypeOf(type) : null;
    }
}
