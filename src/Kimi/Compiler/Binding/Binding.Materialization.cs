// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Materialization (SPEC 3.6, 10.2 row 2, 12.3.1, 14.9.2 step 3; PLAN G585, G924; the owner's O-201 (1) (a), (2) (a)): the owner temporary
// that a value at a `ref/U` position is, materialized once and shared-borrowed.
// - Responsibility: the one rule of every position (an argument's fit, BindAndAdaptNode at any expected Type, the evidence walk, a value
//   call's solve) deciding whether a part at `ref/U` is such a temporary, and of which Type: U, the part's expected Type there
//   (ExpectedTypeOf).
// - Inputs: the part, the position's Type and the part's scope (a join's survey).
// - Outputs: Materialization, U, or null where the part is no such temporary.
// - Invariants: the forms are a Tuple or array literal (a fill is none, 4.3.3), an untyped literal or a literal-only expression (12.3; a
//   mixed range is none, 4.6.2), or an unbound selection, `do` or `loop` with such a source, directly or nested (its survey), whose
//   value is the one temporary at its position. The part is fitted to U as at an expected U, so a misfit is its own record at U; a
//   call's fit tests it (FitLiteral). A form that delivers its operand (a label, an expression body) passes the position to it.
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

        return core is TupleLiteralKoto { Elements.Count: > 0 } or ArrayLiteralKoto { FillLength: null } || IsUnfittedLiteral(core) ||
            (core.BoundType is null && ControlFlowExpression(core) is { } join && this.SurveyResult(join, scope).Literal) ? ExpectedTypeOf(type) : null;
    }
}
