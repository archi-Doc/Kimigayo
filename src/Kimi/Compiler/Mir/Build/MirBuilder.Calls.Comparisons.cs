// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// A comparison operator's call (SPEC 13.4, 13.4.1; DECISIONS R5-M75's operands, R5-M166's one call of a tuple operator).
internal sealed partial class MirBuilder
{
    // SPEC 13.4.1: the call of a comparison operator, a tuple operator's Composite call, any other the Contract call Binding selected; ==
    // gives equals' bool, != its negation and another compare's i32 compared with zero by the relation. A tuple operator whose Self has no
    // comparison (TyCtxt.Comparison's Limit, null) is a located Unsupported (PD30), its operands not built.
    private void Compare(MirPlace destination, CallPlan call, Koto node, MirBinaryOp relation)
    {
        var equality = relation is MirBinaryOp.Eq or MirBinaryOp.Ne;
        var contract = compilation.Library.GetSymbol(equality ? KimiDeclarationId.Equatable : KimiDeclarationId.Comparable)!;
        if (call.TupleOperator && compilation.Types.Comparison(contract, call.ConformingType!, operators: true) is null)
        {
            this.Unsupported(node, destination);
            return;
        }

        var result = relation == MirBinaryOp.Eq ? destination : this.Hold(call.ReturnType, node);
        if (call.TupleOperator)
        {
            this.Composite(result, call, contract, node, relation);
        }
        else
        {
            this.Call(result, call, node, first: null);
        }

        if (relation == MirBinaryOp.Eq)
        {
            return;
        }

        var start = this.body.AddOperand(MirOperand.Move(this.Made(result)));
        if (relation != MirBinaryOp.Ne)
        {
            this.body.AddOperand(MirOperand.Const(this.body.Const(new(MirConstKind.Integer, call.ReturnType))));
        }

        this.Set(
            destination,
            relation == MirBinaryOp.Ne ? MirRvalue.Unary(MirUnaryOp.Not, start, proven: true) : MirRvalue.Binary(relation, start, proven: true),
            node);
    }

    // SPEC 13.4: a tuple operator is one Requirement call of its Contract's own requirement on its Self (TyCtxt.Synthesized) under the
    // operator's ComparisonMode in Arg (Reversed for > and >=), whose instance runs the comparison of each distinct part once (the
    // collector's comparison items); its operands are the call's inputs as Binding adapted them, the left pending and secured as a built-in
    // operator's, then shared borrows of their terminal Places (SPEC 3.4.3), the Never value as it is (it fits any operand, SPEC 3.8).
    private void Composite(MirPlace result, CallPlan call, BindingSymbol contract, Koto node, MirBinaryOp relation)
    {
        var (left, right, self) = (call.ArgumentOperations[0], call.ArgumentOperations[1], call.ConformingType!);
        var start = this.arguments.Count;
        var view = this.Operand(left.Source!, left.AdaptedType ?? left.ParameterType);
        this.arguments.Add(Builds(right.Source!) ? this.Secure(view, left.Source!) : view);
        view = this.Operand(right.Source!, right.AdaptedType ?? right.ParameterType);
        this.arguments[start] = this.Inspected(this.Follow(this.arguments[start].Place, 16, self), node);
        this.arguments.Add(this.Inspected(this.Follow(view.Place, 16, self), node));
        var requirement = contract.Contract!.OwnRequirement()!; // Equatable's equals, Comparable's compare.
        var mode = relation is MirBinaryOp.Gt or MirBinaryOp.Ge ? ComparisonMode.Reversed : ComparisonMode.Operator;
        var subst = compilation.Types.Synthesized(requirement, self, contract.Type!);
        this.Invoke(result, new(MirCalleeKind.Requirement, requirement, subst, (int)mode), start, node);
    }

    private MirOperand Inspected(MirPlace place, Koto node)
        => this.IsNeverValue(place) ? MirOperand.Copy(place) : this.Borrow(SemanticsKind.Ref, place, node);
}
