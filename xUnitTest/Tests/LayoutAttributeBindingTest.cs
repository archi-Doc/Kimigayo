// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class LayoutAttributeBindingTest
{
    [Theory]
    [InlineData("#Layout(\"C\")\nstruct S {}\n    var a: i32\n    var b: u8")]
    [InlineData("#Layout(\"Kimigayo\",)\nstruct S {}")]
    [InlineData("struct S {}\n    var a: i32\n#Layout(\"C\")\nstruct S {}")]
    [InlineData("#Layout(\"C\")\nstruct S {}\n    var a: i32\n#Layout(\"C\")\nstruct S {}")]
    [InlineData("struct S {}\n    var a: i32\n#Layout(\"C\")\nstruct S {}\n    func method() => ()")]
    [InlineData("#Layout(\"Kimigayo\")\nstruct S {}\n    var a: i32\nstruct S {}\n    var b: i32")]
    [InlineData("#Layout(\"C\")\nstruct S<T> {}\n    var a: T")]
    [InlineData("#Layout(\"Kimigayo\")\nopen struct B {}\n#Layout(\"Kimigayo\")\nstruct S {}: B")]
    [InlineData("#Layout(\"Kimigayo\")\nstruct S<T> {}\n#Layout(\"Kimigayo\")\nstruct S<T> {}")]
    [InlineData("#Layout(\"C\")\nstruct S {}\n    var a: i32\nstruct S {}\n    computed value: i32\n        get(self: ref/Self) -> i32 => 1")]
    [InlineData("#if false\n    #Layout(\"bad\")\n    struct S {}\n#Layout(\"C\")\nstruct S {}\n    var a: i32")]
    [InlineData("#Layout(\"C\")\nstruct S {}\n    var a: i32\n#if false\n    struct S {}\n        var b: i32")]
    public void ValidSelectedLayoutsBind(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("; ", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")));
        Assert.True(c.Bind().IsComplete);
        Assert.True(Reload(c).Bind().IsComplete);
    }

    // SPEC 21.1: rejected on declaration, whether or not generation reaches the struct.
    [Theory]
    [InlineData("#Layout(\"C\")\nstruct S {}")]
    [InlineData("#Layout(\"C\")\nstruct S {}\n    static var count: i32")]
    [InlineData("#Layout(\"C\")\nopen struct S {}\n    var n: i32")]
    [InlineData("open struct B {}\n    var n: i32\n#Layout(\"C\")\nstruct S {}: B\n    var m: i32")]
    [InlineData("#Layout(\"C\")\nstruct S {}\n    var n: i32\n    var empty: ()")]
    [InlineData("#Layout(\"C\")\nstruct S {}\n    var empty: [0 of i32]")]
    [InlineData("#Layout(\"C\")\nstruct S {}\n    var empty: [2 of ()]")]
    [InlineData("struct E {}\n#Layout(\"C\")\nstruct S {}\n    var n: i32\n    var e: E")]
    public void InvalidCLayoutFormsAreDiagnosed(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidCLayout_Kd);
    }

    [Theory]
    [InlineData("func f(p: P<()>) => ()", false)]
    [InlineData("func f() -> i32\n    var p: unsafe/P<[0 of i32]> = null\n    return 0", false)]
    [InlineData("struct E {}\nfunc f(p: unsafe/P<E>) => ()", false)]
    [InlineData("func f(p: P<i32>) => ()", true)]
    [InlineData("func f(p: unsafe/P<[1 of u8]>) => ()", true)]
    public void WrittenCLayoutInstantiationsRejectZeroSizedFields(string use, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("#Layout(\"C\")\nstruct P<T> {}\n    var n: i32\n    var value: T\n" + use);
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.Equal(valid, c.Binding.Result.IsComplete);
            Assert.Equal(!valid, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.InvalidCLayout_Kd));
            c.Bind();
        }
    }

    [Theory]
    [InlineData("#Layout(\"C\")\nstruct S {}\n    static var count: i32\n    var n: i32")]
    [InlineData("#Layout(\"C\")\nstruct S<T> {}\n    var n: i32\n    var value: T")]
    public void CLayoutFormationAllowsStaticAndDependentFields(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("; ", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")));
        Assert.True(c.Bind().IsComplete);
        Assert.True(Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("#Layout\nstruct S {}")]
    [InlineData("#Layout()\nstruct S {}")]
    [InlineData("#Layout(\"c\")\nstruct S {}")]
    [InlineData("#Layout(\"C\", \"C\")\nstruct S {}")]
    [InlineData("#Layout(mode: \"C\")\nstruct S {}")]
    [InlineData("#Layout((\"C\"))\nstruct S {}")]
    [InlineData("#Layout(Unknown)\nstruct S {}")]
    [InlineData("#Layout(\"\\(Unknown)\")\nstruct S {}")]
    [InlineData("#Layout(\"C\")\n#Layout(\"C\")\nstruct S {}")]
    [InlineData("#Layout(\"C\")\ngroup G")]
    [InlineData("#Layout(\"C\")\nfunc f() => ()")]
    [InlineData("func f(#Layout(\"C\") value: i32) => ()")]
    [InlineData("struct S {}\n    #Layout(\"C\")\n    var a: i32")]
    [InlineData("#Layout(\"C\")\nenum E\n    A")]
    [InlineData("#Layout(\"C\")\n#Test\nfunc test() => Missing.api()")]
    [InlineData("#Test\n#Layout(\"C\")\nfunc test() => Missing.api()")]
    [InlineData("#Test\nfunc test()\n    #Layout(\"C\")\n    let value = Missing.api()\n    ()")]
    [InlineData("struct S {}\n#Layout(\"bad\")\nstruct S {}")]
    [InlineData("#Layout(\"C\")\nstruct S {}\n#Layout(\"Kimigayo\")\nstruct S {}")]
    [InlineData("#Layout(\"C\")\nstruct S {}\n    var a: i32\nstruct S {}\n    var b: i32")]
    public void InvalidSelectedLayoutsFail(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code is DiagnosticCode.InvalidLayoutAttribute_Kd or DiagnosticCode.ConflictingLayout_Kd or DiagnosticCode.SplitCLayoutStorage_Kd);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd);
        Assert.False(Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SourceOrderAndCanonicalWritingPreserveMergedLayouts(bool storageFirst)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        var storage = new SourceDocument("z-storage.kimi", "#Layout(\"C\")\nstruct S {}\n    var a: i32\n    var b: u8");
        var methods = new SourceDocument("a-methods.kimi", "#Layout(\"C\")\nstruct S {}\n    func method() => ()");
        c.Kotonoha.AddSource(storageFirst ? storage : methods);
        c.Kotonoha.AddSource(storageFirst ? methods : storage);
        Assert.True(c.Bind().IsComplete);
        Assert.True(Reload(c).Bind().IsComplete);
        var builder = default(IndentedStringBuilder);
        try
        {
            c.Kotonoha.RootKoto.UnparseAll(ref builder);
            var text = builder.ToString();
            Assert.True(MinimalEmissionTest.Analyze(text).Binding.Result.IsComplete, text);
            Assert.True(text.IndexOf("var a", StringComparison.Ordinal) < text.IndexOf("var b", StringComparison.Ordinal));
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Theory]
    [InlineData("#Layout(\"Kimigayo\")\nstruct S {}", DiagnosticCode.ConflictingLayout_Kd)]
    [InlineData("struct S {}\n    var b: u8", DiagnosticCode.SplitCLayoutStorage_Kd)]
    [InlineData("#Layout(\"bad\")\nstruct S {}", DiagnosticCode.InvalidLayoutAttribute_Kd)]
    [InlineData("#Unknown\nstruct S {}", DiagnosticCode.UnresolvedBinding_Kd)]
    public void AddedFragmentsInvalidatePreviouslyValidBinding(string source, DiagnosticCode expected)
    {
        var c = MinimalEmissionTest.Analyze("#Layout(\"C\")\nstruct S {}\n    var a: i32");
        Assert.True(c.Binding.Result.IsComplete);
        c.Kotonoha.AddSource(new SourceDocument("later.kimi", source));
        Assert.False(c.Bind().IsComplete);
        var issue = Assert.Single(c.Binding.Issues, x => x.Code == expected);
        Assert.Equal("later.kimi", issue.Node.CodeContext.SourceDocument?.Path);
        Assert.False(Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void RemovingConflictingAttributeClearsItsBindingFailure()
    {
        var c = MinimalEmissionTest.Analyze("#Layout(\"C\")\nstruct S {}\n    var a: i32\n#Layout(\"Kimigayo\")\nstruct S {}");
        Assert.False(c.Binding.Result.IsComplete);
        var container = Assert.Single(c.Kotonoha.RootKoto.NestedContainers);
        Assert.True(container.RemoveAttribute(Assert.IsType<AttributeKoto>(container.AttributeChain)));
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckBound().IsComplete);
    }

    [Theory]
    [InlineData("C")]
    [InlineData("Kimigayo")]
    public void LayoutValidationDoesNotCertifyNominalGeneration(string mode)
    {
        var c = MinimalEmissionTest.Analyze($"#Layout(\"{mode}\")\nstruct S\n    var a: i32\nfunc main() => ()");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void WarmLayoutBindingAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("#Layout(\"C\")\nstruct S {}\n    var a: i32\n#Layout(\"C\")\nstruct S {}\n    func method() => ()");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Layout Binding failed.");
            }
        }));
    }

    private static Compilation Reload(Compilation c)
    {
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(restored);
        return restored;
    }
}
