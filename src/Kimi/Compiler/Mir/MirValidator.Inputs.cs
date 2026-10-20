// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A Call's input temps (G136, DECISIONS R5-M72): A5 reads the Place a Ref lends into a local flow-insensitively (MirBorrows.lent) where a
// Call takes the local as an input, at an in-place target and its destroying reservations, so the builder writes once an input that
// reserves (MirCases.Reserves, the predicate MirBuilder.Operand decides by: an exclusive reference or a pair layer): each body of an if, a
// match or try and a do gives its value through a temp of its own (MirBuilder.Result), as an exit gives its secured temp. An Opaque
// site's roots are derived and left out.
internal sealed partial class MirValidator
{
    private const byte Written = 4; // A local's writes for G136, beside G131's: a whole write MirUses reports, a second one, a Ref's or an
    private const byte Rewritten = 8; // Adapt's, and a Call's taking the local as an input.
    private const byte Lending = 16;
    private const byte Input = 32;

    private partial struct Checker
    {
        // A whole write of a local MirUses reports, the first or a later one.
        private readonly void Write(MirPlace place)
        {
            if (place.Projection == 0)
            {
                writes[place.Root] |= (writes[place.Root] & Written) != 0 ? Rewritten : Written;
            }
        }

        // A Ref or an Adapt, which a case view may resolve to one, into a local's whole Place: the Place lent into it, A5's lent.
        private readonly void Lend(in MirStatement statement)
        {
            if (statement is { Kind: MirStatementKind.Assign, Place: { Projection: 0, Root: >= 0 and var root } } && root < body.Locals.Length &&
                statement.Rvalue.Kind is MirRvalueKind.Ref or MirRvalueKind.Adapt)
            {
                writes[root] |= Lending;
            }
        }

        // The locals a Call takes as a whole as its inputs: its arguments and a Value callee's operand; its operands are in range.
        private readonly void Inputs(in MirTerminator call)
        {
            for (var i = call.Callee.Kind == MirCalleeKind.Value ? -1 : 0; i < call.Count; i++)
            {
                var operand = i < 0 ? call.Operand : body.Operands[call.Start + i];
                if (operand is { Kind: MirOperandKind.Copy or MirOperandKind.Move, Place: { Projection: 0, Root: >= 0 } input } &&
                    input.Root < body.Locals.Length)
                {
                    writes[input.Root] |= Input;
                }
            }
        }

        // An input Temp that reserves and that a Ref or an Adapt writes has no other write, unless an Opaque site names it; a finding at the
        // Temp.
        private void Lent()
        {
            foreach (var site in body.OpaqueSites)
            {
                foreach (var root in body.OpaqueRoots.Slice(site.Start, site.Count))
                {
                    if (root >= 0)
                    {
                        writes[root] &= unchecked((byte)~Lending); // A local's; a static's is no local.
                    }
                }
            }

            for (var local = 0; local < body.Locals.Length; local++)
            {
                if ((writes[local] & (Rewritten | Lending | Input)) == (Rewritten | Lending | Input) && body.Locals[local].Kind == MirLocalKind.Temp &&
                    MirCases.Reserves(body, body.Locals[local].Type))
                {
                    this.at = body.Locals[local].Source;
                    this.Report(MirViolation.Lent);
                }
            }
        }
    }
}
