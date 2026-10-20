// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A diverging Call's destination (G131, DECISIONS R5-M33).
// - Responsibility: each local's G131 write bits (DivergingWrite, OtherWrite; G136's follow in MirValidator.Inputs) and, for a Temp
//   whose only writes are diverging Calls' destinations (Diverging), each read, move or borrow of it in a block a runtime edge reaches
//   (Diverged, DivergedReads).
// - Inputs: a body whose blocks were walked, with its locals' write bits. Outputs: a Diverged finding at each such location.
// - Invariants: a Drop or a StorageDead of the Temp reads nothing; a block only check edges reach is not walked.
// - Prohibitions: no recording of the bits (the Place visitor and Call record them).
internal sealed partial class MirValidator
{
    private const byte DivergingWrite = 1; // A local's writes (G131): a diverging Call's destination, which MirUses does not report,
    private const byte OtherWrite = 2; // and any write MirUses reports; G136's bits follow (MirValidator.Inputs.cs).

    // Whether a local's only writes are diverging Calls' destinations (G131), whatever G136's bits say.
    private static bool Diverging(byte writes) => (writes & (DivergingWrite | OtherWrite)) == DivergingWrite;

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
