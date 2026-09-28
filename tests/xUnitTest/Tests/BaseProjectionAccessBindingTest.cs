// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class BaseProjectionAccessBindingTest
{
    [Theory]
    [InlineData("public", "internal")]
    [InlineData("internal", "public")]
    public void NormalizationCannotHideBaseProjectionAccess(string sourceAccess, string contractAccess)
    {
        var c = MinimalEmissionTest.Analyze($"public open struct Base\n{contractAccess} contract C\n    associate Element\n{sourceAccess} struct Source\n    Self is C\n    associate C.Element is Base\npublic struct Api: Source.C.Element");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("Source.Element")]
    [InlineData("GenericBase<Source.C.Element>")]
    [InlineData("GenericBase<(i32, Source.C.Element)>")]
    public void NestedBaseProjectionsRetainAccess(string type)
        => Check(Prefix + "public open struct GenericBase<T>\npublic struct Api: " + type, false);

    [Theory]
    [InlineData("internal", "public", true)]
    [InlineData("private", "public", true)]
    [InlineData("public", "internal", true)]
    [InlineData("public", "public", false)]
    public void BaseUsesEffectiveDerivedDomain(string outerAccess, string typeAccess, bool valid)
        => Check(Prefix + outerAccess + " group Outer\n    " + typeAccess + " struct Api: Source.C.Element", valid);

    [Theory]
    [InlineData("internal", false)]
    [InlineData("public", true)]
    public void ConstructedQualifierRetainsArgumentAccess(string access, bool valid)
        => Check("public open struct Base\npublic contract C\n    associate Element\n" + access + " struct Argument\npublic struct Source<T>\n    Self is C\n    associate C.Element is Base\npublic struct Api: Source<Argument>.C.Element", valid);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidBasePropagatesToDescendantsAndConformances(bool reverseOrder)
    {
        var middle = "public open struct Middle: Source.C.Element\n";
        var derived = "public struct Api: Middle\n    Self is Marker\n";
        var c = Check(Prefix + "public contract Marker\n" + (reverseOrder ? derived + middle : middle + derived), false);
        var api = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api");
        var marker = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Marker");
        Assert.Equal(BindingState.Invalid, api.BindingState);
        Assert.False(c.Binding.GetConformanceDefinition(api.BoundType!, marker.BoundSymbol!)?.IsVerified ?? false);
    }

    [Theory]
    [InlineData("public", false)]
    [InlineData("internal", true)]
    public void ReloadAndRebindRetainBaseAccess(string access, bool valid)
    {
        var c = Check(Prefix + access + " struct Api: Source.C.Element", valid);
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
    public void ReplacingBaseRechecksProjectionAccess()
    {
        var c = Check(Prefix + "public struct Api: Source.C.Element", false);
        var api = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api");
        var original = api.Bases[0];
        var replacement = Check("public open struct Base\npublic struct Api: Base", true).Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api").Bases[0];
        Assert.True(KotoHelper.Replace(api, original, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(KotoHelper.Replace(api, replacement, original));
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(BindingFailure.Access, api.BindingFailure);
    }

    [Fact]
    public void WarmBaseAccessChecksAllocateNothing()
    {
        var c = Check(Prefix + "internal struct Api: Source.C.Element", true);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Base projection access Binding failed.");
            }
        }));
    }

    private const string Prefix = "public open struct Base\ncontract C\n    associate Element\npublic struct Source\n    Self is C\n    associate C.Element is Base\n";

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
