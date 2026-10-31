// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the MIR analyses (P7–P9) of one program version (DECISIONS R4-M1, R4-M2a).
// Input: a built and validated MirProgram and its root modules, the range of Source.Module Compilation gives the collector too.
// Output: its Problems for that version, which the analyses add through MirReport.
// Runs them once per version, after build and validation, in a fixed order, one line per step (a body's steps 1 and 2 together); a step's
// details are in the comment of the method that runs it or in its analysis's contract:
// 1. per judged body, A3;
// 2. per body, per view A1, then A2, then A4's local facts;
// 3. A4 over the program;
// 4. per judged body, A5: the Origin stratum's judgment, which adds its problems, then the Loan stratum per view; then, once per program,
//    the C1-F4 view's findings (MirOrigins.Findings);
// 5. A6 inside MirReport.
// Input rule (DECISIONS R5-M168): steps 1 and 4 judge the bodies of the root modules (Judged), whose problems are the program's; a body
// outside them is built from its library definition alone, so its A3 and A5 verdicts are the same in every program. Steps 2 and 3 visit
// every body: A4 composes library callees' local facts and code generation reads A2's tables. The library's own A3 and A5 verdicts are
// MirAnalysisTest.TheLibraryHasNoAnalysisProblem's, the check every unit changing A3 or A5 runs.
// Owns one instance of each analysis and their shared scratch for the compilation's lifetime, arrays reset by counts; each analysis unit
// adds its field and its pass row at its step: step 2 in Body, steps 1, 3 and 4 in Run (A5 with its own loop over the bodies after A4's,
// Borrows per body). A2's table (MirProgram.Drops) is per version, like the problems.
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
    private readonly MirOrigins origins;
    private ulong version; // The program version whose problems the program holds (0: none).

    /// <summary>Initializes a new instance of the <see cref="MirAnalysis"/> class.</summary>
    /// <param name="types">The Type context of the programs, which the case views read.</param>
    internal MirAnalysis(TyCtxt types)
    {
        // The scratch the analyses share: MirCases, the one MirReach, the dataflow arena and the move paths.
        var reach = new MirReach(); // The one reachability walk: A3's runtime walks, the dataflow's block order and phases.
        this.cases = new(types);
        this.lints = new(reach);
        this.flow = new(reach);
        this.drops = new(this.moves, this.cases);
        this.init = new(this.flow, this.moves, this.cases, this.drops);
        this.effects = new(types, this.cases, this.flow);
        this.origins = new(types, this.cases); // A5's Origin stratum (2a0-D D13).
        this.borrows = new(types, this.flow, this.moves, this.cases, this.effects, this.origins); // A4's keys (R4-M92), rule W's causes (R4-M132).
    }

    /// <summary>Analyzes a program unless this version already was: a new version starts without problems, and one with validator
    /// findings is not analyzed.</summary>
    /// <param name="program">The built and validated program.</param>
    /// <param name="rootModules">The number of root modules, whose bodies steps 1 and 4 judge (DECISIONS R5-M168).</param>
    /// <returns>The program.</returns>
    internal MirProgram Run(MirProgram program, int rootModules)
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

        foreach (var body in program.Bodies)
        {
            if (Judged(body, rootModules))
            {
                this.lints.Run(body, program.Problems); // 1. A3 per judged body.
            }

            this.Body(program, body); // 2. per body.
        }

        this.effects.Run(program, program.Problems); // 3. A4's summaries and obligations over the program.
        foreach (var body in program.Bodies)
        {
            if (Judged(body, rootModules))
            {
                this.Borrows(program, body); // 4. per judged body.
            }
        }

        this.origins.Findings(program.Problems); // 4. once per program.
        return program;
    }

    // Whether steps 1 and 4 judge a body: a root module's (DECISIONS R5-M168).
    private static bool Judged(MirBody body, int rootModules) => body.Source.Module < rootModules;

    // Step 2 of one body: its block order and move paths, then per view A1, then A2, a body whose A1 arena exceeds the analyses' limit
    // stopping those views (R4-M15a), then per view A4's local facts.
    private void Body(MirProgram program, MirBody body)
    {
        this.flow.Begin(body); // 2.
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

    // 4. A5 over the views of a body A1 and A2 completed, which A2's table publishes (MirDropTable.ViewsOf), after its block order and move
    // paths are built again (design A3 6.1, DECISIONS R4-M57), reading A4's keys (R4-M92): the Origin stratum judges its classes of views
    // and adds their problems, then the Loan stratum runs per view (2a0-D D13, DECISIONS R4-M132).
    private void Borrows(MirProgram program, MirBody body)
    {
        this.flow.Begin(body);
        this.moves.Build(body, this.flow);
        var views = program.Drops.ViewsOf(body.Ordinal);
        this.origins.Judge(body, views, program.Problems);
        for (var view = 0L; view < views; view++)
        {
            this.cases.Select(body, view);
            this.borrows.Run(program, body, view);
        }
    }
}
