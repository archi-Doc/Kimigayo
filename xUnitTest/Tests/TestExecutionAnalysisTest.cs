// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class TestExecutionAnalysisTest
{
    [Theory]
    [InlineData("#Test\nfunc sample()\n    $expect(true)\n    $require(false, message: \"failed\")")]
    [InlineData("#Test\nfunc sample()\n    $expect(1 == 2, message: \"failed\")\n    $expect(true)")]
    [InlineData("$abort(\"startup\")\n#Test\nfunc sample()\n    $expect(true)")]
    [InlineData("#Test\nfunc sample()\n    Console.writeLine(Test.tempDirectory())\n    $expect(true)")]
    public void TestBodiesAreVerifiedWithoutSelectingProductStartup(string source)
    {
        var c = Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        c.Tests.Discover(c);
        Assert.Single(c.Tests.Cases);
        Assert.NotEmpty(c.Tests.Sites);
        Assert.Null(c.Binding.Startup.Function);
        var output = new StringWriter();
        Assert.True(c.Emission.WriteIr(output, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.Contains("@__kimi_test_observe", output.ToString());
    }

    [Theory]
    [InlineData("#Test\nfunc sample() => Missing.call()")]
    [InlineData("Missing.startup()\n#Test\nfunc sample() => ()")]
    [InlineData("#Test\nfunc sample()\n    $expect(1)")]
    [InlineData("#Test\nfunc sample()\n    $require(true, message: 1)")]
    [InlineData("func ordinary()\n    $expect(true)\n#Test\nfunc sample() => ()")]
    [InlineData("#Test\nfunc sample() => ()\n#Test\nfunc another() => sample()")]
    public void InvalidUnselectedBodiesStillFail(string source)
        => Assert.False(Analyze(source).Binding.Result.IsComplete);

    [Theory]
    [InlineData("$expect(true)")]
    [InlineData("defer => $expect(false)")]
    [InlineData("$require(true, message: (message: do => exit to message: \"message\"))")]
    [InlineData("$expect((if true => return else => false))")]
    [InlineData("$expect((do => return))")]
    [InlineData("$expect(false, message: $abort(\"message\"))")]
    [InlineData("$expect(false, message: (message: do\n        $expect(false)\n        exit to message: \"message\"\n    ))")]
    [InlineData("let f = func () -> ()\n        $expect(true)\n    f()")]
    [InlineData("func local()\n        $expect(true)\n    local()")]
    public void VerificationPreservesBodiesAndTransfers(string body)
    {
        var c = Analyze("#Test\nfunc sample()\n    " + body);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        c.Tests.Discover(c);
        Assert.True(c.Emission.Validate(out var error), MinimalEmissionTest.Describe(c, error));
    }

    [Theory]
    [InlineData("$expect(true, message: (do => return))")]
    [InlineData("$expect(true,)")]
    [InlineData("$expect(condition: true)")]
    [InlineData("$expect(true, \"message\")")]
    [InlineData("let value = $expect(true)")]
    [InlineData("$expect(true")]
    public void VerificationRejectsInvalidSyntaxAndEscapingMessages(string body)
    {
        var c = Analyze("#Test\nfunc sample()\n    " + body);
        Assert.True(c.Kotonoha.HasSourceErrors || !c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("expect", false)]
    [InlineData("require", true)]
    public void LazyMessageMoveOnlyReachesExpectContinuation(string operation, bool verified)
    {
        var c = Analyze("#Test\nfunc sample()\n    let text = \"message\"\n    $" + operation + "(true, message: text@move)\n    Console.writeLine(text)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Equal(verified, c.Ownership.Result.IsVerified);
    }

    [Fact]
    public void TestOnlyGenericHelperUsesSharedLowering()
    {
        var c = Compilation.CreateForTest();
        c.IsTestBuild = true;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Helpers.kimi", "func helper<T>(x: T)\n    $expect(false, message: \"shared\")\n    $require(true)\n#Test\nfunc sample() => helper(1)", isTestOnly: true));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        c.Binding.CheckTestStartup();
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        c.Tests.Discover(c);
        Assert.True(c.Emission.Validate(out var error), MinimalEmissionTest.Describe(c, error));
    }

    [Theory]
    [InlineData("func product() => G.helper()", false)]
    [InlineData("#Test\nfunc sample() => G.helper()", true)]
    [InlineData("group G\n    func product() => helper()", false)]
    [InlineData("group G\n    #Test\n    func sample() => helper()", true)]
    public void TestSourcesDoNotBecomeProductDependencies(string source, bool valid)
    {
        var c = Compilation.CreateForTest();
        c.IsTestBuild = true;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Product.kimi", source));
        c.Kotonoha.AddSource(new SourceDocument("Helpers.kimi", "group G\n    public func helper() => ()", isTestOnly: true));
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void ModeChangesInvalidatePriorCertificates()
    {
        var c = Analyze("Console.writeLine(\"product\")\n#Test\nfunc sample() => $expect(true)");
        Assert.True(c.Ownership.Result.IsVerified);
        c.IsTestBuild = false;
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified);
        var output = new StringWriter();
        Assert.True(c.Emission.WriteIr(output, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.DoesNotContain("__kimi_test_observe", output.ToString());
    }

    internal static Compilation Analyze(string source)
    {
        var c = Compilation.CreateForTest();
        c.IsTestBuild = true;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Tests.kimi", source));
        c.Bind();
        c.Binding.CheckTestStartup();
        c.Ownership.Analyze();
        return c;
    }
}
