// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A body's blocks and control flow (plan 5.4; SPEC 14.7, 14.10.3).
// - Responsibility: each block's statement range and its locations in order (Block); a statement's kind rules (Statement: a storage
//   statement of a Var or Temp, an EndBorrow of a reference local, the Never value's writers, WritesNever, DECISIONS R5-M38) and the
//   operand positions it marks for the operand visitor (A3 E13 (4), G154); a terminator's (Terminator: a Goto's target, successors,
//   case range and check target in range, a check target only where it may stand, a SwitchInt's tested Type, a Return's _0); every
//   block reached from the entry through runtime or check edges (Reach); and each region, the body or an Inlined scope's locations,
//   entered only at its first location, whose block no edge from inside the region targets (Entries, DECISIONS R5-M90).
// - Inputs: the body's blocks, statements, terminators and scopes. Outputs: UnknownKind, StatementRange, StorageLocal, EndBorrowLocal,
//   NeverWrite, Unterminated, TargetRange, CheckTarget, Operation, Type, Unreachable and Entry findings; an Assign's rvalue goes to
//   MirValidator.Rvalues, a Call to MirValidator.Calls, a lending write to MirValidator.Inputs, then MirUses walks the location.
// - Invariants: Block and Terminator return false when a successor is out of range, a location lies outside the scopes or MirUses
//   cannot walk a location, and Validate then runs none of Diverged, Reach and Entries. Entries walks the raw scope table, which
//   MirValidator.Tables checked, not a MirBody query that trusts validated IR.
// - Prohibitions: no rule of an rvalue, an operand or a callee (MirValidator.Rvalues, MirValidator.Calls).
internal sealed partial class MirValidator
{
    private int[] regions = []; // Per scope, the innermost region holding it, itself where it is one (Entries).
    private MirLocation[] entries = []; // Per region, the location an edge first entered it at; Block -1: none yet.

    // A location's source: its statement's or its terminator's.
    private static SourceInfo Source(MirBody body, MirLocation at)
    {
        ref readonly var block = ref body.Blocks[at.Block];
        return at.Index < block.Count ? body.Statements[block.Start + at.Index].Source : block.Terminator.Source;
    }

    // DECISIONS R5-M90 (2e-D 5.7): a region, the body (scope 0) or an Inlined scope's locations (a default's copy), is entered only at its
    // first location, and no edge from inside it targets the block that location lies in: the body at (0, 0), so no edge targets block 0,
    // a template's included; a copy where its template's block 0 continues the caller's block, so that location dominates the copy and
    // A5 gathers what is live into the copy there. An edge, from a location to the next in its block or a runtime or check edge to a
    // block's first location, enters each region holding its target but not its source; the body's entry each region holding (0, 0).
    // The first pass records each region's first location and the edges entering it elsewhere, the second the edges back into its block.
    private void Entries(MirBody body, List<MirFinding> findings)
    {
        var blocks = body.Blocks;
        var scopes = body.Scopes;
        if (this.regions.Length < scopes.Length)
        {
            this.regions = new int[Math.Max(scopes.Length, this.regions.Length * 2)];
            this.entries = new MirLocation[this.regions.Length];
        }

        for (var scope = 0; scope < scopes.Length; scope++)
        {
            this.regions[scope] = scope == 0 || scopes[scope].Kind == MirScopeKind.Inlined ? scope : this.regions[scopes[scope].Outer];
            this.entries[scope] = new(-1, 0);
        }

        this.Enter(body, -1, new(0, 0), default, findings);
        var statements = body.Statements;
        for (var block = 0; block < blocks.Length; block++)
        {
            var (start, count, terminator) = blocks[block];
            var from = count > 0 ? statements[start].Source : terminator.Source;
            for (var i = 1; i <= count; i++)
            {
                var to = i < count ? statements[start + i].Source : terminator.Source;
                if (to.Scope != from.Scope)
                {
                    this.Enter(body, this.regions[from.Scope], new(block, i), from, findings);
                }

                from = to;
            }

            for (var edge = -1; edge < MirUses.Successors(terminator); edge++)
            {
                if (MirUses.Successor(body, terminator, edge) is >= 0 and var target)
                {
                    this.Enter(body, this.regions[from.Scope], new(target, 0), from, findings);
                }
            }
        }

        for (var block = 0; block < blocks.Length; block++)
        {
            var terminator = blocks[block].Terminator;
            for (var edge = -1; edge < MirUses.Successors(terminator); edge++)
            {
                if (MirUses.Successor(body, terminator, edge) is >= 0 and var target && this.Begins(scopes, this.regions[terminator.Source.Scope], target))
                {
                    findings.Add(new(MirViolation.Entry, body.Subject(terminator.Source))); // Into the block a region holding it begins in.
                }
            }
        }
    }

    // The region holding a region, -1 for the body's.
    private int Outer(ReadOnlySpan<MirScope> scopes, int region) => region > 0 ? this.regions[scopes[region].Outer] : -1;

    // Whether a region or a region holding it has its first location in a block.
    private bool Begins(ReadOnlySpan<MirScope> scopes, int region, int block)
    {
        for (; region >= 0; region = this.Outer(scopes, region))
        {
            if (this.entries[region].Block == block)
            {
                return true;
            }
        }

        return false;
    }

    // An edge from a location in a region (-1: the body's entry) to a location: each region holding the target but not the source is
    // entered there, which is a finding at the source where an edge entered it elsewhere first.
    private void Enter(MirBody body, int from, MirLocation to, in SourceInfo source, List<MirFinding> findings)
    {
        var scopes = body.Scopes;
        var region = this.regions[Source(body, to).Scope];
        for (var common = this.Common(scopes, from, region); region != common; region = this.Outer(scopes, region))
        {
            if (this.entries[region].Block < 0)
            {
                this.entries[region] = to;
            }
            else if (this.entries[region] != to)
            {
                findings.Add(new(MirViolation.Entry, body.Subject(source)));
                return;
            }
        }
    }

    // The innermost region holding two regions, -1 where one is -1 (outside the body); a region's index exceeds its holder's (Tables).
    private int Common(ReadOnlySpan<MirScope> scopes, int a, int b)
    {
        while (a != b)
        {
            (a, b) = a > b ? (this.Outer(scopes, a), b) : (a, this.Outer(scopes, b));
        }

        return a;
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

    private partial struct Checker
    {
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

        // Whether an rvalue may write a Temp of Type Never (DECISIONS R5-M38): an unadapted Copy or Move of a Place of Type Never, or an
        // Opaque value; a diverging Call's destination is the third writer.
        private readonly bool WritesNever(in MirRvalue rvalue) => rvalue.Kind == MirRvalueKind.Opaque ||
            (rvalue is { Kind: MirRvalueKind.Use, Op: 0, Count: 1 } &&
                body.Operands[rvalue.Start] is { Kind: MirOperandKind.Copy or MirOperandKind.Move } operand &&
                this.IsValid(operand.Place) && ReferenceEquals(body.TypeOf(operand.Place), BoundType.Never));

        // False when MirUses cannot walk it.
        private bool Statement(MirLocation location, in MirStatement statement)
        {
            var placed = this.At(statement.Source);
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
            return placed;
        }

        // False when a successor is out of range or MirUses cannot walk it.
        private bool Terminator(MirLocation location, in MirTerminator terminator)
        {
            var placed = this.At(terminator.Source);
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
            return valid && placed;
        }
    }
}
