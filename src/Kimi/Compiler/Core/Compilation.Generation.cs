// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the one generation entry of a compilation and its gate (R5 plan C0a, C0b1, package C 3.8, S2-S4 X17; DECISIONS R5-M64, R5-M65).
// Input: the latest analysis; callers generate an accepted program version (Project and the test command, after check).
// Output: the module text, written to the caller's writer only when generation succeeds, never in part; otherwise the generation
// records in the Emission partition: ModuleCodegen's, gate step 2's refusals (an inexact body's A1 or A2 code at its subject, an arena
// skip's Unsupported_Kd until H3; DECISIONS R5-M96, R5-M146; a form the backend does not generate is the capability table's gap at
// check) and InternalInvariant_Kd at a broken input contract, each keyed by its location (a record carries no fact; DECISIONS R5-M65),
// and, for a module neither written nor explained, InternalInvariant_Kd at the project.
// Gate, step 1 (PD12): no MIR records of the version (construction records, validator findings and the checked set's records, on every
// pipeline; DECISIONS R5-M156). They are analysis Errors that check publishes (Compilation.Pipeline.cs), so the refusal adds none. MIR
// code generation then needs the deciding analysis's acceptance (Accepts, SPEC 23.2.2, which Project's check reads too).
// Gate, step 2 (PD12; DECISIONS R5-M96): applied as each body item is written (FunctionCodegen.Write): a body A1 or A2 left inexact
// (MirDropTable.Incomplete, R4-M102) is not written and its problem's code is recorded at its subject, every other item still written so
// its gaps are recorded too, and no module (package C 3.8, 3.9); until H5, which decides acceptance by MIR.
// Generated (R5 K11, K package 4.10, DECISIONS R5-M77): MIR code generation writes exactly the checked set's items the output requires, the
// closure of its output roots over runtime targets (CheckedSet.Mark): an Application's startup body, a test build's discovered tests (the
// catalog Project discovers after acceptance, never discovered again here) or a Library's every root but the body of a closed virtual or
// override whose slot Applies refutes and a constructor whose Type has no layout (InstanceCollector.Generates, checked only, R4-M28,
// R5-M149), each a declaration's own body (no Closure's or body-declared function's, which their makers reach, R5-M153); a root without
// an item or with an undecided slot is a broken contract, as is a test build whose catalog was not discovered for the version generated
// (its version stamp; PLAN G157 (15), DECISIONS R5-M136): its roots and its verification sites' numbers are that discovery's.
// Invariants: every product generation request (EmissionArtifacts.Publish, and through Project.PublishArtifacts kimi build and kimi test)
// and the out-string adapters pass this gate, and none runs the old generator: MIR code generation is the one code generator since G4
// (DECISIONS R5-M160). Outside the gate until G7 deletes LlvmEmitter, and with it Compilation.Emission: its direct readers in
// src/Benchmark (two stage measurements of the old generator; the pipeline benchmark generates through this entry, R5-M94) run the old
// generator without the gate; no test reads it since G5 (DECISIONS R5-M161). One ModuleCodegen per compilation keeps MIR code
// generation's storage.
// Must not: decide acceptance again, return failure text from new code (the adapters' text is transitional) or fall back to the old
// generator.

