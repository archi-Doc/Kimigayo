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
                Assert.Equal(AdtDef.CLayoutSelection.None, CLayout(type));
                var error = Assert.Single(CompilationTestHelper.BindingErrors(c));
                Assert.Equal(code, error.Code);
                Assert.Equal(marker.Span, error.Span);
            }
        }
    }

    // A Layout attribute rejected by layout validation invalidates its struct without replacing an earlier cause. An empty C-layout struct
    // is no longer rejected (SPEC 6.7.1, 8.15.3; DECISIONS R4-M134), so a second attribute in one fragment is the rejected one.
    [Fact]
    public void ALayoutRejectionPreservesAnEarlierOriginCause()
    {
        var c = CompilationTestHelper.ParseSuccess("#Layout(\"C\")\n#Layout(\"C\")\nstruct S {a}\n    origin missing outlives a");
        var type = c.Kotonoha.RootKoto.NestedContainers.Single();
        var relation = KotoTree.Walk(type).OfType<OriginRelationKoto>().Single();
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.False(c.Bind().IsComplete);
            Assert.Equal(BindingState.Invalid, type.StateOf());
            Assert.Equal(BindingFailure.InvalidOrigin, type.FailureOf());
            Assert.Equal(BindingState.Unresolved, relation.StateOf());
            Assert.Contains(type, c.Binding.DerivedIssues);
            var key = Assert.Single(c.Binding.FailureCauses(type)!);
            Assert.Equal(type.KeyOf(Kimi.Diagnostics.DiagnosticRequirement.Binding(BindingFailure.InvalidOrigin)), key);
            type.ReportDerived(Kimi.Diagnostics.DiagnosticRequirement.Startup, [key]);
            Assert.Equal(["InvalidLayoutAttribute_Kd", "MissingOriginBinding_Kd"], CompilationTestHelper.BindingErrors(c).Select(x => x.Code));
        }

        Assert.True(type.RemoveAttribute(Assert.IsType<AttributeKoto>(type.AttributeChain)));
        Assert.False(c.Bind().IsComplete);
        Assert.Equal("MissingOriginBinding_Kd", Assert.Single(CompilationTestHelper.BindingErrors(c)).Code);
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

    // SPEC 6.7.1, 8.15.3 (DECISIONS R4-M134): a Field of zero-sized elements, a form beside the shared rows'
    // (LayoutAttributeBindingTest.CLayoutUses.cs), is no error declared and an error at a by-value use.
    [Fact]
    public void AFieldOfZeroSizedElementsIsAnErrorOnlyAtAByValueUse()
    {
        const string Declaration = "#Layout(\"C\")\nstruct S\n    var n: i32\n    var empty: [2 of ()]\n";
        AssertUses("elements.none", Declaration + "let x = 1\n", string.Empty, sel: false);
        AssertUses("elements.param", Declaration + "func f(s: S) => ()\nlet x = 1\n", "InvalidCLayout_Kd func f(s: S) => ()", sel: false);
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

    // AdtDef.CLayout: Selected for a valid #Layout("C"), Unavailable for one on an open, derived or empty struct (SPEC 6.7.1), else None,
    // written for every struct in every pass.
    [Theory]
    [InlineData("#Layout(\"C\")\nstruct S\n    var a: i32", "Selected")]
    [InlineData("#Layout(\"C\")\nstruct S<T>\n    var n: i32\n    var value: T", "Selected")]
    [InlineData("#Layout(\"Kimigayo\")\nstruct S\n    var a: i32", "None")]
    [InlineData("struct S\n    var a: i32", "None")]
    [InlineData("#Layout(\"C\")\nopen struct S\n    var a: i32", "Unavailable")]
    [InlineData("#Layout(\"C\")\nstruct S\n    static var count: i32", "Unavailable")]
    [InlineData("#Layout(\"C\")\nstruct S\n    var a: i32\n#Layout(\"Kimigayo\")\nstruct S", "None")]
    public void CLayoutFactMarksSelectedCLayoutStructs(string source, string expected)
    {
        var c = MinimalEmissionTest.Analyze(source + "\nstruct Other\n    var b: i32");
        Assert.Equal([expected, "None"], c.Kotonoha.RootKoto.NestedContainers.Select(static x => CLayout(x).ToString()));
    }

    [Fact]
    public void CLayoutFactFollowsEditedAttributes()
    {
        var c = MinimalEmissionTest.Analyze("#Layout(\"C\")\nstruct S\n    var a: i32\n#Layout(\"Kimigayo\")\nstruct S");
        var container = Assert.Single(c.Kotonoha.RootKoto.NestedContainers);
        Assert.Equal(AdtDef.CLayoutSelection.None, CLayout(container));
        foreach (var mode in (string[])["Kimigayo", "C"])
        {
            var attribute = container.AttributeChain;
            while (attribute!.LayoutMode != mode)
            {
                attribute = attribute.AttributeChain;
            }

            Assert.True(container.RemoveAttribute(attribute));
            Assert.True(c.Bind().IsComplete);
            Assert.Equal(mode == "Kimigayo" ? AdtDef.CLayoutSelection.Selected : AdtDef.CLayoutSelection.None, CLayout(container));
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

    private static AdtDef.CLayoutSelection CLayout(DeclarationContainerKoto container) => container.SymbolOf()!.Adt!.CLayout;
}
