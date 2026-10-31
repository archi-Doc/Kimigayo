// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the public entry path for post-Binding phases and their Problem records.
// Input: a prepared compilation. Output: analysis, the validated and analyzed MIR with its checked set, and diagnostics, the same on every
// pipeline (Compilation.Switch.cs; DECISIONS R5-M156); generation has its own entry (Compilation.Generation.cs, R5 C0a).
// Until R6 the legacy path decides acceptance; the MIR's construction records, validator findings and every record of its checked set join
// it through the same Problem publication (SPEC 8.15.3, 23.2.2; DECISIONS R5-M156); its analysis problems stay unpublished (R4-M1, until
// H3). Observers of Analyzed (R4-M17) are read-only; no compiler code subscribes.
// Must not expose a second analysis/generation path or re-decide phase-owned facts.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The analysis entry points (plan 12.1, 13.2), the one way to run and read the analyses after Binding and startup (generation has its own
// entry, Compilation.Generation.cs): until R6 the old path decides; on every pipeline the MIR is built, validated, analyzed beside the old
// path and collected, and its construction records, validator findings and checked set's records are analysis Errors (DECISIONS R5-M156;
// its analysis problems stay unpublished, R4-M1).
public partial class Compilation
{
    private readonly List<Problem> problems = [];
    private Mir.MirBuilder? mirBuilder;
    private Mir.MirAnalysis? mirAnalysis;
    private Backend.InstanceCollector? instances;
    private Backend.CheckedSet? collected; // The checked set of the latest Analyze; null before one and after invalidation.
    private Mir.MirProgram? mir; // The MIR of the latest Analyze; null before one and after invalidation.

    /// <summary>Occurs at the end of every <see cref="Analyze"/>, on both pipelines, when the old path's records and the MIR's of the
    /// version are final: the one observation point of the difference ledger (R4-M17). Observers only read.</summary>
    [Transitional("R6", Reason = "The difference ledger compares the old path with the MIR until R6 deletes the old path.")]
    internal static event Action<Compilation>? Analyzed;

    /// <summary>Gets the result of the latest <see cref="Analyze"/>.</summary>
    public AnalysisResult Analysis
    {
        get
        {
            var result = this.Ownership.Result;
            var records = this.MirRecords(null);
            return new(result.IsVerified && records == 0, result.BodyCount, result.ErrorCount + records);
        }
    }

    /// <summary>Gets the checked set of the latest <see cref="Analyze"/> or <see cref="BuildMir"/>, on every pipeline (SPEC 8.15.3), else
    /// null.</summary>
    internal Backend.CheckedSet? Instances => this.instances?.Set;

    /// <summary>Gets the problems of the latest <see cref="Analyze"/>: the old path's in its order, then the MIR's records. The list is
    /// reused by the next read.</summary>
    internal IReadOnlyList<Problem> Problems
    {
        get
        {
            this.problems.Clear();
            var issues = this.Ownership.Issues;
            for (var i = 0; i < issues.Count; i++)
            {
                this.problems.Add(new(issues[i].Code, SourceRef.Of(issues[i].Source)));
            }

            this.MirRecords(this.problems);
            return this.problems;
        }
    }

    /// <summary>Analyzes the bodies after final Binding and the startup check (P6–P9). The MIR is built, validated, analyzed and its checked
    /// set collected first, on every pipeline (SPEC 8.15.3; DECISIONS R5-M156); then <see cref="Analyzed"/> occurs.</summary>
    /// <returns>The analysis result.</returns>
    public AnalysisResult Analyze()
    {
        this.mir = this.BuildMir();
        this.Ownership.Analyze();
        Analyzed?.Invoke(this);
        return this.Analysis;
    }

    /// <summary>Records the diagnostics of the latest <see cref="Analyze"/>: control flow, the MIR's records in the control-flow
    /// partition, then ownership. A MIR record is a code without facts, so it is keyed by its location (plan 6.2, DIAGNOSTICS §4.1,
    /// DECISIONS R4-M71): the records of two nodes at one location, such as the siblings a <c>for</c> expansion synthesizes with its span,
    /// are one problem.</summary>
    public void ReportAnalysis()
    {
        this.Ownership.ControlFlow!.ReportDiagnostics();
        this.problems.Clear();
        this.MirRecords(this.problems);
        foreach (var problem in this.problems)
        {
            var node = this.SyntaxOf(problem.Subject);
            node.DiagnosticCollection?.ReportLocation(DiagnosticRequirement.ControlFlow, node.Span, problem.Code, node.CodeContext.SourceDocument);
        }

        this.Ownership.ReportDiagnostics();
    }

    /// <summary>Gets the syntax a <see cref="SourceRef"/> of this compilation names.</summary>
    /// <param name="source">The reference.</param>
    /// <returns>The node.</returns>
    internal Koto SyntaxOf(SourceRef source)
        => (source.Module < this.SourceModules.Length ? this.SourceModules[source.Module] : this.Library.Kotonoha).Syntax.Nodes[source.Id]!;

    /// <summary>Builds, validates and analyzes the MIR of the latest Binding pass, once per pass (R4: beside the old path, which still
    /// decides), and collects its checked set after the analyses, whatever they found (K package 4.3).</summary>
    /// <returns>The program, with its construction records, validator findings and analysis problems.</returns>
    internal Mir.MirProgram BuildMir()
    {
        var program = (this.mirAnalysis ??= new(this.Types)).Run((this.mirBuilder ??= new(this)).Build(), this.SourceModules.Length);
        this.Collect(program);
        return program;
    }

    internal void InvalidateOwnership()
    {
        this.ownership?.Invalidate();
        this.mir = null;
        this.collected = null;
    }

    // The checked set of a built and validated program, on every pipeline (SPEC 8.15.3: its layout obligations are judged at check), the
    // one the records of the latest Analyze come from.
    private void Collect(Mir.MirProgram program)
        => this.collected = (this.instances ??= new(this.Types, this.Layout)).Run(program, this.SourceModules.Length);

    // The MIR records of the latest Analyze, added to the list when one is given: the construction records and validator findings, the
    // library's as well as the source modules', and every record of the checked set (SPEC 23.2.2: check reports every error of the checked
    // set, its layout obligations among them; DECISIONS R5-M156), on every pipeline.
    private int MirRecords(List<Problem>? into)
    {
        var count = 0;
        if (this.mir is { } program)
        {
            count += program.Records.Count + program.Findings.Count;
            if (into is not null)
            {
                into.AddRange(program.Records);
                foreach (var finding in program.Findings)
                {
                    into.Add(new(DiagnosticCode.InternalInvariant_Kd, finding.Subject));
                }
            }
        }

        if (this.collected is { } set)
        {
            count += set.Records.Count;
            into?.AddRange(set.Records);
        }

        return count;
    }
}

/// <summary>The summary of a compilation's analysis.</summary>
/// <param name="IsVerified">Whether every analyzed body is verified without an analysis Error.</param>
/// <param name="BodyCount">The number of analyzed bodies.</param>
/// <param name="ErrorCount">The number of analysis problems.</param>
public readonly record struct AnalysisResult(bool IsVerified, int BodyCount, int ErrorCount);

/// <summary>A problem of an analysis (plan decision 7): its code and its subject.</summary>
/// <param name="Code">The code.</param>
/// <param name="Subject">The syntax the problem is about.</param>
internal readonly record struct Problem(DiagnosticCode Code, SourceRef Subject);
