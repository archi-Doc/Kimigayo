// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A body's blocks and control flow (plan 5.4; SPEC 14.7, 14.10.3).
// - Responsibility: each block's statement range and its locations in order (Block); a statement's kind rules (Statement: a storage
//   statement of a Var or Temp, an EndBorrow of a reference local, the Never value's writers, WritesNever, DECISIONS R5-M38) and the
//   operand positions it marks for the operand visitor (A3 E13 (4), G154); a terminator's (Terminator: a Goto's target, successors,
//   case range and check target in range, a check target only where it may stand, a SwitchInt's tested Type, a Return's _0); and
//   every block reached from the entry through runtime or check edges (Reach).
// - Inputs: the body's blocks, statements and terminators. Outputs: UnknownKind, StatementRange, StorageLocal, EndBorrowLocal,
//   NeverWrite, Unterminated, TargetRange, CheckTarget, Operation, Type and Unreachable findings; an Assign's rvalue goes to
//   MirValidator.Rvalues, a Call to MirValidator.Calls, a lending write to MirValidator.Inputs, then MirUses walks the location.
// - Invariants: Block and Terminator return false when a successor is out of range or MirUses cannot walk a location, and Validate
//   then runs neither Diverged nor Reach.
// - Prohibitions: no rule of an rvalue, an operand or a callee (MirValidator.Rvalues, MirValidator.Calls).
internal sealed partial class MirValidator
{
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
    }
}
