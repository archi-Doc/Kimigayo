// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// A comparison operator's call (SPEC 13.4.1), and a tuple operator's expansion over the leaves of its Self (SPEC 13.4; DECISIONS R5-M75).
internal sealed partial class MirBuilder
{
    private readonly List<(ComparisonNode Node, MirPlace Left, MirPlace Right)> leaves = []; // The leaves of the expansions being built.

    // SPEC 13.4.1: a tuple operator's expansion (Expand); else the Contract call of a comparison operator, == giving equals' bool, != its
    // negation and another compare's i32 compared with zero by the relation.
    private void Compare(MirPlace destination, CallPlan call, Koto node, MirBinaryOp relation)
    {
        if (call.TupleOperator)
        {
            this.Expand(destination, call, node, relation);
            return;
        }

        if (relation == MirBinaryOp.Eq)
        {
            this.Call(destination, call, node, first: null);
            return;
        }

        var result = this.Hold(call.ReturnType, node);
        this.Call(result, call, node, first: null);
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

    // SPEC 13.4: a tuple operator compares the leaves of its Self that TyCtxt.Comparison composes under source operators (floats by IEEE;
    // Leaves), left to right, in shared views of its operands, the call's inputs as Binding adapted them, the left pending and secured as a built-in
    // operator's (Operands). A leaf but the last decides the result where its pair is unequal (an unordered pair is), by the operator on
    // the pair (for == and !=, the test's bool or its negation), else the next leaf does; the last leaf decides by the operator on its
    // pair, so each path writes the destination once. The Contract is SPEC 13.4.1's for the operator, from the declaration table. A Limit
    // (null) is a located Unsupported (PD30).
    private void Expand(MirPlace destination, CallPlan call, Koto node, MirBinaryOp relation)
    {
        var equality = relation is MirBinaryOp.Eq or MirBinaryOp.Ne;
        var contract = compilation.Library.GetSymbol(equality ? KimiDeclarationId.Equatable : KimiDeclarationId.Comparable)!;
        if (compilation.Types.Comparison(contract, call.ConformingType!, operators: true) is not { } self)
        {
            this.Unsupported(node, destination);
            return;
        }

        var (left, right) = (call.ArgumentOperations[0], call.ArgumentOperations[1]);
        var start = this.arguments.Count;
        var view = this.Operand(left.Source!, left.AdaptedType ?? left.ParameterType);
        this.arguments.Add(Builds(right.Source!) ? this.Secure(view, left.Source!) : view);
        view = this.Operand(right.Source!, right.AdaptedType ?? right.ParameterType);
        var first = this.leaves.Count;
        this.Leaves(self, this.Follow(this.arguments[start].Place, 16, self.Type), this.Follow(view.Place, 16, self.Type));
        this.arguments.RemoveAt(start);
        var join = this.body.AddBlock();
        for (var i = first; i < this.leaves.Count; i++)
        {
            var (x, y, test) = this.Pair(this.leaves[i], contract, call.ReturnType, node, equality);
            var next = -1;
            if (i < this.leaves.Count - 1)
            {
                if (test is not { } tested)
                {
                    tested = this.Temp(BoundType.Boolean, node);
                    this.Set(tested, MirRvalue.Binary(MirBinaryOp.Eq, this.Push(x, y), proven: true), node);
                }

                (next, test) = (this.body.AddBlock(), tested);
                var decided = this.body.AddBlock();
                this.Branch(tested, node, decided, next);
                this.current = decided;
            }

            this.Set(destination, this.Decided(relation, x, y, equality ? test : null), node);
            this.body.Terminate(this.current, MirTerminator.Goto(join, this.Info(node)));
            this.current = next;
        }

        this.leaves.RemoveRange(first, this.leaves.Count - first);
        this.current = join;
    }

    // The leaves of a composite node in order, each with its Places in the two operands: a Tuple's part i is its Field i, a reference
    // layer's its referent, and the Never value its own part. A Tuple part that compares as its Contract's mapping (Mapped) is a leaf,
    // compared whole by that requirement on its Type, so the expansion is linear in the distinct Types (V16); a reference layer and a part
    // with a leaf only source operators compare (a float by IEEE) are expanded.
    private void Leaves(ComparisonNode node, MirPlace left, MirPlace right)
    {
        for (var i = 0; i < node.Parts.Length; i++)
        {
            var (part, x, y) = (node.Parts[i], this.Part(left, node.Type, i), this.Part(right, node.Type, i));
            if (part.Kind == ComparisonKind.Composite && (!part.Mapped || part.Type.Kind != BoundTypeKind.Tuple))
            {
                this.Leaves(part, x, y);
            }
            else
            {
                this.leaves.Add((part, x, y));
            }
        }
    }

    private MirPlace Part(MirPlace place, BoundType composite, int i) => composite.Kind != BoundTypeKind.Tuple ? this.Follow(place, 1)
        : this.IsNeverValue(place) ? place : this.body.Project(place, MirProjectionKind.Field, i);

    // A leaf's pair, the operands its built-in comparison takes: a built-in leaf's Places Copied, a string's (the one built-in leaf Type that
    // is not Copy) through shared borrows as a direct string comparison inspects them (SPEC 13.4); a Contract leaf's call of its witness (a
    // Fn callee) or of its requirement on its Self (a Requirement callee: a Requirement leaf or a Tuple one, compared whole as its
    // mapping, which runs its parts' witnesses in order, SPEC 13.4.1) on shared borrows of its Places, an Equatable leaf's bool its
    // equality (test), a Comparable leaf's i32 compared with zero. A None leaf, a broken contract (DECISIONS R5-M68), is InternalInvariant_Kd
    // with a derived Opaque result.
    private (MirOperand X, MirOperand Y, MirPlace? Test) Pair(
        (ComparisonNode Node, MirPlace Left, MirPlace Right) leaf,
        BindingSymbol contract,
        BoundType result,
        Koto node,
        bool equality)
    {
        if (leaf.Node.Kind == ComparisonKind.Builtin)
        {
            var shared = ReferenceEquals(leaf.Node.Type, BoundType.String);
            return (MirOperand.Copy(this.Inspected(leaf.Left, shared, node).Place), MirOperand.Copy(this.Inspected(leaf.Right, shared, node).Place), null);
        }

        var value = this.Hold(result, node);
        if (leaf.Node.Kind == ComparisonKind.None)
        {
            this.program.Report(SourceRef.Of(node), DiagnosticCode.InternalInvariant_Kd);
            this.Opaque(node, value);
        }
        else
        {
            var start = this.arguments.Count;
            this.arguments.Add(this.Inspected(leaf.Left, shared: true, node));
            this.arguments.Add(this.Inspected(leaf.Right, shared: true, node));
            var (target, subst) = (leaf.Node.Target, leaf.Node.Subst);
            var callee = leaf.Node.Kind == ComparisonKind.Witness ? new MirCallee(MirCalleeKind.Fn, target, subst)
                : new(MirCalleeKind.Requirement, target, subst, Contract: contract);
            this.Invoke(value, callee, start, node);
        }

        this.Made(value);
        return equality ? (default, default, value)
            : (MirOperand.Copy(value), MirOperand.Const(this.body.Const(new(MirConstKind.Integer, result))), null);
    }

    // A leaf's Place as its comparison inspects it: Copied, or through a shared borrow; the Never value as it is (it fits any operand
    // position, SPEC 3.8).
    private MirOperand Inspected(MirPlace place, bool shared, Koto node)
        => shared && !this.IsNeverValue(place) ? this.Borrow(SemanticsKind.Ref, place, node) : MirOperand.Copy(place);

    // The result a leaf decides: the operator on its pair, or for == and != its equality's bool (equality) or that negated.
    private MirRvalue Decided(MirBinaryOp relation, in MirOperand x, in MirOperand y, MirPlace? equality) => equality is not { } bit
        ? MirRvalue.Binary(relation, this.Push(x, y), proven: true) : relation == MirBinaryOp.Eq ? MirRvalue.Use(this.body.AddOperand(MirOperand.Copy(bit)))
        : MirRvalue.Unary(MirUnaryOp.Not, this.body.AddOperand(MirOperand.Copy(bit)), proven: true);

    // Two operands pushed for an rvalue; where the first is.
    private int Push(in MirOperand x, in MirOperand y)
    {
        var start = this.body.AddOperand(x);
        this.body.AddOperand(y);
        return start;
    }
}
