// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the Transitional pipeline switch: the pipeline (plan 12.1) and the code generator (R5 PD4) a compilation runs, and the compiler
// identity the process defaults mark (SPEC 23.2.1).
// Input: KIMI_PIPELINE and KIMI_CODEGEN, read once per process. Output: the process defaults (DefaultPipeline, DefaultCodegen), a
// compilation's selection (Pipeline, Codegen), which the analysis entry (Compilation.Pipeline.cs) and the generation entry
// (Compilation.Generation.cs) follow, and CompilerIdentity.
// Invariants: the type initializer reads both variables once, so every thread gets the one pair the environment names (PLAN G199); MIR
// code generation needs the mir pipeline (R5 PD4); changing either selection revokes prior analysis.
// Must not: read the environment again, fall back silently from an invalid value, or decide what a selection runs (the entries do).
// Removal (DECISIONS R5-M152): G2 drops PD4's pairing, G4 the code generator selection, H12 the rest.

namespace Kimi.Compiler;

public partial class Compilation
{
    /// <summary>The pipeline of a new compilation, read once per process from <c>KIMI_PIPELINE</c> (R3–R5) by the type initializer, which
    /// the runtime runs once, so every thread reads the one value (a Nullable set on first read could be read as its default by a thread
    /// racing the first, PLAN G199).</summary>
    [Transitional("R6", Reason = "The legacy/MIR selection disappears when R6 makes MIR the only pipeline.")]
    internal static readonly CompilerPipeline DefaultPipeline = PipelineOf(Environment.GetEnvironmentVariable("KIMI_PIPELINE"));

    /// <summary>The code generator of a new compilation, read once per process from <c>KIMI_CODEGEN</c> (R5 PD4) after
    /// <see cref="DefaultPipeline"/>, which this file's textual order initializes first.</summary>
    [Transitional("R6-gen", Reason = "G4 removes the code generator selection once MIR code generation is the only one.")]
    internal static readonly CompilerCodegen DefaultCodegen = CodegenOf(DefaultPipeline, Environment.GetEnvironmentVariable("KIMI_CODEGEN"));

    [Transitional("R6", Reason = "The legacy/MIR selection disappears when R6 makes MIR the only pipeline.")]
    private CompilerPipeline pipeline = DefaultPipeline;
    [Transitional("R6-gen", Reason = "G4 removes the code generator selection once MIR code generation is the only one.")]
    private CompilerCodegen codegen = DefaultCodegen;

    /// <summary>Gets the compiler identity a check document records (SPEC 23.2.1): the pipeline and the code generator are part of the
    /// effective configuration, so the mir pipeline of the process marks <see cref="CompilerVersion"/> with <c> [mir]</c>, and MIR code
    /// generation with <c> [mir, codegen mir]</c>.</summary>
    internal static string CompilerIdentity => (DefaultPipeline, DefaultCodegen) switch
    {
        (not CompilerPipeline.Mir, _) => CompilerVersion,
        (_, CompilerCodegen.Mir) => CompilerVersion + " [mir, codegen mir]",
        _ => CompilerVersion + " [mir]",
    };

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
