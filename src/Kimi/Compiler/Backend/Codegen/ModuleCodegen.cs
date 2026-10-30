// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// ModuleCodegen (R5 plan C0b1, K11, C3a, C5b, C9b, package C 3.3 and 3.9, design 2 2.3 and 2.12; DECISIONS R5-M65, R5-M77, R5-M89, R5-M104),
// one per Compilation.
// Responsibility: one module of a program version from its checked set: every Generated item in admission order, a body item written by
// FunctionCodegen as __kimi_f<item> under its signature (Abi.Of, internal convention), which this owner gives from the item's own body to
// its definition and every call (Signature), a called #LibraryImport function's C signature and declaration (Import), the output form
// around the output roots (IMPL 21.4.2: an Application's __kimi_start calls its startup root and exits with 0, a Library has no entry, a
// test build's is the test runtime's, which runs the case the runner selects, each test root called in catalog order; DECISIONS R5-M136) and
// the module frame through LlvmModuleText in the old generator's order (header with the pooled constants, runtime, the called imports'
// declarations, overflow declarations, functions, entry, footer); a row per item kind and output form: DropGlue
// (__kimi_drop<item>, written by FunctionCodegen by its Type's row, DropGlue.Row: a string's (R5-M78), a struct's, Tuple's, enum's or fixed
// array's (R5-M95), a Closure environment's (R5-M102), an erased function's (R5-M104), an owning handle's (R5-M112), an Array's (R5-M121), a
// Dictionary's (R5-M124)),
// ClosureErasure and ItemAdapter (__kimi_erase<item>, its entry under its target Function Type's erased signature, its table and its
// heap environment's release, written by FunctionCodegen; R5-M108), the LegacyRow items of the Array rows and of placeEntry (their routines,
// their helpers written before the definitions, ModuleCodegen.Arrays and ModuleCodegen.Dictionaries; R5-M121, R5-M124), the Builtin shims
// (__kimi_builtin<item>, a comparison's, an arithmetic operation's or a built-in formatting's function under its requirement's signature,
// Shim, written by FunctionCodegen.Shims; R5-M126, R5-M130), a user or library formatter's wrappers (__kimi_write<item>, a conversion over
// it; ModuleCodegen.Formatting, R5-M130), the property witnesses (__kimi_witness<item>, a standard property witness's function under its
// key's signature, Shim, written by FunctionCodegen.Shims; R5-M141), K8's ObjectDescriptor items (the module's runtime
// identities, a descriptor's constants, payload destructor and factory through ObjectRuntime, ModuleCodegen.Objects; R5-M110) and VTable
// items, whose text is their descriptors' virtual tables (R5-M114).
// Inputs: the program the generation gate passed (validated, analyzed and accepted); the instance collector's checked set, marked from the
// output roots the generation entry gives (K package 4.10), TypeOf and location rule (K package 4.9); the compilation's Layout and runtime
// location texts (Compilation.Locations); the startup selection; the test catalog, a test build's cases, identity and verification site
// numbers (Compilation.Tests, discovered for the version; DECISIONS R5-M136).
// Output: the complete module written to the caller's writer, or nothing and the Records of why: gate step 2's refusal of a body A1 or A2
// left inexact (its problem's code at its subject, an arena skip's Unsupported_Kd until H3, FunctionCodegen.Write; DECISIONS R5-M96,
// R5-M146), InternalInvariant_Kd at a broken input contract; no record only for a broken contract
// without a subject (an Application's startup that is no generated root), which the caller records. The caller publishes each record
// keyed by its location (DECISIONS R5-M65). The entry's call goes through FunctionCodegen's one call writer (DECISIONS R5-M89).
// Invariants: generation runs over every item, so every gap is recorded (SPEC 23.1.2), and the buffered text is discarded when any
// record was made; deterministic (admission order, item-indexed names); each item's signature is reused while Abi.Matches holds and every
// buffer is kept, so a warm module allocates nothing; a request for an item outside Generated (its Signature or Glue, the requests naming an
// item) is a broken contract, InternalInvariant_Kd at the item's site.
// Must not decide acceptance (the gate) or what is generated (the generation entry's mark), read syntax or HIR, write a
// partial module, use LINQ, Supports* or catch-alls.
// Files: the items and the records (this file); the body, glue, Builtin, ensure and virtual slot signatures and the signature caches
// (ModuleCodegen.Signatures.cs), the called imports' C signatures and declarations (ModuleCodegen.Imports.cs), the common Function values'
// entries (ModuleCodegen.Erasures.cs), the formatting wrappers of formatter items (ModuleCodegen.Formatting.cs), the object descriptors
// with their virtual tables (ModuleCodegen.Objects.cs), the Array rows' routines and helpers (ModuleCodegen.Arrays.cs) and the Dictionary
// rows' (ModuleCodegen.Dictionaries.cs); the module frame and the output form (ModuleCodegen.Frame.cs).

using System.Globalization;
using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

/// <summary>Writes the LLVM module of a program version's checked set (R5 package C 3.3).</summary>
internal sealed partial class ModuleCodegen
{
    private readonly StringWriter functions = new(CultureInfo.InvariantCulture); // The definitions, buffered until the module is complete.
    private readonly LlvmConstantPool constants = new();
    private readonly HashSet<Problem> reported = new();
    private readonly FunctionCodegen bodies;
    private readonly TyCtxt types; // Its erasure and Copy proofs, which the object descriptors read (ModuleCodegen.Objects).
    private readonly IReadOnlyList<LibraryImport> imports; // Binding's validated #LibraryImport declarations (F2b).
    private readonly List<ExternalFunction> externals = []; // The imports the latest Write's definitions call, one per symbol (Import).
    private int writes; // The number of Writes.
    private CheckedSet set = null!; // The checked set of the latest Write.
    private MirProgram program = null!; // The program of the latest Write.
    private InstanceCollector collector = null!; // The collector of the latest Write.

