// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// Calls (plan 5.4; A3 E14; DECISIONS R5-M38, R5-M71).
// - Responsibility: a Call's operand range and its callee by kind (Call): a Fn, Requirement or Virtual callee's function, a Value
//   callee's TypeToken constant, an intrinsic's row (defined, the library declaration of the compiler function it serves, its arity, no
//   target when it diverges, staticEnsure's static slot by Ensures); and its destination: a diverging Call's write bit for G131, no
//   Never value written by a returning Call.
// - Inputs: a Call terminator. Outputs: OperandRange, Callee, UnknownKind, Arity, StaticSlot and NeverWrite findings; a callee with a
//   signature goes to MirValidator.Signatures and the Call's inputs to MirValidator.Inputs (G136).
// - Invariants: Call returns false when its arguments are out of range, and the location is then not walked.
// - Prohibitions: no argument or result Type (MirValidator.Signatures).
internal sealed partial class MirValidator
{
    private partial struct Checker
    {
        // F10 (plan 5.6.6): a staticEnsure names its static by an Integer constant indexing the body's statics, the form
        // MirProgram.StaticInit decodes; a constant id out of range is the walk's Const finding.
        private readonly bool Ensures(in MirOperand operand) => operand.Kind == MirOperandKind.Const && ((uint)operand.Arg >= (uint)body.Consts.Length ||
            (body.Consts[operand.Arg] is { Kind: MirConstKind.Integer, Bits: var slot } && slot >= 0 && slot < body.Statics.Length));

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
