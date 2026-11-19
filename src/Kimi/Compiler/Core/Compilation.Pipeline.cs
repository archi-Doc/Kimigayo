// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns a check's phase sequence (Check) and the public entry path for post-Binding phases and their Problem records.
// Input: a prepared compilation whose inputs are established. Output: the check's verdict (Check: final Binding, the startup check and the
// analysis, each followed by its records, then a test build's discovery), analysis, the constructed and validated MIR (ConstructMir), that
// MIR analyzed with its checked set (BuildMir, which composes it), and diagnostics (DECISIONS R5-M156); generation has its own entry
// (Compilation.Generation.cs, R5 C0a).
// The MIR decides acceptance (DECISIONS R5-M185): Analysis is verified when the MIR of the version exists and its rows hold no Error, its
// rows being every merged problem of its report (construction and collection records, the analyses' problems) and its validator findings,
// which Problems gives and Core's publisher publishes (Compilation.Publication.cs; SPEC 8.15.3, 23.2.2; DECISIONS R5-M156, R5-M184).
// Must not expose a second analysis/generation path or re-decide phase-owned facts.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The analysis entry points (plan 12.1, 13.2), the one way to run and read the analyses after Binding and startup (generation has its own
// entry, Compilation.Generation.cs): the MIR is built, validated, analyzed and collected, and it decides (DECISIONS
// R5-M156, R5-M185).
public partial class Compilation
{
    private readonly List<Problem> problems = [];
    private Mir.MirBuilder? mirBuilder;
    private Mir.MirAnalysis? mirAnalysis;
    private Backend.InstanceCollector? instances;
    private Mir.MirProgram? mir; // The MIR of the latest Analyze; null before one and after invalidation.

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

