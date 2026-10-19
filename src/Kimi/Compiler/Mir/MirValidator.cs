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
    private readonly MirCases cases = new(types);
    private readonly List<(MirRvalue Adapt, SourceInfo Source)> adapts = []; // The body's Adapts its case views resolve.
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
}
