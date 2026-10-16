// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the MIR analyses (P7–P9) of one program version (DECISIONS R4-M1, R4-M2a).
// Input: a built and validated MirProgram. Output: its Problems for that version, which the analyses add through MirReport.
// Runs them once per version, after build and validation, in a fixed order: per body, 1. A3, then 2. per view A1, then A2, a body whose
// A1 arena exceeds the analyses' limit stopping those views (R4-M15a), then per view A4's local facts; 3. A4's summaries and obligations over the
// program; 4. A5 per view; 5. A6 inside MirReport. Owns one instance of each analysis and their shared scratch (MirCases, the one MirReach,
// the dataflow arena) for the compilation's lifetime, arrays reset by counts; each analysis unit adds its field and its pass row at its
// position. A2's table (MirProgram.Drops) is per version, like the problems.
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
    private ulong version; // The program version whose problems the program holds (0: none).

    /// <summary>Initializes a new instance of the <see cref="MirAnalysis"/> class.</summary>
    /// <param name="types">The Type context of the programs, which the case views read.</param>
    internal MirAnalysis(TyCtxt types)
    {
        var reach = new MirReach(); // The one reachability walk: A3's runtime walks and the dataflow's block order.
        this.cases = new(types);
        this.lints = new(reach);
        this.flow = new(reach);
        this.drops = new(this.moves, this.cases);
        this.init = new(this.flow, this.moves, this.cases, this.drops);
        this.effects = new(types, this.cases, reach);
    }

    /// <summary>Analyzes a program unless this version already was: a new version starts without problems, and one with validator
    /// findings is not analyzed.</summary>
    /// <param name="program">The built and validated program.</param>
    /// <returns>The program.</returns>
    internal MirProgram Run(MirProgram program)
    {
        if (program.Version != this.version)
        {
            this.version = program.Version;
            program.Problems.Clear();
            program.Drops.Clear();
            if (program.Findings.Count > 0)
            {
                return program;
            }

            foreach (var body in program.Bodies)
            {
                this.lints.Run(body, program.Problems); // 1. A3 per body.
                this.flow.Begin(body); // 2. Per body its block order and move paths, then per view A1, then A2, then A4's local facts.
                this.moves.Build(body, this.flow);
                this.drops.Begin(body, program);
                for (var view = 0L; view < MirCases.Count(body); view++)
                {
                    this.cases.Select(body, view);
                    if (!this.init.Run(body, view, program.Problems))
                    {
                        break;
                    }

                    this.drops.Judge(view);
                }

                this.drops.End();
                for (var view = 0L; view < MirCases.Count(body); view++)
                {
                    this.cases.Select(body, view);
                    this.effects.Local(program, body, view); // Every view, over the body's closed A2 table.
                }
            }

            this.effects.Run(program, program.Problems); // 3. A4's summaries and obligations over the program.
        }

        return program;
    }
}
