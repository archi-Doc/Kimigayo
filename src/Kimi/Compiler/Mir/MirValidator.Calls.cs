// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// Calls (plan 5.4; A3 E14; DECISIONS R5-M38, R5-M71, R5-M166).
// - Responsibility: a Call's operand range and its callee by kind (Call): a Fn, Requirement or Virtual callee's function (a Requirement
//   callee's comparison mode, Compares), a Value callee's TypeToken constant, an intrinsic's row (defined, the library declaration of the
//   compiler function it serves, its arity, no target when it diverges, staticEnsure's static slot by Ensures, MirValidator.Statics' Slot,
//   testRecordValue's recorded value a Scalar by Records); and its destination:
//   a diverging Call's, which MirUses does not report, a Place the body names (MirValidator.Tables' Names) with its write bit for G131,
//   no Never value written by a returning Call; a constant record only as an input of a Fn call that keeps nothing of it
//   (ConstantRecords), and nothing else as Utf8Writer.deferLiteral's text (Defers); a Places Call's Fn callee and whole-Temp Moves
//   (Placed; PLAN G442).
// - Inputs: a Call terminator; an intrinsic's arity from its MirDescriptors row. Outputs: OperandRange, Callee, UnknownKind, Arity,
//   StaticSlot, Type, NeverWrite, Operation, Place and Projection findings; a callee with a signature goes to MirValidator.Signatures
//   and the Call's inputs to MirValidator.Inputs (G136).
// - Invariants: Call returns false when its arguments are out of range, and the location is then not walked; an intrinsic row after
//   the arity row sees the row's operand count.
// - Prohibitions: no argument or result Type of a signature (MirValidator.Signatures); an intrinsic row's Type only through the Type
//   tier's TypeOf and SPEC 3.1's one Scalar predicate (ScalarTypes.Supports, which the builder's records read too).
internal sealed partial class MirValidator
{
    // A whole local's index, -1 for another Place.
    private static int Whole(MirPlace place, int locals) => place is { Projection: 0, Root: >= 0 and var root } && root < locals ? root : -1;

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
        // Comparable's) through its own Contract (Contract null) on a Self whose comparison under source operators TyCtxt.Comparison composes
        // as a Composite node (not a Limit, not a leaf), the node A4, the collector and code generation read.
        private readonly bool Compares(in MirCallee callee)
        {
            var contract = (ComparisonMode)callee.Arg switch
            {
                ComparisonMode.Operator when ReferenceEquals(callee.Symbol, types.OwnRequirement(KimiDeclarationId.Equatable)) => KimiDeclarationId.Equatable,
                ComparisonMode.Operator or ComparisonMode.Reversed when ReferenceEquals(callee.Symbol, types.OwnRequirement(KimiDeclarationId.Comparable))
                    => KimiDeclarationId.Comparable,
                _ => (KimiDeclarationId?)null,
            };

            return contract is not null && callee is { Contract: null, Subst.Self: { } self } &&
                types.Comparison(TyCtxt.Contract(callee.Symbol!), self, operators: true) is { Kind: ComparisonKind.Composite };
        }

        // SPEC 13.4, 14.8.1: a constant record, a comparison's literal, has no Place, Loan or destruction, so A5 follows nothing from it, and only
        // here is it stated that nothing needs following: each is an input of a Fn call that its Clears clears. Else an Operation finding.
        private void ConstantRecords(in MirTerminator call)
        {
            for (var position = 0; position < call.Count; position++)
            {
                var input = body.Operands[call.Start + position];
                if (input.Kind == MirOperandKind.Const && (uint)input.Arg < (uint)body.Consts.Length && body.Consts[input.Arg].IsRecord &&
                    !this.Clears(call.Callee, position))
                {
                    this.Report(MirViolation.Operation);
                }
            }
        }

        // SPEC 3.1.2, 22.10.5 (DECISIONS R8-M7; R8-W2 c1): Utf8Writer.deferLiteral keeps its text's bytes as static storage, the obligation its
        // contract leaves to its caller, so its text (operand 1, after the adapter) is a constant record at every call; a constant id out
        // of range is the walk's Const finding and another operand count the signature's Arity finding. True for another callee.
        private readonly bool Defers(in MirTerminator call)
            => call.Callee is not { Kind: MirCalleeKind.Fn, Symbol.LibraryDeclaration: KimiDeclarationId.WriterDeferLiteral } ||
                call.Count != 2 || (body.Operands[call.Start + 1] is { Kind: MirOperandKind.Const } text &&
                ((uint)text.Arg >= (uint)body.Consts.Length || body.Consts[text.Arg].IsRecord));

        // A Fn call of a declared function, not a default template's (Compared's is one), and the two facts a Loan would otherwise carry: the
        // parameter is not `during static`, an Origin bound that TyCtxt.Retains does not judge, and the call keeps nothing of the input, by that
        // fact, A5's own. A Value callee has no declaration to read, a default template's call no contract and an intrinsic an operand beyond
        // its declaration (its row's Retained), and nothing is built with a record at a Requirement or Virtual call.
        private readonly bool Clears(in MirCallee callee, int position)
            => callee is { Kind: MirCalleeKind.Fn, Arg: 0, Symbol: { } function } && MirCallee.Slot(function, position) is var slot &&
                TyCtxt.Signature(function, callee.Subst, slot, out _) is { Origin.Kind: not OriginKind.Static } &&
                !types.Retains(function, callee.Subst, body.Key.Decl).Keeps(slot);

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

            if (call.Places && !this.Placed(call))
            {
                this.Report(MirViolation.Operation); // PLAN G442: a construction's insertion transfers whole temps into it.
            }

            this.Inputs(call);
            this.ConstantRecords(call);
            if (!this.Defers(call))
            {
                this.Report(MirViolation.Operation);
            }

            if (call.Target < 0)
            {
                // The destination MirUses does not report, which A5 and code generation still read: a Place the body names, and G131's write.
                if (this.Names(call.Place) && call.Place is { Projection: 0, Root: >= 0 and var root })
                {
                    writes[root] |= DivergingWrite;
                }
            }
            else if (call is { Place: { Projection: 0, Root: >= 0 and var written } } && written < body.Locals.Length &&
                body.Locals[written] is { Kind: MirLocalKind.Temp } local && ReferenceEquals(local.Type, BoundType.Never))
            {
                this.Report(MirViolation.NeverWrite); // A returning Call writes the Never value (R5-M38).
            }

            return true;
        }

        // A Places Call (PLAN G442; the owner's O-97 (a)): a construction's insertion, a Fn callee each of whose operands is a Move of a
        // whole Temp that the call transfers into the construction.
        private readonly bool Placed(in MirTerminator call)
        {
            if (call.Callee.Kind != MirCalleeKind.Fn)
            {
                return false;
            }

            foreach (ref readonly var operand in body.Operands.Slice(call.Start, call.Count))
            {
                if (operand.Kind != MirOperandKind.Move || Whole(operand.Place, body.Locals.Length) is not (>= 0 and var root) ||
                    body.Locals[root].Kind != MirLocalKind.Temp)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
