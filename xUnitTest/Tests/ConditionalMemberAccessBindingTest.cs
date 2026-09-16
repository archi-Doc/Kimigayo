// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConditionalMemberAccessBindingTest
{
    [Theory]
    [InlineData(false, "Hidden")]
    [InlineData(false, "Source.Hidden.Item")]
    [InlineData(true, "Hidden")]
    [InlineData(true, "Source.Hidden.Item")]
    public void PublicConditionalMemberCannotExposeRestrictedPremise(bool property, string requirement)
    {
        var c = MinimalEmissionTest.Analyze(Source(property, requirement, "public"));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        AssertMember(c, property, false);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        AssertMember(restored, property, false);
    }

    [Theory]
    [InlineData(false, "internal")]
    [InlineData(false, "private")]
    [InlineData(true, "internal")]
    [InlineData(true, "private")]
    public void NarrowMemberDomainsCanPublishInternalPremises(bool property, string access)
    {
        var c = MinimalEmissionTest.Analyze(Source(property, "Source.Hidden.Item", access));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        AssertMember(c, property, true);
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
        AssertMember(restored, property, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicPremisesRemainAvailableToPublicMembers(bool property)
    {
        var source = Source(property, "([2 of Source.Hidden.Item], i32)", "public").Replace("internal contract Hidden", "public contract Hidden", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        AssertMember(c, property, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EffectiveEnclosingDomainAppliesToMembers(bool property)
    {
        var source = Source(property, "Hidden", "public").Replace("public struct S", "internal struct S", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        AssertMember(c, property, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidPublishedPremiseCannotSupplyWitness(bool property)
    {
        var requirement = property ? "property value: i32 has get" : "func value() -> i32";
        var c = MinimalEmissionTest.Analyze(Source(property, "Source.Hidden.Item", "public").Replace("internal contract C", "internal contract C\n    " + requirement, StringComparison.Ordinal));
        Assert.False(c.Binding.Result.IsComplete);
        AssertMember(c, property, false);
        Assert.False(Definition(c).IsVerified);
    }

    [Theory]
    [InlineData("public", false)]
    [InlineData("internal", true)]
    public void CallCertificateObservesPublishedPremiseAccess(string access, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Source(false, "Source.Hidden.Item", access) + "\nS<i32>.value()");
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, Assert.IsType<InvocationKoto>(c.Kotonoha.GeneratedFunction!.Body!.Items.Last()).BoundCall is not null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnusedInvalidMemberDoesNotInvalidateIndependentMembers(bool property)
    {
        var source = Source(property, "Source.Hidden.Item", "public") + "\n        internal func sibling() -> i32 => 2\n    public func outside() -> i32 => 3";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        AssertMember(c, property, false);
        Assert.True(Definition(c).IsVerified);
        Assert.Equal(BindingState.Resolved, Block(c).Items.OfType<FunctionKoto>().Single(x => x.Name == "sibling").BindingState);
        Assert.Equal(BindingState.Resolved, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").Members.OfType<FunctionKoto>().Single().BindingState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NarrowingTheMemberRestoresItsCertificate(bool property)
    {
        var c = MinimalEmissionTest.Analyze(Source(property, "Source.Hidden.Item", "public"));
        var original = Member(c);
        var donor = MinimalEmissionTest.Analyze(Source(property, "Source.Hidden.Item", "internal"));
        var replacement = Member(donor);
        var parent = Block(c);
        Assert.True(KotoHelper.Replace(parent, original, replacement));
        Assert.True(c.Bind().IsComplete);
        AssertMember(c, property, true);
        Assert.True(KotoHelper.Replace(parent, replacement, original));
        Assert.False(c.Bind().IsComplete);
        AssertMember(c, property, false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmPublishedPremiseChecksAllocateNothing(bool property)
    {
        var c = MinimalEmissionTest.Analyze(Source(property, "Source.Hidden.Item", "internal"));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Conditional member API validation failed.");
            }
        }));
    }

    private static Compilation Reload(Compilation c)
    {
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        return restored;
    }

    private static string Source(bool property, string requirement, string memberAccess)
        => "internal contract Hidden\n    associate Item\npublic struct Source\n    Self is Hidden\n    associate Hidden.Item is i32\ninternal contract C\npublic struct S<T>\n    Self is C when T is " + requirement + "\n        " + memberAccess + (property
            ? " computed value: i32\n            get(self: ref/Self) -> i32 => 1"
            : " func value() -> i32 => 1");

    private static void AssertMember(Compilation c, bool property, bool valid)
    {
        var member = Member(c);
        Assert.Equal(valid ? BindingState.Resolved : BindingState.Invalid, member.BindingState);
        if (property)
        {
            Assert.Equal(valid, Assert.IsType<PropertyKoto>(member).BoundSymbol!.Property!.IsVerified);
        }
    }

    private static CodeBlockKoto Block(Compilation c)
        => Assert.IsType<CodeBlockKoto>(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").Members.OfType<SyntaxFormKoto>().Single().Operands[2]);

    private static Koto Member(Compilation c)
        => Block(c).Items.Single(x => x.BoundSymbol?.Name == "value");

    private static BoundConformance Definition(Compilation c)
        => c.Binding.GetConformanceDefinition(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").BoundType!, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C").BoundSymbol!)!;
}