    /// <summary>Gets the checked set of the latest <see cref="Analyze"/> or <see cref="BuildMir"/> (SPEC 8.15.3), else
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
    /// set collected (SPEC 8.15.3; DECISIONS R5-M156, R5-M185).</summary>
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
        this.generated = null; // A written module's result ends when the next analysis begins, a throwing one too (Generated; R9-B1).
        this.mir = this.BuildMir();
        return this.Analysis;
    }

    /// <summary>Records the diagnostics of the latest <see cref="Analyze"/>: its rows, through Core's publisher (Compilation.Publication.cs;
    /// DECISIONS R5-M184, R5-M185).</summary>
    public void ReportAnalysis()
    {
        if (!CompilerThread.IsCurrent)
        {
            CompilerThread.Run(this, static s =>
            {
                s.ReportAnalysis();
                return default(ValueTuple);
            });
            return;
        }

        this.PublishMirRecords();
    }

    /// <summary>Collects the checked set of an analyzed program (SPEC 8.15.3: its layout obligations are judged at check), whose records join
    /// the program's report, by the collector's round rule (PLAN G951): a run that missed (its set holds no version) built bodies the
    /// analyses' one stepping path steps before the next run, until a run is clean.</summary>
    /// <param name="analysis">The analysis that ran on the program.</param>
    /// <param name="collector">The collector.</param>
    /// <param name="program">The program.</param>
    /// <param name="rootModules">The root range of both.</param>
    /// <returns>The checked set.</returns>
    internal static Backend.CheckedSet Collect(Mir.MirAnalysis analysis, Backend.InstanceCollector collector, Mir.MirProgram program, int rootModules)
    {
        var set = collector.Run(program, rootModules);
        for (; set.Version != program.Version; set = collector.Run(program, rootModules))
        {
            analysis.Step(program, rootModules);
        }

        return set;
    }

    /// <summary>Checks the established inputs (SPEC 23.2.2): the one phase sequence of a check, which every command and the check service
    /// run through <c>Project</c>. Final Binding, the startup check (a test build's test startup, else the project's output kind; IMPL
    /// 20.6.1) and the analysis each run whatever an earlier one found (SPEC 23.1.2) and publish their records, except over a refused Kimi
    /// library, whose Bind leaves no result: the check then publishes Binding's records and stops (PLAN G949 Stage 2 U2a); an accepted test build
    /// discovers its tests, the catalog its generation reads. It is not a site: it composes sites that hop to a compiler thread
    /// themselves (Bind, BuildMir, ReportAnalysis, TestCatalog.Discover; DECISIONS R4-M178) and runs the startup check and Binding's and
    /// the startup's publication on its caller's thread.</summary>
    /// <returns>Whether the check accepted the program (<see cref="Accepts"/>).</returns>
    internal bool Check()
    {
        this.Bind();
        this.Binding.ReportDiagnostics();
        if (this.Binding.Result == default)
        {
            return false; // A refused library (PLAN G949 Stage 2 U2a, ruling 33): only its refusal is published; no startup check, no MIR.
        }

        _ = this.IsTestBuild ? this.Binding.CheckTestStartup() : this.Binding.CheckStartup(this.Project.ProjectFile.OutputKind);
        this.Binding.ReportStartupDiagnostics();
        this.Analyze();
        this.ReportAnalysis();
        if (!this.Accepts)
        {
            return false;
        }

        if (this.IsTestBuild)
        {
            this.Tests.Discover(this);
        }

        return true;
    }

    /// <summary>Gets whether the latest <see cref="Analyze"/> judged a function's own body without a problem (R6-check H6, H0b §2.1;
    /// GR-1a): its MIR body exists, the analyses ran on it (no validator finding, <c>MirBody.HasFinding</c>, DECISIONS R5-M183, and A2's
    /// table holds it, <c>MirDropTable.Holds</c>: the one stepping path stepped it, so a body a lookup built and nothing stepped is not
    /// judged, PLAN G951), no construction record lies in it (<c>MirBody.HasConstructionRecord</c>) and no merged problem of the report
    /// has a member in it (DECISIONS R5-M185); hover's ownership line reads it. A record made without a body in hand, a collection record or
    /// a module's broken HIR contract, is outside it.</summary>
    /// <param name="function">The function's symbol.</param>
    /// <returns>Whether its body was judged without a problem; false before an analysis, after invalidation and without a body.</returns>
    internal bool JudgedWithoutProblem(BindingSymbol function)
    {
        if (this.mir is not { } program || program[new(function, Mir.MirBodyKind.Fn)] is not { HasFinding: false, HasConstructionRecord: false } body
            || !program.Drops.Holds(body.Ordinal))
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
        => this.ModuleOf(source).Syntax.Nodes[source.Id]!;

    /// <summary>Analyzes the MIR of the latest Binding pass that <see cref="ConstructMir"/> builds and validates, once per pass, and
    /// collects its checked set after the analyses, whatever they found (K package 4.3).</summary>
    /// <returns>The program, with its construction records, validator findings and analysis problems.</returns>
    internal Mir.MirProgram BuildMir()
    {
        if (!CompilerThread.IsCurrent)
        {
            return CompilerThread.Run(this, static s => s.BuildMir());
        }

        var analysis = this.mirAnalysis ??= new(this.Types);
        var program = analysis.Run(this.ConstructMir(), this.SourceModules.Length);
        Collect(analysis, this.instances ??= new(this.Types, this.Layout), program, this.SourceModules.Length);
        program.Enter(Mir.MirProgram.Phase.Sealed); // Later lookups read only (PLAN G951).
        return program;
    }

    /// <summary>Builds and validates the MIR of the latest Binding pass, once per pass: the first step of <see cref="BuildMir"/>, and the
    /// work <c>--compiler-pipeline</c>'s stage 3 times (R6-check H0 D-c5). The root range is the source modules' (PLAN G951 U2, DECISIONS
    /// R9-M9): their bodies and the library's default templates; a library body is built where a lookup of the version demands it.</summary>
    /// <returns>The program, with its construction records and validator findings.</returns>
    internal Mir.MirProgram ConstructMir() => (this.mirBuilder ??= new(this)).Build(this.SourceModules.Length);

    internal void InvalidateMir() => this.mir = null;
}

/// <summary>The summary of a compilation's analysis.</summary>
/// <param name="IsVerified">Whether the MIR of the version exists and no analysis row is an Error.</param>
/// <param name="ErrorCount">The number of analysis rows of Error severity.</param>
public readonly record struct AnalysisResult(bool IsVerified, int ErrorCount);

/// <summary>A problem of an analysis (plan decision 7): its code and its subject.</summary>
/// <param name="Code">The code.</param>
/// <param name="Subject">The syntax the problem is about.</param>
internal readonly record struct Problem(DiagnosticCode Code, SourceRef Subject);
