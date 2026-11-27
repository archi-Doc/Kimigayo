// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the one generation entry of a compilation and its gate (R5 plan C0a, C0b1, package C 3.8, S2-S4 X17; DECISIONS R5-M64, R5-M65).
// Input: the latest analysis; callers generate an accepted program version (Project and the test command, after check).
// Output: the module text, written to the caller's writer only when generation succeeds, never in part, with what it states for its link
// (Generated: the output kind at the write, the required libraries and the external declarations; R9-B1), which exists only for the
// request that wrote the module, until the next generation request or analysis: a request that writes nothing leaves none, so code
// generation's lists of a failed write (a partial or the previous module's) are never readable, and a read without one is a broken
// contract; otherwise the generation records in the Emission partition: ModuleCodegen's (a form the backend does not generate is the capability table's gap at check) and
// InternalInvariant_Kd at a broken input contract, an inexact body reaching FunctionCodegen.Write among them (IC's contract: every such
// body has a kept Error, so check rejects its program; DECISIONS R5-M96, R5-M185), each keyed by its location (Locate; a record carries
// no fact; DECISIONS R5-M65),
// and, for a module neither written nor explained, InternalInvariant_Kd at the project. A publication failure (settings, native supplies,
// the backend or the file system) is EmissionArtifacts.Publish's own record in the same partition (DECISIONS R5-M165), which Project renders.
// A broken contract checked without a subject throws (a closed table without a row, a missing fact, a framework operation's contract); it
// ends generation unrecorded here, and the command renders it as the Emission result's fault, CheckFaulted_Kd (DIAGNOSTICS rule 9;
// DECISIONS R5-M169), as the front end's.
// Gate (PD12; DECISIONS R5-M185): the acceptance check decided (Accepts, SPEC 23.2.2, which Check reads too); every MIR row of
// Error severity fails Analysis, and check publishes it (Compilation.Publication.cs), so the refusal adds none, and, like every request,
// clears the partition first.
// Generated (R5 K11, K package 4.10, DECISIONS R5-M77): MIR code generation writes exactly the checked set's items the output requires, the
// closure of its output roots over runtime targets (CheckedSet.Mark): an Application's startup body (then, with a platform, its witnesses'
// bodies in PlatformRows' order, IMPL 21.6), a test build's discovered tests (the catalog Check discovers after acceptance, never
// discovered again here) or a Library's every root but the body of a closed virtual or override whose slot Applies refutes and a
// constructor whose Type has no layout (InstanceCollector.Generates, checked only, R4-M28, R5-M149), each a declaration's own body (no
// Closure's or body-declared function's, which their makers reach, R5-M153); a root without an item or with an undecided slot is a broken
// contract, as are a platform's witness count other than PlatformRows' and a test build whose catalog was not discovered for the version
// generated (its version stamp; PLAN G157 (15), DECISIONS R5-M136): its roots and its verification sites' numbers are that discovery's.
// Invariants: every generation request (EmissionArtifacts.Publish, and through Project.PublishArtifacts kimi build and kimi test; tests
// and src/Benchmark's measurements, which read its records) passes this gate, and none runs the old generator: MIR code generation is the
// one code generator since G4 (DECISIONS R5-M160), and R6-gen's G7 deleted the old one (DECISIONS R5-M162). One ModuleCodegen per
// compilation keeps MIR code generation's storage, made for the target's row (TargetProfile.Find); a target without a row reaching
// generation is a broken contract, InternalInvariant_Kd at the project.
// Must not: decide acceptance again, return failure text or fall back to the old generator.

