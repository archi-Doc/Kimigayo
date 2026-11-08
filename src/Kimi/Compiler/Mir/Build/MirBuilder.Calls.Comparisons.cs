// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// A comparison operator's call (SPEC 13.4, 13.4.1; DECISIONS R5-M75's operands, R5-M166's one call of a tuple operator), and a library
// witness's call for a built-in comparison, an operator's or a Literal Pattern's (Witnessed, Compared), which TyCtxt.Comparison publishes.
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

        if (relation != MirBinaryOp.Eq)
        {
            this.Related(destination, result, call.ReturnType, node, relation);
        }
    }

    // SPEC 13.4.1: != is equals' bool negated, an ordering compare's i32 compared with zero by the relation.
    private void Related(MirPlace destination, MirPlace result, BoundType returned, Koto node, MirBinaryOp relation)
    {
        var start = this.body.AddOperand(MirOperand.Move(this.Made(result)));
        if (relation != MirBinaryOp.Ne)
        {
            this.body.AddOperand(MirOperand.Const(this.body.Const(new(MirConstKind.Integer, returned))));
        }

        this.Set(
            destination,
            relation == MirBinaryOp.Ne ? MirRvalue.Unary(MirUnaryOp.Not, start, proven: true) : MirRvalue.Binary(relation, start, proven: true),
            node);
    }

    // SPEC 13.4, 13.5.5, 14.8.1, 8.2.3: the comparison TyCtxt.Comparison gives the Type the left operand compares (the right one's after a
    // Never left), Binding's one decision for that operand (TyCtxt.OperandTerminal: its Type as the identity premises make it, below its
    // ref, uniq and qualifying pair layers), where a library witness runs it (Binding.WitnessGroup, read first so that a scalar operator asks
    // no memo); null where the operator runs it.
    private ComparisonNode? Witnessed(MirBinaryOp relation, int start, Koto left, Koto right)
    {
        if (relation < MirBinaryOp.Eq)
        {
            return null;
        }

        var never = ReferenceEquals(this.TypeOf(this.arguments[start]), BoundType.Never);
        var terminal = compilation.Types.OperandTerminal(never ? right : left, this.TypeOf(this.arguments[never ? start + 1 : start])!);
        var contract = relation is MirBinaryOp.Eq or MirBinaryOp.Ne ? KimiDeclarationId.Equatable : KimiDeclarationId.Comparable;
        var row = Binding.WitnessGroup(terminal, contract) is null ? null
            : compilation.Types.Comparison(compilation.Library.GetSymbol(contract)!, terminal, operators: true);
        return row is { Kind: ComparisonKind.Witness } ? row : null;
    }

    // SPEC 13.4, 13.4.1, 14.8.1: a built-in comparison a library witness runs: equals' or compare's call on the operands pushed since start and
    // the relation of its result, the one statement of that call for an operator and a Literal Pattern. A comparison inspects: an operand is
    // the shared borrow an operator's is, and a literal, which no Place holds, the constant record of its text (MirConst.IsRecord), since the
    // call keeps nothing of it (MirValidator): no temporary, Loan or destruction, as the operator had none.
    private void Compared(MirPlace destination, ComparisonNode comparison, MirBinaryOp relation, int start, Koto node)
    {
        for (var i = start; i < this.arguments.Count; i++)
        {
            this.arguments[i] = this.Viewed(this.arguments[i], comparison.Type, node);
        }

        var returned = relation is MirBinaryOp.Eq or MirBinaryOp.Ne ? BoundType.Boolean : BoundType.I32;
        var result = relation == MirBinaryOp.Eq ? destination : this.Hold(returned, node);
        this.Invoke(result, new(MirCalleeKind.Fn, comparison.Target, comparison.Subst), start, node);
        if (relation != MirBinaryOp.Eq)
        {
            this.Related(destination, result, returned, node, relation);
        }
    }

    // An operand as a shared reference to the Type: as it is where it is one, the constant record of a constant, else a borrow of its
    // terminal Place, the Never value as it is (it fits any operand, SPEC 3.8).
    private MirOperand Viewed(in MirOperand operand, BoundType type, Koto node)
    {
        if (operand.Kind == MirOperandKind.Const)
        {
            return MirOperand.Const(this.body.Const(this.body.Consts[operand.Arg] with { Type = compilation.Types.Reference(SemanticsKind.Ref, type) }));
        }

        if (this.TypeOf(operand) is { Semantics: SemanticsKind.Ref, Components: [var referent] } && ReferenceEquals(referent, type))
        {
            return operand;
        }

        // SPEC 8.2.3, 13.4: a Place whose Type the body's premises make another (T is uniq/string) is walked as that Type, every layer of
        // it: a shared borrow of it, retyped by a copy into a reference to the premised Type (MirTypes.Agrees admits it), whose referent the
        // walk then follows, since MIR Types keep the parameter (DECISIONS BU-72).
        var place = operand.Place;
        if (this.body.TypeOf(place) is { } actual && MirTypes.Premised(this.body, actual) is var premised && !MirTypes.Agree(actual, premised))
        {
            var view = this.Temp(compilation.Types.Reference(SemanticsKind.Ref, premised), node);
            this.Set(view, MirRvalue.Use(this.body.AddOperand(this.Borrow(SemanticsKind.Ref, place, node))), node);
            place = this.body.Project(view, MirProjectionKind.Deref, 0);
        }

        return this.Inspected(this.Follow(place, 16, type), node);
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
