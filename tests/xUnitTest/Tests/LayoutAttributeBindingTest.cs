// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public partial class LayoutAttributeBindingTest
{
    [Theory]
    [InlineData("#Layout(\"bad\")\nstruct S", "InvalidLayoutAttribute_Kd")]
    [InlineData("#Layout(\"Kimigayo\")\n#Layout(\"Kimigayo\")\nstruct S", "InvalidLayoutAttribute_Kd")]
    [InlineData("#Layout(\"C\")\nstruct S\n    var x: i32\n#Layout(\"Kimigayo\")\nstruct S", "ConflictingLayout_Kd")]
    public void FailedLayoutSpecificationsInvalidateThroughTheirAttribute(string source, string code)
    {
        foreach (var testBuild in new[] { false, true })
        {
            var c = CompilationTestHelper.ParseSuccess(source);
            c.IsTestBuild = testBuild;
            var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
            for (var pass = 0; pass < 2; pass++)
            {
                Assert.False(c.Bind().IsComplete);
                Assert.Equal(BindingState.Invalid, type.StateOf());
                Assert.Equal(BindingFailure.None, type.FailureOf());
                var marker = Assert.IsType<AttributeKoto>(Assert.Single(c.Binding.FailureCauses(type)!).Subject);
                Assert.Equal(BindingState.Invalid, marker.StateOf());
                Assert.False(type.SymbolOf()!.Adt!.IsCLayout);
                var error = Assert.Single(CompilationTestHelper.BindingErrors(c));
                Assert.Equal(code, error.Code);
                Assert.Equal(marker.Span, error.Span);
            }
        }
    }

    [Theory]
    [InlineData("#Layout(\"C\")\nstruct S\n    var a: i32\n    var b: u8")]
    [InlineData("#Layout(\"Kimigayo\",)\nstruct S")]
    [InlineData("struct S\n    var a: i32\n#Layout(\"C\")\nstruct S")]
    [InlineData("#Layout(\"C\")\nstruct S\n    var a: i32\n#Layout(\"C\")\nstruct S")]
    [InlineData("struct S\n    var a: i32\n#Layout(\"C\")\nstruct S\n    func method() => ()")]
    [InlineData("#Layout(\"Kimigayo\")\nstruct S\n    var a: i32\nstruct S\n    var b: i32")]
    [InlineData("#Layout(\"C\")\nstruct S<T>\n    var a: T")]
    [InlineData("#Layout(\"Kimigayo\")\nopen struct B\n#Layout(\"Kimigayo\")\nstruct S: B")]
    [InlineData("#Layout(\"Kimigayo\")\nstruct S<T>\n#Layout(\"Kimigayo\")\nstruct S<T>")]
    [InlineData("#Layout(\"C\")\nstruct S\n    var a: i32\nstruct S\n    computed value: i32\n        get(self: ref/Self) -> i32 => 1")]
    [InlineData("#if false\n    #Layout(\"bad\")\n    struct S\n#Layout(\"C\")\nstruct S\n    var a: i32")]
    [InlineData("#Layout(\"C\")\nstruct S\n    var a: i32\n#if false\n    struct S\n        var b: i32")]
    public void ValidSelectedLayoutsBind(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("; ", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")));
        Assert.True(c.Bind().IsComplete);
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    // SPEC 6.7.1, 8.15.3 (DECISIONS R4-M134 Q8): a declaration whose C layout is unavailable, open, derived, empty or storing a zero-sized
    // Field, and a written instance storing one are no Binding error; the error is at each by-value use (LayoutAttributeBindingTest.
    // CLayoutUses.cs), which Layout judges.
    [Theory]
    [InlineData("#Layout(\"C\")\nstruct S")]
    [InlineData("#Layout(\"C\")\nstruct S\n    static var count: i32")]
    [InlineData("#Layout(\"C\")\nopen struct S\n    var n: i32")]
    [InlineData("open struct B\n    var n: i32\n#Layout(\"C\")\nstruct S: B\n    var m: i32")]
    [InlineData("#Layout(\"C\")\nstruct S\n    var n: i32\n    var empty: ()")]
    [InlineData("#Layout(\"C\")\nstruct S\n    var empty: [0 of i32]")]
    [InlineData("#Layout(\"C\")\nstruct S\n    var empty: [2 of ()]")]
    [InlineData("struct E\n#Layout(\"C\")\nstruct S\n    var n: i32\n    var e: E")]
    [InlineData("#Layout(\"C\")\nstruct P<T>\n    var n: i32\n    var value: T\nfunc f(p: P<()>) => ()")]
    [InlineData("#Layout(\"C\")\nstruct P<T>\n    var n: i32\n    var value: T\nfunc f() -> i32\n    var p: raw/P<[0 of i32]> = null\n    return 0")]
    [InlineData("struct E\n#Layout(\"C\")\nstruct P<T>\n    var n: i32\n    var value: T\nfunc f(p: raw/P<E>) => ()")]
    public void AnUnavailableCLayoutIsNoBindingError(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.True(c.Binding.Result.IsComplete, string.Join("; ", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")));
            Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidCLayout_Kd);
            c.Bind();
        }
    }

    [Theory]
    [InlineData("#Layout(\"C\")\nstruct S\n    static var count: i32\n    var n: i32")]
    [InlineData("#Layout(\"C\")\nstruct S<T>\n    var n: i32\n    var value: T")]
    public void CLayoutFormationAllowsStaticAndDependentFields(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("; ", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")));
        Assert.True(c.Bind().IsComplete);
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("#Layout\nstruct S")]
    [InlineData("#Layout()\nstruct S")]
    [InlineData("#Layout(\"c\")\nstruct S")]
    [InlineData("#Layout(\"C\", \"C\")\nstruct S")]
    [InlineData("#Layout(mode: \"C\")\nstruct S")]
    [InlineData("#Layout((\"C\"))\nstruct S")]
    [InlineData("#Layout(Unknown)\nstruct S")]
    [InlineData("#Layout(\"\\(Unknown)\")\nstruct S")]
    [InlineData("#Layout(\"C\")\n#Layout(\"C\")\nstruct S")]
    [InlineData("#Layout(\"C\")\ngroup G")]
    [InlineData("#Layout(\"C\")\nfunc f() => ()")]
    [InlineData("func f(#Layout(\"C\") value: i32) => ()")]
    [InlineData("struct S\n    #Layout(\"C\")\n    var a: i32")]
    [InlineData("#Layout(\"C\")\nenum E\n    A")]
    [InlineData("#Layout(\"C\")\n#Test\nfunc test() => Missing.api()")]
    [InlineData("#Test\n#Layout(\"C\")\nfunc test() => Missing.api()")]
    [InlineData("struct S\n#Layout(\"bad\")\nstruct S")]
    [InlineData("#Layout(\"C\")\nstruct S\n#Layout(\"Kimigayo\")\nstruct S")]
    [InlineData("#Layout(\"C\")\nstruct S\n    var a: i32\nstruct S\n    var b: i32")]
    public void InvalidSelectedLayoutsFail(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code is DiagnosticCode.InvalidLayoutAttribute_Kd or DiagnosticCode.ConflictingLayout_Kd or DiagnosticCode.SplitCLayoutStorage_Kd);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd);
        Assert.False(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    // DIAGNOSTICS.md §4.4: an attribute before a statement is misplaced syntax. It attaches to nothing, so the parser's Error is the
    // only one and the statement is checked on its own; Binding does not judge the Layout target again.
    [Fact]
    public void ALayoutAttributeOnAStatementIsMisplacedSyntax()
    {
        var c = MinimalEmissionTest.Analyze("#Test\nfunc test()\n    #Layout(\"C\")\n    let value = 1\n    ()");
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.MisplacedSyntax_Kd), error.Code);
        Assert.Equal("#Layout(\"C\")", error.Text);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidLayoutAttribute_Kd);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SourceOrderAndCanonicalWritingPreserveMergedLayouts(bool storageFirst)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        var storage = new SourceDocument("z-storage.kimi", "#Layout(\"C\")\nstruct S\n    var a: i32\n    var b: u8");
        var methods = new SourceDocument("a-methods.kimi", "#Layout(\"C\")\nstruct S\n    func method() => ()");
        c.Kotonoha.AddSource(storageFirst ? storage : methods);
        c.Kotonoha.AddSource(storageFirst ? methods : storage);
        Assert.True(c.Bind().IsComplete);
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
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
    [InlineData("#Layout(\"Kimigayo\")\nstruct S", DiagnosticCode.ConflictingLayout_Kd)]
    [InlineData("struct S\n    var b: u8", DiagnosticCode.SplitCLayoutStorage_Kd)]
    [InlineData("#Layout(\"bad\")\nstruct S", DiagnosticCode.InvalidLayoutAttribute_Kd)]
    [InlineData("#Unknown\nstruct S", DiagnosticCode.UnresolvedBinding_Kd)]
    public void AddedFragmentsInvalidatePreviouslyValidBinding(string source, DiagnosticCode expected)
    {
        var c = MinimalEmissionTest.Analyze("#Layout(\"C\")\nstruct S\n    var a: i32");
        Assert.True(c.Binding.Result.IsComplete);
        c.Kotonoha.AddSource(new SourceDocument("later.kimi", source));
        Assert.False(c.Bind().IsComplete);
        var issue = Assert.Single(c.Binding.Issues, x => x.Code == expected);
        Assert.Equal("later.kimi", issue.Node.CodeContext.SourceDocument?.Path);
        Assert.False(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void RemovingConflictingAttributeClearsItsBindingFailure()
    {
        var c = MinimalEmissionTest.Analyze("#Layout(\"C\")\nstruct S\n    var a: i32\n#Layout(\"Kimigayo\")\nstruct S");
        Assert.False(c.Binding.Result.IsComplete);
        var container = Assert.Single(c.Kotonoha.RootKoto.NestedContainers);
        Assert.True(container.RemoveAttribute(Assert.IsType<AttributeKoto>(container.AttributeChain)));
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckBound().IsComplete);
    }

    // AdtDef.IsCLayout: true exactly for a struct that selected C layout validly and whose layout is available at the declaration;
    // AdtDef.CLayoutUnavailable: true exactly for a valid selection on an open, derived or empty struct (SPEC 6.7.1), a fact Layout reads
    // (DECISIONS R4-M134 Q8); both written for every struct in every pass.
    [Theory]
    [InlineData("#Layout(\"C\")\nstruct S\n    var a: i32", true, false)]
    [InlineData("#Layout(\"C\")\nstruct S<T>\n    var n: i32\n    var value: T", true, false)]
    [InlineData("#Layout(\"Kimigayo\")\nstruct S\n    var a: i32", false, false)]
    [InlineData("struct S\n    var a: i32", false, false)]
    [InlineData("#Layout(\"C\")\nopen struct S\n    var a: i32", false, true)]
    [InlineData("open struct B\n    var n: i32\n#Layout(\"C\")\nstruct S: B\n    var a: i32", false, true)]
    [InlineData("#Layout(\"C\")\nstruct S\n    static var a: i32", false, true)]
    [InlineData("#Layout(\"C\")\nstruct S\n    var a: i32\n#Layout(\"Kimigayo\")\nstruct S", false, false)]
    [InlineData("#Layout(\"C\")\n#Layout(\"C\")\nstruct S", false, false)]
    public void CLayoutFactMarksValidatedCLayoutStructs(string source, bool expected, bool unavailable)
    {
        var c = MinimalEmissionTest.Analyze(source + "\nstruct Other\n    var b: i32");
        var s = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        Assert.Equal([expected, false], c.Kotonoha.RootKoto.NestedContainers.Where(x => x.Name is "S" or "Other").Select(IsCLayout));
        Assert.Equal(unavailable, s.SymbolOf()!.Adt!.CLayoutUnavailable);
    }

    [Fact]
    public void CLayoutFactFollowsEditedAttributes()
    {
        var c = MinimalEmissionTest.Analyze("#Layout(\"C\")\nstruct S\n    var a: i32\n#Layout(\"Kimigayo\")\nstruct S");
        var container = Assert.Single(c.Kotonoha.RootKoto.NestedContainers);
        Assert.False(IsCLayout(container));
        foreach (var mode in (string[])["Kimigayo", "C"])
        {
            var attribute = container.AttributeChain;
            while (attribute!.LayoutMode != mode)
            {
                attribute = attribute.AttributeChain;
            }

            Assert.True(container.RemoveAttribute(attribute));
            Assert.True(c.Bind().IsComplete);
            Assert.Equal(mode == "Kimigayo", IsCLayout(container));
        }
    }

    [Theory]
    [InlineData("C")]
    [InlineData("Kimigayo")]
    public void LayoutValidationDoesNotCertifyNominalGeneration(string mode)
    {
        var c = MinimalEmissionTest.Analyze($"#Layout(\"{mode}\")\nstruct S\n    var a: i32\nfunc main() => ()");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.False(c.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmLayoutBindingAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("#Layout(\"C\")\nstruct S\n    var a: i32\n#Layout(\"C\")\nstruct S\n    func method() => ()");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Layout Binding failed.");
            }
        }));
    }

    private static bool IsCLayout(DeclarationContainerKoto container) => container.SymbolOf()!.Adt!.IsCLayout;
}
