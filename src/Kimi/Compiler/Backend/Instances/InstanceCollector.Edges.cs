// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns an item's targets (R5 package K 4.3, K 4.5 rows 1-3 and 5-13, K 4.7, K 4.8; DECISIONS R5-M102, R5-M111): a body item's template
// replayed under its representative (Replay), each site's edge row in the item's instance: for a call, a Drop or a statement of the item's
// body, the row its form selects (a Fn callee's implementation or bodyless row, a Requirement callee's witness, a Value callee's body, an
// intrinsic's target column, a dropped value's glue; a case adaptation's creation, an aggregate's, a function item constant's or an
// erasure's) and its target under the item's representative; a Virtual call has none (X5: its receiver's VTable holds the entries); after
// an owner Value call's body, the operand's glue where its callee borrows its environment (Rest, SPEC 8.10); a type-driven item's targets
// from its representative Type by its kind's row (Expand).
// Input: an admitted item; a body item's template (InstanceCollector.Templates.cs); TyCtxt (Compose, Implementation, Witness,
// OwnRequirement); the intrinsic rows.
// Output: the item's targets in the checked set, each with its site and runtime bit: a site's admitted target item (none for a site
// without one or a refused edge) and an owner Value call's second target (Rest); a type-driven item's at the default site; on the way the
// site's by-value uses take their layout verdict (K 4.8) and an operation whose Types depend on slots its capability check (K 4.7).
// Invariants: one target per site, plus the operand's glue at a borrowing callee's owner Value call, after its body (DECISIONS R5-M102: a
// departure from package C 3.2 I8); a composed substitution's storage verdict is read at the site (Made); a kind without a row is a fault.
// Must not: resolve a callable value's body or a bodyless callee's row (InstanceCollector.Callees.cs) or a requirement's witness
// (InstanceCollector.Witnesses.cs) itself.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

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

    // An item's targets: a body item's template replayed under its representative, the target of each site that resolves and is admitted;
    // a type-driven item's from its representative Type.
    private void Replay(MirProgram program, int item)
    {
        var set = this.Set;
        if (set.Items[item].BodyOrdinal < 0)
        {
            this.Expand(program, item);
            return;
        }

        var body = program.Bodies[set.Items[item].BodyOrdinal];
        this.LayOutLocals(item, body);
        this.LayOutNeeds(item, body);
        for (var i = this.starts[body.Ordinal]; i < this.starts[body.Ordinal + 1]; i++)
        {
            this.Work++;
            var (at, runtime) = this.sites[i];
            var block = body.Blocks[at.Block];
            var target = at.Index == block.Count
                ? this.Edge(program, item, body, block.Terminator)
                : this.Edge(program, item, body, body.Statements[block.Start + at.Index]);
            if (target >= 0)
            {
                set.Target(item, new(target, at, runtime));
            }

            var rest = at.Index == block.Count ? this.Rest(program, item, body, block.Terminator, target) : -1;
            if (rest >= 0)
            {
                set.Target(item, new(rest, at, runtime));
            }
        }
    }

    // K 4.3: a type-driven item's targets, from its representative Type, each at the default site, by its kind's row: a ClosureErasure's
    // body and its environment's glue; an ItemAdapter's body; a shim's (InstanceCollector.Witnesses.cs); a DropGlue's direct parts and a
    // LegacyRow item's declared needs (InstanceCollector.Glue.cs); an object item's (InstanceCollector.Objects.cs). A kind without a row is a
    // fault.
    private void Expand(MirProgram program, int item)
    {
        var key = this.Set.Items[item].Key;
        var type = this.Set.Items[item].RepresentativeType!;
        var site = this.Set.Items[item].Site;
        switch (key.Kind)
        {
            case MonoKind.Builtin or MonoKind.PropertyWitness:
                this.Shim(program, item, site, key, type);
                return;
            case MonoKind.ClosureErasure or MonoKind.ItemAdapter:
                this.Target(item, this.Callee(program, item, site, type));
                this.Target(item, key.Kind == MonoKind.ClosureErasure ? this.Glue(item, site, type) : -1);
                return;
            case MonoKind.DropGlue:
                this.DestructionTargets(program, item, site, type);
                return;
            case MonoKind.LegacyRow:
                this.RowTargets(program, item, site, type);
                return;
            case MonoKind.ObjectDescriptor or MonoKind.VTable:
                this.Object(program, item, site, key.Kind, type);
                return;
            default:
                throw new InvalidOperationException($"The mono item kind {key.Kind} has no target row.");
        }
    }

    // A type-driven item's target at the default site, which runs whenever the item does.
    private void Target(int item, int target)
    {
        if (target >= 0)
        {
            this.Set.Target(item, new(target, default, Runtime: true));
        }
    }

    // K 4.5 rows 1-3, 5-10: a Fn callee's body under the site's substitution composed with the item's representative
    // (TyCtxt.Compose, whose declaring Type is the site's own, so a nested function's call carries its enclosing instance only as Admit
    // checks; a default template's, the original's, SPEC 8.13.2; else the implementation the composed key selects, SPEC 8.13.3), or for a
    // callee without a body the witness its published row runs; a Requirement callee's witness under the composed substitution
    // (InstanceCollector.Witnesses.cs), a tuple operator's its comparison item (Compared); the body a Value callee's Closure or function
    // item runs; an intrinsic's target where its row names one (an erased writer's reserve witness, a staticEnsure's StaticInit, a
    // creation's payload descriptor, a DestroyValues' glue) and a dropped value's glue; an intrinsic's Type arguments uses where its row's
    // need says so (K 4.8, IntrinsicRow.Need). The admitted item, or -1 for a callee without a body or row or a refused edge.
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
            if (row.Need == LayoutNeed.TypeArguments)
            {
                this.LaysOutArguments(item, subject, callee.Subst!);
            }

            return row.Target == IntrinsicTarget.StaticInit
                ? this.Static(program, item, body, subject, call)
                : this.Intrinsic(program, item, subject, row, this.In(item, subject, callee.Subst!.Types[0]!));
        }

        var representative = this.Set.Items[item].Representative;
        var (subst, storage, _) = callee.Subst is { } inner && representative is not null
            ? this.types.Compose(inner, representative) : (callee.Subst, Binding.StorageOutcome.Prepared, null);
        if (!this.Made(storage, subject))
        {
            return -1;
        }

        if (callee.Kind == MirCalleeKind.Requirement)
        {
            var target = this.types.Witness(callee.Symbol!, callee.Contract, subst!);
            return callee.Arg == 0 ? this.Called(program, item, subject, callee.Symbol!, target) : this.Compared(item, subject, callee, target);
        }

        if (callee.Arg > 0)
        {
            return program.Callee(callee) is { } template ? this.Admit(item, subject, template, subst) : -1;
        }

        return this.Implementation(program, item, subject, callee.Symbol!, subst);
    }

    // SPEC 8.10 (DECISIONS R5-M102): an owner Callable call, its operand by value, of a callee that borrows its environment (a Closure body
    // whose environment, its Arg local 1, is a reference in its instance, or a common Function value, whose entries borrow) destroys the
    // operand after the call: the operand Type's glue (GlueOf, when the Type may need a drop) is the call's next target, after its body.
    // -1 for any other site.
    private int Rest(MirProgram program, int item, MirBody body, in MirTerminator call, int target)
    {
        if (call is not { Kind: MirTerminatorKind.Call, Callee.Kind: MirCalleeKind.Value } ||
            this.TypeOf(item, OperandType(body, call.Operand)) is not { } operand || TyCtxt.Layer(operand, out _, out _) != HirPlaceKind.Value)
        {
            return -1;
        }

        var borrows = operand.Kind == BoundTypeKind.Function || (operand.Kind == BoundTypeKind.Closure && target >= 0 &&
            this.TypeOf(target, program.Bodies[this.Set.Items[target].BodyOrdinal].Locals[1].Type) is { } environment &&
            TyCtxt.Layer(environment, out _, out _) != HirPlaceKind.Value);
        return borrows ? this.GlueOf(item, this.Subject(item, body, call.Source), operand) : -1;
    }

    // K 4.5 rows 6, 8 and 9 (DECISIONS R5-M74): the target an intrinsic's row names, for its call or for an Item of its function, from the
    // substitution's Type argument in the item's instance (the one operand form the rows read; its layout need, IntrinsicRow.Need, is its
    // caller's, LaysOutArguments): an erased writer's reserve witness on its destination (SPEC 22.10.1), a creation's payload
    // descriptor (Descriptor), a DestroyValues' element glue. -1 for a row without a target or a Type not made, whose verdict the caller
    // read (In); a staticEnsure's initializer is its call's operand row (Static), so it never reaches here.
    private int Intrinsic(MirProgram program, int item, SourceRef subject, in IntrinsicRow row, BoundType? argument)
    {
        if (argument is null)
        {
            return -1;
        }

        return row.Target switch
        {
            IntrinsicTarget.None => -1,
            IntrinsicTarget.Witness => this.Runs(program, item, subject, this.types.OwnRequirement(KimiDeclarationId.BufferWriter)!, argument),
            IntrinsicTarget.Descriptor => this.Descriptor(item, subject, argument),
            IntrinsicTarget.Glue => this.GlueOf(item, subject, argument),
            _ => throw new InvalidOperationException($"The intrinsic target {row.Target} has no Type argument row."),
        };
    }

    // K 4.5 row 8 (DECISIONS R5-M74, R5-M111): a creation's target, its payload's object descriptor, admitted with the payload's layout
    // verdict (InstanceCollector.Objects.cs): a creation intrinsic's, an Item of its function's, a case adaptation's that creates.
    private int Descriptor(int item, SourceRef subject, BoundType payload)
        => this.AdmitType(item, subject, new(MonoKind.ObjectDescriptor, default, null, null, 0, null), payload, null);

    // SPEC 13.6.2, 8.14 (DECISIONS R5-M111): an Adapt whose operation in the item's instance (TyCtxt.Adaptation from its source token's
    // Type to its target, the rule MirCases.Adapt reads under the item's view) creates an object targets its payload's descriptor, as every
    // creation does; any other operation none.
    private int Adaptation(int item, SourceRef subject, MirBody body, in MirRvalue adapt)
    {
        var token = body.Operands[adapt.Start + 1];
        if (this.In(item, subject, body.Consts[token.Arg].Type) is not { } source || this.In(item, subject, adapt.Type!) is not { } target)
        {
            return -1;
        }

        var operation = TyCtxt.Adaptation(source, target, (adapt.Op & MirRvalue.AdaptShorthand) != 0);
        return operation == ConversionBinding.ObjectCreation ? this.Descriptor(item, subject, target.Components[0]) : -1;
    }

    // K 4.5 rows 8 and 11-13, K 4.7 and K 4.8, in the item's instance: a case adaptation that creates an object its payload's descriptor
    // (Adaptation; DECISIONS R5-M111); an aggregate's Type a by-value use, a Closure environment's also its body, a Dictionary from none's
    // the body of the DictionaryStorage function the Dictionary runtime declares for construction, initialize (Declared; DECISIONS R5-M124);
    // a function item constant's body (as row 5); an erasure's ClosureErasure or ItemAdapter item, keyed by the erased Closure or Item Type
    // and the erased target Function Type, the Cast's, whose inputs and result are by-value uses at the erasure (its entry takes and gives
    // them; a Never-result source fits Function Types of other results, SPEC 10.7), a target that does not lay out dropping the edge
    // (DECISIONS R5-M108); an operation whose Types depend on slots checked against the capability table (no target).
    private int Edge(MirProgram program, int item, MirBody body, in MirStatement statement)
    {
        var rvalue = statement.Rvalue;
        var subject = this.Subject(item, body, statement.Source);
        if (rvalue.Kind == MirRvalueKind.BinaryOp || rvalue is { Kind: MirRvalueKind.Cast, Op: not (byte)MirCastKind.Erasure })
        {
            this.CheckOperation(item, subject, body, rvalue);
            if (rvalue.Kind == MirRvalueKind.BinaryOp &&
                LayoutNeeds.Of((MirBinaryOp)rvalue.Op, OperandType(body, body.Operands[rvalue.Start])) != LayoutNeed.None &&
                this.In(item, subject, LayoutNeeds.TypeOf(body, rvalue)) is { } needed)
            {
                _ = this.LaysOut(subject, needed); // K 4.8 (DECISIONS R5-M149): a raw pointer's displacement reads its pointee's stride.
            }

            return -1;
        }

        if (rvalue.Kind == MirRvalueKind.Adapt)
        {
            return this.Adaptation(item, subject, body, rvalue);
        }

        var aggregate = rvalue.Kind == MirRvalueKind.Aggregate;
        if (this.In(item, subject, aggregate ? rvalue.Type! : OperandType(body, body.Operands[rvalue.Start])) is not { } closed)
        {
            return -1;
        }

        if (aggregate)
        {
            _ = this.LaysOut(subject, closed);
            return (MirAggregateKind)rvalue.Op switch
            {
                MirAggregateKind.Closure => this.Callee(program, item, subject, closed),
                _ when closed.Kind == BoundTypeKind.Dictionary => this.Declared(program, item, subject, LegacyRuntimeHelpers.Construction, null),
                _ => -1,
            };
        }

        if (rvalue.Kind != MirRvalueKind.Cast)
        {
            return this.Callee(program, item, subject, closed);
        }

        var adapter = closed.Kind == BoundTypeKind.Closure ? MonoKind.ClosureErasure : MonoKind.ItemAdapter;
        return this.In(item, subject, rvalue.Type!) is { } function && this.LaysOutSignature(subject, function)
            ? this.AdmitType(item, subject, new(adapter, default, null, null, 0, this.types.Erase(function)), closed, null) : -1;
    }
}
