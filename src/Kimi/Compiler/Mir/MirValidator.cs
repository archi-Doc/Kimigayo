// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

#pragma warning disable SA1402 // The validator's rules and findings belong to it.

namespace Kimi.Compiler.Mir;

/// <summary>The rule a validator finding breaks.</summary>
internal enum MirViolation : byte
{
    UnknownKind, // A statement, rvalue, terminator, operand, projection, callee, intrinsic, local or borrow kind without a row.
    NoBlocks,
    Unterminated,
    StatementRange,
    TargetRange, // A successor, case range or check target out of range.
    CheckTarget, // A check target on a terminator other than Goto, a Call without a target and Unreachable.
    Unreachable, // A block that neither runtime nor check edges reach from the entry.
    Local, // Local 0 is not the Return local, an Arg local does not directly follow it, or a local has no Type.
    Scope, // A scope that its outer scope does not precede, or a source outside the scopes.
    StorageLocal, // StorageLive or StorageDead of a Place other than a Var or Temp local.
    EndBorrowLocal, // EndBorrow of a Place other than a reference local.
    Place, // A root or a projection chain out of range.
    Projection, // A projection its base Type does not admit, or a chain out of order.
    Const, // A constant id out of range.
    Binder, // A PairAcquire of a binder out of range or other than its Place's pair layer.
    OperandRange,
    Arity, // An rvalue's or a Call's operands disagree with its kind, its Type or its callee.
    Callee, // A Fn, Requirement or Virtual callee without its function, or a diverging intrinsic with a target.
    Discriminant, // A Discriminant of a Place that is not an enum.
    Len, // A Len of a Place that is not a sequence.
    Opaque, // An Opaque site or root out of range, or Opaque sites without an Opaque value.
}

/// <summary>A validator finding: its rule and the syntax of its statement, terminator or local, else the body's. The build reports it
/// as <c>InternalInvariant_Kd</c>.</summary>
/// <param name="Rule">The rule.</param>
/// <param name="Subject">The syntax.</param>
internal readonly record struct MirFinding(MirViolation Rule, SourceRef Subject);

/// <summary>The one structural verifier of MIR (plan 14-5): a body's structure, locals, projections against their base Types, operands,
/// rvalues and arities, before any analysis reads it. It keeps its scratch, so a warm run allocates nothing.</summary>
/// <remarks>Deferred: the Type agreement of an Assign, an operand or an argument with its destination (the projections' Types are checked),
/// and the arity of a Fn, Requirement or Virtual callee, which needs a published declaration view.</remarks>
internal sealed class MirValidator
{
    private bool[] reached = [];
    private int[] pending = [];

    /// <summary>Validates a body.</summary>
    /// <param name="body">The body.</param>
    /// <param name="findings">The caller's list the findings are added to.</param>
    internal void Validate(MirBody body, List<MirFinding> findings)
    {
        var checker = new Checker(body, findings);
        if (!checker.Tables())
        {
            return; // A chain or site out of order or range cannot be walked.
        }

        var blocks = body.Blocks;
        var targets = blocks.Length > 0;
        if (!targets)
        {
            checker.Report(MirViolation.NoBlocks);
        }

        for (var block = 0; block < blocks.Length; block++)
        {
            targets &= checker.Block(block);
        }

        checker.Finish();
        if (targets)
        {
            this.Reach(body, findings);
        }
    }

    private static SourceRef Subject(MirBody body, in SourceInfo source) => source.Syntax.Id != 0 ? source.Syntax : body.Source;

    // Every block is reached from the entry through runtime or check edges.
    private void Reach(MirBody body, List<MirFinding> findings)
    {
        var blocks = body.Blocks;
        if (this.reached.Length < blocks.Length)
        {
            this.reached = new bool[Math.Max(blocks.Length, this.reached.Length * 2)];
            this.pending = new int[this.reached.Length];
        }

        Array.Clear(this.reached, 0, blocks.Length);
        var count = 0;
        this.reached[0] = true;
        this.pending[count++] = 0;
        while (count > 0)
        {
            var terminator = blocks[this.pending[--count]].Terminator;
            for (var i = -1; i < MirUses.Successors(terminator); i++)
            {
                var next = i < 0 ? terminator.CheckTarget : MirUses.Successor(body, terminator, i);
                if (next >= 0 && !this.reached[next])
                {
                    this.reached[next] = true;
                    this.pending[count++] = next;
                }
            }
        }

        for (var block = 0; block < blocks.Length; block++)
        {
            if (!this.reached[block])
            {
                findings.Add(new(MirViolation.Unreachable, Subject(body, blocks[block].Terminator.Source)));
            }
        }
    }