    /// <summary>Initializes a new instance of the <see cref="ModuleCodegen"/> class.</summary>
    /// <param name="layout">The layout of the compilation's Types.</param>
    /// <param name="locations">The compilation's runtime location texts.</param>
    /// <param name="types">The compilation's Type context, which FunctionCodegen's case views read.</param>
    /// <param name="imports">Binding's validated <c>#LibraryImport</c> declarations, kept current across its passes.</param>
    /// <param name="tests">The compilation's test catalog, which a test build's discovery fills.</param>
    internal ModuleCodegen(Layout layout, SourceLocationTable locations, TyCtxt types, IReadOnlyList<LibraryImport> imports, Testing.TestCatalog tests)
    {
        (this.Layout, this.Locations, this.types, this.imports, this.Tests) = (layout, locations, types, imports, tests);
        this.bodies = new(this, types);
        this.caseCall = (output, index) => this.bodies.WriteEntryCall(output, this.cases[index]);
    }

    /// <summary>Gets the layout of the compilation's Types.</summary>
    internal Layout Layout { get; }

    /// <summary>Gets the runtime location texts (SPEC 22.5.1) a runtime routine receives.</summary>
    internal SourceLocationTable Locations { get; }

    /// <summary>Gets the test catalog: a test build's cases and identity, and the numbers of its verification sites (TestCatalog.SiteOf).</summary>
    internal Testing.TestCatalog Tests { get; }

    /// <summary>Gets a value indicating whether the latest <see cref="Write"/> writes a test build, whose runtime is the test runtime and
    /// whose code reports its phases (FunctionCodegen.Phases).</summary>
    internal bool IsTestBuild { get; private set; }

    /// <summary>Gets the module's private UTF-8 constants, which the header defines.</summary>
    internal LlvmConstantPool Constants => this.constants;

    /// <summary>Gets the records of the latest <see cref="Write"/>, one per code and subject.</summary>
    internal List<Problem> Records { get; } = [];

    /// <summary>Writes the module of a program version, or records why not.</summary>
    /// <param name="program">The program the generation gate passed.</param>
    /// <param name="collector">The collector of its checked set, marked from the output roots.</param>
    /// <param name="startup">The startup selection.</param>
    /// <param name="roots">The output roots: an Application's startup item, a test build's tests or a Library's roots.</param>
    /// <param name="output">The caller's writer, written only when the module is complete.</param>
    /// <returns>Whether the module was written.</returns>
    internal bool Write(MirProgram program, InstanceCollector collector, in StartupResult startup, ReadOnlySpan<int> roots, TextWriter output)
    {
        this.Records.Clear();
        this.reported.Clear();
        this.needs = Needs.None;
        this.constants.Clear();
        this.functions.GetStringBuilder().Clear();
        this.helperText.GetStringBuilder().Clear();
        this.externals.Clear();
        this.wrapped.Clear();
        (this.set, this.program, this.collector, this.writes) = (collector.Set, program, collector, this.writes + 1);
        this.IsTestBuild = startup.Kind == StartupKind.Test;
        this.Number();
        for (var item = 0; item < this.set.Items.Length; item++)
        {
            if (this.set.IsGenerated(item))
            {
                this.Item(program, collector, item);
            }
        }

        this.WriteWrappers(collector);

        if (!this.Start(program, collector, startup, roots, out var start) || this.Records.Count != 0)
        {
            return false;
        }

        this.Assemble(output, start);
        return true;
    }

    /// <summary>Records gate step 2's refusal of a body or a broken input contract at its subject, once per code and subject.</summary>
    /// <param name="code">A refused body's A1 or A2 code, Unsupported_Kd for an arena skip until H3 (DECISIONS R5-M96, R5-M146), or
    /// InternalInvariant_Kd.</param>
    /// <param name="subject">The located subject.</param>
    internal void Report(DiagnosticCode code, SourceRef subject)
    {
        if (this.reported.Add(new(code, subject)))
        {
            this.Records.Add(new(code, subject));
        }
    }

    // An item by its kind's row: a body item, a glue and an adapter through FunctionCodegen, a Builtin or PropertyWitness shim, a descriptor
    // through ObjectRuntime, a VTable as its descriptor's table, a LegacyRow item's routine; MonoKind has no other member.
    private void Item(MirProgram program, InstanceCollector collector, int item)
    {
        var entry = collector.Set.Items[item];
        switch (entry.Key.Kind)
        {
            case MonoKind.Body:
                this.bodies.Write(program, collector, item, this.functions);
                return;
            case MonoKind.DropGlue:
                this.bodies.WriteGlue(collector, item, this.functions);
                return;
            case MonoKind.ClosureErasure or MonoKind.ItemAdapter:
                this.bodies.WriteAdapter(collector, item, this.functions);
                return;
            case MonoKind.Builtin or MonoKind.PropertyWitness:
                this.bodies.WriteShim(collector, item, this.functions); // Its shim's function (FunctionCodegen.Shims; R5-M126, R5-M141).
                return;
            case MonoKind.ObjectDescriptor:
                this.Descriptor(item);
                return;
            case MonoKind.VTable:
                return; // Its text is its descriptor's virtual table (ModuleCodegen.Objects' Table; R5-M114).
            case MonoKind.LegacyRow:
                _ = this.Row(item, out _); // Its routine, its helper's text and its row's needs (ModuleCodegen.Arrays; R5-M121, R5-M124).
                return;
            default:
                throw new InvalidOperationException($"The mono item kind {entry.Key.Kind} has no code generation row.");
        }
    }
}
