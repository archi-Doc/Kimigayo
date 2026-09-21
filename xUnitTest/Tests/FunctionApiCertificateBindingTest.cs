// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class FunctionApiCertificateBindingTest
{
    [Theory]
    [InlineData("Source.Hidden.Element", "i32")]
    [InlineData("i32", "Source.Hidden.Element")]
    public void InvalidProjectionSignatureCannotCertifyWitness(string input, string result)
    {
        var c = MinimalEmissionTest.Analyze("contract Hidden\n    associate Element\npublic struct Source\n    Self is Hidden\n    associate Hidden.Element is i32\npublic contract Export\n    func identity(self: ref/Self, value: i32) -> i32\npublic struct S\n    Self is Export\n    public func identity(self: ref/Self, value: " + input + ") -> " + result + " => value");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Export");
        Assert.False(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)?.IsVerified ?? false);
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.False(restored.Bind().IsComplete);
        Assert.False(Definition(restored).IsVerified);
    }

    [Theory]
    [InlineData("public", "public")]
    [InlineData("internal", "internal")]
    public void ValidProjectionSignaturesStillCertify(string hiddenAccess, string typeAccess)
    {
        var c = MinimalEmissionTest.Analyze(Source(hiddenAccess, typeAccess));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Definition(c).IsVerified);
    }

    [Fact]
    public void RebindingChangedSignatureInvalidatesAndRestoresWitness()
    {
        var source = Source("internal", "public").Replace("value: Source.Hidden.Element", "value: i32", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").Members.OfType<FunctionKoto>().Single();
        var original = function.ReturnType!;
        var donor = MinimalEmissionTest.Analyze(source.Replace("-> Source.Hidden.Element", "-> i32", StringComparison.Ordinal));
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").Members.OfType<FunctionKoto>().Single().ReturnType!;
        Assert.True(KotoHelper.Replace(function, original, replacement));
        Assert.True(c.Bind().IsComplete);
        var definition = Definition(c);
        Assert.True(definition.IsVerified);
        Assert.True(KotoHelper.Replace(function, replacement, original));
        Assert.False(c.Bind().IsComplete);
        Assert.False(definition.IsVerified);
        Assert.False(Definition(c).IsVerified);
        Assert.True(KotoHelper.Replace(function, original, replacement));
        Assert.True(c.Bind().IsComplete);
        Assert.True(Definition(c).IsVerified);
    }

    [Fact]
    public void WarmApiCertificateChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source("public", "public"));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("API conformance Binding failed.");
            }
        }));
    }

    private static string Source(string hiddenAccess, string typeAccess)
        => hiddenAccess + " contract Hidden\n    associate Element\npublic struct Source\n    Self is Hidden\n    associate Hidden.Element is i32\npublic contract Export\n    func identity(self: ref/Self, value: i32) -> i32\n" + typeAccess + " struct S\n    Self is Export\n    public func identity(self: ref/Self, value: Source.Hidden.Element) -> Source.Hidden.Element => value";

    private static BoundConformance Definition(Compilation c)
    {
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Export");
        return Assert.IsType<BoundConformance>(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!));
    }
}