    // The checks of one body: tables first, then each location before MirUses walks it, and the visitor's checks of what it reports.
    private struct Checker(MirBody body, List<MirFinding> findings) : IMirUses
    {
        private SourceInfo at;
        private int opaqueValues;

        public void Operand(in MirOperand operand)
        {
            if (!MirDescriptors.Of(operand.Kind).Defined)
            {
                this.Report(MirViolation.UnknownKind);
            }
            else if (operand.Kind == MirOperandKind.Const && (uint)operand.Arg >= (uint)body.Consts.Length)
            {
                this.Report(MirViolation.Const);
            }
            else if (operand.Kind == MirOperandKind.PairAcquire && this.IsValid(operand.Place) && !body.Binds(operand.Arg, PairBinderOf(body.TypeOf(operand.Place))))
            {
                this.Report(MirViolation.Binder);
            }
        }

        // The table checked each step against the step it extends; the first step is checked against the root's Type.
        public void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            if (!this.IsValid(place))
            {
                this.Report(MirViolation.Place);
                return;
            }

            var projections = body.Projections;
            var first = place.Projection;
            while (first != 0 && projections[first].Prefix != 0)
            {
                first = projections[first].Prefix;
            }

            if (first != 0 && !this.Admits(place.Root >= 0 ? body.Locals[place.Root].Type : body.Statics[~place.Root].Type, projections[first]))
            {
                this.Report(MirViolation.Projection);
            }
        }

        public readonly void Effect(MirEffect effects, int site)
        {
        }

        // Locals, scopes, Opaque sites and the projection table; false when a chain or site cannot be walked.
        internal bool Tables()
        {
            var locals = body.Locals;
            var arguments = true;
            for (var i = 0; i < locals.Length; i++)
            {
                ref readonly var local = ref locals[i];
                this.At(local.Source);
                arguments &= i == 0 || local.Kind == MirLocalKind.Arg;
                if (local.Type is null || local.Kind > MirLocalKind.DropFlag || (local.Kind == MirLocalKind.Return) != (i == 0) || (local.Kind == MirLocalKind.Arg && !arguments))
                {
                    this.Report(MirViolation.Local);
                }
            }

            this.at = default;
            if (locals.Length == 0)
            {
                this.Report(MirViolation.Local);
            }

            var scopes = body.Scopes;
            for (var i = 0; i < scopes.Length; i++)
            {
                if (i == 0 ? scopes[i].Outer != -1 : (uint)scopes[i].Outer >= (uint)i)
                {
                    this.Report(MirViolation.Scope);
                }
            }

            var walkable = true;
            foreach (var site in body.OpaqueSites)
            {
                var valid = InRange(site.Start, site.Count, body.OpaqueRoots.Length);
                foreach (var root in valid ? body.OpaqueRoots.Slice(site.Start, site.Count) : default)
                {
                    valid &= this.IsValid(new(root));
                }

                if (!valid)
                {
                    this.Report(MirViolation.Opaque);
                    walkable = false;
                }
            }

            var projections = body.Projections;
            for (var id = 1; id < projections.Length; id++)
            {
                ref readonly var row = ref projections[id];
                if (!MirDescriptors.Of(row.Kind).Defined || (uint)row.Prefix >= (uint)id)
                {
                    this.Report(MirDescriptors.Of(row.Kind).Defined ? MirViolation.Projection : MirViolation.UnknownKind);
                    walkable = false;
                }
                else if (row.Prefix != 0 && !this.Admits(projections[row.Prefix].Type, row))
                {
                    this.Report(MirViolation.Projection);
                }
            }

            return walkable;
        }

        // A block's statements and terminator; false when a successor is out of range.
        internal bool Block(int index)
        {
            ref readonly var block = ref body.Blocks[index];
            this.at = block.Terminator.Source;
            if (!InRange(block.Start, block.Count, body.Statements.Length))
            {
                this.Report(MirViolation.StatementRange);
            }
            else
            {
                for (var i = 0; i < block.Count; i++)
                {
                    this.Statement(new(index, i), body.Statements[block.Start + i]);
                }
            }

            return this.Terminator(new(index, block.Count), block.Terminator);
        }

