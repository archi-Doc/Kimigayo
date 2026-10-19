// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns structural and Type validation of producer-owned MIR in every configuration.
// Input: MIR bodies and published Type facts. Output: located invariant findings.
// Every IR invariant is checked here; a broken producer contract is InternalInvariant_Kd.
// Must not repair broken IR, recover silently or leave consumers to revalidate it.

#pragma warning disable SA1402 // The validator's rules and findings belong to it.

namespace Kimi.Compiler.Mir;

/// <summary>The rule a validator finding breaks.</summary>
internal enum MirViolation : byte
{
    UnknownKind, // A statement, rvalue, terminator, operand, projection, callee, intrinsic, local or borrow kind without a row.
    NoBlocks,
    Unterminated,
    StatementRange,
    TargetRange, // A successor, case range, check target or MustNotComplete block out of range; a MustNotComplete end with a check or not Unreachable.
    CheckTarget, // A check target on a terminator other than Goto, a Call without a target and Unreachable.
    Unreachable, // A block that neither runtime nor check edges reach from the entry.
    Diverged, // A read, move or borrow, where runtime edges reach, of a Temp whose only writes are diverging Calls' destinations (G131).
    NeverWrite, // A Temp of Type Never written by other than an unadapted Copy or Move of a Never Place, an Opaque value or a diverging Call.
    Local, // Local 0 is not the Return local, an Arg local does not directly follow it, or a local has no Type.
    Scope, // A scope that its outer scope does not precede, or a source outside the scopes.
    StorageLocal, // StorageLive or StorageDead of a Place other than a Var or Temp local.
    EndBorrowLocal, // EndBorrow of a Place other than a reference local.
    Place, // A root or a projection chain out of range.
    Projection, // A projection its base Type does not admit, or a chain out of order.
    Const, // A constant id out of range.
    Binder, // A PairAcquire of a binder out of range or other than its Place's pair layer, or more cases than PairBinder.CaseBound.
    OperandRange,
    Arity, // An rvalue's or a Call's operands disagree with its kind, its Type or its callee.
    Callee, // A Fn, Requirement or Virtual callee without its function, a Value one without its signature, a misdeclared or diverging intrinsic.
    Discriminant, // A Discriminant of a Place that is not an enum.
    Len, // A Len of a Place that is not a sequence.
    Opaque, // An Opaque site or root out of range, or Opaque sites without an Opaque value.
    Type, // An rvalue, an aggregate's operand, a call's argument or result, or _0 at a Return that disagrees with the Type it goes to.
    Operation, // An operator's, a Cast's, a SwitchInt's or an Adapt's operand, a Ref's Type or an Adapt's case operation, that its kind does not admit;
    // a PairAcquire other than the operand of a Use assigned to a local's whole Place with the Uniq or TwoPhaseUniq it marks (A3 E13 (4)), a
    // marked Use of another operand, or a function item constant other than the operand of an Assign's Use, the collector's site (G154).
    StaticSlot, // A staticEnsure's operand other than an Integer constant indexing the body's statics, which MirProgram.StaticInit decodes (F10).
    Lent, // A Temp a Call takes as an input that reserves (MirCases.Reserves), written by a Ref or an Adapt and by another write (G136).
}

/// <summary>A validator finding: its rule and the syntax of its statement, terminator or local, else the body's. The build reports it
/// as <c>InternalInvariant_Kd</c>.</summary>
/// <param name="Rule">The rule.</param>
/// <param name="Subject">The syntax.</param>
internal readonly record struct MirFinding(MirViolation Rule, SourceRef Subject);

