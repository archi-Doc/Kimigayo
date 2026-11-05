// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the public entry path for post-Binding phases and their Problem records.
// Input: a prepared compilation. Output: analysis, the validated and analyzed MIR with its checked set, and diagnostics, the same on every
// pipeline (Compilation.Switch.cs; DECISIONS R5-M156); generation has its own entry (Compilation.Generation.cs, R5 C0a).
// The MIR decides acceptance (DECISIONS R5-M185): Analysis is verified when the MIR of the version exists and its rows hold no Error, its
// rows being every merged problem of its report (construction and collection records, the analyses' problems) and its validator findings,
// which Problems gives and Core's publisher publishes (Compilation.Publication.cs; SPEC 8.15.3, 23.2.2; DECISIONS R5-M156, R5-M184).
// Observers of Analyzed (R4-M17, removal H10a) are read-only; no compiler code subscribes.
// Analyze runs no old analysis; its test and benchmark readers reach it only through AnalyzeLegacyOwnership, which runs it once per version
// (R6-check H0b §2.1, review B3; H0c D-c1; removal H10b).
// Must not expose a second analysis/generation path or re-decide phase-owned facts.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The analysis entry points (plan 12.1, 13.2), the one way to run and read the analyses after Binding and startup (generation has its own
// entry, Compilation.Generation.cs): on every pipeline the MIR is built, validated, analyzed and collected, and it decides (DECISIONS
// R5-M156, R5-M185).
public partial class Compilation
{
    private readonly List<Problem> problems = [];
    private Mir.MirBuilder? mirBuilder;
    private Mir.MirAnalysis? mirAnalysis;
    private Backend.InstanceCollector? instances;
    private Mir.MirProgram? mir; // The MIR of the latest Analyze; null before one and after invalidation.
    private bool legacyAnalyzed; // Whether the old analysis ran for this version (AnalyzeLegacyOwnership); invalidation clears it.

    /// <summary>Occurs at the end of every <see cref="Analyze"/>, on both pipelines, when the MIR's rows of the version are final: the one
    /// observation point of the difference ledger (R4-M17), which runs the old analysis through <see cref="AnalyzeLegacyOwnership"/>.
    /// Observers only read.</summary>
    [Transitional("R6", Reason = "The difference ledger compares the old analysis with the MIR until R6-check H10a deletes it.")]
    internal static event Action<Compilation>? Analyzed;

    /// <summary>Gets the result of the latest <see cref="Analyze"/>: verified when its MIR exists and no row of <see cref="Problems"/> is an
    /// Error (DECISIONS R5-M185); not verified before an analysis and after invalidation.</summary>
    public AnalysisResult Analysis
    {
        get
        {
            // Indexed: the interface's enumerator of a nonempty list is boxed, and a warm Analyze reads this (T61).
            var (rows, errors) = (this.Problems, 0);
            for (var i = 0; i < rows.Count; i++)
            {
                errors += DiagnosticEntries.TryGet(rows[i].Code, out var entry) && entry.Severity == DiagnosticSeverity.Error ? 1 : 0;
            }

            return new(this.mir is not null && errors == 0, errors);
        }
    }

    /// <summary>Gets the checked set of the latest <see cref="Analyze"/> or <see cref="BuildMir"/>, on every pipeline (SPEC 8.15.3), else
    /// null.</summary>
    internal Backend.CheckedSet? Instances => this.instances?.Set;

    /// <summary>Gets the rows of the latest <see cref="Analyze"/> in the publisher's order (DECISIONS R5-M185): each merged problem of its
    /// MIR's report with its code, then each validator finding as <c>InternalInvariant_Kd</c>; none before an analysis and after
    /// invalidation. A derived problem publishes as <c>PrerequisiteUnavailable_Kd</c> (Compilation.Publication.cs). The list is reused by
    /// the next read.</summary>
    internal IReadOnlyList<Problem> Problems
    {
        get
        {
            this.problems.Clear();
            if (this.mir is { } program)
            {
                foreach (var problem in program.Problems.Merged)
                {
                    this.problems.Add(new(problem.Code, problem.Subject));
                }

                foreach (var finding in program.Findings)
                {
                    this.problems.Add(new(DiagnosticCode.InternalInvariant_Kd, finding.Subject));
                }
            }

            return this.problems;
        }
    }

