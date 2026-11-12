// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class ProductTestMembershipTest
{
    [Theory]
    [InlineData("#Test\nfunc test() => TestOnly.missing()\nConsole.writeLine(\"product\")")]
    [InlineData("#Test()\nfunc test() -> ()\n    let text = \"moved\"\n    Console.writeLine(text)\n    Console.writeLine(text)\nConsole.writeLine(\"product\")")]
    [InlineData("#Test\npublic func main() => TestOnly.missing()\nConsole.writeLine(\"product\")")]
    public void ProductDoesNotBindOrAnalyzeTestBodies(string source)
    {
        var compilation = MinimalEmissionTest.Analyze(source);
        Assert.True(compilation.Binding.Result.IsComplete, MinimalEmissionTest.Describe(compilation));
        Assert.True(compilation.Binding.Startup.IsComplete);
        Assert.True(compilation.Analysis.IsVerified);
        Assert.Single(compilation.BuildMir().Bodies.ToArray(), static x => x.Source.Module == 0); // The startup body, no test body.
    }

    [Theory]
    [InlineData("UnknownBody", "Console.writeLine(\"product\")\n#Test\nfunc test() => TestOnly.missing()")]
    [InlineData("MoveError", "Console.writeLine(\"product\")\n#Test\nfunc test()\n    let text = \"test-only\"\n    Console.writeLine(text)\n    Console.writeLine(text)")]
    [InlineData("TestMain", "#Test\npublic func main() => TestOnly.missing()\nConsole.writeLine(\"product\")")]
    [InlineData("ExplicitMain", "public func main() => Console.writeLine(\"product\")\n#Test\nfunc test() => TestOnly.missing()")]
    [InlineData("InfiniteTest", "Console.writeLine(\"product\")\n#Test\nfunc test()\n    loop => ()")]
    [InlineData("AbortTest", "Console.writeLine(\"product\")\n#Test\nfunc test() => $abort(\"test-only\")")]
    [InlineData("Overload", "func helper(value: i32) => Console.writeLine(\"product\")\nhelper(1)\n#Test\nfunc helper() => TestOnly.missing()")]
    public void OnlyProductCodeIsEmitted(string name, string source)
        => ScalarEmissionTest.EmitFixture("ProductTest" + name, source, "product\n");

    [Theory]
    [InlineData("#Test(unknown())\nfunc test() => ()")]
    [InlineData("#Test\n#Test\nfunc test() => ()")]
    [InlineData("#Test\nfunc test(value: i32) => ()")]
    [InlineData("#Test\nfunc test(value: i32 = 1) => ()")]
    [InlineData("#Test\nfunc test() -> i32 => 1")]
    [InlineData("#Test\nunsafe func test() => ()")]
    [InlineData("#Test\nfunc test<T>() => ()")]
    [InlineData("struct Box<T>\n    #Test\n    func test() => ()")]
    [InlineData("func outer()\n    #Test\n    func test() => ()\n    ()")]
    [InlineData("#Test\ngroup Invalid")]
    [InlineData("func ordinary(#Test value: i32) => ()")]
    [InlineData("#Test\nfunc outer()\n    #Test\n    func nested() => ()\n    ()")]
    public void InvalidSelectedTestDefinitionsRemainErrors(string source)
    {
        var compilation = MinimalEmissionTest.Analyze(source + "\nConsole.writeLine(\"product\")");
        Assert.False(compilation.Binding.Result.IsComplete);
        Assert.Contains(compilation.Binding.Issues, x => x.Code == DiagnosticCode.InvalidTestDefinition_Kd);
    }

    // DIAGNOSTICS.md §4.4: a Test marker before a binding is a misplaced attribute. The parser reports it once, it attaches to
    // nothing, and the binding is an ordinary product item, so no test definition remains to be judged.
    [Theory]
    [InlineData("#Test\nlet invalid = 1")]
    [InlineData("#Test\nfunc outer()\n    #Test\n    let value = 1\n    ()")]
    public void ATestMarkerOnABindingIsMisplacedSyntax(string source)
    {
        var compilation = MinimalEmissionTest.Analyze(source + "\nConsole.writeLine(\"product\")");
        var error = Assert.Single(TestDiagnostics.Of(compilation), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.MisplacedSyntax_Kd), error.Code);
        Assert.Equal("#Test", error.Text);
        Assert.DoesNotContain(compilation.Binding.Issues, x => x.Code == DiagnosticCode.InvalidTestDefinition_Kd);
    }

    [Theory]
    [InlineData("group")]
    [InlineData("rootgroup")]
    [InlineData("struct")]
    [InlineData("enum")]
    public void ReceiverFreeContainerTestsAreExcluded(string kind)
    {
        var cases = kind == "enum" ? "    Value\n" : string.Empty;
        var compilation = MinimalEmissionTest.Analyze($"{kind} Container\n{cases}    #Test\n    func test() => Tests.missing()\nConsole.writeLine(\"product\")");
        Assert.True(compilation.Binding.Result.IsComplete, MinimalEmissionTest.Describe(compilation));
        Assert.True(compilation.Analysis.IsVerified);
    }

    [Theory]
    [InlineData("test()")]
    [InlineData("let value = test")]
    public void ProductCannotReferenceTestFunctions(string use)
    {
        var compilation = MinimalEmissionTest.Analyze("#Test\nfunc test() => ()\n" + use);
        Assert.False(compilation.Binding.Result.IsComplete);
        Assert.Contains(compilation.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd);
    }

    // SPEC 6.7.2, 18.6: a Product build excludes a test's body, not its declaration (PLAN G660: dev's Product build accepted the eight
    // Binding rows). Each marked declaration has the same records in either build mode, and its unmarked twin has its own.
    [Theory]
    [InlineData("group G\n    #Test\n    virtual func t() => ()", "InvalidVirtualDeclaration_Kd", "InvalidVirtualDeclaration_Kd")]
    [InlineData("open struct B\n    var x: i32\n    #Test\n    public virtual func t() => ()", "InvalidVirtualDeclaration_Kd", "InvalidVirtualDeclaration_Kd")]
    [InlineData("#Test\nvirtual func t() => ()", "InvalidVirtualDeclaration_Kd", "InvalidVirtualDeclaration_Kd")]
    [InlineData("group G\n    func t() => ()\n    #Test\n    func t() => ()", "DuplicateBinding_Kd", "DuplicateBinding_Kd")]
    [InlineData("group G\n    #Test\n    protected func t() => ()", "ProtectedPlacement_Kd", "ProtectedPlacement_Kd")]
    [InlineData("open struct B\n    var x: i32\n    public func t(self: ref/Self) -> i32 => 1\nstruct D : B\n    #Test\n    func t() => ()", "DuplicateBinding_Kd", "DuplicateBinding_Kd")]
    [InlineData("enum E\n    T\n    #Test\n    func T() => ()", "DuplicateBinding_Kd", "DuplicateBinding_Kd")]
    [InlineData("struct S\n    var t: i32\n    #Test\n    func t() => ()", "DuplicateBinding_Kd", "DuplicateBinding_Kd")]
    [InlineData("group G\n    #Test\n    static func t() => ()", "MisplacedSyntax_Kd", "MisplacedSyntax_Kd")]
    [InlineData("group G\n    #Test\n    override func t() => ()", "InvalidVirtualDeclaration_Kd MisplacedSyntax_Kd", "InvalidVirtualDeclaration_Kd")]
    public void ATestDeclarationIsJudgedInEitherBuildMode(string source, string codes, string twinCodes)
    {
        var twin = source.Replace("    #Test\n", string.Empty).Replace("#Test\n", string.Empty);
        var product = DeclarationErrors(source, false);
        Assert.Equal(codes, string.Join(' ', product.Select(static x => x.Code).Order(StringComparer.Ordinal)));
        Assert.Equal(product.Select(static x => (x.Code, x.Span)), DeclarationErrors(source, true).Select(static x => (x.Code, x.Span)));
        Assert.Equal(twinCodes, string.Join(' ', DeclarationErrors(twin, false).Select(static x => x.Code).Order(StringComparer.Ordinal)));
    }

    [Theory]
    [InlineData("group G\n    #Test\n    func t() => ()")]
    [InlineData("#Test\nfunc t() => ()")]
    [InlineData("struct S\n    var x: i32\n    #Test\n    func t() => ()")]
    [InlineData("open struct B\n    var x: i32\n    #Test\n    public func t() => ()")]
    [InlineData("group Outer\n    func t() -> i32 => 1\n    group Inner\n        #Test\n        func t() => ()\n        func use() -> i32 => t()")] // SPEC 9.5: access filters the test.
    public void AValidTestDeclarationIsAcceptedInEitherBuildMode(string source)
    {
        Assert.Empty(DeclarationErrors(source, false));
        Assert.Empty(DeclarationErrors(source, true));
    }

    [Fact]
    public void TestMembershipDoesNotRecognizeOtherMarkers()
    {
        var compilation = MinimalEmissionTest.Analyze("#Test\n#Unknown\nfunc test() => Missing.api()\n()");
        Assert.False(compilation.Binding.Result.IsComplete);
        Assert.Contains(compilation.Binding.Issues, x => x.Code == DiagnosticCode.InvalidTestDefinition_Kd);
        Assert.DoesNotContain(compilation.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd);
    }

    [Fact]
    public void TestEditsDoNotChangeProductIrWhenProductLocationsAreUnchanged()
    {
        const string Product = "Console.writeLine(\"product\")\n";
        var original = MinimalEmissionTest.Analyze(Product + "#Test\nfunc first() => Missing.first()");
        var changed = MinimalEmissionTest.Analyze(Product + "#Test\nfunc renamed()\n    loop => Missing.second()");
        var first = new StringWriter();
        var second = new StringWriter();
        Assert.True(original.Generate(first));
        Assert.True(changed.Generate(second));
        Assert.Equal(first.ToString(), second.ToString());
    }

    [Fact]
    public void ReloadRetainsRawTestsAndReappliesProductMembership()
    {
        var original = MinimalEmissionTest.Analyze("Console.writeLine(\"product\")\n#Test\nfunc test() => Missing.api()");
        var bytes = TinyhandSerializer.Serialize(original.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var module = restored.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref module);
        Assert.NotNull(module);
        module.OnDeserialized(restored);
        Assert.Contains("Missing.api", Assert.Single(module.SourceDocuments).SourceText);
        Assert.True(restored.Bind().IsComplete);
        Assert.True(restored.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(restored.Analyze().IsVerified);
    }

    [Fact]
    public void RebindingReclassifiesReplacedDeclarations()
    {
        var compilation = MinimalEmissionTest.Analyze("func target() => ()\ntarget()");
        Assert.True(compilation.Analysis.IsVerified);
        var original = FindFunction(compilation.Kotonoha.RootKoto, "target")!;
        var replacementSource = MinimalEmissionTest.Analyze("#Test\nfunc target() => Missing.api()\n()");
        var replacement = FindFunction(replacementSource.Kotonoha.RootKoto, "target")!;
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(original.Parent!, original, replacement));
        Assert.False(compilation.Bind().IsComplete);
        Assert.False(compilation.Analysis.IsVerified);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmProductMembershipAllocatesNothing()
    {
        var compilation = MinimalEmissionTest.Analyze("Console.writeLine(\"product\")\n#Test\nfunc test() => Missing.api()");
        void Analyze()
        {
            if (!compilation.Bind().IsComplete || !compilation.Binding.CheckStartup(OutputKind.Application).IsComplete || !compilation.Analyze().IsVerified)
            {
                throw new InvalidOperationException("Product membership analysis failed.");
            }
        }

        Analyze();
        Assert.Equal(0, AllocationMeasurement.Measure(Analyze));
    }

    private static TestDiagnostic[] DeclarationErrors(string source, bool testBuild)
    {
        var c = CompilationTestHelper.Parse(source);
        c.IsTestBuild = testBuild;
        c.Bind();
        return CompilationTestHelper.BindingErrors(c);
    }

    private static FunctionKoto? FindFunction(Koto node, string name)
    {
        if (node is FunctionKoto function && function.Name == name)
        {
            return function;
        }

        foreach (var child in node.Children())
        {
            if (FindFunction(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
