// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// The program pass (G191; plan 5.6.2; DECISIONS R4-M121): the bodies one body names in its program.
// - Responsibility: once all bodies are built, each staticEnsure's static with its StaticInit body, since the builder ensures only the
//   statics whose initializers it builds; each call of a default template (a Fn callee's Arg above 0) with the template's body
//   (MirProgram.Callee) and each created Closure (a Closure aggregate) with its Closure body. So A4, A5, the instance collector and code
//   generation read MirProgram.StaticInit, a template call's body and a Closure's body without a guard.
// - Inputs: the program's bodies once all are built (MirBuilder.Build); a staticEnsure's static by MirValidator.Statics' Slot. Outputs:
//   StaticSlot and Callee findings in the program's findings at the call or the statement, each marking the body it is located in
//   (MirBody.HasFinding).
// - Invariants: a staticEnsure of another form, a Fn callee without its function and an aggregate of another Type than a Closure Type
//   are the body walk's findings (MirValidator.Calls, .Rvalues) and name no body here.
// - Prohibitions: no rule of one body alone (the body walk's, MirValidator.cs).
internal sealed partial class MirValidator
{
    /// <summary>Validates what a program's bodies require of each other once all are built: each staticEnsure names a static whose
    /// StaticInit body the program has (a StaticSlot finding at the call otherwise), and each default template's call and each created
    /// Closure has its body (a Callee finding at the call or the statement otherwise), each finding marking its body (MirBody.HasFinding).</summary>
    /// <param name="program">The program, whose findings the findings are added to.</param>
    internal void Validate(MirProgram program)
    {
        foreach (var body in program.Bodies)
        {
            foreach (ref readonly var block in body.Blocks)
            {
                var terminator = block.Terminator;
                if (Slot(body, terminator) is >= 0 and var slot && program.StaticInit(body, slot) is null)
                {
                    Missing(program, body, MirViolation.StaticSlot, terminator.Source);
                }
                else if (terminator is { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Fn, Arg: > 0, Symbol: not null } callee } &&
                    program.Callee(callee) is null)
                {
                    Missing(program, body, MirViolation.Callee, terminator.Source);
                }
            }

            foreach (ref readonly var statement in body.Statements)
            {
                if (statement.Kind == MirStatementKind.Assign &&
                    statement.Rvalue is { Kind: MirRvalueKind.Aggregate, Op: (byte)MirAggregateKind.Closure, Type: { Kind: BoundTypeKind.Closure } closure } &&
                    program[new(closure.Symbol, MirBodyKind.Closure)] is null)
                {
                    Missing(program, body, MirViolation.Callee, statement.Source);
                }
            }
        }
    }

    // A body another body names that the program does not have: a finding at the naming location, which marks its body.
    private static void Missing(MirProgram program, MirBody body, MirViolation rule, in SourceInfo source)
    {
        program.Findings.Add(new(rule, body.Subject(source)));
        body.HasFinding = true;
    }
}
