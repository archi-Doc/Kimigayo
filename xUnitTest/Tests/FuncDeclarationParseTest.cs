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
            private func find<s/T, T2>(
                value?: T,
                owned: owner/T,
                borrowed: borrow/T,
                stacked: stack/T,
                ownerReference: ownerref/T,
                borrowReference: /T,
                longBorrowReference: borrowref/T,
                shared: rc/T,
                atomic: arc/T,
                raw: unsafe/T,
                in => collection: Collection<s/T>,
                using => comparer: (s/T, T2) -> borrowref/Bool
                ) -> owner/i32
                return 0

            public func Main() -> ()
                return
            """;
        context.Parse(kotonoha.RootKoto, source);

        var diagnostics = kotonoha.DiagnosticCollection.GetArray();
        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(x => $"{x.Span}: {x.Message}")));

        var builder = default(IndentedStringBuilder);
        try
        {
            kotonoha.RootKoto.UnparseAll(ref builder);
            var text = builder.ToString();
            Assert.Contains(
                "private func find<s/T, T2>(value?: T, owned: owner/T, borrowed: borrow/T, stacked: stack/T, ownerReference: ownerref/T, borrowReference: borrowref/T, longBorrowReference: borrowref/T, shared: rc/T, atomic: arc/T, raw: unsafe/T, in => collection: Collection<s/T>, using => comparer: (s/T, T2) -> borrowref/Bool) -> owner/i32",
                text);
            Assert.Contains("public func Main() -> ()", text);
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Theory]
    [InlineData("owner", SemanticsKind.Owner)]
    [InlineData("borrow", SemanticsKind.Borrow)]
    [InlineData("stack", SemanticsKind.Stack)]
    [InlineData("ownerref", SemanticsKind.OwnerRef)]
    [InlineData("borrowref", SemanticsKind.BorrowRef)]
    [InlineData("rc", SemanticsKind.Rc)]
    [InlineData("arc", SemanticsKind.Arc)]
    [InlineData("unsafe", SemanticsKind.Unsafe)]
    public void ClassifiesBuiltInSemantics(string text, SemanticsKind expected)
    {
        Assert.True(CompilerHelper.TryParse(text, out var actual));
        Assert.Equal(expected, actual);
        Assert.Equal(expected <= SemanticsKind.Stack, actual.IsValue());
        Assert.Equal(expected >= SemanticsKind.OwnerRef, actual.IsReference());
    }

    [Fact]
    public void ClassifiesSemanticsParameter()
    {
        Assert.False(CompilerHelper.TryParse("s", out var actual));
        Assert.Equal(SemanticsKind.Parameter, actual);
        Assert.False(actual.IsValue());
        Assert.False(actual.IsReference());
    }
}
