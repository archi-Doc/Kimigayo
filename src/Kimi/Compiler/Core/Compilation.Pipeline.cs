// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the public entry path for post-Binding phases and their Problem records.
// Input: a prepared compilation and its pipeline and code generator choice. Output: analysis, validated and analyzed MIR, under MIR code
// generation its checked set, and diagnostics; generation has its own entry (Compilation.Generation.cs, R5 C0a).
// Until R6 the legacy path decides acceptance; MIR findings join through the same Problem publication. Under MIR code generation (sel,
// R5 PD4) the checked set's records join them (K4b).
// Observers of Analyzed (R4-M17) are read-only; no compiler code subscribes.
// Must not expose a second analysis/generation path or re-decide phase-owned facts.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The analysis entry points (plan 12.1, 13.2), the one way to run and read the analyses after Binding and startup (generation has its own
// entry, Compilation.Generation.cs), follow the pipeline: until R6 the old path decides under both, and under mir the MIR is built,
// validated and analyzed beside it and its construction records and validator findings are analysis Errors (its analysis problems stay
// unpublished, R4-M1).
public partial class Compilation
{
    [Transitional("R6", Reason = "The legacy/MIR selection disappears when R6 makes MIR the only pipeline.")]
    private static CompilerPipeline? defaultPipeline;
    [Transitional("R6-gen", Reason = "G4 removes the code generator selection once MIR code generation is the only one.")]
    private static CompilerCodegen? defaultCodegen;
    private readonly List<Problem> problems = [];
    [Transitional("R6", Reason = "The legacy/MIR selection disappears when R6 makes MIR the only pipeline.")]
    private CompilerPipeline pipeline = DefaultPipeline;
    [Transitional("R6-gen", Reason = "G4 removes the code generator selection once MIR code generation is the only one.")]
    private CompilerCodegen codegen = DefaultCodegen;
    private Mir.MirBuilder? mirBuilder;
    private Mir.MirAnalysis? mirAnalysis;
    private Backend.InstanceCollector? instances;
    private Mir.MirProgram? mir; // The MIR of the latest Analyze under the mir pipeline; null otherwise and after invalidation.

    /// <summary>Occurs at the end of every <see cref="Analyze"/>, on both pipelines, when the old path's records and, under mir, the
    /// MIR's of the version are final: the one observation point of the difference ledger (R4-M17). Observers only read.</summary>
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

    /// <summary>Gets the compiler identity a check document records (SPEC 23.2.1): the pipeline and the code generator are part of the
    /// effective configuration, so the mir pipeline of the process marks <see cref="CompilerVersion"/> with <c> [mir]</c>, and MIR code
    /// generation with <c> [mir, codegen mir]</c>.</summary>
    internal static string CompilerIdentity => DefaultPipeline != CompilerPipeline.Mir ? CompilerVersion
        : DefaultCodegen == CompilerCodegen.Mir ? CompilerVersion + " [mir, codegen mir]" : CompilerVersion + " [mir]";

    /// <summary>Gets the pipeline of a new compilation, read once per process from <c>KIMI_PIPELINE</c> (R3–R5).</summary>
    [Transitional("R6", Reason = "The legacy/MIR selection disappears when R6 makes MIR the only pipeline.")]
    internal static CompilerPipeline DefaultPipeline => defaultPipeline ??= PipelineOf(Environment.GetEnvironmentVariable("KIMI_PIPELINE"));

    /// <summary>Gets the code generator of a new compilation, read once per process from <c>KIMI_CODEGEN</c> (R5 PD4).</summary>
    [Transitional("R6-gen", Reason = "G4 removes the code generator selection once MIR code generation is the only one.")]
    internal static CompilerCodegen DefaultCodegen => defaultCodegen ??= CodegenOf(DefaultPipeline, Environment.GetEnvironmentVariable("KIMI_CODEGEN"));

    /// <summary>Gets or sets the pipeline the entry points run; the code generator follows it, the process default's under mir and the
    /// old one otherwise. Changing it revokes prior analysis.</summary>
    [Transitional("R6", Reason = "The legacy/MIR selection disappears when R6 makes MIR the only pipeline.")]
    internal CompilerPipeline Pipeline
    {
        get => this.pipeline;
        set
        {
            if (this.pipeline != value)
            {
                this.pipeline = value;
                this.codegen = value == CompilerPipeline.Mir ? DefaultCodegen : CompilerCodegen.Legacy;
                this.InvalidateSourceAnalysis();
            }
        }
    }

    /// <summary>Gets or sets the code generator; MIR code generation needs the mir pipeline (the pair is "sel"). Changing it revokes prior
    /// analysis.</summary>
    /// <exception cref="InvalidOperationException">MIR code generation under the legacy pipeline.</exception>
    [Transitional("R6-gen", Reason = "G4 removes the code generator selection once MIR code generation is the only one.")]
    internal CompilerCodegen Codegen
    {
        get => this.codegen;
        set
        {
            if (value == CompilerCodegen.Mir && this.pipeline != CompilerPipeline.Mir)
            {
                throw new InvalidOperationException("MIR code generation needs the mir pipeline (R5 PD4).");
            }

            if (this.codegen != value)
            {
                this.codegen = value;
                this.InvalidateSourceAnalysis();
            }
        }
    }