/// <summary>The one structural verifier of MIR (plan 14-5): a body's structure, locals, projections against their base Types, operands,
/// rvalues and arities, the Types values flow between (<see cref="MirTypes.Agree"/>, modulo Origins), each Adapt under every
/// Semantics case view (<see cref="MirCases"/>), the blocks that read a Temp only diverging Calls write (G131) and a Call's input temps
/// (G136), before any analysis reads it. It keeps its scratch, so a warm run allocates nothing.</summary>
/// <remarks>An intrinsic's operands are checked by its row's arity (its declaration's parameters come first, a fact of the table,
/// MirValidatorTest) and staticEnsure's also as its static's slot (DECISIONS R5-M71), a Value callee's against its Type's signature where
/// that Type is a Function Type, else by its declared signature's arity only, and every Call's whole-local inputs by one write of an input
/// that reserves and that a Ref or an Adapt writes (DECISIONS R5-M72).</remarks>
/// <param name="types">The Type context of the bodies, which interns their case Types.</param>
internal sealed partial class MirValidator(TyCtxt types)
{
    private const byte DivergingWrite = 1; // A local's writes (G131): a diverging Call's destination, which MirUses does not report,
    private const byte OtherWrite = 2; // and any write MirUses reports; G136's bits follow (MirValidator.Inputs.cs).

    private readonly MirCases cases = new(types);
    private readonly List<(MirRvalue Adapt, SourceInfo Source)> adapts = []; // The body's Adapts its case views resolve.
    private readonly MirReach reach = new();
    private bool[] unknown = [];
    private byte[] writes = []; // Per local, its writes' kinds.

    /// <summary>Validates a body.</summary>
    /// <param name="body">The body.</param>
    /// <param name="findings">The caller's list the findings are added to.</param>
    internal void Validate(MirBody body, List<MirFinding> findings)
    {
        if (MirCases.Count(body) > PairBinder.CaseBound)
        {
            findings.Add(new(MirViolation.Binder, body.Source)); // Binding binds no part of a definition beyond the bound.
            return; // Nor are its views resolved.
        }

        if (this.writes.Length < body.Locals.Length)
        {
            this.writes = new byte[Math.Max(body.Locals.Length, this.writes.Length * 2)];
        }

        Array.Clear(this.writes, 0, body.Locals.Length);
        this.adapts.Clear();
        var checker = new Checker(body, findings, this.Unknown(body), this.adapts, this.writes);
        if (!checker.Tables())
        {
            return; // A chain or site out of order or range cannot be walked.
        }

        var blocks = body.Blocks;
        var walked = blocks.Length > 0;
        if (!walked)
        {
            checker.Report(MirViolation.NoBlocks);
        }

        for (var block = 0; block < blocks.Length; block++)
        {
            walked &= checker.Block(block);
        }

        checker.Finish();
        checker.Views(this.cases);
        if (walked)
        {
            this.Diverged(body, findings);
            this.Reach(body, findings);
        }
    }

    // The temps an Opaque value defines, whose Type is the replaced syntax's, which Binding may not know (an Error node has none), and those a
    // Use, a unary operator or an arithmetic, bit or shift operator of such a temp defines, whose Type is that operand's: the Type tier reads
    // them as of an unknown Type.
    private bool[] Unknown(MirBody body)
    {
        if (this.unknown.Length < body.Locals.Length)
        {
            this.unknown = new bool[Math.Max(body.Locals.Length, this.unknown.Length * 2)];
        }

        var locals = body.Locals;
        var operands = body.Operands;
        Array.Clear(this.unknown, 0, locals.Length);
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (ref readonly var statement in body.Statements)
            {
                if (statement is not { Kind: MirStatementKind.Assign, Place: { Projection: 0, Root: >= 0 and var root }, Rvalue: var rvalue } ||
                    root >= locals.Length || locals[root].Kind != MirLocalKind.Temp || this.unknown[root])
                {
                    continue;
                }

                if (rvalue.Kind != MirRvalueKind.Opaque)
                {
                    if (rvalue is not { Kind: MirRvalueKind.Use or MirRvalueKind.UnaryOp } and not
                        { Kind: MirRvalueKind.BinaryOp, Op: < (byte)MirBinaryOp.Eq } ||
                        (uint)rvalue.Start >= (uint)operands.Length)
                    {
                        continue;
                    }

                    if (operands[rvalue.Start] is not { Kind: not MirOperandKind.Const, Place.Root: >= 0 and var read } ||
                        read >= locals.Length || !this.unknown[read])
                    {
                        continue;
                    }
                }

                this.unknown[root] = changed = true;
            }
        }

