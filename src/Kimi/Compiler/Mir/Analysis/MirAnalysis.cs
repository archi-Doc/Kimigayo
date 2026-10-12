// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the MIR analyses (P7–P9) of one program version (DECISIONS R4-M1, R4-M2a).
// Input: a built and validated MirProgram. Output: its Problems for that version, which the analyses add through MirReport.
// Runs them once per version, after build and validation, in a fixed order: 1. A3 per body; 2. per body and view, A1, then A2, then A4's
// local facts; 3. A4's summaries and obligations over the program; 4. A5 per view; 5. A6 inside MirReport. Owns one instance of each
// analysis and their shared scratch (MirCases, the dataflow arena) for the compilation's lifetime, arrays reset by counts; each analysis
// unit adds its field and its pass row at its position.
// Must not read syntax, HIR or plans, publish, decide acceptance, or analyze a program with validator findings.

namespace Kimi.Compiler.Mir;

/// <summary>The driver of the MIR analyses (plan P7–P9, R4 shadow: the old path still decides): <see cref="Run"/> analyzes a program
/// version once; a repeated run of the same version keeps its problems.</summary>
internal sealed class MirAnalysis
{
    private readonly MirLints lints = new();
    private ulong version; // The program version whose problems the program holds (0: none).

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
            if (program.Findings.Count > 0)
            {
                return program;
            }

            foreach (var body in program.Bodies)
            {
                this.lints.Run(body, program.Problems); // 1. A3 per body.
            }
        }

        return program;
    }
}
