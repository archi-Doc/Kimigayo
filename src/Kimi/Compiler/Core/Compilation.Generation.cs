// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the one generation entry of a compilation and its gate (R5 plan C0a, package C 3.8, S2-S4 X17; DECISIONS R5-M64).
// Input: the latest analysis; callers generate an accepted program version (Project and the test command, after check).
// Output: the module text, written to the caller's writer only when generation succeeds, never in part; otherwise the generation
// records in the Emission partition: under MIR code generation (sel) one Unsupported_Kd at the body of each output root until C0b1, and
// the old generator's own record (its instance's located records, GenerationResourceLimit_Kd or, until G9b, GenerationFailed_Kd).
// Gate, step 1 (PD12): no MIR records of the version (construction records, validator findings and, under sel, the checked set's
// records). They are analysis Errors that check publishes (Compilation.Pipeline.cs), so the refusal adds none.
// Invariants: every generation request, the out-string adapters included, passes this gate; under sel the old generator never runs.
// Must not: decide acceptance again, return failure text from new code (the adapters' text is transitional) or fall back to the old
// generator under sel. C4a adds gate step 2 and K11 the Generated mark here.

using Kimi.Diagnostics;

namespace Kimi.Compiler;

public partial class Compilation
{
    /// <summary>The failure text of the out-string adapters under MIR code generation (R5 K1 to C0b1).</summary>
    [Transitional("R6-gen", Reason = "C0b1 replaces the refusal with the codegen coverage rows.")]
    private const string CodegenRefusal = "MIR code generation writes no module before R5 C0b1: its output roots are Unsupported.";

    /// <summary>Writes inspection IR after checking the latest analysis; it does not certify a published artifact or native execution.</summary>
    /// <param name="writer">The caller-owned output.</param>
    /// <param name="failure">The failed generation obligation, if any.</param>
    /// <returns>Whether checked IR was written.</returns>
    [Transitional("R6-gen", Reason = "G9b moves the test callers to the records of Generate.")]
    public bool WriteIr(TextWriter writer, out string? failure)
    {
        failure = this.Refusal();
        return failure is null && this.Emission.WriteIr(writer, out failure);
    }

    /// <summary>Checks that the latest analysis can be generated.</summary>
    /// <param name="failure">The failed generation obligation, if any.</param>
    /// <returns>Whether generation can proceed.</returns>
    [Transitional("R6-gen", Reason = "G9b moves the test callers to the records of Generate.")]
    public bool Validate(out string? failure)
    {
        failure = this.Refusal();
        return failure is null && this.Emission.Validate(out failure);
    }

    /// <summary>Generates the module of the latest analysis: the gate, then the code generator, which writes the complete module or records
    /// why not in the Emission partition.</summary>
    /// <param name="writer">The caller-owned output, written only when generation succeeds.</param>
    /// <returns>Whether the module was written.</returns>
    internal bool Generate(TextWriter writer)
    {
        if (!this.Generates(out _))
        {
            return false;
        }

        if (this.Emission.WriteIr(writer, out var failure))
        {
            return true;
        }

        // SPEC 21.3.2: an exceeded mandatory generation limit is a resource diagnostic, not a semantic error.
        this.Emission.ReportFailure(failure);
        return false;
    }

    // The gate: generation, like acceptance, requires an analysis without MIR records (the old path checks its own); under MIR code
    // generation it is then refused.
    private bool Generates(out int records)
    {
        records = this.MirRecords(null);
        return records == 0 && !this.RefusesCodegen();
    }

    // The out-string adapters' text of a refused gate.
    [Transitional("R6-gen", Reason = "G9b moves the test callers to the records of Generate.")]
    private string? Refusal()
        => this.Generates(out var records) ? null
            : records > 0 ? $"Generation requires an analysis without errors; the MIR has {records} error records." : CodegenRefusal;

    /// <summary>Refuses generation under MIR code generation, which writes no module before C0b1 and never falls back to the old generator
    /// (K package 5, PD11's sel exception): one <c>Unsupported_Kd</c> in the Emission partition at the body of each output root, an
    /// Application's startup, a test build's tests or a Library's roots.</summary>
    /// <returns>Whether generation is refused.</returns>
    [Transitional("R6-gen", Reason = "C0b1 replaces the refusal with the codegen coverage rows.")]
    private bool RefusesCodegen()
    {
        if (this.codegen != CompilerCodegen.Mir)
        {
            return false;
        }

        var program = this.BuildMir();
        var set = this.instances!.Set;
        var startup = this.Binding.Startup;
        var entry = new Mir.MirDefKey(startup.Kind == StartupKind.Explicit ? startup.Symbol : null, Mir.MirBodyKind.Fn);
        this.Diagnostics.Invalidate(DiagnosticPartition.Emission);
        foreach (var item in set.Items[..set.RootCount])
        {
            var body = program.Bodies[item.BodyOrdinal];
            if (this.IsTestBuild ? body.Key.Kind == Mir.MirBodyKind.Test : startup.OutputKind == OutputKind.Library || body.Key == entry)
            {
                this.SyntaxOf(body.Source).Report(DiagnosticRequirement.Emission, DiagnosticCode.Unsupported_Kd);
            }
        }

        return true;
    }
}
