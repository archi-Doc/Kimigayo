// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;

namespace Kimi.Testing;

internal static class TestRuntime
{
    private static readonly string Runtime = Read();

    internal static string Create(TestCatalog catalog, IReadOnlyDictionary<FunctionKoto, FunctionAbi> functions)
    {
        var text = new StringBuilder(Runtime);
        text.Append("\n@__kimi_test_artifact = private constant [67 x i8] c\"").Append(catalog.ArtifactId).Append("\"\n");
        for (var i = 0; i < catalog.Cases.Count; i++)
        {
            text.Append("@__kimi_test_case_").Append(i).Append(" = private constant [67 x i8] c\"").Append(catalog.Cases[i].CaseId).Append("\"\n");
        }

        text.Append("define void @__kimi_start() noreturn #0 {\nentry:\n  %case = call i64 @__kimi_test_begin()\n  switch i64 %case, label %invalid [\n");
        for (var i = 0; i < catalog.Cases.Count; i++)
        {
            text.Append("    i64 ").Append(i).Append(", label %case_").Append(i).Append('\n');
        }

        text.Append("  ]\n");
        for (var i = 0; i < catalog.Cases.Count; i++)
        {
            text.Append("case_").Append(i).Append(":\n  call void @__kimi_test_frame(i16 1, i64 0, i32 -1, ptr @__kimi_test_case_").Append(i)
                .Append(", i32 67)\n  call void @").Append(functions[catalog.Cases[i].Function].Name).Append("()\n  call void @__kimi_test_frame(i16 6, i64 0, i32 -1, ptr null, i32 0)\n  call void @ExitProcess(i32 0)\n  unreachable\n");
        }

        return text.Append("invalid:\n  call void @ExitProcess(i32 125)\n  unreachable\n}\n").ToString();
    }

    private static string Read()
    {
        using var stream = typeof(TestRuntime).Assembly.GetManifestResourceStream("Kimi.Testing.TestRuntime.ll")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
