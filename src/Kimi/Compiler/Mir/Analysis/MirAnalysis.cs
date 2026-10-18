// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the MIR analyses (P7–P9) of one program version (DECISIONS R4-M1, R4-M2a).
// Input: a built and validated MirProgram. Output: its Problems for that version, which the analyses add through MirReport.
// Runs them once per version, after build and validation, in a fixed order: per body, 1. A3, then 2. per view A1, then A2, a body whose
// A1 arena exceeds the analyses' limit stopping those views (R4-M15a), then per view A4's local facts; 3. A4's summaries and obligations
// over the program; 4. per body A5 over the views A1 and A2 completed, its block order and move paths built again (design A3 6.1,
// DECISIONS R4-M57); 5. A6 inside MirReport. Owns one instance of each analysis and their shared scratch (MirCases, the one MirReach, the
// dataflow arena, the move paths) for the compilation's lifetime, arrays reset by counts; each analysis unit adds its field and its pass
// row at its step: steps 1 and 2 in Body, steps 3 and 4 in Run (A5 with its own loop over the bodies after A4's, Borrows per body). A2's
// table (MirProgram.Drops) is per version, like the problems.
// Must not read syntax, HIR or plans, publish, decide acceptance, or analyze a program with validator findings.

namespace Kimi.Compiler.Mir;

/// <summary>The driver of the MIR analyses (plan P7–P9, R4 shadow: the old path still decides): <see cref="Run"/> analyzes a program
/// version once; a repeated run of the same version keeps its problems.</summary>
internal sealed class MirAnalysis
{
    private readonly MirCases cases;
    private readonly MirMoveData moves = new();
    private readonly MirLints lints;
    private readonly MirDataflow flow;
    private readonly MirInit init;
    private readonly MirDrops drops;
    private readonly MirEffects effects;
    private readonly MirBorrows borrows;
    private long[] completed = []; // By body ordinal: the views A1 and A2 completed, which A5 checks (none over the limit, R4-M15a).
    private ulong version; // The program version whose problems the program holds (0: none).

    /// <summary>Initializes a new instance of the <see cref="MirAnalysis"/> class.</summary>
    /// <param name="types">The Type context of the programs, which the case views read.</param>
    internal MirAnalysis(TyCtxt types)
    {
        var reach = new MirReach(); // The one reachability walk: A3's runtime walks, the dataflow's block order and phases.
        this.cases = new(types);
        this.lints = new(reach);
        this.flow = new(reach);
        this.drops = new(this.moves, this.cases);
        this.init = new(this.flow, this.moves, this.cases, this.drops);
        this.effects = new(types, this.cases, this.flow);
        this.borrows = new(types, this.flow, this.moves, this.cases);
    }

    /// <summary>Analyzes a program unless this version already was: a new version starts without problems, and one with validator
    /// findings is not analyzed.</summary>
    /// <param name="program">The built and validated program.</param>
    /// <returns>The program.</returns>
    internal MirProgram Run(MirProgram program)
    {
        if (program.Version == this.version)
        {
            return program;
        }

        this.version = program.Version;
        program.Problems.Clear();
        program.Drops.Clear();
        if (program.Findings.Count > 0)
        {
            return program;
        }

        MirDataflow.Grow(ref this.completed, program.Bodies.Length);
        foreach (var body in program.Bodies)
        {
            this.Body(program, body); // 1. and 2. per body.
        }

        this.effects.Run(program, program.Problems); // 3. A4's summaries and obligations over the program.
        foreach (var body in program.Bodies)
        {
            this.Borrows(program, body); // 4. per body.
        }

        return program;
    }

    // The passes of one body: 1. A3, then 2. its block order and move paths, then per view A1, then A2, then A4's local facts.
    private void Body(MirProgram program, MirBody body)
    {
        this.lints.Run(body, program.Problems); // 1. A3 per body.
        this.flow.Begin(body); // 2.
        this.moves.Build(body, this.flow);
        this.drops.Begin(body, program);
        var view = 0L;
        for (; view < MirCases.Count(body); view++)
        {
            this.cases.Select(body, view);
            if (!this.init.Run(body, view, program.Problems))
            {
                break;
            }

            this.drops.Judge(view);
        }

        this.completed[body.Ordinal] = view;
        this.drops.End();
        for (view = 0L; view < MirCases.Count(body); view++)
        {
            this.cases.Select(body, view);
            this.effects.Local(program, body, view); // Every view, over the body's closed A2 table.
        }
    }

    // 4. A5 over the views of a body A1 and A2 completed, after its block order and move paths are built again.
    private void Borrows(MirProgram program, MirBody body)
    {
        this.flow.Begin(body);
        this.moves.Build(body, this.flow);
        for (var view = 0L; view < this.completed[body.Ordinal]; view++)
        {
            this.cases.Select(body, view);
            this.borrows.Run(program, body, view);
        }
    }
}
