// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the Transitional pipeline switch: the pipeline (plan 12.1) a compilation runs, and the compiler identity the process's non-default
// pipeline marks (SPEC 23.2.1). Every pipeline analyzes, publishes and generates alike (DECISIONS R5-M156; MIR code generation, the one
// code generator since R6-gen's G4, DECISIONS R5-M160), so the pipeline selects only the difference ledger's observation (tests), the
// identity mark and the test manifest entry.
// Input: KIMI_PIPELINE, read once per process. Output: the process default (DefaultPipeline), a compilation's selection (Pipeline) and
// CompilerIdentity.
// Invariants: the type initializer reads the variable once, so every thread gets the one value the environment names (PLAN G199);
// changing the selection revokes prior analysis.
// Must not: read the environment again, fall back silently from an invalid value, or decide what a selection runs (the entries do).
// Removal (DECISIONS R5-M152): H12.

namespace Kimi.Compiler;

public partial class Compilation
{
    /// <summary>The pipeline of a new compilation, read once per process from <c>KIMI_PIPELINE</c> (R3–R5) by the type initializer, which
    /// the runtime runs once, so every thread reads the one value (a Nullable set on first read could be read as its default by a thread
    /// racing the first, PLAN G199).</summary>
    [Transitional("R6", Reason = "The legacy/MIR selection disappears when R6 makes MIR the only pipeline.")]
    internal static readonly CompilerPipeline DefaultPipeline = PipelineOf(Environment.GetEnvironmentVariable("KIMI_PIPELINE"));

    [Transitional("R6", Reason = "The legacy/MIR selection disappears when R6 makes MIR the only pipeline.")]
    private CompilerPipeline pipeline = DefaultPipeline;

    /// <summary>Gets the compiler identity a check document records (SPEC 23.2.1): the pipeline is part of the effective configuration, so
    /// the process's non-default pipeline marks <see cref="CompilerVersion"/> with <c> [mir]</c> (DECISIONS R5-M156, R5-M160).</summary>
    internal static string CompilerIdentity => DefaultPipeline == CompilerPipeline.Mir ? CompilerVersion + " [mir]" : CompilerVersion;

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
