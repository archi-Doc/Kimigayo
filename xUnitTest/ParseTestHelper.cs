// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

internal static class ParseTestHelper
{
    internal static Kotonoha Parse(string source)
    {
        var tree = Compilation.CreateForTest().Kotonoha;
        tree.CreateCodeContext().Parse(tree.RootKoto, source);
        return tree;
    }

    internal static void AssertValid(Kotonoha tree)
    {
        var diagnostics = tree.DiagnosticCollection.GetArray();
        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(x => $"{x.Span}: {x.Message}")));
    }

    internal static Kotonoha ParseSuccess(string source)
    {
        var tree = Parse(source);
        AssertValid(tree);
        return tree;
    }

    internal static FunctionKoto ParseSingleFunction(string source)
        => Assert.IsType<FunctionKoto>(Assert.Single(GetChildren(ParseSuccess(source).RootKoto)));

    internal static IReadOnlyList<Koto> GetChildren(DeclarationContainerKoto container)
        => ReferenceEquals(container, container.Kotonoha.RootKoto)
            ? container.Kotonoha.GeneratedFunction?.Body?.Items ?? []
            : container.Members;
}
