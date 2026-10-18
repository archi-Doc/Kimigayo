// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The edge rows (K package 4.5) of a site of an item's body and the targets of a type-driven item.
internal sealed partial class InstanceCollector
{
    // What a Value callee calls: its operand's Type below a reference, a Closure being called through a borrow in its call's mode (SPEC
    // 7.7.3).
    private static BoundType? Callable(MirBody body, in MirOperand operand)
    {
        _ = TyCtxt.Layer(OperandType(body, operand), out var callable, out _);
        return callable;
    }

    private static BoundType OperandType(MirBody body, in MirOperand operand)
        => operand.Kind == MirOperandKind.Const ? body.Consts[operand.Arg].Type : body.TypeOf(operand.Place)!;

    // K 4.5 rows 1, 3, 5, 7, 9 and 10: a Fn callee's body under the site's substitution composed with the item's representative
    // (TyCtxt.Compose, whose declaring Type is the site's own, so a nested function's call carries its enclosing instance only as Admit
    // checks; a default template's, the original's, SPEC 8.13.2; else the implementation the composed key selects, SPEC 8.13.3), a
    // Requirement callee's witness under the composed substitution (InstanceCollector.Witnesses.cs), the body a Value callee's Closure or
    // function item runs, an intrinsic's target where its row names one (a staticEnsure's StaticInit, a DestroyValues' glue) and a dropped
    // value's glue; an intrinsic's Type argument a by-value use (K 4.8) where its row says so. The admitted item, or -1 for a callee
    // without a body, a refused edge or a row not collected yet (row 2's shims and row 6's writer witness, K6b; the descriptors, K8).
    private int Edge(MirProgram program, int item, MirBody body, in MirTerminator call)
    {
        var callee = call.Callee;
        var subject = this.Subject(item, body, call.Source);
        if (call.Kind == MirTerminatorKind.Drop)
        {
            return this.Glue(item, subject, body.TypeOf(call.Place)!);
        }

        if (callee.Kind == MirCalleeKind.Value)
        {
            return Callable(body, call.Operand) is { } callable && this.In(item, subject, callable) is { } closed
                ? this.Callee(program, item, subject, closed) : -1;
        }

        if (callee.Kind == MirCalleeKind.Intrinsic)
        {
            var row = IntrinsicRows.Of((IntrinsicId)callee.Arg);
            if (row.LaysOutType && this.In(item, subject, callee.Subst!.Types[0]!) is { } argument)
            {
                _ = this.LaysOut(subject, argument);
            }

            return row.Target switch
            {
                IntrinsicTarget.StaticInit => this.Static(program, item, body, subject, call),
                IntrinsicTarget.Glue => this.Glue(item, subject, callee.Subst!.Types[0]!),
                IntrinsicTarget.None or IntrinsicTarget.Witness or IntrinsicTarget.Descriptor => -1, // The writer's witness: K6b; the descriptors: K8.
                _ => throw new InvalidOperationException($"The intrinsic target {row.Target} has no edge row."),
            };
        }

        var representative = this.Set.Items[item].Representative;
        var subst = callee.Subst is { } inner && representative is not null ? this.types.Compose(inner, representative) : callee.Subst;
        if (subst is null && callee.Subst is not null)
        {
            this.Set.Report(Capability.Code(CapabilityGap.StorageNesting), subject);
            return -1;
        }

        if (callee.Kind == MirCalleeKind.Requirement)
        {
            return this.Requirement(program, item, subject, callee, subst!);
        }

        var function = callee.Arg == 0 ? this.types.Implementation(callee.Symbol!, subst, 0) : callee.Symbol;
        var found = ReferenceEquals(function, callee.Symbol) ? program.Callee(callee) : program.Callee(new(MirCalleeKind.Fn, function));
        return found is null ? -1 : this.Admit(item, subject, found, subst);
    }

    // K 4.5 rows 11-13, K 4.7 and K 4.8, in the item's instance: an aggregate's Type a by-value use, a Closure environment's also its body;
    // a function item constant's body (as row 5); an erasure's ClosureErasure or ItemAdapter item, keyed by the erased Closure or Item Type;
    // an operation whose Types depend on slots checked against the capability table (no target).
    private int Edge(MirProgram program, int item, MirBody body, in MirStatement statement)
    {
        var rvalue = statement.Rvalue;
        var subject = this.Subject(item, body, statement.Source);
        if (rvalue.Kind == MirRvalueKind.BinaryOp || rvalue is { Kind: MirRvalueKind.Cast, Op: not (byte)MirCastKind.Erasure })
        {
            this.CheckOperation(item, subject, body, rvalue);
            return -1;
        }

        var aggregate = rvalue.Kind == MirRvalueKind.Aggregate;
        if (this.In(item, subject, aggregate ? rvalue.Type! : OperandType(body, body.Operands[rvalue.Start])) is not { } closed)
        {
            return -1;
        }

        if (aggregate)
        {
            _ = this.LaysOut(subject, closed);
            return (MirAggregateKind)rvalue.Op == MirAggregateKind.Closure ? this.Callee(program, item, subject, closed) : -1;
        }

        return rvalue.Kind != MirRvalueKind.Cast ? this.Callee(program, item, subject, closed)
            : this.AdmitType(item, subject, new(closed.Kind == BoundTypeKind.Closure ? MonoKind.ClosureErasure : MonoKind.ItemAdapter, default), closed);
    }

    // The body a Closure or function item Type of an item's instance runs: a Closure's under its context, the substitution of the instance
    // that closed it (TyCtxt.SubstOfClosure, R5-M36); an ordinary item's function under the item's substitution (TyCtxt.SubstOfItem), as the
    // implementation that selects (SPEC 8.13.3). -1 for the Item of a function whose calls dispatch (a requirement, K6b; a virtual slot, K8),
    // a Function Type value, whose erasure made its adapter (row 13), or a refused edge.
    private int Callee(MirProgram program, int item, SourceRef subject, BoundType callable)
    {
        if (callable is { Kind: BoundTypeKind.Closure, Symbol: { } closure })
        {
            return program[new(closure, MirBodyKind.Closure)] is { } body ? this.Admit(item, subject, body, this.types.SubstOfClosure(callable)) : -1;
        }

        if (callable is not { Kind: BoundTypeKind.FunctionItem, Symbol: { } symbol } || this.types.Dispatches(symbol))
        {
            return -1;
        }

        var subst = this.types.SubstOfItem(callable);
        return program.Callee(new(MirCalleeKind.Fn, this.types.Implementation(symbol, subst, 0))) is { } target ? this.Admit(item, subject, target, subst) : -1;
    }

    // A Type of an item's body in the item's instance; null, with the storage-nesting gap at the site, when TyCtxt cannot make it.
    private BoundType? In(int item, SourceRef subject, BoundType type)
    {
        if (this.Set.Items[item].Representative is not { } representative)
        {
            return type;
        }

        var closed = this.types.Apply(type, representative);
        if (closed is null)
        {
            this.Set.Report(Capability.Code(CapabilityGap.StorageNesting), subject);
        }

        return closed;
    }
}
