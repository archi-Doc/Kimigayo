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
                sharedValue: ref/T,
                exclusiveValue: uniq/T,
                object: obj/T,
                sharedObject: rc/T,
                atomicObject: arc/T,
                sharedObjectBorrow: objref/T,
                exclusiveObjectBorrow: objuniq/T,
                raw: unsafe/T,
                in => collection: Collection<s/T>,
                using => comparer: (s/T, T2) -> ref/Bool
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
                "private func find<s/T, T2>(value?: T, owned: owner/T, sharedValue: ref/T, exclusiveValue: uniq/T, object: obj/T, sharedObject: rc/T, atomicObject: arc/T, sharedObjectBorrow: objref/T, exclusiveObjectBorrow: objuniq/T, raw: unsafe/T, in => collection: Collection<s/T>, using => comparer: (s/T, T2) -> ref/Bool) -> owner/i32",
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
    [InlineData("ref", SemanticsKind.Ref)]
    [InlineData("uniq", SemanticsKind.Uniq)]
    [InlineData("obj", SemanticsKind.Obj)]
    [InlineData("rc", SemanticsKind.Rc)]
    [InlineData("arc", SemanticsKind.Arc)]
    [InlineData("objref", SemanticsKind.ObjRef)]
    [InlineData("objuniq", SemanticsKind.ObjUniq)]
    [InlineData("unsafe", SemanticsKind.Unsafe)]
    public void ClassifiesBuiltInSemantics(string text, SemanticsKind expected)
    {
        Assert.True(CompilerHelper.TryParse(text, out var actual));
        Assert.Equal(expected, actual);
        Assert.Equal(text, actual.ToText());
        Assert.Equal(expected == SemanticsKind.Owner, actual.IsValue());
        Assert.Equal(expected is >= SemanticsKind.Ref and <= SemanticsKind.Uniq, actual.IsValueBorrow());
        Assert.Equal(expected is >= SemanticsKind.Obj and <= SemanticsKind.Arc, actual.IsObject());
        Assert.Equal(expected is >= SemanticsKind.ObjRef and <= SemanticsKind.ObjUniq, actual.IsObjectBorrow());
        Assert.Equal(expected != SemanticsKind.Owner, actual.IsReference());
    }

    [Fact]
    public void ClassifiesSemanticsParameter()
    {
        Assert.False(CompilerHelper.TryParse("s", out var actual));
        Assert.Equal(SemanticsKind.Parameter, actual);
        Assert.False(actual.IsValue());
        Assert.False(actual.IsValueBorrow());
        Assert.False(actual.IsObject());
        Assert.False(actual.IsObjectBorrow());
        Assert.False(actual.IsReference());
    }

    [Theory]
    [InlineData(SemanticsKind.Owner, SemanticsMask.Owner)]
    [InlineData(SemanticsKind.Ref, SemanticsMask.Ref)]
    [InlineData(SemanticsKind.Uniq, SemanticsMask.Uniq)]
    [InlineData(SemanticsKind.Obj, SemanticsMask.Obj)]
    [InlineData(SemanticsKind.Rc, SemanticsMask.Rc)]
    [InlineData(SemanticsKind.Arc, SemanticsMask.Arc)]
    [InlineData(SemanticsKind.ObjRef, SemanticsMask.ObjRef)]
    [InlineData(SemanticsKind.ObjUniq, SemanticsMask.ObjUniq)]
    [InlineData(SemanticsKind.Unsafe, SemanticsMask.Unsafe)]
    public void ConvertsSemanticsKindToMask(SemanticsKind kind, SemanticsMask expected)
    {
        Assert.Equal(expected, kind.ToMask());
        Assert.True(expected.Contains(kind));
    }

    [Fact]
    public void ProvidesCompositeSemanticsMasks()
    {
        Assert.Equal(
            SemanticsMask.Ref | SemanticsMask.Uniq,
            SemanticsMask.ValueBorrow);
        Assert.Equal(
            SemanticsMask.Obj | SemanticsMask.Rc | SemanticsMask.Arc,
            SemanticsMask.Object);
        Assert.Equal(
            SemanticsMask.ObjRef | SemanticsMask.ObjUniq,
            SemanticsMask.ObjectBorrow);
        Assert.Equal(
            SemanticsMask.ValueBorrow | SemanticsMask.ObjectBorrow,
            SemanticsMask.Borrow);
        Assert.Equal(
            SemanticsMask.Value | SemanticsMask.Object,
            SemanticsMask.Owning);
        Assert.Equal(
            SemanticsMask.ValueBorrow | SemanticsMask.Object | SemanticsMask.ObjectBorrow | SemanticsMask.Unsafe,
            SemanticsMask.Reference);
        Assert.Equal(
            SemanticsMask.Value | SemanticsMask.ValueBorrow | SemanticsMask.Object | SemanticsMask.ObjectBorrow,
            SemanticsMask.Safe);
        Assert.Equal(SemanticsMask.Safe | SemanticsMask.Unsafe, SemanticsMask.All);
    }

    [Fact]
    public void MatchesPositiveAndNegatedSemanticsConstraints()
    {
        var borrowed = SemanticsMask.ValueBorrow;

        Assert.True(borrowed.IsSatisfiedBy(SemanticsKind.Ref));
        Assert.False(borrowed.IsSatisfiedBy(SemanticsKind.Owner));
        Assert.False(borrowed.IsSatisfiedBy(SemanticsKind.Ref, isNegated: true));
        Assert.True(borrowed.IsSatisfiedBy(SemanticsKind.Owner, isNegated: true));
        Assert.False(borrowed.IsSatisfiedBy(SemanticsKind.Parameter));
        Assert.False(borrowed.IsSatisfiedBy(SemanticsKind.Parameter, isNegated: true));
    }
}