using System.Runtime.InteropServices;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public partial class Compilation
{
    private readonly List<int> outputRoots = []; // The output roots of the latest MIR code generation (R5 K11).
    private Backend.ModuleCodegen? moduleCodegen; // MIR code generation's pooled storage (R5 C0b1).

    /// <summary>Gets a value indicating whether the latest check accepted the program (SPEC 23.2.2): final Binding, the startup selection
    /// and the analysis complete without errors, and no Error in any front-end partition (their error state, never the displayed list).</summary>
    internal bool Accepts => this.Binding.Result.IsComplete && this.Binding.Startup.IsComplete && this.Analysis.IsVerified &&
        !this.Diagnostics.HasErrorsThrough(DiagnosticPartition.Ownership);

    /// <summary>Gets the version of the latest MIR program (0: none), which a test build's discovery stamps on its catalog
    /// (TestCatalog.Version) and MIR code generation requires of it.</summary>
    internal ulong ProgramVersion => this.mir?.Version ?? 0;

    /// <summary>Writes inspection IR after checking the latest analysis; it does not certify a published artifact or native execution.</summary>
    /// <param name="writer">The caller-owned output.</param>
    /// <param name="failure">The failed generation obligation, if any.</param>
    /// <returns>Whether checked IR was written.</returns>
    [Transitional("R6-gen", Reason = "G9b moves the test callers to the records of Generate.")]
    public bool WriteIr(TextWriter writer, out string? failure)
    {
        var written = this.Generate(writer);
        failure = written ? null : this.MirFailure();
        return written;
    }

    /// <summary>Checks that the latest analysis can be generated.</summary>
    /// <param name="failure">The failed generation obligation, if any.</param>
    /// <returns>Whether generation can proceed.</returns>
    [Transitional("R6-gen", Reason = "G9b moves the test callers to the records of Generate.")]
    public bool Validate(out string? failure) => this.WriteIr(TextWriter.Null, out failure);

    /// <summary>Generates the module of the latest analysis: the gate, then MIR code generation (R5 C0b1, K11; DECISIONS R5-M65, R5-M77)
    /// of an accepted program, ModuleCodegen over the Generated items of its checked set, which writes the complete module or records why
    /// not in the Emission partition.</summary>
    /// <param name="writer">The caller-owned output, written only when generation succeeds.</param>
    /// <returns>Whether the module was written.</returns>
    /// <remarks>A program check did not accept adds no record (check reported why); an accepted one without its MIR, its checked set or its
    /// output roots, or a module neither written nor explained by a record, is a broken contract. A record is a code without facts
    /// (Unsupported_Kd, location-only by DIAGNOSTICS rule 3, gate step 2's refusal, an A1 or A2 code of no argument (DECISIONS R5-M96), or
    /// InternalInvariant_Kd), so its location is its subject (DIAGNOSTICS 4.1): the records of two nodes at one location, such as the
    /// siblings an expansion synthesizes with its span, are one problem.</remarks>
    internal bool Generate(TextWriter writer)
    {
        if (this.MirRecords(null) > 0)
        {
            return false;
        }

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

        if (!this.OutputRoots(program, collector))
        {
            return false;
        }

        var roots = CollectionsMarshal.AsSpan(this.outputRoots);
        collector.Set.Mark(roots);
        var generator = this.moduleCodegen ??= new(this.Layout, this.Locations, this.Types, this.Binding.LibraryImports, this.Tests);
        if (generator.Write(program, collector, this.Binding.Startup, roots, writer))
        {
            return true;
        }

        if (generator.Records.Count == 0)
        {
            this.Diagnostics.Report(DiagnosticPartition.Emission, DiagnosticCode.InternalInvariant_Kd, this.Project.FilePath);
        }

        foreach (var record in generator.Records)
        {
            this.ReportLocation(record.Code, record.Subject);
        }

        return false;
    }

    // A code generation record keyed by its location (DECISIONS R5-M65).
    private void ReportLocation(DiagnosticCode code, SourceRef subject)
    {
        var node = this.SyntaxOf(subject);
        node.DiagnosticCollection?.ReportLocation(DiagnosticRequirement.Emission, node.Span, code, node.CodeContext.SourceDocument);
    }

    // K package 4.10: the items of the output roots, an Application's startup body, a test build's discovered tests or a Library's every
    // root the collector generates; false, recorded, when one has no item or a test build's catalog is another version's (at the project) or
    // a slot is undecided (at the root's site, as the collector records it at a VTable's).
    private bool OutputRoots(Mir.MirProgram program, Backend.InstanceCollector collector)
    {
        var (roots, set) = (this.outputRoots, collector.Set);
        roots.Clear();
        var startup = this.Binding.Startup;
        if (startup.Kind == StartupKind.Test)
        {
            if (this.Tests.Version != program.Version)
            {
                this.Diagnostics.Report(DiagnosticPartition.Emission, DiagnosticCode.InternalInvariant_Kd, this.Project.FilePath);
                return false;
            }

            foreach (var test in this.Tests.Cases)
            {
                roots.Add(set.Find(new(Backend.MonoKind.Body, new(new(test.Symbol, Mir.MirBodyKind.Test), null), null, null, 0, null)));
            }
        }
        else if (startup.OutputKind == OutputKind.Library)
        {
            for (var root = 0; root < set.RootCount; root++)
            {
                if (collector.Generates(program, root) is not { } generates)
                {
                    this.ReportLocation(DiagnosticCode.InternalInvariant_Kd, set.Items[root].Site);
                    return false;
                }

                if (generates)
                {
                    roots.Add(root);
                }
            }
        }
        else
        {
            var body = new Mir.MirDefKey(startup.Kind == StartupKind.Explicit ? startup.Symbol : null, Mir.MirBodyKind.Fn);
            roots.Add(set.Find(new(Backend.MonoKind.Body, new(body, null), null, null, 0, null)));
        }

        if (roots.Contains(-1))
        {
            this.Diagnostics.Report(DiagnosticPartition.Emission, DiagnosticCode.InternalInvariant_Kd, this.Project.FilePath);
            return false;
        }

        return true;
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
