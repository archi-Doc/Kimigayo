// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the one generation entry of a compilation and its gate (R5 plan C0a, C0b1, package C 3.8, S2-S4 X17; DECISIONS R5-M64, R5-M65).
// Input: the latest analysis; callers generate an accepted program version (Project and the test command, after check).
// Output: the module text, written to the caller's writer only when generation succeeds, never in part; otherwise the generation
// records in the Emission partition: under MIR code generation (sel) ModuleCodegen's, Unsupported_Kd at each location of a code generation
// coverage row not written yet (PD11, until the R5 plan's unit X2) and InternalInvariant_Kd at a broken input contract, each keyed by its
// location (a record carries no fact; DECISIONS R5-M65), and, for a module neither written nor explained, InternalInvariant_Kd at the
// project; under the old generator its own record (its instance's located records, GenerationResourceLimit_Kd or, until G9b,
// GenerationFailed_Kd).
// Gate, step 1 (PD12): no MIR records of the version (construction records, validator findings and, under sel, the checked set's
// records). They are analysis Errors that check publishes (Compilation.Pipeline.cs), so the refusal adds none. MIR code generation then
// needs the deciding analysis's acceptance (Accepts, SPEC 23.2.2, which Project's check reads too); the old generator checks its own
// inputs (LlvmEmitter's CheckInputs).
// Invariants: every product generation request (EmissionArtifacts.Publish, and through Project.PublishArtifacts kimi build and kimi test)
// and the out-string adapters pass this gate, and under sel none of them runs the old generator. Outside the gate until G7 deletes
// LlvmEmitter, and with it Compilation.Emission: its direct readers in tests (TryPrepare inspections and failure facts, which G5 deletes)
// and src/Benchmark (four stage measurements of the old generator) run the old generator without the gate, under sel too. One
// ModuleCodegen per compilation keeps MIR code generation's storage.
// Must not: decide acceptance again, return failure text from new code (the adapters' text is transitional) or fall back to the old
// generator under sel. C4a adds gate step 2 and K11 the Generated mark here.

using Kimi.Diagnostics;

namespace Kimi.Compiler;

public partial class Compilation
{
    private Backend.ModuleCodegen? moduleCodegen; // MIR code generation's pooled storage (R5 C0b1).

    /// <summary>Gets a value indicating whether the latest check accepted the program (SPEC 23.2.2): final Binding, the startup selection
    /// and the analysis complete without errors, and no Error in any front-end partition (their error state, never the displayed list).</summary>
    internal bool Accepts => this.Binding.Result.IsComplete && this.Binding.Startup.IsComplete && this.Analysis.IsVerified &&
        !this.Diagnostics.HasErrorsThrough(DiagnosticPartition.Ownership);

    /// <summary>Writes inspection IR after checking the latest analysis; it does not certify a published artifact or native execution.</summary>
    /// <param name="writer">The caller-owned output.</param>
    /// <param name="failure">The failed generation obligation, if any.</param>
    /// <returns>Whether checked IR was written.</returns>
    [Transitional("R6-gen", Reason = "G9b moves the test callers to the records of Generate.")]
    public bool WriteIr(TextWriter writer, out string? failure)
    {
        if (this.codegen == CompilerCodegen.Mir)
        {
            var written = this.Generate(writer);
            failure = written ? null : this.MirFailure();
            return written;
        }

        failure = this.Refusal();
        return failure is null && this.Emission.WriteIr(writer, out failure);
    }

    /// <summary>Checks that the latest analysis can be generated.</summary>
    /// <param name="failure">The failed generation obligation, if any.</param>
    /// <returns>Whether generation can proceed.</returns>
    [Transitional("R6-gen", Reason = "G9b moves the test callers to the records of Generate.")]
    public bool Validate(out string? failure)
    {
        if (this.codegen == CompilerCodegen.Mir)
        {
            return this.WriteIr(TextWriter.Null, out failure);
        }

        failure = this.Refusal();
        return failure is null && this.Emission.Validate(out failure);
    }

    /// <summary>Generates the module of the latest analysis: the gate, then the code generator, which writes the complete module or records
    /// why not in the Emission partition.</summary>
    /// <param name="writer">The caller-owned output, written only when generation succeeds.</param>
    /// <returns>Whether the module was written.</returns>
    internal bool Generate(TextWriter writer)
    {
        if (this.MirRecords(null) > 0)
        {
            return false;
        }

        if (this.codegen == CompilerCodegen.Mir)
        {
            return this.GenerateMir(writer);
        }

        if (this.Emission.WriteIr(writer, out var failure))
        {
            return true;
        }

        // SPEC 21.3.2: an exceeded mandatory generation limit is a resource diagnostic, not a semantic error.
        this.Emission.ReportFailure(failure);
        return false;
    }

    // MIR code generation (R5 C0b1, DECISIONS R5-M65) of an accepted program: ModuleCodegen over its checked set. A program check did not
    // accept adds no record (check reported why); an accepted one without its MIR or checked set, or a module neither written nor
    // explained by a record, is a broken contract. A record is a code without facts (Unsupported_Kd, location-only by DIAGNOSTICS rule 3, or
    // InternalInvariant_Kd), so its location is its subject (DIAGNOSTICS 4.1): the records of two nodes at one location, such as the
    // siblings an expansion synthesizes with its span, are one problem.
    private bool GenerateMir(TextWriter writer)
    {
        this.Diagnostics.Invalidate(DiagnosticPartition.Emission);
        if (!this.Accepts)
        {
            return false;
        }

        if (this.mir is not { } program || this.instances is not { } collector)
        {
            this.Diagnostics.Report(DiagnosticPartition.Emission, DiagnosticCode.InternalInvariant_Kd, this.Project.FilePath);
            return false;
        }

        var generator = this.moduleCodegen ??= new(this.Layout);
        if (generator.Write(program, collector, this.Binding.Startup, writer))
        {
            return true;
        }

        if (generator.Records.Count == 0)
        {
            this.Diagnostics.Report(DiagnosticPartition.Emission, DiagnosticCode.InternalInvariant_Kd, this.Project.FilePath);
        }

        foreach (var record in generator.Records)
        {
            var node = this.SyntaxOf(record.Subject);
            node.DiagnosticCollection?.ReportLocation(DiagnosticRequirement.Emission, node.Span, record.Code, node.CodeContext.SourceDocument);
        }

        return false;
    }

    // The out-string adapters' text of the gate's step 1.
    [Transitional("R6-gen", Reason = "G9b moves the test callers to the records of Generate.")]
    private string? Refusal()
        => this.MirRecords(null) is > 0 and var records ? $"Generation requires an analysis without errors; the MIR has {records} error records." : null;

    // The out-string adapters' text of a module MIR code generation did not write.
    [Transitional("R6-gen", Reason = "G9b moves the test callers to the records of Generate.")]
    private string MirFailure()
    {
        if (this.Refusal() is { } refusal)
        {
            return refusal;
        }

        return this.Accepts ? "MIR code generation wrote no module: its records are in the Emission partition."
            : "MIR code generation needs an accepted analysis: final Binding, startup and analysis without errors.";
    }
}