        // An Opaque value exactly when the body has Opaque sites.
        internal void Finish()
        {
            this.at = default;
            if (body.HasOpaque != this.opaqueValues > 0)
            {
                this.Report(MirViolation.Opaque);
            }
        }

        internal readonly void Report(MirViolation rule) => findings.Add(new(rule, Subject(body, this.at)));

        private static BindingSymbol? PairBinderOf(BoundType? type)
        {
            _ = TyCtxt.Layer(type, out _, out var binder);
            return binder;
        }

        // Whether a range of a table lies within it.
        private static bool InRange(int start, int count, int length) => start >= 0 && count >= 0 && start <= length - count;

        private static bool IsSequence(BoundType type) => type.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary || ReferenceEquals(type, BoundType.String);

        // The operands an rvalue has: one or two, as many as its Type's elements, Fields (the base first) or Case payload, or captures; -1
        // when an aggregate's kind and Type disagree. A fixed array of a generic length is counted by its instance, and one built from no
        // operand stores no element (SPEC 4.3.4 noinit).
        private static int Arity(in MirRvalue rvalue)
        {
            var type = rvalue.Type;
            return rvalue.Kind switch
            {
                MirRvalueKind.Use or MirRvalueKind.UnaryOp or MirRvalueKind.Cast => 1,
                MirRvalueKind.BinaryOp => 2,
                MirRvalueKind.Aggregate => (MirAggregateKind)rvalue.Op switch
                {
                    MirAggregateKind.Tuple when type?.Kind == BoundTypeKind.Tuple => type.Components.Count,
                    MirAggregateKind.Closure when type?.Kind == BoundTypeKind.Closure => type.Components.Count,
                    MirAggregateKind.FixedArray when type?.Kind == BoundTypeKind.FixedArray => type.LengthExpression is null && type.Length >= 0 && rvalue.Count > 0 ? (int)type.Length : rvalue.Count,
                    MirAggregateKind.Adt when AdtDef.IsStruct(type) => AdtDef.Count(type!) + (AdtDef.Base(type!) is null ? 0 : 1),
                    MirAggregateKind.Adt when type is not null && AdtDef.CaseTypes(type) is { } cases => (uint)rvalue.Arg < (uint)cases.Length ? cases[rvalue.Arg].Components.Count : -1,
                    _ => -1,
                },
                _ => 0,
            };
        }

        private void At(in SourceInfo source)
        {
            this.at = source;
            if (source.Scope >= body.Scopes.Length)
            {
                this.Report(MirViolation.Scope);
            }
        }

        private readonly bool IsValid(MirPlace place)
            => (place.Root >= 0 ? place.Root < body.Locals.Length : ~place.Root < body.Statics.Length) && (uint)place.Projection < (uint)body.Projections.Length;

        private readonly bool Admits(BoundType? type, in MirProjection row) => row.Type is not null && ReferenceEquals(row.Type, body.ProjectedType(type, row.Kind, row.Arg));

        private readonly BoundType? TypeOf(in MirOperand operand)
            => operand.Kind == MirOperandKind.Const ? ((uint)operand.Arg < (uint)body.Consts.Length ? body.Consts[operand.Arg].Type : null) : this.IsValid(operand.Place) ? body.TypeOf(operand.Place) : null;

        private void Statement(MirLocation location, in MirStatement statement)
        {
            this.At(statement.Source);
            if (!MirDescriptors.Of(statement.Kind).Defined)
            {
                this.Report(MirViolation.UnknownKind);
                return;
            }

            var place = statement.Place;
            var local = place.Projection == 0 && (uint)place.Root < (uint)body.Locals.Length ? body.Locals[place.Root] : default;
            switch (statement.Kind)
            {
                case MirStatementKind.StorageLive or MirStatementKind.StorageDead when local.Type is null || local.Kind is not (MirLocalKind.Var or MirLocalKind.Temp):
                    this.Report(MirViolation.StorageLocal);
                    break;
                case MirStatementKind.EndBorrow when !MirTypes.IsReference(local.Type):
                    this.Report(MirViolation.EndBorrowLocal);
                    break;
                case MirStatementKind.Assign when !this.Rvalue(statement.Rvalue):
                    return;
            }

            MirUses.ForEach(body, location, ref this);
        }

