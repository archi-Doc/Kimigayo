// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class InferredPropertyAccessBindingTest
{
    [Theory]
    [InlineData("1@Source.C.Element")]
    [InlineData("(1, 1@Source.C.Element)")]
    [InlineData("makeArray()")]
    [InlineData("make()")]
    [InlineData("(make(), 1)")]
    public void InitializerOnlyProjectionDoesNotExposeItsSpelling(string expression)
    {
        Check("contract C\n    associate Element\npublic struct Source\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public let item = " + expression + "\n    private func make() -> Source.C.Element => 1\n    private func makeArray() -> [2 of Source.C.Element] => [1, 2]", true);
    }

    [Theory]
    [InlineData("Hidden.A", "public", false)]
    [InlineData("(1, Hidden.A)", "public", false)]
    [InlineData("makeArray()", "public", false)]
    [InlineData("make()", "public", false)]
    [InlineData("Hidden.A", "internal", true)]
    [InlineData("make()", "private", true)]
    public void InferredConcreteTypesStillUseThePropertyDomain(string expression, string access, bool valid)
    {
        var c = Check("enum Hidden\n    A\ncontract C\n    associate Element\nstruct Source\n    Self is C\n    associate C.Element is Hidden\npublic group Api\n    " + access + " let item = " + expression + "\n    private func make() -> Source.C.Element => Hidden.A\n    private func makeArray() -> [2 of Hidden] => [Hidden.A, Hidden.A]", valid);
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
    public void ReplacingInitializerRechecksTheInferredType()
    {
        var c = Check("enum Hidden\n    A\npublic group Api\n    public let item = Hidden.A", false);
        var property = Property(c);
        var original = property.InitializerKoto!;
        var replacement = Property(Check("public group Api\n    public let item = true", true)).InitializerKoto!;
        Assert.True(KotoHelper.Replace(property, original, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null) + string.Join(", ", c.Binding.Issues.Select(x => $"{x.Node.Akind}:{x.Node.BindingFailure}:{x.Node}")));
        Assert.True(property.BoundSymbol!.Property!.IsVerified);
        Assert.True(KotoHelper.Replace(property, replacement, original));
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(BindingFailure.Access, property.BindingFailure);
        Assert.False(property.BoundSymbol!.Property!.IsVerified);
    }

    [Fact]
    public void WarmInferredPropertyAccessAllocatesNothing()
    {
        var c = Check("contract C\n    associate Element\npublic struct Source\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public let item = (1, 1@Source.C.Element)", true);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Inferred Property access Binding failed.");
            }
        }));
    }

    private static PropertyKoto Property(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api").Members.OfType<PropertyKoto>().Single();

    private static Compilation Check(string source, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.True(c.Binding.Result.IsComplete == valid, MinimalEmissionTest.Describe(c, null));
        var property = Property(c);
        Assert.Equal(valid, property.BoundSymbol!.Property!.IsVerified);
        if (!valid)
        {
            Assert.Equal(BindingFailure.Access, property.BindingFailure);
            Assert.False(c.Emission.Validate(out _));
        }

        return c;
    }
}
