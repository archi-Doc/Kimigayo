// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// Calls (plan 5.4; A3 E14; DECISIONS R5-M38, R5-M71, R5-M166).
// - Responsibility: a Call's operand range and its callee by kind (Call): a Fn, Requirement or Virtual callee's function (a Requirement
//   callee's comparison mode, Compares), a Value callee's TypeToken constant, an intrinsic's row (defined, the library declaration of the
//   compiler function it serves, its arity, no target when it diverges, staticEnsure's static slot by Ensures, MirValidator.Statics' Slot,
//   testRecordValue's recorded value a Scalar by Records); and its destination: a diverging Call's write bit for G131, no Never value
//   written by a returning Call.
// - Inputs: a Call terminator; an intrinsic's arity from its MirDescriptors row. Outputs: OperandRange, Callee, UnknownKind, Arity,
//   StaticSlot, Type and NeverWrite findings; a callee with a signature goes to MirValidator.Signatures and the Call's inputs to
//   MirValidator.Inputs (G136).
// - Invariants: Call returns false when its arguments are out of range, and the location is then not walked; an intrinsic row after
//   the arity row sees the row's operand count.
// - Prohibitions: no argument or result Type of a signature (MirValidator.Signatures); an intrinsic row's Type only through the Type
//   tier's TypeOf and SPEC 3.1's one Scalar predicate (ScalarTypes.Supports, which the builder's records read too).
internal sealed partial class MirValidator
{
    private partial struct Checker
    {
        // F10 (plan 5.6.6): a staticEnsure names its static (Slot); a constant id out of range is the walk's Const finding.
        private readonly bool Ensures(in MirTerminator call) => Slot(body, call) >= 0 ||
            (body.Operands[call.Start] is { Kind: MirOperandKind.Const } operand && (uint)operand.Arg >= (uint)body.Consts.Length);

        // SPEC 17.6.3 (F8b, DECISIONS R5-M136): a testRecordValue records a Scalar (SPEC 3.1, ScalarTypes.Supports), its value (operand 1
        // of its row's 2, after its side) of a Scalar Type or of one the Type tier reads as unknown: an Opaque temp's, which code generation
        // never sees, or the Never value, which fits any operand (SPEC 3.8) and which the builder never records.
        private readonly bool Records(in MirTerminator call)
            => this.TypeOf(body.Operands[call.Start + 1]) is not { } type || ScalarTypes.Supports(type);

        // DECISIONS R5-M166: a Requirement callee's comparison mode names Equatable's or Comparable's own requirement (Reversed only
        // Comparable's) through its own Contract (Contract null) on a composite Self (ComparisonTypes) whose comparison under source operators
        // TyCtxt.Comparison composes (not a Limit), the node A4 and the collector read.
        private readonly bool Compares(in MirCallee callee)
        {
            var contract = (ComparisonMode)callee.Arg switch
            {
                ComparisonMode.Operator when ReferenceEquals(callee.Symbol, types.OwnRequirement(KimiDeclarationId.Equatable)) => KimiDeclarationId.Equatable,
                ComparisonMode.Operator or ComparisonMode.Reversed when ReferenceEquals(callee.Symbol, types.OwnRequirement(KimiDeclarationId.Comparable))
                    => KimiDeclarationId.Comparable,
                _ => (KimiDeclarationId?)null,
            };

            return contract is not null && callee is { Contract: null, Subst.Self: { } self } && ComparisonTypes.IsComposite(self) &&
                types.Comparison(TyCtxt.Contract(callee.Symbol!), self, operators: true) is not null;
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
                case MirCalleeKind.Requirement when callee.Arg != 0 && !this.Compares(callee):
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
                case MirCalleeKind.Intrinsic when (IntrinsicId)callee.Arg == IntrinsicId.StaticEnsure && !this.Ensures(call):
                    this.Report(MirViolation.StaticSlot); // DECISIONS R5-M71.
                    break;
                case MirCalleeKind.Intrinsic when (IntrinsicId)callee.Arg == IntrinsicId.TestRecordValue && !this.Records(call):
                    this.Report(MirViolation.Type);
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
