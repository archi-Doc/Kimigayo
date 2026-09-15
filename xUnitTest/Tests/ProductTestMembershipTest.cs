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
    [InlineData("#Test\nfunc test() => TestOnly.missing()\nwriteLine(\"product\")")]
    [InlineData("#Test()\nfunc test() -> ()\n    let text = \"moved\"\n    writeLine(text)\n    writeLine(text)\nwriteLine(\"product\")")]
    [InlineData("#Test\npublic func main() => TestOnly.missing()\nwriteLine(\"product\")")]
    public void ProductDoesNotBindOrAnalyzeTestBodies(string source)
    {
        var compilation = MinimalEmissionTest.Analyze(source);
        Assert.True(compilation.Binding.Result.IsComplete, MinimalEmissionTest.Describe(compilation, null));
        Assert.True(compilation.Binding.Startup.IsComplete);
        Assert.True(compilation.Ownership.Result.IsVerified);
        Assert.Single(compilation.Ownership.Bodies);
    }

    [Theory]
    [InlineData("UnknownBody", "writeLine(\"product\")\n#Test\nfunc test() => TestOnly.missing()")]
    [InlineData("MoveError", "writeLine(\"product\")\n#Test\nfunc test()\n    let text = \"test-only\"\n    writeLine(text)\n    writeLine(text)")]
    [InlineData("TestMain", "#Test\npublic func main() => TestOnly.missing()\nwriteLine(\"product\")")]
    [InlineData("ExplicitMain", "public func main() => writeLine(\"product\")\n#Test\nfunc test() => TestOnly.missing()")]
    [InlineData("InfiniteTest", "writeLine(\"product\")\n#Test\nfunc test()\n    loop => ()")]
    [InlineData("AbortTest", "writeLine(\"product\")\n#Test\nfunc test() => $abort(\"test-only\")")]
    [InlineData("Overload", "func helper(value: i32) => writeLine(\"product\")\nhelper(1)\n#Test\nfunc helper() => TestOnly.missing()")]
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
    [InlineData("#Test\nlet invalid = 1")]
    [InlineData("func ordinary(#Test value: i32) => ()")]
    [InlineData("#Test\nfunc outer()\n    #Test\n    func nested() => ()\n    ()")]
    [InlineData("#Test\nfunc outer()\n    #Test\n    let value = 1\n    ()")]
    public void InvalidSelectedTestDefinitionsRemainErrors(string source)
    {
        var compilation = MinimalEmissionTest.Analyze(source + "\nwriteLine(\"product\")");
        Assert.False(compilation.Binding.Result.IsComplete);
        Assert.Contains(compilation.Binding.Issues, x => x.Code == DiagnosticCode.InvalidTestDefinition_Kd);
    }

    [Theory]
    [InlineData("group")]
    [InlineData("rootgroup")]
    [InlineData("struct")]
    [InlineData("enum")]
    public void ReceiverFreeContainerTestsAreExcluded(string kind)
    {
        var cases = kind == "enum" ? "    Value\n" : string.Empty;
        var compilation = MinimalEmissionTest.Analyze($"{kind} Container\n{cases}    #Test\n    func test() => Tests.missing()\nwriteLine(\"product\")");
        Assert.True(compilation.Binding.Result.IsComplete, MinimalEmissionTest.Describe(compilation, null));
        Assert.True(compilation.Ownership.Result.IsVerified);
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

    [Fact]
    public void TestMembershipDoesNotRecognizeOtherMarkers()
    {
        var compilation = MinimalEmissionTest.Analyze("#Test\n#Unknown\nfunc test() => Missing.api()\n()");
        Assert.False(compilation.Binding.Result.IsComplete);
        Assert.Contains(compilation.Binding.Issues, x => x.Code == DiagnosticCode.UnsupportedBinding_Kd);
        Assert.DoesNotContain(compilation.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd);
    }

    [Fact]
    public void TestEditsDoNotChangeProductIrWhenProductLocationsAreUnchanged()
    {
        const string Product = "writeLine(\"product\")\n";
        var original = MinimalEmissionTest.Analyze(Product + "#Test\nfunc first() => Missing.first()");
        var changed = MinimalEmissionTest.Analyze(Product + "#Test\nfunc renamed()\n    loop => Missing.second()");
        var first = new StringWriter();
        var second = new StringWriter();
        Assert.True(original.Emission.WriteIr(first, out _));
        Assert.True(changed.Emission.WriteIr(second, out _));
        Assert.Equal(first.ToString(), second.ToString());
    }

    [Fact]
    public void ReloadRetainsRawTestsAndReappliesProductMembership()
    {
        var original = MinimalEmissionTest.Analyze("writeLine(\"product\")\n#Test\nfunc test() => Missing.api()");
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
        Assert.True(restored.Ownership.Analyze().IsVerified);
    }

    [Fact]
    public void RebindingReclassifiesReplacedDeclarations()
    {
        var compilation = MinimalEmissionTest.Analyze("func target() => ()\ntarget()");
        Assert.True(compilation.Ownership.Result.IsVerified);
        var original = FindFunction(compilation.Kotonoha.RootKoto, "target")!;
        var replacementSource = MinimalEmissionTest.Analyze("#Test\nfunc target() => Missing.api()\n()");
        var replacement = FindFunction(replacementSource.Kotonoha.RootKoto, "target")!;
        Assert.True(KotoHelper.Replace(original.Parent!, original, replacement));
        Assert.False(compilation.Bind().IsComplete);
        Assert.False(compilation.Ownership.Result.IsVerified);
    }

    [Fact]
    public void WarmProductMembershipAllocatesNothing()
    {
        var compilation = MinimalEmissionTest.Analyze("writeLine(\"product\")\n#Test\nfunc test() => Missing.api()");
        void Analyze()
        {
            if (!compilation.Bind().IsComplete || !compilation.Binding.CheckStartup(OutputKind.Application).IsComplete || !compilation.Ownership.Analyze().IsVerified)
            {
                throw new InvalidOperationException("Product membership analysis failed.");
            }
        }

        for (var i = 0; i < 100; i++)
        {
            Analyze();
        }

        Assert.Equal(0, AllocationMeasurement.Measure(Analyze));
    }

    private static FunctionKoto? FindFunction(Koto node, string name)
    {
        if (node is FunctionKoto function && function.Name == name)
        {
            return function;
        }

        foreach (var child in node.ChildNodes)
        {
            if (FindFunction(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
