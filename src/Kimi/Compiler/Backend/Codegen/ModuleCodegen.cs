// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// ModuleCodegen (R5 plan C0b1, package C 3.3 and 3.9, design 2 2.3 and 2.12; DECISIONS R5-M65), one per Compilation.
// Responsibility: one module of a program version from its checked set: every item in admission order, a body item written by
// FunctionCodegen as __kimi_f<item> under its signature (Abi.Of, internal convention), the output form around them (IMPL 21.4.2: an
// Application's __kimi_start calls its startup body and exits with 0, a Library has no entry) and the module frame through LlvmModuleText
// in the old generator's order (header, runtime, overflow declarations, functions, entry, footer); the coverage rows (PD11) of the item
// kinds and output forms: DropGlue (C4a), ClosureErasure and ItemAdapter (C5b), a test build's TestEntry (C10a).
// Inputs: the program the generation gate passed (validated, analyzed and accepted); the instance collector's checked set, TypeOf and
// location rule (K package 4.9); the compilation's Layout; the startup selection.
// Output: the complete module written to the caller's writer, or nothing and the Records of why: one Unsupported_Kd per subject of a
// row not written yet (until the R5 plan's unit X2), InternalInvariant_Kd at a broken input contract; no record only for a broken contract
// without a subject (an Application's startup that is no generated root), which the caller records. The caller publishes each record
// keyed by its location (DECISIONS R5-M65). The entry's call goes through FunctionCodegen's call writer once C3a writes calls.
// Invariants: generation runs over every item, so every gap is recorded (SPEC 23.1.2), and the buffered text is discarded when any
// record was made; deterministic (admission order, item-indexed names); each item's signature is reused while Abi.Matches holds and every
// buffer is kept, so a warm module allocates nothing.
// Must not decide acceptance (the gate) or what is generated (K11's Generated mark will narrow the items), read syntax, HIR or the old
// Emission, write a partial module, use LINQ, Supports* or catch-alls.

using System.Globalization;
using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

/// <summary>Writes the LLVM module of a program version's checked set (R5 package C 3.3).</summary>
internal sealed class ModuleCodegen
{
    private readonly StringWriter functions = new(CultureInfo.InvariantCulture); // The definitions, buffered until the module is complete.
    private readonly LlvmConstantPool constants = new();
    private readonly HashSet<Problem> reported = new();
    private readonly FunctionCodegen bodies;
    private FunctionAbi?[] signatures = []; // By item.

    /// <summary>Initializes a new instance of the <see cref="ModuleCodegen"/> class.</summary>
    /// <param name="layout">The layout of the compilation's Types.</param>
    internal ModuleCodegen(Layout layout)
    {
        this.Layout = layout;
        this.bodies = new(this);
    }

    /// <summary>Gets the layout of the compilation's Types.</summary>
    internal Layout Layout { get; }

    /// <summary>Gets the records of the latest <see cref="Write"/>, one per code and subject.</summary>
    internal List<Problem> Records { get; } = [];

    /// <summary>Writes the module of a program version, or records why not.</summary>
    /// <param name="program">The program the generation gate passed.</param>
    /// <param name="collector">The collector of its checked set.</param>
    /// <param name="startup">The startup selection.</param>
    /// <param name="output">The caller's writer, written only when the module is complete.</param>
    /// <returns>Whether the module was written.</returns>
    internal bool Write(MirProgram program, InstanceCollector collector, in StartupResult startup, TextWriter output)
    {
        this.Records.Clear();
        this.reported.Clear();
        this.constants.Clear();
        this.functions.GetStringBuilder().Clear();
        for (var item = 0; item < collector.Set.Items.Length; item++)
        {
            this.Item(program, collector, item);
        }

        if (!this.Start(program, collector, startup, out var start) || this.Records.Count != 0)
        {
            return false;
        }

        LlvmModuleText.WriteHeader(output, this.constants);
        LlvmModuleText.WriteRuntime(output, testRuntime: null);
        output.Write(LlvmModuleText.OverflowDeclarations);
        output.Write(this.functions.GetStringBuilder());
        if (start is not null)
        {
            WriteStart(output, start);
        }

        output.Write(LlvmModuleText.Footer);
        return true;
    }