    /// <summary>Gets the checked set of the latest <see cref="BuildMir"/> under MIR code generation, else null.</summary>
    internal Backend.CheckedSet? Instances => this.codegen == CompilerCodegen.Mir ? this.instances?.Set : null;

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

    /// <summary>Analyzes the bodies after final Binding and the startup check (P6–P9). Under mir the MIR is built, validated and analyzed
    /// first; then <see cref="Analyzed"/> occurs.</summary>
    /// <returns>The analysis result.</returns>
    public AnalysisResult Analyze()
    {
        this.mir = this.pipeline == CompilerPipeline.Mir ? this.BuildMir() : null;
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

    /// <summary>Reads a <c>KIMI_PIPELINE</c> value: <c>mir</c>, or <c>legacy</c> or unset for the old path alone.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The pipeline.</returns>
    /// <exception cref="InvalidOperationException">Any other value: a configuration error, never a silent fallback.</exception>
    [Transitional("R6", Reason = "The legacy/MIR selection disappears when R6 makes MIR the only pipeline.")]
    internal static CompilerPipeline PipelineOf(string? value) => value switch
    {
        null or "" or "legacy" => CompilerPipeline.Legacy,
        "mir" => CompilerPipeline.Mir,
        _ => throw new InvalidOperationException($"KIMI_PIPELINE must be mir or legacy, not '{value}'."),
    };

    /// <summary>Reads a <c>KIMI_CODEGEN</c> value: <c>legacy</c> or unset for the old generator, or <c>mir</c>, valid only with
    /// <c>KIMI_PIPELINE=mir</c> until G2 (R5 PD4).</summary>
    /// <param name="pipeline">The pipeline of the process.</param>
    /// <param name="value">The value.</param>
    /// <returns>The code generator.</returns>
    /// <exception cref="InvalidOperationException">Any other value or combination: a configuration error, never a silent fallback.</exception>
    [Transitional("R6-gen", Reason = "G4 removes the code generator selection once MIR code generation is the only one.")]
    internal static CompilerCodegen CodegenOf(CompilerPipeline pipeline, string? value) => value switch
    {
        null or "" or "legacy" => CompilerCodegen.Legacy,
        "mir" when pipeline == CompilerPipeline.Mir => CompilerCodegen.Mir,
        _ => throw new InvalidOperationException($"KIMI_CODEGEN must be legacy, or mir with KIMI_PIPELINE=mir, not '{value}'."),
    };

    /// <summary>Gets the syntax a <see cref="SourceRef"/> of this compilation names.</summary>
    /// <param name="source">The reference.</param>
    /// <returns>The node.</returns>
    internal Koto SyntaxOf(SourceRef source)
        => (source.Module < this.SourceModules.Length ? this.SourceModules[source.Module] : this.Library.Kotonoha).Syntax.Nodes[source.Id]!;

    /// <summary>Builds, validates and analyzes the MIR of the latest Binding pass, once per pass (R4: beside the old path, which still
    /// decides), and under MIR code generation collects its checked set after the analyses, whatever they found (K package 4.3).</summary>
    /// <returns>The program, with its construction records, validator findings and analysis problems.</returns>
    internal Mir.MirProgram BuildMir()
    {
        var program = (this.mirAnalysis ??= new(this.Types)).Run((this.mirBuilder ??= new(this)).Build());
        if (this.codegen == CompilerCodegen.Mir)
        {
            (this.instances ??= new(this.Types, this.Layout)).Run(program, this.SourceModules.Length);
        }

        return program;
    }

    internal void InvalidateOwnership()
    {
        this.ownership?.Invalidate();
        this.mir = null;
    }

    // The MIR records the mir pipeline publishes, added to the list when one is given: construction records and validator findings, the
    // library's as well as the source modules', and under MIR code generation the checked set's records of the same version (R5 K4b,
    // DECISIONS R5-M41), which join acceptance like the others.
    private int MirRecords(List<Problem>? into)
    {
        if (this.mir is not { } program)
        {
            return 0;
        }

        var collected = this.Instances is { } set && set.Version == program.Version ? set.Records : null;
        if (into is not null)
        {
            into.AddRange(program.Records);
            foreach (var finding in program.Findings)
            {
                into.Add(new(DiagnosticCode.InternalInvariant_Kd, finding.Subject));
            }

            if (collected is not null)
            {
                into.AddRange(collected);
            }
        }

        return program.Records.Count + program.Findings.Count + (collected?.Count ?? 0);
    }
}

/// <summary>The pipeline the entry points of a <see cref="Compilation"/> run (plan 12.1; R6 removes the switch).</summary>
[Transitional("R6", Reason = "The legacy/MIR selection disappears when R6 makes MIR the only pipeline.")]
internal enum CompilerPipeline : byte
{
    /// <summary>The old Analysis and Emission path alone.</summary>
    Legacy,

    /// <summary>The MIR built, validated and analyzed beside the old path, which still decides.</summary>
    Mir,
}

/// <summary>The code generator of a <see cref="Compilation"/> (R5 PD4; G4 removes the selection).</summary>
[Transitional("R6-gen", Reason = "G4 removes the code generator selection once MIR code generation is the only one.")]
internal enum CompilerCodegen : byte
{
    /// <summary>The old Emission path.</summary>
    Legacy,

    /// <summary>MIR code generation, under the mir pipeline only until G2.</summary>
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
