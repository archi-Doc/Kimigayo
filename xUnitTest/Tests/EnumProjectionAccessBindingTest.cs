// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class EnumProjectionAccessBindingTest
{
    [Theory]
    [InlineData("public", "internal")]
    [InlineData("internal", "public")]
    public void NormalizationCannotHidePayloadProjectionAccess(string typeAccess, string contractAccess)
    {
        var c = MinimalEmissionTest.Analyze($"{contractAccess} contract C\n    associate Element\n{typeAccess} struct Source\n    Self is C\n    associate C.Element is i32\npublic enum Api\n    Item(Source.C.Element)");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("Source.Element")]
    [InlineData("(i32, Source.C.Element)")]
    [InlineData("[2 of Source.C.Element]")]
    [InlineData("Box<Source.C.Element>")]
    [InlineData("(Source.C.Element) -> ()")]
    public void NestedPayloadProjectionsRetainAccess(string payload)
        => Check(Prefix + "public struct Box<T>\npublic enum Api\n    Item(" + payload + ")", false);

    [Theory]
    [InlineData("internal", "public", true)]
    [InlineData("private", "public", true)]
    [InlineData("public", "internal", true)]
    [InlineData("public", "public", false)]
    public void PayloadUsesEffectiveEnumDomain(string outerAccess, string enumAccess, bool valid)
        => Check(Prefix + outerAccess + " group Outer\n    " + enumAccess + " enum Api\n        Item(Source.C.Element)", valid);

    [Theory]
    [InlineData("internal", false)]
    [InlineData("public", true)]
    public void DependentPayloadRetainsContractDomain(string access, bool valid)
        => Check(access + " contract C\n    associate Element\npublic enum Api<T>\n    T is C\n    Item(T.C.Element)", valid);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidPayloadCannotCertifyConstructionOrConformance(bool property)
    {
        var consumer = property ? "private let value = Api.Item(1)" : "private func value() -> Api => Api.Item(1)";
        var c = Check(Prefix + "public contract Marker\npublic enum Api\n    Item(Source.C.Element)\n    Self is Marker\npublic group Consumer\n    " + consumer, false);
        var api = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api");
        var marker = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Marker");
        Assert.False(c.Binding.GetConformanceDefinition(api.BoundType!, marker.BoundSymbol!)?.IsVerified ?? false);
        Assert.DoesNotContain(Walk(c.Kotonoha.RootKoto), x => c.Binding.TryGetEnumConstruction(x, out _));
    }

    [Theory]
    [InlineData("public", false)]
    [InlineData("internal", true)]
    public void ReloadAndRebindRetainPayloadAccess(string access, bool valid)
    {
        var c = Check(Prefix + access + " enum Api\n    Item(Source.C.Element)", valid);
        Assert.Equal(valid, c.Bind().IsComplete);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.Equal(valid, restored.Bind().IsComplete);
    }

    [Fact]
    public void ReplacingPayloadRechecksProjectionAccess()
    {
        var c = Check(Prefix + "public enum Api\n    Item(Source.C.Element)", false);
        var original = Payload(c).Operands[0];
        var replacement = Payload(Check("public enum Api\n    Item(i32)", true)).Operands[0];
        Assert.True(KotoHelper.Replace(Payload(c), original, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(KotoHelper.Replace(Payload(c), replacement, original));
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(BindingFailure.Access, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api").BindingFailure);
    }

    [Fact]
    public void WarmPayloadAccessChecksAllocateNothing()
    {
        var c = Check(Prefix + "internal enum Api\n    Item((Source.C.Element, [2 of Source.C.Element]))", true);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Enum payload access Binding failed.");
            }
        }));
    }

    private const string Prefix = "contract C\n    associate Element\npublic struct Source\n    Self is C\n    associate C.Element is i32\n";

    private static SyntaxFormKoto Payload(Compilation c)
        => (SyntaxFormKoto)((SyntaxFormKoto)c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api").Members[0]).Operands[1];

    private static IEnumerable<Koto> Walk(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var descendant in Walk(child))
            {
                yield return descendant;
            }
        }
    }

    private static Compilation Check(string source, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.True(c.Binding.Result.IsComplete == valid, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
            Assert.False(c.Emission.Validate(out _));
        }

        return c;
    }
}
