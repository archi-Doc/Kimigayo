// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the runtime lints of MIR bodies (A3, plan 5.5; DECISIONS R4-M4, R4-M7a).
// Input: a validated body, its MustNotComplete rows and their guessed sites. Output: RequireFallthrough_Kd problems into MirReport, each
// with one Cause fact of Value -1 (a Binding cause) per guessed site of its row, so MirReport's rule derives it (DECISIONS R5-M186).
// A failure body is assumed entered (SPEC 14.11), also where only the checks reach it (SPEC 14.10.3); its end must not be reached along
// runtime edges.
// Must not read syntax, HIR or plans, follow CheckTarget edges, or decide that a problem is derived (MirReport's rule).

using System.Runtime.InteropServices;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

/// <summary>A3, the runtime lints (plan P9): a require whose failure body a runtime path takes from its entry to its end, so the code after
/// the require may continue (SPEC 14.11), is a <c>RequireFallthrough_Kd</c> problem at the failure body, in every view
/// (<see cref="MirCases.Every"/>: it depends on no Semantics case), resting on the sites Binding guessed its completion rests on. Its walks
/// and facts keep their storage, so a warm run allocates nothing.</summary>
/// <param name="reach">The one reachability walk, which the analyses share.</param>
internal sealed class MirLints(MirReach reach)
{
    private readonly List<MirFact> causes = []; // A problem's Cause facts.

    /// <summary>Reports each failure body of a body whose end a runtime path from its entry reaches.</summary>
    /// <param name="body">A validated body.</param>
    /// <param name="report">The problems of the body's program version.</param>
    internal void Run(MirBody body, MirReport report)
    {
        var rows = body.MustNotComplete;
        if (rows.Length == 0)
        {
            return;
        }

        // The rows are in post-order, so from the last to the first a failure body is judged before a require nested in it seeds its entry;
        // a nested entry the outer walk did not reach is reached only by the checks, and only a transfer the checks alone reach leads from it
        // to the outer end (SPEC 14.10.3). A seed walked before is outside the judged failure body and reaches its end only through its
        // entry, so the reached blocks accumulate, each verdict is exact and each block is visited once.
        reach.Clear(body.Blocks.Length);
        for (var i = rows.Length - 1; i >= 0; i--)
        {
            reach.Walk(body, rows[i].Entry, checks: false);
            if (reach[rows[i].End])
            {
                this.causes.Clear();
                foreach (var site in body.GuessedSites.Slice(rows[i].Start, rows[i].Count))
                {
                    this.causes.Add(new(MirFactKind.Cause, site, -1)); // A Binding cause at the site.
                }

                var problem = new MirProblem(
                    DiagnosticCode.RequireFallthrough_Kd, DiagnosticRequirement.ControlFlow, rows[i].Subject, body.Ordinal, MirCases.Every(body));
                report.Add(problem, CollectionsMarshal.AsSpan(this.causes));
            }
        }
    }
}
