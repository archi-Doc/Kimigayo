// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// ModuleCodegen (R5 plan C0b1, K11, C3a, package C 3.3 and 3.9, design 2 2.3 and 2.12; DECISIONS R5-M65, R5-M77, R5-M89), one per
// Compilation.
// Responsibility: one module of a program version from its checked set: every Generated item in admission order, a body item written by
// FunctionCodegen as __kimi_f<item> under its signature (Abi.Of, internal convention), which this owner gives from the item's own body to
// its definition and every call (Signature), a called #LibraryImport function's C signature and declaration (Import), the output form
// around the output roots (IMPL 21.4.2: an Application's __kimi_start calls its startup root and exits with 0, a Library has no entry) and
// the module frame through LlvmModuleText in the old generator's order (header with the pooled constants, runtime, the called imports'
// declarations, overflow declarations, functions, entry, footer); the coverage rows (PD11) of the item kinds and output forms: DropGlue
// (__kimi_drop<item>, written by FunctionCodegen for a Type DropGlue has a row for, the string's, R5-M78; C4a the others), ClosureErasure
// and ItemAdapter (C5b), the Builtin shims (C8f, C9b), the property witnesses (C3c), K8's ObjectDescriptor and VTable items (C6a, C6c), a
// test build's TestEntry (C10a).
// Inputs: the program the generation gate passed (validated, analyzed and accepted); the instance collector's checked set, marked from the
// output roots the generation entry gives (K package 4.10), TypeOf and location rule (K package 4.9); the compilation's Layout and runtime
// location texts (Compilation.Locations); the startup selection.
// Output: the complete module written to the caller's writer, or nothing and the Records of why: one Unsupported_Kd per subject of a
// row not written yet (until the R5 plan's unit X2), InternalInvariant_Kd at a broken input contract; no record only for a broken contract
// without a subject (an Application's startup that is no generated root), which the caller records. The caller publishes each record
// keyed by its location (DECISIONS R5-M65). The entry's call goes through FunctionCodegen's one call writer (DECISIONS R5-M89).
// Invariants: generation runs over every item, so every gap is recorded (SPEC 23.1.2), and the buffered text is discarded when any
// record was made; deterministic (admission order, item-indexed names); each item's signature is reused while Abi.Matches holds and every
// buffer is kept, so a warm module allocates nothing; a request for an item outside Generated (its Signature or Glue, the requests naming an
// item) is a broken contract, InternalInvariant_Kd at the item's site.
// Must not decide acceptance (the gate) or what is generated (the generation entry's mark), read syntax, HIR or the old Emission, write a
// partial module, use LINQ, Supports* or catch-alls.
// Files: the items, their names and signatures and the records (this file); the module frame and the output form (ModuleCodegen.Frame.cs).

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
    private readonly IReadOnlyList<LibraryImport> imports; // Binding's validated #LibraryImport declarations (F2b).
    private readonly List<ExternalFunction> externals = []; // The imports the latest Write's definitions call, one per symbol (Import).
    private FunctionAbi?[] signatures = []; // By item.
    private int[] signed = []; // By item, the Write whose signature signatures holds (0: none).
    private FunctionAbi?[] importSignatures = []; // By import.
    private int[] importSigned = []; // By import, the Write whose signature importSignatures holds (0: none).
    private string?[] importSymbols = []; // By import, the external symbol its signature is named by.
    private BoundType[] parameters = []; // A signature's parameter Types in slot order.
    private int writes; // The number of Writes.
    private CheckedSet set = null!; // The checked set of the latest Write.
    private MirProgram program = null!; // The program of the latest Write.
    private InstanceCollector collector = null!; // The collector of the latest Write.

    /// <summary>Initializes a new instance of the <see cref="ModuleCodegen"/> class.</summary>
    /// <param name="layout">The layout of the compilation's Types.</param>
    /// <param name="locations">The compilation's runtime location texts.</param>
    /// <param name="types">The compilation's Type context, which FunctionCodegen's case views read.</param>
    /// <param name="imports">Binding's validated <c>#LibraryImport</c> declarations, kept current across its passes.</param>
    internal ModuleCodegen(Layout layout, SourceLocationTable locations, TyCtxt types, IReadOnlyList<LibraryImport> imports)
    {
        (this.Layout, this.Locations, this.imports) = (layout, locations, imports);
        this.bodies = new(this, types);
    }

    /// <summary>Gets the layout of the compilation's Types.</summary>
    internal Layout Layout { get; }

    /// <summary>Gets the runtime location texts (SPEC 22.5.1) a runtime routine receives.</summary>
    internal SourceLocationTable Locations { get; }

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
        this.externals.Clear();
        (this.set, this.program, this.collector, this.writes) = (collector.Set, program, collector, this.writes + 1);
        for (var item = 0; item < this.set.Items.Length; item++)
        {
            if (this.set.IsGenerated(item))
            {
                this.Item(program, collector, item);
            }
        }

        if (!this.Start(program, collector, startup, roots, out var start) || this.Records.Count != 0)
        {
            return false;
        }

        this.Assemble(output, start);
        return true;
    }

    /// <summary>Gets the signature of a body item from its own body (IMPL 21.3.1; DECISIONS R5-M89), the one its definition and every call of
    /// it write: under the internal convention, its result Type local 0's and its parameters its Arg locals' in slot order
    /// (FunctionCodegen.ArgumentPosition), in the item's instance, a caller-location library function's flag included (SPEC 22.5.1); named
    /// <c>__kimi_f</c> with its index, computed once per Write and the previous one while it still matches.</summary>
    /// <param name="item">The item, a body item of a kind the frame writes.</param>
    /// <returns>The signature; null, recorded at the item's site, where the instance does not make a Type of its signature.</returns>
    internal FunctionAbi? Signature(int item)
    {
        var entry = this.set.Items[item];
        if (!this.set.IsGenerated(item))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, entry.Site); // Only a Generated item is requested (K11).
        }

        if (item >= this.signatures.Length)
        {
            Array.Resize(ref this.signatures, Math.Max(item + 1, 2 * this.signatures.Length));
        }

        if (item >= this.signed.Length)
        {
            Array.Resize(ref this.signed, this.signatures.Length);
        }

        if (this.signed[item] == this.writes && this.signatures[item] is { } current)
        {
            return current;
        }

        var body = this.program.Bodies[entry.BodyOrdinal];
        var count = 0;
        foreach (var local in body.Locals)
        {
            count += local.Kind == MirLocalKind.Arg ? 1 : 0;
        }

        this.Reserve(count);
        for (var slot = 0; slot < count; slot++)
        {
            if (this.collector.TypeOf(item, body.Locals[1 + FunctionCodegen.ArgumentPosition(body.Key, slot)].Type) is not { } parameter)
            {
                this.Report(DiagnosticCode.InternalInvariant_Kd, entry.Site); // The collector laid out every local of an admitted item.
                return null;
            }

            this.parameters[slot] = parameter;
        }

        if (this.collector.TypeOf(item, body.Locals[0].Type) is not { } result)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, entry.Site);
            return null;
        }

        // SPEC 22.5.1: a caller-location library function receives its caller's location; the old generator's constructor and destructor
        // result slot is its own ABI choice, which local 0's Type replaces here (IMPL 21.3.1).
        var flags = KimiLibraryCatalog.RequiresCallerLocation(body.Key.Decl) ? Abi.Flags.CallerLocation : Abi.Flags.None;
        var types = this.parameters.AsSpan(0, count);
        this.signed[item] = this.writes;
        if (this.signatures[item] is { } previous && Abi.Matches(previous, this.Layout, Abi.Convention.Internal, flags, result, types))
        {
            return previous;
        }

        var name = "__kimi_f" + item.ToString(CultureInfo.InvariantCulture);
        return this.signatures[item] = Abi.Of(this.Layout, Abi.Convention.Internal, flags, result, types).Named(name);
    }

    /// <summary>Gets the C signature of a validated <c>#LibraryImport</c> function a definition calls (SPEC 22.6; Abi.Of's C convention)
    /// and requests its declaration, one per symbol (DECISIONS R5-M89).</summary>
    /// <param name="function">The called function.</param>
    /// <returns>The signature, or null for a function without an import.</returns>
    internal FunctionAbi? Import(BindingSymbol function)
    {
        for (var index = 0; index < this.imports.Count; index++)
        {
            if (ReferenceEquals(this.imports[index].Decl, function))
            {
                var abi = this.ImportSignature(index);
                foreach (var external in this.externals)
                {
                    if (ReferenceEquals(external.Abi, abi))
                    {
                        return abi;
                    }
                }

                this.externals.Add(new(abi, this.imports[index].Kind == "import"));
                return abi;
            }
        }

        return null;
    }

    /// <summary>Gets the signature of a DropGlue item (package C 3.6), named <c>__kimi_drop</c> with its index: the previous one while it is
    /// a glue signature (its parameters <see cref="DropGlue.Parameters"/>).</summary>
    /// <param name="item">The item.</param>
    /// <returns>The signature.</returns>
    internal FunctionAbi Glue(int item)
    {
        if (!this.set.IsGenerated(item))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, this.set.Items[item].Site); // Only a Generated item is requested (K11).
        }

        if (item >= this.signatures.Length)
        {
            Array.Resize(ref this.signatures, Math.Max(item + 1, 2 * this.signatures.Length));
        }

        if (this.signatures[item] is { } previous && ReferenceEquals(previous.Parameters, DropGlue.Parameters))
        {
            return previous;
        }

        return this.signatures[item] = new("__kimi_drop" + item.ToString(CultureInfo.InvariantCulture), "void", DropGlue.Parameters);
    }

    /// <summary>Records a coverage row not written yet or a broken input contract at its subject, once per code and subject.</summary>
    /// <param name="code">Unsupported_Kd or InternalInvariant_Kd.</param>
    /// <param name="subject">The located subject.</param>
    internal void Report(DiagnosticCode code, SourceRef subject)
    {
        if (this.reported.Add(new(code, subject)))
        {
            this.Records.Add(new(code, subject));
        }
    }

    // The C signature of an import from its declared Types (TyCtxt.Signature), named by its external symbol: the first import's of that
    // symbol, so imports of one symbol share one declaration (the old generator's rule); computed once per Write and the previous one
    // while it still matches.
    private FunctionAbi ImportSignature(int index)
    {
        if (index >= this.importSignatures.Length)
        {
            Array.Resize(ref this.importSignatures, Math.Max(index + 1, 2 * this.importSignatures.Length));
            Array.Resize(ref this.importSymbols, this.importSignatures.Length);
            Array.Resize(ref this.importSigned, this.importSignatures.Length);
        }

        if (this.importSigned[index] == this.writes && this.importSignatures[index] is { } current)
        {
            return current;
        }

        var symbol = this.imports[index].Symbol;
        this.importSigned[index] = this.writes;
        for (var first = 0; first < index; first++)
        {
            if (string.Equals(this.imports[first].Symbol, symbol, StringComparison.Ordinal))
            {
                this.importSymbols[index] = symbol;
                return this.importSignatures[index] = this.ImportSignature(first);
            }
        }

        var function = this.imports[index].Decl;
        var result = TyCtxt.Signature(function, null, -1, out var count)!;
        this.Reserve(count);
        for (var slot = 0; slot < count; slot++)
        {
            this.parameters[slot] = TyCtxt.Signature(function, null, slot, out _)!;
        }

        var types = this.parameters.AsSpan(0, count);
        if (this.importSignatures[index] is { } previous && string.Equals(this.importSymbols[index], symbol, StringComparison.Ordinal) &&
            Abi.Matches(previous, this.Layout, Abi.Convention.C, Abi.Flags.None, result, types))
        {
            return previous;
        }

        this.importSymbols[index] = symbol;
        return this.importSignatures[index] = Abi.Of(this.Layout, Abi.Convention.C, Abi.Flags.None, result, types).Named(LlvmModuleText.ExternalName(symbol));
    }

    // The scratch for a signature's parameter Types, grown to at least the count given.
    private void Reserve(int count)
    {
        if (this.parameters.Length < count)
        {
            this.parameters = new BoundType[Math.Max(count, 2 * this.parameters.Length)];
        }
    }

    // An item by its kind's row: a body item through FunctionCodegen; a type-driven item is a row not written yet, at the site that
    // admitted it.
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
                this.Report(DiagnosticCode.Unsupported_Kd, entry.Site); // C5b writes the erasure and item adapters.
                return;
            case MonoKind.Builtin or MonoKind.PropertyWitness:
                this.Report(DiagnosticCode.Unsupported_Kd, entry.Site); // C3c writes property witnesses; C8f and C9b the Builtin shims (R5-M67).
                return;
            case MonoKind.ObjectDescriptor or MonoKind.VTable:
                this.Report(DiagnosticCode.Unsupported_Kd, entry.Site); // C6a writes descriptors, C6c the VTables (R5-M74).
                return;
            default:
                throw new InvalidOperationException($"The mono item kind {entry.Key.Kind} has no code generation row.");
        }
    }
}
