// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// The program pass (G191; DECISIONS R4-M121): the bodies one body names in its program.
// - Responsibility: once all bodies are built, each staticEnsure's static with its StaticInit body (Validate(MirProgram)), since the
//   builder ensures only the statics whose initializers it builds. So A4, A5, the instance collector and code generation read
//   MirProgram.StaticInit without a guard.
// - Inputs: the program's bodies once all are built (MirBuilder.Build); a staticEnsure's static by MirValidator.Statics' Slot. Outputs:
//   StaticSlot findings in the program's findings, each marking the body it is located in (MirBody.HasFinding).
// - Invariants: a staticEnsure of another form names no static here (Slot).
// - Prohibitions: no rule of one body alone (the body walk's, MirValidator.cs).
internal sealed partial class MirValidator
{
    /// <summary>Validates what a program's bodies require of each other once all are built: each staticEnsure names a static whose
    /// StaticInit body the program has (a StaticSlot finding at the call otherwise, which the ensuring body has, MirBody.HasFinding).</summary>
    /// <param name="program">The program, whose findings the findings are added to.</param>
    internal void Validate(MirProgram program)
    {
        foreach (var body in program.Bodies)
        {
            foreach (ref readonly var block in body.Blocks)
            {
                if (Slot(body, block.Terminator) is >= 0 and var slot && program.StaticInit(body, slot) is null)
                {
                    program.Findings.Add(new(MirViolation.StaticSlot, body.Subject(block.Terminator.Source)));
                    body.HasFinding = true;
                }
            }
        }
    }
}
