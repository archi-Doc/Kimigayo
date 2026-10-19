// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// Body-wide validation facts: unknown Types propagated from Opaque values, reachability through all edges, and runtime reads of
// temps only diverging Calls write. These checks share the validator's recorded writes; they publish findings without repairing MIR.
internal sealed partial class MirValidator
{
    private const byte DivergingWrite = 1; // A local's writes (G131): a diverging Call's destination, which MirUses does not report,
    private const byte OtherWrite = 2; // and any write MirUses reports; G136's bits follow (MirValidator.Inputs.cs).

    private readonly MirReach reach = new();
    private bool[] unknown = [];

    // Whether a local's only writes are diverging Calls' destinations (G131), whatever G136's bits say.
    private static bool Diverging(byte writes) => (writes & (DivergingWrite | OtherWrite)) == DivergingWrite;

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
