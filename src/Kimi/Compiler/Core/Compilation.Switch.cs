// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the Transitional pipeline switch: the pipeline (plan 12.1) and the code generator (R5 PD4) a compilation runs, two independent
// selections, and the compiler identity the process defaults mark (SPEC 23.2.1).
// Input: KIMI_PIPELINE and KIMI_CODEGEN, read once per process. Output: the process defaults (DefaultPipeline, DefaultCodegen), a
// compilation's selection (Pipeline, Codegen) and CompilerIdentity. The generation entry (Compilation.Generation.cs) follows the code
// generator; every pipeline analyzes and publishes alike (DECISIONS R5-M156), so the pipeline selects only the difference ledger's
// observation (tests), the identity mark and the test manifest entry.
// Invariants: the type initializer reads both variables once, so every thread gets the one pair the environment names (PLAN G199); either
// code generator runs on either pipeline (R5-M156 ends PD4's pairing); changing either selection revokes prior analysis.
// Must not: read the environment again, fall back silently from an invalid value, or decide what a selection runs (the entries do).
// Removal (DECISIONS R5-M152): G4 the code generator selection, H12 the rest.

namespace Kimi.Compiler;

public partial class Compilation
{
    /// <summary>The pipeline of a new compilation, read once per process from <c>KIMI_PIPELINE</c> (R3–R5) by the type initializer, which
    /// the runtime runs once, so every thread reads the one value (a Nullable set on first read could be read as its default by a thread
    /// racing the first, PLAN G199).</summary>
    [Transitional("R6", Reason = "The legacy/MIR selection disappears when R6 makes MIR the only pipeline.")]
    internal static readonly CompilerPipeline DefaultPipeline = PipelineOf(Environment.GetEnvironmentVariable("KIMI_PIPELINE"));

    /// <summary>The code generator of a new compilation, read once per process from <c>KIMI_CODEGEN</c> (R5 PD4) by the type initializer,
    /// as <see cref="DefaultPipeline"/> is.</summary>
    [Transitional("R6-gen", Reason = "G4 removes the code generator selection once MIR code generation is the only one.")]
    internal static readonly CompilerCodegen DefaultCodegen = CodegenOf(Environment.GetEnvironmentVariable("KIMI_CODEGEN"));

    [Transitional("R6", Reason = "The legacy/MIR selection disappears when R6 makes MIR the only pipeline.")]
    private CompilerPipeline pipeline = DefaultPipeline;
    [Transitional("R6-gen", Reason = "G4 removes the code generator selection once MIR code generation is the only one.")]
    private CompilerCodegen codegen = DefaultCodegen;

    /// <summary>Gets the compiler identity a check document records (SPEC 23.2.1): the pipeline and the code generator are part of the
    /// effective configuration, so each non-default selection of the process marks <see cref="CompilerVersion"/>, the pipeline first:
    /// <c> [mir]</c>, <c> [codegen mir]</c> or <c> [mir, codegen mir]</c> (DECISIONS R5-M156).</summary>
    internal static string CompilerIdentity => (DefaultPipeline, DefaultCodegen) switch
    {
        (CompilerPipeline.Legacy, CompilerCodegen.Legacy) => CompilerVersion,
        (CompilerPipeline.Mir, CompilerCodegen.Legacy) => CompilerVersion + " [mir]",
        (CompilerPipeline.Legacy, _) => CompilerVersion + " [codegen mir]",
        _ => CompilerVersion + " [mir, codegen mir]",
    };

    /// <summary>Gets or sets the pipeline; changing it revokes prior analysis.</summary>
    [Transitional("R6", Reason = "The legacy/MIR selection disappears when R6 makes MIR the only pipeline.")]
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

    /// <summary>Gets or sets the code generator, on either pipeline (with the mir pipeline the pair is "sel"); changing it revokes prior
    /// analysis.</summary>
    [Transitional("R6-gen", Reason = "G4 removes the code generator selection once MIR code generation is the only one.")]
    internal CompilerCodegen Codegen
    {
        get => this.codegen;
        set
        {
            if (this.codegen != value)
            {
                this.codegen = value;
                this.InvalidateSourceAnalysis();
            }
        }
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

    /// <summary>Reads a <c>KIMI_CODEGEN</c> value: <c>legacy</c> or unset for the old generator, or <c>mir</c>, on either pipeline
    /// (DECISIONS R5-M156).</summary>
    /// <param name="value">The value.</param>
    /// <returns>The code generator.</returns>
    /// <exception cref="InvalidOperationException">Any other value: a configuration error, never a silent fallback.</exception>
    [Transitional("R6-gen", Reason = "G4 removes the code generator selection once MIR code generation is the only one.")]
    internal static CompilerCodegen CodegenOf(string? value) => value switch
    {
        null or "" or "legacy" => CompilerCodegen.Legacy,
        "mir" => CompilerCodegen.Mir,
        _ => throw new InvalidOperationException($"KIMI_CODEGEN must be mir or legacy, not '{value}'."),
    };
}

/// <summary>The pipeline the entry points of a <see cref="Compilation"/> run (plan 12.1; R6 removes the switch).</summary>
[Transitional("R6", Reason = "The legacy/MIR selection disappears when R6 makes MIR the only pipeline.")]
internal enum CompilerPipeline : byte
{
    /// <summary>The default: the old path decides, and the MIR's records publish beside it (DECISIONS R5-M156).</summary>
    Legacy,

    /// <summary>As <see cref="Legacy"/>, observed by the difference ledger and marked in the compiler identity and the test manifest.</summary>
    Mir,
}

/// <summary>The code generator of a <see cref="Compilation"/> (R5 PD4; G4 removes the selection).</summary>
[Transitional("R6-gen", Reason = "G4 removes the code generator selection once MIR code generation is the only one.")]
internal enum CompilerCodegen : byte
{
    /// <summary>The old Emission path.</summary>
    Legacy,

    /// <summary>MIR code generation, on either pipeline.</summary>
    Mir,
}
