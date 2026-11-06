// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// Statics and their initializers (G191; plan 5.6.6; K package 4.5 row 7; SPEC 22.4.3; DECISIONS R5-M71, R5-M106, R4-M121).
// - Responsibility: the static a staticEnsure names (Slot, F10's form, which MirProgram.StaticInit decodes); in a body, each static root
//   of a Place a location accesses named by a staticEnsure of the body (rule (2): Ensured, Accessed) and, where a runtime edge reaches the
//   access, ensured before it on every runtime path from the entry (rule (3): Unensured), the builder's contract (MirBuilder.Places
//   builds a static's Place after its staticEnsure); over a program, each staticEnsure's static with its StaticInit body by Slot
//   (MirValidator.Program), since the builder ensures only the statics whose initializers it builds. So A4, A5, the instance collector
//   and code generation read MirProgram.StaticInit and a body's slots without a guard, and a static's first access initializes it.
// - Inputs: a body's terminators, its runtime edges and the Places MirUses reports. Outputs: Unensured findings; the static a staticEnsure
//   names (Slot) for the program pass; the solve's Work.
// - Invariants: an Opaque site's roots, the Places MirUses reports with the Opaque row's access (MirDescriptors.IsOpaqueRoot), name what
//   its replaced syntax may access, whose static Binding may have rejected; that syntax never runs (its program has a record and is not
//   generated), so no first access initializes through them (Accessed); a staticEnsure of another form is the walk's StaticSlot finding
//   (MirValidator.Calls) and names no static here. An access rule (2) rejects is its one finding, and rule (3) reads only the others; a
//   staticEnsure is a terminator, so it precedes its successors' locations only; a block only check edges reach runs on no runtime path.
// - Prohibitions: no MirDataflow (DECISIONS R4-M121 as amended by U-C: its input is a validated body and its storage limit the
//   analyses', while this solve runs before them on unvalidated input, over runtime edges only); no rule of the operand's form beyond
//   decoding it (MirValidator.Calls).
internal sealed partial class MirValidator
{
    private readonly List<(int Block, int Slot, SourceInfo Source)> accesses = []; // The static accesses rule (2) admits, for rule (3).
    private bool[] ensured = [];
    private ulong[] states = []; // Per block, a group of words: the statics ensured on entry to it on every runtime path (Unensured).

    /// <summary>Gets the work of the latest rule (3) solve: the bit words its runtime edges met, over its passes (the loop depth and 2),
    /// linear in the body's blocks times its statics over the word size.</summary>
    internal long Work { get; private set; }

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

    // Rule (3) (SPEC 22.4.3): a forward must-solve over the blocks runtime edges reach from the entry, in reverse post-order to a fixed
    // point. A block's state has a bit per static, set when every runtime path to its entry passes a staticEnsure of it: the entry's none,
    // every other's all at first; each runtime edge ANDs into its target's state its source's, with the static of the source's terminator
    // where that is a staticEnsure. Then an access rule (2) admitted whose block's state lacks its static is an Unensured finding at it.
    private void Unensured(MirBody body, List<MirFinding> findings)
    {
        this.Work = 0;
        if (this.accesses.Count == 0)
        {
            return;
        }

        var blocks = body.Blocks;
        var words = (body.Statics.Length + 63) >> 6;
        if (this.states.Length < blocks.Length * words)
        {
            this.states = new ulong[Math.Max(blocks.Length * words, this.states.Length * 2)];
        }

        this.states.AsSpan(0, words).Clear();
        this.states.AsSpan(words, (blocks.Length - 1) * words).Fill(ulong.MaxValue);
        this.reach.Clear(blocks.Length);
        this.reach.Walk(body, 0, checks: false);
        var order = this.reach.Postorder;
        for (var changed = true; changed;)
        {
            changed = false;
            for (var i = order.Length - 1; i >= 0; i--)
            {
                var terminator = blocks[order[i]].Terminator;
                var (from, slot) = (order[i] * words, Slot(body, terminator));
                for (var edge = 0; edge < MirUses.Successors(terminator); edge++)
                {
                    var into = MirUses.Successor(body, terminator, edge) * words; // Negative for no target.
                    for (var word = 0; into >= 0 && word < words; word++)
                    {
                        var met = this.states[into + word] & (this.states[from + word] | (slot >> 6 == word ? 1UL << slot : 0));
                        changed |= met != this.states[into + word];
                        this.states[into + word] = met;
                    }

                    this.Work += words;
                }
            }
        }

        foreach (var (block, slot, source) in this.accesses)
        {
            if (this.reach[block] && (this.states[(block * words) + (slot >> 6)] & (1UL << slot)) == 0)
            {
                findings.Add(new(MirViolation.Unensured, body.Subject(source)));
            }
        }
    }

    private partial struct Checker
    {
        // No effect is a rule here: a static access is its Place's (Accessed), an Opaque site's root its access (MirDescriptors.IsOpaqueRoot).
        public readonly void Effect(MirEffect effects, int site)
        {
        }

        // A valid Place a location accesses: a static root a staticEnsure of the body names (rule (2)), recorded with its location for
        // rule (3) (Unensured); an Opaque site's root never runs.
        private readonly void Accessed(MirPlace place, MirAccess access)
        {
            if (!place.IsStatic || MirDescriptors.IsOpaqueRoot(access))
            {
                return;
            }

            if (ensured[~place.Root])
            {
                accesses.Add((this.atBlock, ~place.Root, this.at));
            }
            else
            {
                this.Report(MirViolation.Unensured);
            }
        }
    }
}