        return this.unknown;
    }

    // Every block is reached from the entry through runtime or check edges.
    private void Reach(MirBody body, List<MirFinding> findings)
    {
        var blocks = body.Blocks;
        this.reach.Clear(blocks.Length);
        this.reach.Walk(body, 0, checks: true);
        for (var block = 0; block < blocks.Length; block++)
        {
            if (!this.reach[block])
            {
                findings.Add(new(MirViolation.Unreachable, body.Subject(blocks[block].Terminator.Source)));
            }
        }
    }

    // G131 (DECISIONS R5-M33): a diverging Call writes no destination, so a Temp only such Calls write holds no value at run time and only
    // check-only blocks, which no runtime edge reaches, read, move or borrow it. Any other place, Never-typed or written on another path, is
    // read anywhere. A Drop of the Temp, like its StorageDead, reads nothing: a runtime Drop of storage nothing initialized is valid MIR.
    private void Diverged(MirBody body, List<MirFinding> findings)
    {
        var locals = body.Locals;
        var diverged = false;
        for (var local = 0; local < locals.Length; local++)
        {
            diverged |= Diverging(this.writes[local]) && locals[local].Kind == MirLocalKind.Temp;
        }

        if (!diverged)
        {
            return;
        }

        this.reach.Clear(body.Blocks.Length);
        this.reach.Walk(body, 0, checks: false);
        var reads = new DivergedReads(body, findings, this.writes);
        for (var block = 0; block < body.Blocks.Length; block++)
        {
            if (this.reach[block])
            {
                reads.Block(block);
            }
        }
    }

    // The checks of one body: tables first, then each location before MirUses walks it, and the visitor's checks of what it reports; it
    // records each local's writes.
    private partial struct Checker(
        MirBody body,
        List<MirFinding> findings,
        bool[] unknown,
        List<(MirRvalue Adapt, SourceInfo Source)> adapts,
        byte[] writes) : IMirUses
    {
        private SourceInfo at;
        private int opaqueValues;
        private bool acquires; // Whether the operands walked are a marked Use's assigned to a local's whole Place, a PairAcquire's one position.
        private bool used; // Whether the operands walked are an Assign's Use's, a function item constant's one position.

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
            else if (operand.Kind == MirOperandKind.PairAcquire && !this.acquires)
            {
                this.Report(MirViolation.Operation); // A3 E13 (4): always the whole rvalue of an Assign to a local, marked.
            }
            else if (operand.Kind == MirOperandKind.Const && body.Consts[operand.Arg].Kind == MirConstKind.FnItem && !this.used)
            {
                this.Report(MirViolation.Operation); // G154: the collector finds a function item's edge only at a Use (K 4.8).
            }
            else if (operand.Kind == MirOperandKind.PairAcquire && this.IsValid(operand.Place) && !body.Binds(
                operand.Arg,
                PairBinderOf(body.TypeOf(operand.Place))))
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

            if ((access & MirAccess.Writes) != 0 && place.Root >= 0)
            {
                writes[place.Root] |= OtherWrite;
                this.Write(place);
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

        // Locals, MustNotComplete rows, scopes, Opaque sites and the projection table; false when a chain or site cannot be walked.
        internal bool Tables()
        {
            var locals = body.Locals;
            var arguments = true;
            for (var i = 0; i < locals.Length; i++)
            {
                ref readonly var local = ref locals[i];
                this.At(local.Source);
                arguments &= i == 0 || local.Kind == MirLocalKind.Arg;
                if (local.Type is null || local.Kind > MirLocalKind.DropFlag || (local.Kind == MirLocalKind.Return) != (i == 0) ||
                    (local.Kind == MirLocalKind.Arg && !arguments))
                {
                    this.Report(MirViolation.Local);
                }
            }

            var blocks = body.Blocks;
            foreach (var row in body.MustNotComplete)
            {
                this.at = new(row.Subject, 0, StatementStart: false);
                if ((uint)row.Entry >= (uint)blocks.Length || (uint)row.End >= (uint)blocks.Length ||
                    blocks[row.End].Terminator is not { Kind: MirTerminatorKind.Unreachable, CheckTarget: -1 })
                {
                    this.Report(MirViolation.TargetRange);
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

        // A block's statements and terminator; false when a successor is out of range or MirUses cannot walk a location.
        internal bool Block(int index)
        {
            ref readonly var block = ref body.Blocks[index];
            this.at = block.Terminator.Source;
            var walked = InRange(block.Start, block.Count, body.Statements.Length);
            if (!walked)
            {
                this.Report(MirViolation.StatementRange);
            }
            else
            {
                for (var i = 0; i < block.Count; i++)
                {
                    walked &= this.Statement(new(index, i), body.Statements[block.Start + i]);
                }
            }

            return this.Terminator(new(index, block.Count), block.Terminator) && walked;
        }

        // An Opaque value exactly when the body has Opaque sites, and the writes of the Call inputs (G136).
        internal void Finish()
        {
            this.at = default;
            if (body.HasOpaque != this.opaqueValues > 0)
            {
                this.Report(MirViolation.Opaque);
            }

            this.Lent();
        }

        internal readonly void Report(MirViolation rule) => findings.Add(new(rule, body.Subject(this.at)));

        private static BindingSymbol? PairBinderOf(BoundType? type)
        {
            _ = TyCtxt.Layer(type, out _, out var binder);
            return binder;
        }

        // Whether a range of a table lies within it.
        private static bool InRange(int start, int count, int length) => start >= 0 && count >= 0 && start <= length - count;

        private static bool IsSequence(BoundType type)
            => type.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary ||
                ReferenceEquals(type, BoundType.String);

        // The operands an rvalue has: one or two, as many as its Type's elements, Fields (the base first) or Case payload, or captures; -1
        // when an aggregate's kind and Type disagree. A fixed array built from no operand stores no element (SPEC 4.3.4 noinit), the only
        // count at a generic length, and a Fill has its one value (SPEC 4.3.3, DECISIONS R4-M81); an Array or a Dictionary is built from
        // none, the empty collection (SPEC 12.4.1).
        private static int Arity(in MirRvalue rvalue)
        {
            var type = rvalue.Type;
            return rvalue.Kind switch
            {
                MirRvalueKind.Use or MirRvalueKind.UnaryOp or MirRvalueKind.Cast => 1,
                MirRvalueKind.BinaryOp or MirRvalueKind.Adapt => 2,
                MirRvalueKind.Aggregate => (MirAggregateKind)rvalue.Op switch
                {
                    MirAggregateKind.Tuple when type?.Kind == BoundTypeKind.Tuple => type.Components.Count,
                    MirAggregateKind.Closure when type?.Kind == BoundTypeKind.Closure => type.Components.Count,
                    MirAggregateKind.FixedArray when type?.Kind == BoundTypeKind.FixedArray => rvalue.Count == 0 || type.LengthExpression is not null
                        ? 0 : (int)type.Length,
                    MirAggregateKind.FixedArray when type?.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary => 0,
                    MirAggregateKind.Fill when type?.Kind == BoundTypeKind.FixedArray => 1,
                    MirAggregateKind.Adt when AdtDef.IsStruct(type) => AdtDef.Count(type!) + (AdtDef.Base(type!) is null ? 0 : 1),
                    MirAggregateKind.Adt when type is not null && AdtDef.CaseTypes(type) is { } cases => (uint)rvalue.Arg < (uint)cases.Length
                        ? cases[rvalue.Arg].Components.Count : -1,
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
            => (place.Root >= 0 ? place.Root < body.Locals.Length : ~place.Root < body.Statics.Length) &&
                (uint)place.Projection < (uint)body.Projections.Length;

        // Whether an rvalue may write a Temp of Type Never (DECISIONS R5-M38): an unadapted Copy or Move of a Place of Type Never, or an
        // Opaque value; a diverging Call's destination is the third writer.
        private readonly bool WritesNever(in MirRvalue rvalue) => rvalue.Kind == MirRvalueKind.Opaque ||
            (rvalue is { Kind: MirRvalueKind.Use, Op: 0, Count: 1 } &&
                body.Operands[rvalue.Start] is { Kind: MirOperandKind.Copy or MirOperandKind.Move } operand &&
                this.IsValid(operand.Place) && ReferenceEquals(body.TypeOf(operand.Place), BoundType.Never));

        private readonly bool Admits(BoundType? type, in MirProjection row) => row.Type is not null && ReferenceEquals(
            row.Type,
            body.ProjectedType(type, row.Kind, row.Arg));

        // F10 (plan 5.6.6): a staticEnsure names its static by an Integer constant indexing the body's statics, the form
        // MirProgram.StaticInit decodes; a constant id out of range is the walk's Const finding.
        private readonly bool Ensures(in MirOperand operand) => operand.Kind == MirOperandKind.Const && ((uint)operand.Arg >= (uint)body.Consts.Length ||
            (body.Consts[operand.Arg] is { Kind: MirConstKind.Integer, Bits: var slot } && slot >= 0 && slot < body.Statics.Length));

        // False when MirUses cannot walk it.
        private bool Statement(MirLocation location, in MirStatement statement)
        {
            this.At(statement.Source);
            if (!MirDescriptors.Of(statement.Kind).Defined)
            {
                this.Report(MirViolation.UnknownKind);
                return false;
            }

            var place = statement.Place;
            var local = place.Projection == 0 && (uint)place.Root < (uint)body.Locals.Length ? body.Locals[place.Root] : default;
            switch (statement.Kind)
            {
                case MirStatementKind.StorageLive or MirStatementKind.StorageDead when local.Type is null ||
                    local.Kind is not (MirLocalKind.Var or MirLocalKind.Temp):
                    this.Report(MirViolation.StorageLocal);
                    break;
                case MirStatementKind.EndBorrow when !MirTypes.IsReference(local.Type):
                    this.Report(MirViolation.EndBorrowLocal);
                    break;
                case MirStatementKind.Assign when !this.Rvalue(statement.Rvalue, this.IsValid(place) ? body.TypeOf(place) : null):
                    return false;
            }

            if (statement.Kind == MirStatementKind.Assign && local is { Kind: MirLocalKind.Temp } && ReferenceEquals(local.Type, BoundType.Never) &&
                !this.WritesNever(statement.Rvalue))
            {
                this.Report(MirViolation.NeverWrite); // R5-M38: the Never value's writers are a whitelist.
            }

            this.Lend(statement);
            this.acquires = statement is
            {
                Kind: MirStatementKind.Assign, Place: { Projection: 0, Root: >= 0 },
                Rvalue: { Kind: MirRvalueKind.Use, Op: (byte)MirRefKind.Uniq or (byte)MirRefKind.TwoPhaseUniq },
            };
            this.used = statement is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Use };
            MirUses.ForEach(body, location, ref this);
            (this.acquires, this.used) = (false, false);
            return true;
        }

        // False when the operands or the Opaque site are out of range, which MirUses then does not walk.
        private bool Rvalue(in MirRvalue rvalue, BoundType? destination)
        {
            if (!MirDescriptors.Of(rvalue.Kind).Defined || (rvalue.Kind == MirRvalueKind.Ref && rvalue.Op > (byte)MirRefKind.Raw) ||
                (rvalue.Kind == MirRvalueKind.Adapt && rvalue.Op > (MirRvalue.AdaptShorthand | MirRvalue.AdaptInput)))
            {
                this.Report(MirViolation.UnknownKind);
                return false;
            }

            if (!InRange(rvalue.Start, rvalue.Count, body.Operands.Length))
            {
                this.Report(MirViolation.OperandRange);
                return false;
            }

            if (rvalue is { Kind: MirRvalueKind.Use, Op: not 0, Count: 1 } && body.Operands[rvalue.Start].Kind != MirOperandKind.PairAcquire)
            {
                this.Report(MirViolation.Operation); // Only a PairAcquire's Use is marked (A3 E13 (4)).
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
            else
            {
                this.Types(rvalue, destination);
            }

            var type = rvalue.Kind is MirRvalueKind.Discriminant or MirRvalueKind.Len && this.IsValid(rvalue.Place) ? body.TypeOf(rvalue.Place) : null;
            if (type is not null && (rvalue.Kind == MirRvalueKind.Discriminant ? !AdtDef.IsEnum(type) : !IsSequence(type)))
            {
                this.Report(rvalue.Kind == MirRvalueKind.Discriminant ? MirViolation.Discriminant : MirViolation.Len);
            }

            return true;
        }

        // False when a successor is out of range or MirUses cannot walk it.
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
                (terminator.Kind != MirTerminatorKind.Call || terminator.Target >= -1) && (terminator.CheckTarget == -1 ||
                    (uint)terminator.CheckTarget < blocks);
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

            // SPEC 14.7: a branch tests a bool, an integer or a discriminant; a Return gives _0, of the body's result.
            if (terminator.Kind == MirTerminatorKind.SwitchInt && this.TypeOf(terminator.Operand) is { } tested && !tested.IsInteger &&
                !ReferenceEquals(tested, BoundType.Boolean))
            {
                this.Report(MirViolation.Operation);
            }
            else if (terminator.Kind == MirTerminatorKind.Return && body.Locals.Length > 0)
            {
                this.Agree(Result(body.Key), body.Locals[0].Type);
            }

            if (terminator.Kind == MirTerminatorKind.Call && !this.Call(terminator))
            {
                return false;
            }

            MirUses.ForEach(body, location, ref this);
            return valid;
        }

        // False when the arguments are out of range.
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
                case MirCalleeKind.Value when (uint)callee.Arg >= (uint)body.Consts.Length || body.Consts[callee.Arg].Kind != MirConstKind.TypeToken:
                    this.Report(MirViolation.Callee);
                    break;
                case MirCalleeKind.Fn or MirCalleeKind.Requirement or MirCalleeKind.Virtual or MirCalleeKind.Value:
                    this.Signature(call);
                    break;
                case MirCalleeKind.Intrinsic when !row.Defined || (uint)callee.Arg > byte.MaxValue:
                case > MirCalleeKind.Intrinsic:
                    this.Report(MirViolation.UnknownKind);
                    break;
                case MirCalleeKind.Intrinsic when callee.Symbol is { } declaration &&
                    (declaration.LibraryDeclaration is null || IntrinsicDisposition.Of(declaration.CompilerFunction).Id != (IntrinsicId)callee.Arg):
                    this.Report(MirViolation.Callee); // A3 E14: its function is the library declaration of a compiler function it serves.
                    break;
                case MirCalleeKind.Intrinsic when row.Arity >= 0 && row.Arity != call.Count:
                    this.Report(MirViolation.Arity);
                    break;
                case MirCalleeKind.Intrinsic when row.Diverges && call.Target >= 0:
                    this.Report(MirViolation.Callee);
                    break;
                case MirCalleeKind.Intrinsic when (IntrinsicId)callee.Arg == IntrinsicId.StaticEnsure && !this.Ensures(body.Operands[call.Start]):
                    this.Report(MirViolation.StaticSlot); // DECISIONS R5-M71.
                    break;
            }

            this.Inputs(call);
            if (call is { Target: < 0, Place: { Projection: 0, Root: >= 0 and var root } } && root < body.Locals.Length)
            {
                writes[root] |= DivergingWrite; // The destination MirUses does not report.
            }
            else if (call is { Place: { Projection: 0, Root: >= 0 and var written } } && written < body.Locals.Length &&
                body.Locals[written] is { Kind: MirLocalKind.Temp } local && ReferenceEquals(local.Type, BoundType.Never))
            {
                this.Report(MirViolation.NeverWrite); // A returning Call writes the Never value (R5-M38).
            }

            return true;
        }
    }

    // A runtime block's reads, moves and borrows (not Drops or StorageDeads) of the Temps whose only writes are diverging Calls' destinations,
    // each a finding at its location.
    private struct DivergedReads(MirBody body, List<MirFinding> findings, byte[] writes) : IMirUses
    {
        private SourceInfo at;

        public readonly void Operand(in MirOperand operand)
        {
        }

        public readonly void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            if ((access & (MirAccess.Reads | MirAccess.Moves | MirAccess.Borrows)) != 0 &&
                (uint)place.Root < (uint)body.Locals.Length && Diverging(writes[place.Root]) &&
                body.Locals[place.Root].Kind == MirLocalKind.Temp)
            {
                findings.Add(new(MirViolation.Diverged, body.Subject(this.at)));
            }
        }

        public readonly void Effect(MirEffect effects, int site)
        {
        }

        internal void Block(int index)
        {
            ref readonly var block = ref body.Blocks[index];
            for (var i = 0; i <= block.Count; i++)
            {
                this.at = i < block.Count ? body.Statements[block.Start + i].Source : block.Terminator.Source;
                MirUses.ForEach(body, new(index, i), ref this);
            }
        }
    }
}