using System.Runtime.InteropServices;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public partial class Compilation
{
    private readonly List<int> outputRoots = []; // The output roots of the latest MIR code generation (R5 K11).
    private Backend.ModuleCodegen? moduleCodegen; // MIR code generation's pooled storage (R5 C0b1).
    private GeneratedModule? generated; // The written module's link facts (Generated): set by the request that wrote it, else none.

    /// <summary>Gets a value indicating whether the latest check accepted the program (SPEC 23.2.2): final Binding, the startup selection
    /// and the analysis complete without errors, and no Error in any front-end partition (their error state, never the displayed list).</summary>
    internal bool Accepts => this.Binding.Result.IsComplete && this.Binding.Startup.IsComplete && this.Analysis.IsVerified &&
        !this.Diagnostics.HasErrorsThrough(DiagnosticPartition.Ownership);

    /// <summary>Gets the version of the latest MIR program (0: none), which a test build's discovery stamps on its catalog
    /// (TestCatalog.Version) and MIR code generation requires of it.</summary>
    internal ulong ProgramVersion => this.mir?.Version ?? 0;

    /// <summary>Gets what the module the latest generation request wrote states for its link (R9-B1): its output kind, the startup
    /// selection's at the write, which chose its output roots (a test build's is an Application), and code generation's required
    /// libraries and external declarations. It exists only after <see cref="Generate"/> returned true, until the next generation request
    /// or analysis; the toolchain's publication is its reader.</summary>
    /// <exception cref="InvalidOperationException">No module was written since the latest generation request or analysis (a broken
    /// contract, never a stale or partial list).</exception>
    internal GeneratedModule Generated => this.generated ?? throw new InvalidOperationException("The latest generation request wrote no module.");

    /// <summary>Generates the module of the latest analysis: the gate, then MIR code generation (R5 C0b1, K11; DECISIONS R5-M65, R5-M77)
    /// of an accepted program, ModuleCodegen over the Generated items of its checked set, which writes the complete module or records why
    /// not in the Emission partition.</summary>
    /// <param name="writer">The caller-owned output, written only when generation succeeds.</param>
    /// <returns>Whether the module was written.</returns>
    /// <remarks>A program check did not accept adds no record (check reported why); an accepted one without its MIR, its checked set or its
    /// output roots, or a module neither written nor explained by a record, is a broken contract. A record is InternalInvariant_Kd, a code
    /// without facts and the only generation record since R6-check H5's unit G (DECISIONS R5-M96's amendment), so its location is its
    /// subject (DIAGNOSTICS 4.1): the records of two nodes at one location, such as the siblings an expansion synthesizes with its span, are
    /// one problem.</remarks>
    internal bool Generate(TextWriter writer)
    {
        if (!CompilerThread.IsCurrent)
        {
            return CompilerThread.Run((this, writer), static s => s.Item1.Generate(s.writer));
        }

        this.Diagnostics.Invalidate(DiagnosticPartition.Emission); // The partition is the latest request's (DECISIONS R5-M165).
        this.generated = null; // So is the written module's result (Generated).
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

        // The target row, whose frame the generator writes (its constructor input: one generator, one triple); a target without a row
        // never reaches generation.
        if (TargetProfile.Find(this.TargetTriple.Value) is not { } row)
        {
            this.Diagnostics.Report(DiagnosticPartition.Emission, DiagnosticCode.InternalInvariant_Kd, this.Project.FilePath);
            return false;
        }

        var roots = CollectionsMarshal.AsSpan(this.outputRoots);
        collector.Set.Mark(roots);
        var generator = this.moduleCodegen ??= new(this.Layout, this.Locations, this.Types, this.Binding.LibraryImports, this.Tests, row);
        if (generator.Write(program, collector, this.Binding.Startup, roots, writer))
        {
            // The lists by reference.
            this.generated = new(this.Binding.Startup.OutputKind, generator.Libraries, generator.Externals, generator.RuntimeImports, generator.Entry);
            return true;
        }

        if (generator.Records.Count == 0)
        {
            this.Diagnostics.Report(DiagnosticPartition.Emission, DiagnosticCode.InternalInvariant_Kd, this.Project.FilePath);
        }

        foreach (var record in generator.Records)
        {
            this.Locate(DiagnosticRequirement.Emission, record.Code, record.Subject);
        }

        return false;
    }

    // K package 4.10: the items of the output roots, an Application's startup body (then, with a platform, its witnesses' in
    // PlatformRows' order, positional: PlatformRoots), a test build's discovered tests or a Library's every root the collector generates;
    // false, recorded, when one has no item or a test build's catalog is another version's (at the project) or a slot is undecided (at the
    // root's site, as the collector records it at a VTable's).
    private bool OutputRoots(Mir.MirProgram program, Backend.InstanceCollector collector)
    {
        var (roots, set) = (this.outputRoots, collector.Set);
        roots.Clear();
        var startup = this.Binding.Startup;
        var platformRoots = true;
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
                    this.Locate(DiagnosticRequirement.Emission, DiagnosticCode.InternalInvariant_Kd, set.Items[root].Site);
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
            platformRoots = startup.Platform is not { } platform || this.PlatformRoots(platform, set);
        }

        if (roots.Contains(-1))
        {
            this.Diagnostics.Report(DiagnosticPartition.Emission, DiagnosticCode.InternalInvariant_Kd, this.Project.FilePath);
            return false;
        }

        return platformRoots;
    }

    // IMPL 21.6: an Application's platform roots, after its startup body, each witness's Body item in PlatformRows' order (Binding's
    // PlatformWitnesses, positional), which ModuleCodegen reads as roots 1 to 6 and its adapters trust. Witness i is paired with
    // row i here, the one place the count is checked. Every witness is a body: an import has no receiver and takes and returns no struct
    // by value (SPEC 22.6), while `start` takes and returns structs by value and the other requirements take a receiver, so no checked
    // program selects a platform with an import witness. A witness count other than PlatformRows' (before any root is added) or a
    // witness without its Body item is a broken Binding contract, InternalInvariant_Kd at the platform struct.
    private bool PlatformRoots(BindingSymbol platform, Backend.CheckedSet set)
    {
        var witnesses = this.Binding.PlatformWitnesses;
        if (witnesses.Count != Backend.PlatformRows.Rows.Length)
        {
            this.Locate(DiagnosticRequirement.Emission, DiagnosticCode.InternalInvariant_Kd, SourceRef.Of(platform.Declaration));
            return false;
        }

        for (var i = 0; i < witnesses.Count; i++)
        {
            var root = set.Find(new(Backend.MonoKind.Body, new(new(witnesses[i], Mir.MirBodyKind.Fn), null), null, null, 0, null));
            if (root < 0)
            {
                this.Locate(DiagnosticRequirement.Emission, DiagnosticCode.InternalInvariant_Kd, SourceRef.Of(platform.Declaration));
                return false;
            }

            this.outputRoots.Add(root);
        }

        return true;
    }
}