        // False when the operands or the Opaque site are out of range, which MirUses then does not walk.
        private bool Rvalue(in MirRvalue rvalue)
        {
            if (!MirDescriptors.Of(rvalue.Kind).Defined || (rvalue.Kind == MirRvalueKind.Ref && rvalue.Op > (byte)MirRefKind.Raw))
            {
                this.Report(MirViolation.UnknownKind);
                return false;
            }

            if (!InRange(rvalue.Start, rvalue.Count, body.Operands.Length))
            {
                this.Report(MirViolation.OperandRange);
                return false;
            }

            if (rvalue.Kind == MirRvalueKind.Opaque && (uint)rvalue.Arg >= (uint)body.OpaqueSites.Length)
            {
                this.Report(MirViolation.Opaque);
                return false;
            }

            this.opaqueValues += rvalue.Kind == MirRvalueKind.Opaque ? 1 : 0;
            if (Arity(rvalue) != rvalue.Count)
            {
                this.Report(MirViolation.Arity);
            }

            var type = rvalue.Kind is MirRvalueKind.Discriminant or MirRvalueKind.Len && this.IsValid(rvalue.Place) ? body.TypeOf(rvalue.Place) : null;
            if (type is not null && (rvalue.Kind == MirRvalueKind.Discriminant ? !AdtDef.IsEnum(type) : !IsSequence(type)))
            {
                this.Report(rvalue.Kind == MirRvalueKind.Discriminant ? MirViolation.Discriminant : MirViolation.Len);
            }

            return true;
        }

        // False when a successor is out of range.
        private bool Terminator(MirLocation location, in MirTerminator terminator)
        {
            this.At(terminator.Source);
            if (!MirDescriptors.Of(terminator.Kind).Defined)
            {
                this.Report(MirViolation.UnknownKind);
                return false;
            }

            if (terminator.Kind == MirTerminatorKind.Goto && terminator.Target < 0)
            {
                this.Report(MirViolation.Unterminated);
                return false;
            }

            var blocks = (uint)body.Blocks.Length;
            var valid = (terminator.Kind != MirTerminatorKind.SwitchInt || InRange(terminator.Start, terminator.Count, body.Cases.Length)) &&
                (terminator.Kind != MirTerminatorKind.Call || terminator.Target >= -1) && (terminator.CheckTarget == -1 || (uint)terminator.CheckTarget < blocks);
            for (var i = 0; valid && i < MirUses.Successors(terminator); i++)
            {
                valid = (uint)MirUses.Successor(body, terminator, i) < blocks;
            }

            if (!valid)
            {
                this.Report(MirViolation.TargetRange);
            }

            if (terminator.CheckTarget != -1 && terminator.Kind is not (MirTerminatorKind.Goto or MirTerminatorKind.Unreachable) &&
                !(terminator.Kind == MirTerminatorKind.Call && terminator.Target < 0))
            {
                this.Report(MirViolation.CheckTarget);
            }

            if (terminator.Kind == MirTerminatorKind.Call && !this.Call(terminator))
            {
                return false;
            }

            MirUses.ForEach(body, location, ref this);
            return valid;
        }

        // False when the arguments are out of range. A Fn, Requirement or Virtual callee's arity awaits a declaration view.
        private bool Call(in MirTerminator call)
        {
            if (!InRange(call.Start, call.Count, body.Operands.Length))
            {
                this.Report(MirViolation.OperandRange);
                return false;
            }

            var callee = call.Callee;
            var row = MirDescriptors.Of((IntrinsicId)callee.Arg);
            switch (callee.Kind)
            {
                case MirCalleeKind.Fn or MirCalleeKind.Requirement or MirCalleeKind.Virtual when callee.Symbol is null:
                    this.Report(MirViolation.Callee);
                    break;
                case MirCalleeKind.Value when this.TypeOf(call.Operand) is { Kind: BoundTypeKind.Function, Components: [var inputs, _] } && inputs.Components.Count != call.Count:
                    this.Report(MirViolation.Arity);
                    break;
                case MirCalleeKind.Intrinsic when !row.Defined || (uint)callee.Arg > byte.MaxValue:
                case > MirCalleeKind.Intrinsic:
                    this.Report(MirViolation.UnknownKind);
                    break;
                case MirCalleeKind.Intrinsic when row.Arity >= 0 && row.Arity != call.Count:
                    this.Report(MirViolation.Arity);
                    break;
                case MirCalleeKind.Intrinsic when row.Diverges && call.Target >= 0:
                    this.Report(MirViolation.Callee);
                    break;
            }

            return true;
        }
    }
}