    /// <summary>Gets the signature of a body item, named <c>__kimi_f</c> with its index: the previous one while it still matches.</summary>
    /// <param name="item">The item.</param>
    /// <param name="flags">The signature's facts beyond its Types.</param>
    /// <param name="result">The closed result Type.</param>
    /// <param name="parameters">The closed parameter Types in slot order.</param>
    /// <returns>The signature.</returns>
    internal FunctionAbi Signature(int item, Abi.Flags flags, BoundType result, ReadOnlySpan<BoundType> parameters)
    {
        if (item >= this.signatures.Length)
        {
            Array.Resize(ref this.signatures, Math.Max(item + 1, 2 * this.signatures.Length));
        }

        if (this.signatures[item] is { } previous && Abi.Matches(previous, this.Layout, Abi.Convention.Internal, flags, result, parameters))
        {
            return previous;
        }

        var name = "__kimi_f" + item.ToString(CultureInfo.InvariantCulture);
        return this.signatures[item] = Abi.Of(this.Layout, Abi.Convention.Internal, flags, result, parameters).Named(name);
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

    // __kimi_start (IMPL 21.4.2): the startup body, which takes no parameter (Binding's startup check), then a normal exit with 0.
    private static void WriteStart(TextWriter output, FunctionAbi startup)
    {
        output.Write(RuntimeAbi.Start.GetDefinition(exported: true));
        output.Write("entry:\n  call ");
        output.Write(startup.Result);
        output.Write(" @");
        output.Write(startup.Name);
        output.Write("()\n  call void @");
        output.Write(RuntimeAbi.Exit.Name);
        output.Write("(i32 0)\n  unreachable\n}\n");
    }

    // An item by its kind's row: a body item through FunctionCodegen; a type-driven item is a row not written yet, at the site that
    // admitted it.
    private void Item(MirProgram program, InstanceCollector collector, int item)
    {
        var entry = collector.Set.Items[item];
        switch (entry.Key.Kind)
        {
            case MonoKind.Body:
                this.bodies.Write(collector, item, program.Bodies[entry.BodyOrdinal], this.functions);
                return;
            case MonoKind.DropGlue or MonoKind.ClosureErasure or MonoKind.ItemAdapter:
                this.Report(DiagnosticCode.Unsupported_Kd, entry.Site); // C4a writes drop glue; C5b the erasure and item adapters.
                return;
            default:
                throw new InvalidOperationException($"The mono item kind {entry.Key.Kind} has no code generation row.");
        }
    }

    // The output form (IMPL 21.4.2): an Application's entry calls its startup item, a root of the checked set; a Library has none; a test
    // build's TestEntry is a row not written yet (C10a), at each test it calls. False for an Application whose startup has no item or no
    // signature while nothing explains it: a broken contract without a subject.
    private bool Start(MirProgram program, InstanceCollector collector, in StartupResult startup, out FunctionAbi? entry)
    {
        entry = null;
        var set = collector.Set;
        if (startup.Kind == StartupKind.Test)
        {
            for (var root = 0; root < set.RootCount; root++)
            {
                if (program.Bodies[set.Items[root].BodyOrdinal] is { Key.Kind: MirBodyKind.Test } test)
                {
                    this.Report(DiagnosticCode.Unsupported_Kd, collector.Subject(root, test, default));
                }
            }

            return true;
        }

        if (startup.OutputKind == OutputKind.Library)
        {
            return true;
        }

        var body = new MirDefKey(startup.Kind == StartupKind.Explicit ? startup.Symbol : null, MirBodyKind.Fn);
        var item = set.Find(new(MonoKind.Body, new(body, null)));
        entry = item >= 0 && item < this.signatures.Length ? this.signatures[item] : null;
        return entry is not null || this.Records.Count != 0;
    }
}
