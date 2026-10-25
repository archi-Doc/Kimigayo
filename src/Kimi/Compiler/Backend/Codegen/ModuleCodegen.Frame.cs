// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The module frame of ModuleCodegen (R5 package C 3.3, IMPL 21.4.2): the output form around the output roots, and the complete module
// through LlvmModuleText in the old generator's order: the header with the pooled constants, the runtime, the external declarations, the
// zero-sized symbol, the overflow declarations, the memory declarations, the Array runtime and the Array helpers, the nullable Alloc, the
// string-comparison routines, the buffered definitions, the entry, the object runtime and the footer. The external declarations, the imports
// a definition calls (Import, DECISIONS R5-M89), the zero-sized symbol, the memory declarations, the Array runtime (DECISIONS R5-M121), the
// nullable Alloc (DECISIONS R5-M92), the string-comparison routines (DECISIONS R5-M118) and the object runtime (DECISIONS R5-M110) are
// written only when a definition requested them (Request; package C 3.2's requests, DECISIONS R5-M87), the Array helpers (ModuleCodegen.Arrays)
// before every definition (PD8). The entry calls its root through FunctionCodegen's one call writer (PD3).
internal sealed partial class ModuleCodegen
{
    /// <summary>The zero-sized symbol, the home of every zero-sized local: one 16-aligned address (IMPL 21.1.3: a borrow of a zero-sized
    /// value holds a nonnull address aligned for it; package C row 10).</summary>
    internal const string ZeroSized = "__kimi_zero_sized";

    private const string ZeroSizedDefinition = "@" + ZeroSized + " = private unnamed_addr constant [1 x i8] zeroinitializer, align 16\n";

    private Needs needs; // The module parts the definitions of the latest Write requested.

    /// <summary>The module parts a definition requests, which the frame writes only when requested.</summary>
    [Flags]
    internal enum Needs : byte
    {
        /// <summary>None.</summary>
        None = 0,

        /// <summary>The memory intrinsics' declarations (LlvmModuleText.MemoryDeclarations), for a copy of a value in memory.</summary>
        Memory = 1,

        /// <summary>The zero-sized symbol's definition, for a zero-sized local's home.</summary>
        ZeroSized = 2,

        /// <summary>The nullable Alloc's definition (LlvmModuleText.TryAllocate), for a tryAllocateBytes call (DECISIONS R5-M92).</summary>
        TryAllocate = 4,

        /// <summary>The object runtime (ObjectRuntime.WriteRuntime), for a descriptor, a clone or a runtime Type test (DECISIONS R5-M110).</summary>
        Objects = 8,

        /// <summary>The string-comparison routines (LegacyRuntimeHelpers.StringComparisons), for a string comparison (DECISIONS R5-M118).</summary>
        Strings = 16,

        /// <summary>The Array runtime (LegacyRuntimeHelpers.WriteArrayRuntime: the capacity routines), for an Array row that declares it (DECISIONS
        /// R5-M46, R5-M121).</summary>
        Arrays = 32,
    }

    /// <summary>Requests a module part for the module being written.</summary>
    /// <param name="need">The part.</param>
    internal void Request(Needs need) => this.needs |= need;

    // The complete module around the buffered definitions, an Application's entry calling its startup root.
    private void Assemble(TextWriter output, FunctionAbi? start)
    {
        LlvmModuleText.WriteHeader(output, this.constants);
        LlvmModuleText.WriteRuntime(output, testRuntime: null);
        LlvmModuleText.WriteExternals(output, CollectionsMarshal.AsSpan(this.externals), testRuntime: null);
        if ((this.needs & Needs.ZeroSized) != 0)
        {
            output.Write(ZeroSizedDefinition);
        }

        output.Write(LlvmModuleText.OverflowDeclarations);
        if ((this.needs & Needs.Memory) != 0)
        {
            output.Write(LlvmModuleText.MemoryDeclarations);
        }

        if ((this.needs & Needs.Arrays) != 0)
        {
            LegacyRuntimeHelpers.WriteArrayRuntime(output);
        }

        output.Write(this.helperText.GetStringBuilder());

        if ((this.needs & Needs.TryAllocate) != 0)
        {
            output.Write(LlvmModuleText.TryAllocate);
        }

        if ((this.needs & Needs.Strings) != 0)
        {
            output.Write(LegacyRuntimeHelpers.StringComparisons);
        }

        output.Write(this.functions.GetStringBuilder());
        if (start is not null)
        {
            this.WriteStart(output, start);
        }

        if ((this.needs & Needs.Objects) != 0)
        {
            ObjectRuntime.WriteRuntime(output);
        }

        output.Write(LlvmModuleText.Footer);
    }

    // __kimi_start (IMPL 21.4.2): the startup body, which takes no parameter (Binding's startup check), then a normal exit with 0.
    private void WriteStart(TextWriter output, FunctionAbi startup)
    {
        output.Write(RuntimeAbi.Start.GetDefinition(exported: true));
        output.Write("entry:\n");
        this.bodies.WriteEntryCall(output, startup);
        output.Write("  call void @");
        output.Write(RuntimeAbi.Exit.Name);
        output.Write("(i32 0)\n  unreachable\n}\n");
    }

    // The output form (IMPL 21.4.2) around the output roots: an Application's entry calls its startup root; a Library has none; a test
    // build's TestEntry is a row not written yet (C10a), at each test root. False for an Application whose startup root has no signature
    // while nothing explains it: a broken contract without a subject.
    private bool Start(MirProgram program, InstanceCollector collector, in StartupResult startup, ReadOnlySpan<int> roots, out FunctionAbi? entry)
    {
        entry = null;
        if (startup.Kind == StartupKind.Test)
        {
            foreach (var root in roots)
            {
                this.Report(DiagnosticCode.Unsupported_Kd, collector.Subject(root, program.Bodies[this.set.Items[root].BodyOrdinal], default));
            }

            return true;
        }

        if (startup.OutputKind == OutputKind.Library)
        {
            return true;
        }

        entry = roots[0] < this.signatures.Length ? this.signatures[roots[0]] : null;
        return entry is not null || this.Records.Count != 0;
    }
}
