// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// Statics and their initializers (G191; plan 5.6.6; K package 4.5 row 7; SPEC 22.4.3; DECISIONS R5-M71, R5-M106, R4-M121).
// - Responsibility: the static a staticEnsure names (Slot, F10's form, which MirProgram.StaticInit decodes); in a body, each static root
//   of a Place a location accesses named by a staticEnsure of the body (Ensured, Accessed), the builder's contract (MirBuilder.Places
//   builds a static's Place after its staticEnsure); over a program, each staticEnsure's static with its StaticInit body by Slot
//   (MirValidator.Program), since the builder ensures only the statics whose initializers it builds. So A4, A5, the instance collector
//   and code generation read MirProgram.StaticInit and a body's slots without a guard.
// - Inputs: a body's terminators and the Places MirUses reports. Outputs: Unensured findings; the static a staticEnsure names (Slot)
//   for the program pass.
// - Invariants: an Opaque site's roots, the Places MirUses reports with the Opaque row's access (MirDescriptors.IsOpaqueRoot), name what
//   its replaced syntax may access, whose static Binding may have rejected; that syntax never runs (its program has a record and is not
//   generated), so no first access initializes through them (Accessed); a staticEnsure of another form is the walk's StaticSlot finding
//   (MirValidator.Calls) and names no static here.
// - Prohibitions: no order between a staticEnsure and the accesses it serves (any block of the body names it, as code generation maps a
//   body's slots; that it precedes each access is the builder's, unchecked, DECISIONS R4-M121); no rule of the operand's form beyond
//   decoding it (MirValidator.Calls).
internal sealed partial class MirValidator
{
    private bool[] ensured = [];

    // F10 (plan 5.6.6, DECISIONS R5-M71): the static a staticEnsure names by its one operand, an Integer constant indexing the body's statics,
    // the form MirProgram.StaticInit decodes; -1 for another terminator or form.
    private static int Slot(MirBody body, in MirTerminator terminator)
    {
        if (terminator is not { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.StaticEnsure }, Count: 1 } ||
            (uint)terminator.Start >= (uint)body.Operands.Length || body.Operands[terminator.Start] is not { Kind: MirOperandKind.Const } operand ||
            (uint)operand.Arg >= (uint)body.Consts.Length)
        {
            return -1;
        }

        return body.Consts[operand.Arg] is { Kind: MirConstKind.Integer, Bits: var slot } && slot >= 0 && slot < body.Statics.Length ? (int)slot : -1;
    }

    // The statics a staticEnsure of the body names, by index in its statics.
    private bool[] Ensured(MirBody body)
    {
        if (this.ensured.Length < body.Statics.Length)
        {
            this.ensured = new bool[Math.Max(body.Statics.Length, this.ensured.Length * 2)];
        }

        Array.Clear(this.ensured, 0, body.Statics.Length);
        foreach (ref readonly var block in body.Blocks)
        {
            if (Slot(body, block.Terminator) is >= 0 and var slot)
            {
                this.ensured[slot] = true;
            }
        }

        return this.ensured;
    }

    private partial struct Checker
    {
        // No effect is a rule here: a static access is its Place's (Accessed), an Opaque site's root its access (MirDescriptors.IsOpaqueRoot).
        public readonly void Effect(MirEffect effects, int site)
        {
        }

        // A valid Place a location accesses: a static root a staticEnsure of the body names; an Opaque site's root never runs.
        private readonly void Accessed(MirPlace place, MirAccess access)
        {
            if (place.IsStatic && !MirDescriptors.IsOpaqueRoot(access) && !ensured[~place.Root])
            {
                this.Report(MirViolation.Unensured);
            }
        }
    }
}
