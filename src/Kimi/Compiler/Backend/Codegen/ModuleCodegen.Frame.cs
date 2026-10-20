// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The module frame of ModuleCodegen (R5 package C 3.3, IMPL 21.4.2): the output form around the output roots, and the complete module
// through LlvmModuleText in the old generator's order: the header with the pooled constants, the runtime, the overflow declarations, the
// buffered definitions, the entry and the footer.
internal sealed partial class ModuleCodegen
{
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

    // The complete module around the buffered definitions, an Application's entry calling its startup root.
    private void Assemble(TextWriter output, FunctionAbi? start)
    {
        LlvmModuleText.WriteHeader(output, this.constants);
        LlvmModuleText.WriteRuntime(output, testRuntime: null);
        output.Write(LlvmModuleText.OverflowDeclarations);
        output.Write(this.functions.GetStringBuilder());
        if (start is not null)
        {
            WriteStart(output, start);
        }

        output.Write(LlvmModuleText.Footer);
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