    /// <summary>Analyzes the bodies after final Binding and the startup check (P6–P9): the MIR is built, validated, analyzed and its checked
    /// set collected, on every pipeline (SPEC 8.15.3; DECISIONS R5-M156, R5-M185); then <see cref="Analyzed"/> occurs.</summary>
    /// <returns>The analysis result.</returns>
    /// <exception cref="InvalidOperationException">The latest Binding pass is not final.</exception>
    public AnalysisResult Analyze()
    {
        if (this.Binding.Result.Mode != BindingMode.Final)
        {
            throw new InvalidOperationException("Analysis requires final Binding.");
        }

        this.Diagnostics.Invalidate(DiagnosticPartition.ControlFlow);
        this.Diagnostics.Invalidate(DiagnosticPartition.Ownership);
        this.mir = this.BuildMir();
        Analyzed?.Invoke(this);
        return this.Analysis;
    }

    /// <summary>Records the diagnostics of the latest <see cref="Analyze"/>: its rows, through Core's publisher (Compilation.Publication.cs;
    /// DECISIONS R5-M184, R5-M185).</summary>
    public void ReportAnalysis() => this.PublishMirRecords();

    /// <summary>Gets whether the latest <see cref="Analyze"/> judged a function's own body without a problem (R6-check H6, H0b §2.1;
    /// GR-1a): its MIR body exists, the analyses ran on it (no validator finding, <c>MirBody.HasFinding</c>, DECISIONS R5-M183), no
    /// construction record lies in it (<c>MirBody.HasConstructionRecord</c>) and no merged problem of the report has a member in it
    /// (DECISIONS R5-M185); hover's ownership line reads it. A record made without a body in hand, a collection record or a module's broken
    /// HIR contract, is outside it.</summary>
    /// <param name="function">The function's symbol.</param>
    /// <returns>Whether its body was judged without a problem; false before an analysis, after invalidation and without a body.</returns>
    internal bool JudgedWithoutProblem(BindingSymbol function)
    {
        if (this.mir is not { } program || program[new(function, Mir.MirBodyKind.Fn)] is not { HasFinding: false, HasConstructionRecord: false } body)
        {
            return false;
        }

        var report = program.Problems;
        for (var merged = 0; merged < report.Merged.Length; merged++)
        {
            foreach (var member in report.Members(merged))
            {
                if (report.All[member].Body == body.Ordinal)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Gets the syntax a <see cref="SourceRef"/> of this compilation names.</summary>
    /// <param name="source">The reference.</param>
    /// <returns>The node.</returns>
    internal Koto SyntaxOf(SourceRef source)
        => (source.Module < this.SourceModules.Length ? this.SourceModules[source.Module] : this.Library.Kotonoha).Syntax.Nodes[source.Id]!;

    /// <summary>Builds, validates and analyzes the MIR of the latest Binding pass, once per pass, and collects its checked set after the
    /// analyses, whatever they found (K package 4.3).</summary>
    /// <returns>The program, with its construction records, validator findings and analysis problems.</returns>
    internal Mir.MirProgram BuildMir()
    {
        var program = (this.mirAnalysis ??= new(this.Types)).Run((this.mirBuilder ??= new(this)).Build(), this.SourceModules.Length);
        this.Collect(program);
        return program;
    }

    /// <summary>Runs the old ownership analysis once per version and returns it: the one entry of its test and benchmark readers (R6-check
    /// H0b §2.1, review B3; H0c D-c1). A second call on one version runs nothing.</summary>
    /// <returns>The old analysis of this version.</returns>
    [Transitional("R6-check", Reason = "Its readers migrate off by H8a–c and H9; H10b deletes it with the old analysis.")]
    internal OwnershipAnalysis AnalyzeLegacyOwnership()
    {
        var ownership = this.ownership ??= new(this);
        if (!this.legacyAnalyzed)
        {
            ownership.Analyze();
            this.legacyAnalyzed = true;
        }

        return ownership;
    }

    internal void InvalidateOwnership()
    {
        this.ownership?.Invalidate();
        this.legacyAnalyzed = false;
        this.mir = null;
    }

    // The checked set of a built and validated program, on every pipeline (SPEC 8.15.3: its layout obligations are judged at check), whose
    // records join the program's report.
    private void Collect(Mir.MirProgram program) => (this.instances ??= new(this.Types, this.Layout)).Run(program, this.SourceModules.Length);
}

/// <summary>The summary of a compilation's analysis.</summary>
/// <param name="IsVerified">Whether the MIR of the version exists and no analysis row is an Error.</param>
/// <param name="ErrorCount">The number of analysis rows of Error severity.</param>
public readonly record struct AnalysisResult(bool IsVerified, int ErrorCount);

/// <summary>A problem of an analysis (plan decision 7): its code and its subject.</summary>
/// <param name="Code">The code.</param>
/// <param name="Subject">The syntax the problem is about.</param>
internal readonly record struct Problem(DiagnosticCode Code, SourceRef Subject);
