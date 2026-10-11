// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The analysis and generation entry points (plan 12.1, 13.2), the one way to run and read the phases after Binding and startup, follow
// the pipeline: until R6 the old path decides under both, and under mir the MIR is built beside it and its records are analysis Errors.
public partial class Compilation
{
    private static CompilerPipeline? defaultPipeline;
    private readonly List<Problem> problems = [];
    private CompilerPipeline pipeline = DefaultPipeline;
    private Mir.MirBuilder? mirBuilder;
    private Mir.MirProgram? mir; // The MIR of the latest Analyze under the mir pipeline; null otherwise and after invalidation.

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

    /// <summary>Gets the compiler identity a check document records (SPEC 23.3.1): the pipeline is part of the effective
    /// configuration, so the mir pipeline of the process marks <see cref="CompilerVersion"/> with <c> [mir]</c>.</summary>
    internal static string CompilerIdentity => DefaultPipeline == CompilerPipeline.Mir ? CompilerVersion + " [mir]" : CompilerVersion;

    /// <summary>Gets the pipeline of a new compilation, read once per process from <c>KIMI_PIPELINE</c> (R3–R5).</summary>
    internal static CompilerPipeline DefaultPipeline => defaultPipeline ??= PipelineOf(Environment.GetEnvironmentVariable("KIMI_PIPELINE"));

    /// <summary>Gets or sets the pipeline the entry points run. Changing it revokes prior analysis.</summary>
    internal CompilerPipeline Pipeline
    {
        get => this.pipeline;
        set
        {
            if (this.pipeline != value)
            {
                this.pipeline = value;
                this.InvalidateSourceAnalysis();
            }
        }
    }

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

    /// <summary>Analyzes the bodies after final Binding and the startup check (P6–P9). Under mir the MIR is built and validated first.</summary>
    /// <returns>The analysis result.</returns>
    public AnalysisResult Analyze()
    {
        this.mir = this.pipeline == CompilerPipeline.Mir ? this.BuildMir() : null;
        this.Ownership.Analyze();
        return this.Analysis;
    }

    /// <summary>Records the diagnostics of the latest <see cref="Analyze"/>: control flow, the MIR's records in the control-flow
    /// partition, each merged by location (plan 6.2), then ownership.</summary>
    public void ReportAnalysis()
    {
        this.Ownership.ControlFlow!.ReportDiagnostics();
        this.problems.Clear();
        this.MirRecords(this.problems);
        foreach (var problem in this.problems)
        {
            this.SyntaxOf(problem.Subject).Report(DiagnosticRequirement.ControlFlow, problem.Code);
        }

        this.Ownership.ReportDiagnostics();
    }

    /// <summary>Writes inspection IR after checking the latest analysis; it does not certify a published artifact or native execution.</summary>
    /// <param name="writer">The caller-owned output.</param>
    /// <param name="failure">The failed generation obligation, if any.</param>
    /// <returns>Whether checked IR was written.</returns>
    public bool WriteIr(TextWriter writer, out string? failure)
        => this.Generates(out failure) && this.Emission.WriteIr(writer, out failure);

    /// <summary>Checks that the latest analysis can be generated.</summary>
    /// <param name="failure">The failed generation obligation, if any.</param>
    /// <returns>Whether generation can proceed.</returns>
    public bool Validate(out string? failure)
        => this.Generates(out failure) && this.Emission.Validate(out failure);

    /// <summary>Reads a <c>KIMI_PIPELINE</c> value: <c>mir</c>, or <c>legacy</c> or unset for the old path alone.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The pipeline.</returns>
    /// <exception cref="InvalidOperationException">Any other value: a configuration error, never a silent fallback.</exception>
    internal static CompilerPipeline PipelineOf(string? value) => value switch
    {
        null or "" or "legacy" => CompilerPipeline.Legacy,
        "mir" => CompilerPipeline.Mir,
        _ => throw new InvalidOperationException($"KIMI_PIPELINE must be mir or legacy, not '{value}'."),
    };

    /// <summary>Gets the syntax a <see cref="SourceRef"/> of this compilation names.</summary>
    /// <param name="source">The reference.</param>
    /// <returns>The node.</returns>
    internal Koto SyntaxOf(SourceRef source)
        => (source.Module < this.SourceModules.Length ? this.SourceModules[source.Module] : this.Library.Kotonoha).Syntax.Nodes[source.Id]!;

    /// <summary>Builds and validates the MIR of the latest Binding pass, once per pass (R3: beside the old path, which still decides).</summary>
    /// <returns>The program, with its construction records and validator findings.</returns>
    internal Mir.MirProgram BuildMir() => (this.mirBuilder ??= new(this)).Build();

    internal void InvalidateOwnership()
    {
        this.ownership?.Invalidate();
        this.mir = null;
    }

    // Generation, like acceptance, requires an analysis without Errors; the old path checks its own.
    private bool Generates(out string? failure)
    {
        var records = this.MirRecords(null);
        failure = records == 0 ? null : $"Generation requires an analysis without errors; the MIR has {records} Unsupported or invariant records.";
        return records == 0;
    }

    // The MIR records the mir pipeline publishes, added to the list when one is given: construction records and validator findings, the
    // library's as well as the source modules'.
    private int MirRecords(List<Problem>? into)
    {
        if (this.mir is not { } program)
        {
            return 0;
        }

        if (into is not null)
        {
            into.AddRange(program.Records);
            foreach (var finding in program.Findings)
            {
                into.Add(new(DiagnosticCode.InternalInvariant_Kd, finding.Subject));
            }
        }

        return program.Records.Count + program.Findings.Count;
    }
}

/// <summary>The pipeline the entry points of a <see cref="Compilation"/> run (plan 12.1; R6 removes the switch).</summary>
internal enum CompilerPipeline : byte
{
    /// <summary>The old Analysis and Emission path alone.</summary>
    Legacy,

    /// <summary>The MIR built and validated beside the old path, which still decides.</summary>
    Mir,
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
