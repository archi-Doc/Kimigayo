// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class TypeArityBindingTest
{
    [Theory]
    [InlineData("struct Box {}\nstruct Box<T> {}")]
    [InlineData("struct Box<T> {}\nstruct Box {}")]
    [InlineData("struct Box<T, U> {}\nstruct Box {}\nstruct Box<T> {}")]
    [InlineData("struct Box<T> {}\nstruct Box {}\nstruct Box<T> {}\nstruct Box {}")]
    public void ExplicitAritiesSelectDistinctDeclarations(string declarations)
    {
        var c = Parse(declarations + "\nfunc plain(x?: Box) => ()\nfunc generic(x?: Box<i32>) => ()");
        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
        Assert.True(c.Bind().IsComplete, Describe(c));
        var functions = c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>().ToArray();
        var plain = functions.Single(x => x.Name == "plain").Parameters[0].Type.BoundType!;
        var generic = functions.Single(x => x.Name == "generic").Parameters[0].Type.BoundType!;
        Assert.NotSame(plain.Symbol, generic.Symbol);
        Assert.Empty(Assert.IsType<StructKoto>(plain.Symbol!.Declaration).GenericParameterNodes);
        Assert.Single(Assert.IsType<StructKoto>(generic.Symbol!.Declaration).GenericParameterNodes);
    }

    [Theory]
    [InlineData("G.Box", "G.Box<i32>")]
    [InlineData("::G.Box", "::G.Box<i32>")]
    [InlineData("Box", "Box<i32>")]
    public void QualifiedRootedAndImportedTypesSelectArity(string plain, string generic)
    {
        var c = Parse($"alias G\ngroup G\n    public struct Box\n    public struct Box<T>\nfunc f(x?: {plain}, y?: {generic}) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void ImportStageSelectsArityBeforeDeclaringAmbiguity()
    {
        var c = Parse("alias A\nalias B\ngroup A\n    public struct Box {}\ngroup B\n    public struct Box<T> {}\nfunc f(x?: Box, y?: Box<i32>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void NearerWrongArityBlocksOuterDeclaration()
    {
        var c = Parse("struct Box<T> {}\ngroup G\n    struct Box<T, U> {}\n    func f(x?: Box<i32>) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.TypeMismatch_Kd);
    }

    [Theory]
    [InlineData("struct Box {}\nstruct Box<T, U> {}\nstruct Box<T> {}", "func f(x?: Box<i32, u8>) => ()")]
    [InlineData("enum Box\n    A\nenum Box<T>\n    B(T)", "func f(x?: Box, y?: Box<i32>) => ()")]
    [InlineData("struct Box {}\n    public func value() -> i32 => 1\nstruct Box<T> {}\n    public func value() -> i64 => 2", "func f() -> i32 => Box.value()\nfunc g() -> i64 => Box<i32>.value()")]
    [InlineData("open struct Base {}\n    public func value() -> i64 => 1\nopen struct Base<T> {}\n    public func value() -> i32 => 2\nstruct Derived {}: Base<i32>", "func f() -> i32 => Derived.value()")]
    [InlineData("#Layout(\"C\")\nstruct Box {}\n    var a: i32\n#Layout(\"Kimigayo\")\nstruct Box<T> {}\n    var a: i32\nstruct Box<T> {}\n    var b: i32", "func f(x?: Box, y?: Box<i32>) => ()")]
    [InlineData("struct Box<T> {}\n#if false\n    struct Box<T> {}\n        var a: Missing\nstruct Box {}", "func f(x?: Box, y?: Box<i32>) => ()")]
    public void AritySelectionPreservesRelatedSemantics(string declarations, string use)
    {
        var c = Parse(declarations + "\n" + use);
        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("group Box\nstruct Box<T> {}", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("struct Box<T> {}\ngroup Box", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("struct Box {}\nenum Box<T>\n    A", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("struct Box<T> {}\nstruct Box {}\nstruct Box<U> {}", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("enum Box<T>\n    A\nenum Box\n    B\nenum Box<T>\n    C", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("struct Box {}\nstruct Box<T> {}\nfunc f(x?: Box<i32, u8>) => ()", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("struct Box<T> {}\nstruct Box<T, U> {}\nfunc f(x?: Box) => ()", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("alias A\nalias B\ngroup A\n    public struct Box<T> {}\ngroup B\n    public struct Box<T> {}\nfunc f(x?: Box<i32>) => ()", DiagnosticCode.AmbiguousBinding_Kd)]
    [InlineData("struct Box<T> {}\ngroup G\n    struct Box<T, U> {}\n    func f(x?: Box<i32>) => ()", DiagnosticCode.TypeMismatch_Kd)]
    public void InvalidAritiesAndDeclarationsRemainErrors(string source, DiagnosticCode expected)
    {
        var c = Parse(source);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == expected);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Contains(restored.Binding.Issues, x => x.Code == expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceOrderReloadAndCanonicalWritingRetainEveryArity(bool reverse)
    {
        var c = Parse(string.Empty);
        var first = new SourceDocument("one.kimi", "struct Box {}\n    var a: i32\nstruct Box<T> {}\n    var a: i64");
        var second = new SourceDocument("two.kimi", "struct Box<T> {}\n    var b: u8\nstruct Box {}\n    var b: u16\nfunc f(x?: Box, y?: Box<i32>) => ()");
        c.Kotonoha.AddSource(reverse ? second : first);
        c.Kotonoha.AddSource(reverse ? first : second);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(2, c.Kotonoha.RootKoto.NestedContainers.Count);
        Assert.All(c.Kotonoha.RootKoto.NestedContainers, x => Assert.Equal(2, x.Members.Count));
        Assert.True(Reload(c).Bind().IsComplete);
        var builder = default(IndentedStringBuilder);
        try
        {
            c.Kotonoha.RootKoto.UnparseAll(ref builder);
            var written = Parse(builder.ToString());
            Assert.True(written.Bind().IsComplete, Describe(written));
            Assert.Equal(2, written.Kotonoha.RootKoto.NestedContainers.Count);
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Fact]
    public void ReplacingAnArityRebuildsFragmentLookupWithoutLosingItsSibling()
    {
        var c = Parse("struct Box {}\nstruct Box<T> {}\nstruct Box<T, U> {}");
        Assert.True(c.Bind().IsComplete);
        var root = c.Kotonoha.RootKoto;
        var original = root.NestedContainers.Single(x => x.GenericParameterNodes.Count == 1);
        var replacement = Assert.Single(Parse("struct Box<T> {}\n    var a: i32").Kotonoha.RootKoto.NestedContainers);
        Assert.True(KotoHelper.Replace(root, original, replacement));
        c.Kotonoha.AddSource(new SourceDocument("later.kimi", "struct Box<T> {}\n    var b: i32\nfunc f(x?: Box, y?: Box<i32>, z?: Box<i32, u8>) => ()"));
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(3, root.NestedContainers.Count);
        Assert.Equal(2, replacement.Members.Count);
        var duplicate = Assert.Single(Parse("struct Box {}").Kotonoha.RootKoto.NestedContainers);
        Assert.False(KotoHelper.Replace(root, replacement, duplicate));
        Assert.True(c.Bind().IsComplete);
    }

    [Fact]
    public void WarmArityBindingAllocatesNothing()
    {
        var c = Parse("alias A\nalias B\ngroup A\n    public struct Box {}\ngroup B\n    public struct Box<T> {}\nfunc f(x?: Box, y?: Box<i32>) => ()");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Type arity Binding failed.");
            }
        }));
    }

    private static Compilation Reload(Compilation c)
    {
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Parse(string.Empty);
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(restored);
        return restored;
    }

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("arity.kimi", source));
        return c;
    }

    private static string Describe(Compilation c) => string.Join("; ", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
