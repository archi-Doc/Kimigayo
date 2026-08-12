// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class FuncDeclarationParseTest
{
    [Fact]
    public void ParsesCompleteFunctionDeclaration()
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        var context = kotonoha.CreateCodeContext();

        var source = """
            private func find<&s T, T2>(
                value?: T,
                in => collection: Collection<T>,
                using => comparer: (T, T) -> Bool
                ) -> i32
                return 0

            public func Main() -> ()
                return
            """;
        context.Parse(kotonoha.RootKoto, source);

        var diagnostics = kotonoha.DiagnosticCollection.GetArray();
        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(x => $"{x.Range}: {x.Message}")));

        var builder = default(IndentedStringBuilder);
        try
        {
            kotonoha.RootKoto.UnparseAll(ref builder);
            var text = builder.ToString();
            Assert.Contains(
                "private func find<&s T, T2>(value?: T, in => collection: Collection<T>, using => comparer: (T, T) -> Bool) -> i32",
                text);
            Assert.Contains("public func Main() -> ()", text);
        }
        finally
        {
            builder.Dispose();
        }
    }
}
