// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the MIR analyses (P7–P9) of one program version (DECISIONS R4-M1, R4-M2a).
// Input: a built and validated MirProgram and its root modules, the range of Source.Module Compilation gives the collector too.
// Output: its Problems for that version, which the analyses add through MirReport after the construction records (MirProgram.Clear resets
// them with the bodies, R6-check H3-1), and the version's count of judgments (Judgments), which the library check reads in each version.
// Runs them once per version, after build and validation: the version alone keys the run because an instance analyzes the programs of
// one builder (Compilation pairs its analysis with its builder; a test or measurement makes both), which builds a version with one root
// range (MirBuilder.Build); that build range is the builder's, not Run's rootModules. The steps run in a fixed order, one line per step
// (a judged body's steps 1 and 2 together, another body's step 2 only); a step's details are in the comment of the method that runs it
// or in its analysis's contract:
// 1. per judged body, A3;
// 2. per body, per view A1, then A2, then A4's local facts;
// 3. A4 over the program, in rounds;
// 4. per judged body, A5: the Origin stratum's judgment, which adds its problems, then the Loan stratum per view; then, once per program,
//    the rows of layers no constructor body includes (MirOrigins.Findings);
// 5. A6 inside MirReport.
// Steps 1 and 2 have one path (Step): the bodies built since the last step, in build order, a body a lookup builds meanwhile included
// (MirProgram's demand point, PLAN G951). A round of a consumer of step-2 facts (A4's here, the collector's) that reads a body built since
// then misses and commits nothing (MirDropTable.Holds); the consumer's driver steps and runs it again until a round is clean. While step
// 4 judges, the program's demand table is Judging: no lookup may build a body.
// Input rule (DECISIONS R5-M168, PLAN G951): steps 1 and 4 judge the bodies of the root modules (Judged), step 3 the obligations whose
// subject is in them and MirOrigins.Findings their layers' rows, whose problems are the program's; a body or declaration outside them is
// built from its library definition alone, so its verdicts are the same in every program. Steps 2 and 3 visit every body without a
// validator finding: A4 composes library callees' local facts and code generation reads A2's tables. The library's own verdicts are
// MirAnalysisTest.TheLibraryHasNoAnalysisProblem's, the check every unit changing the library, MIR construction, the validator or A1-A5
// runs.
// Owns one instance of each analysis and their shared scratch for the compilation's lifetime, arrays reset by counts; each analysis unit
// adds its field and its pass row at its step: step 1 in Steps, step 2 in Body, steps 3 and 4 in Run (A5 with its own loop over the
// bodies after A4's, Borrows per body). A2's table (MirProgram.Drops) is per version, like the problems.
// Must not read syntax, HIR or plans, publish, decide acceptance, or read a body with a validator finding (MirBody.HasFinding, DECISIONS
// R5-M183): steps 1, 2 and 4 skip it (MirDrops.Exclude closes its empty table entry); A4 composes a body with one as Derived
// (MirEffects.Views); the initialization proof that consults one rests on it (MirBorrows.Unproved).

namespace Kimi.Compiler.Mir;

/// <summary>The driver of the MIR analyses (plan P7–P9), whose problems decide acceptance (DECISIONS R5-M185): <see cref="Run"/>
/// analyzes a program version once; a repeated run of the same version keeps its problems.</summary>
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
    private int stepped; // The bodies of the version steps 1 and 2 have run on, in build order.

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

    /// <summary>Gets the judgments of the latest version: one per body step 1 (A3) judged and one per body step 4 (A5) judged; the library
    /// check reads it in each version (MirAnalysisTest.TheLibraryHasNoAnalysisProblem, PLAN G950's row M3).</summary>
    internal int Judgments { get; private set; }

    /// <summary>Analyzes a program unless this version already was: a new version starts with its construction records only (MirProgram.Clear),
    /// and a body with a validator finding is not analyzed (DECISIONS R5-M183).</summary>
    /// <param name="program">The built and validated program.</param>
    /// <param name="rootModules">The root range: steps 1 and 4 judge the bodies whose module is below it, step 3 the obligations and
    /// MirOrigins.Findings the layer rows located there (DECISIONS R5-M168, PLAN G951). Any bound is a range: Compilation passes the source
    /// modules' count, and <see cref="Parsing.SyntaxTable.LibraryModule"/> + 1 includes the library's.</param>
    /// <returns>The program.</returns>
    internal MirProgram Run(MirProgram program, int rootModules)
    {
        if (program.Version == this.version)
        {
            return program;
        }

        (this.version, this.stepped, this.Judgments) = (program.Version, 0, 0);
        program.Drops.Clear();
        this.Steps(program, rootModules); // 1 and 2.

        // 3. A4's rounds over the program, until one is clean.
        while (!this.effects.Run(program, program.Problems, rootModules))
        {
            this.Step(program, rootModules);
        }

        program.Enter(MirProgram.Phase.Judging);
        foreach (var body in program.Bodies)
        {
            if (Judged(body, rootModules) && !body.HasFinding)
            {
                this.Borrows(program, body); // 4. per judged body.
                this.Judgments++;
            }
        }

        this.origins.Findings(program, program.Problems, rootModules); // 4. once per program.
        program.Enter(MirProgram.Phase.Open);
        return program;
    }

    /// <summary>Runs steps 1 and 2 over the bodies built since the last step, in build order, before a consumer's next round after its
    /// round missed (A4's in <see cref="Run"/>, the collector's): the one stepping path (PLAN G951). A round misses only at a body built
    /// since the last step, so a miss with none is a broken contract (R3-N2), never a rerun.</summary>
    /// <param name="program">The program of the version <see cref="Run"/> analyzes.</param>
    /// <param name="rootModules">The root range of that run.</param>
    internal void Step(MirProgram program, int rootModules)
    {
        if (this.stepped == program.Bodies.Length)
        {
            throw new InvalidOperationException($"A round of version {program.Version} missed with every body stepped (InternalInvariant).");
        }

        this.Steps(program, rootModules);
    }

    // Whether steps 1 and 4 judge a body: a root module's (DECISIONS R5-M168).
    private static bool Judged(MirBody body, int rootModules) => body.Source.Module < rootModules;

    // Steps 1 and 2 of each body built since the last step, in build order; a lookup in step 2 appends a body the loop reaches, so A2's
    // entry of each body is its build position (MirDropTable.Holds).
    private void Steps(MirProgram program, int rootModules)
    {
        for (; this.stepped < program.Bodies.Length; this.stepped++)
        {
            var body = program.Body(this.stepped);
            if (body.HasFinding)
            {
                this.drops.Exclude(program); // No consumer reads it (R5-M183): an empty entry keeps the table's ordinals.
                continue;
            }

            if (Judged(body, rootModules))
            {
                this.lints.Run(body, program.Problems); // 1. A3 per judged body.
                this.Judgments++;
            }

            this.Body(program, body); // 2. per body.
        }
    }

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
