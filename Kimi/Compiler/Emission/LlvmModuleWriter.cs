// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // LLVM text serialization and its retained constant encoding.

internal sealed class LlvmConstant(string name)
{
    private readonly StringBuilder encoded = new();
    private string? value;

    internal string Name { get; } = name;

    internal int Length { get; private set; }

    internal void SetValue(string value)
    {
        if (this.value == value)
        {
            return;
        }

        this.value = value;
        this.Length = Encoding.UTF8.GetByteCount(value);
        var text = this.encoded;
        text.Clear();
        text.Append('@').Append(this.Name).Append(" = private unnamed_addr constant [").Append(this.Length).Append(" x i8] c\"");
        Span<byte> bytes = stackalloc byte[4];
        const string Hex = "0123456789ABCDEF";
        foreach (var rune in value.EnumerateRunes())
        {
            var count = rune.EncodeToUtf8(bytes);
            for (var i = 0; i < count; i++)
            {
                text.Append('\\').Append(Hex[bytes[i] >> 4]).Append(Hex[bytes[i] & 15]);
            }
        }

        text.Append("\", align 1\n");
    }

    internal void AppendTo(StringBuilder text) => text.Append(this.encoded);
}

/// <summary>Serializes physical instructions only; never reads Binding, AST or ownership state.</summary>
internal sealed class LlvmModuleWriter
{
    private static readonly string Runtime = ReadRuntime();
    private readonly StringBuilder text = new(16384);

    internal void Write(EmissionPlan plan, TextWriter output)
    {
        var b = this.text;
        b.Clear();
        b.Append("; Kimigayo checked literal-output lowering; pre-optimization inspection IR\ntarget triple = \"").Append(WindowsProfile.Target)
            .Append("\"\ntarget datalayout = \"").Append(WindowsProfile.DataLayout).Append("\"\n");
        WindowsLowering.AppendTypes(b);
        b.Append("@_fltused = global i32 0, align 4\n");
        plan.Literal.AppendTo(b);
        plan.Location.AppendTo(b);
        b.Append(Runtime).Append('\n');
        WindowsLowering.Entry.AppendDefinition(b);
        b.Append("entry:\n");
        foreach (var slot in plan.Slots)
        {
            b.Append("  %p").Append(slot.Place).Append(" = alloca ").Append(slot.Value.Layout.StorageType)
                .Append(", align ").Append(slot.Value.Layout.Alignment).Append('\n');
        }

        foreach (var instruction in plan.Instructions)
        {
            if (instruction.Callee is { } callee)
            {
                callee.AppendCall(b, [new("%p", instruction.Place), new("@__kimi_location"), new(null, plan.Location.Length)]);
            }
            else
            {
                var value = WindowsLowering.String;
                b.Append("  store ").Append(value.ComputationType).Append(" { ptr @").Append(plan.Literal.Name).Append(", i64 ")
                    .Append(plan.Literal.Length).Append(", i8 0 }, ptr %p").Append(instruction.Place).Append(", align ").Append(value.Layout.Alignment).Append('\n');
            }
        }

        b.Append("  ret void\n}\n"); // Lowering proved a normal Exit; no fallback terminator.
        WindowsLowering.Start.AppendDefinition(b, exported: true);
        b.Append("entry:\n");
        WindowsLowering.Entry.AppendCall(b, []);
        WindowsLowering.Exit.AppendCall(b, [new(null, 0)]);
        b.Append("  unreachable\n}\n")
            .Append("attributes #0 = { uwtable(async) \"target-cpu\"=\"x86-64\" \"target-features\"=\"+sse2\" \"denormal-fp-math\"=\"ieee,ieee\" }\n")
            .Append("!llvm.module.flags = !{!0}\n!0 = !{i32 8, !\"PIC Level\", i32 2}\n");
        foreach (var chunk in b.GetChunks())
        {
            output.Write(chunk.Span);
        }
    }

    private static string ReadRuntime()
    {
        using var stream = typeof(LlvmModuleWriter).Assembly.GetManifestResourceStream("Kimi.Compiler.Emission.WindowsRuntime.ll.in")!;
        using var reader = new StreamReader(stream);
        var runtime = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        var signature = new StringBuilder();
        foreach (var abi in new[] { WindowsLowering.Exit, WindowsLowering.DestroyString, WindowsLowering.WriteLine })
        {
            signature.Clear();
            abi.AppendDefinition(signature);
            var marker = "{{" + abi.Name + "}}\n";
            if (!runtime.Contains(marker, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The runtime template is missing a shared ABI definition.");
            }

            runtime = runtime.Replace(marker, signature.ToString(), StringComparison.Ordinal);
        }

        return runtime;
    }
}
