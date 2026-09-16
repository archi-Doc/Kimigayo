// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class RefinementNameBindingTest
{
    [Theory]
    [InlineData("Origin", true)]
    [InlineData("Api.Origin", true)]
    [InlineData("::Origin", true)]
    [InlineData("::Api.Origin", true)]
    [InlineData("ref/Origin", false)]
    [InlineData("ref/::Origin", false)]
    [InlineData("(Origin)", false)]
    [InlineData("::Origin<i32>", false)]
    [InlineData("::Api.Origin<i32>", false)]
    [InlineData("::ref/Origin", false)]
    [InlineData("::(Origin)", false)]
    public void RefinementParentsAreContractNames(string parent, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public contract Origin\npublic group Api\n    public contract Origin\npublic contract Child: " + parent);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid ? BindingState.Resolved : BindingState.Invalid, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Child").BindingState);
        if (valid)
        {
            var root = c.Kotonoha.RootKoto;
            var expected = parent.Contains("Api.", StringComparison.Ordinal) ? root.NestedContainers.Single(x => x.Name == "Api").NestedContainers.Single() : root.NestedContainers.Single(x => x.Name == "Origin");
            Assert.Same(expected.BoundSymbol, root.NestedContainers.Single(x => x.Name == "Child").BoundSymbol!.Contract!.Ancestors.Single());
        }

        Assert.Equal(valid, c.Bind().IsComplete);
        Assert.Equal(valid, Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("Origin<>")]
    [InlineData("Api.Origin<>")]
    [InlineData("::Origin<>")]
    [InlineData("::Api.Origin<>")]
    public void EmptyGenericListsRemainSyntaxErrors(string parent)
    {
        var c = MinimalEmissionTest.Analyze("public contract Origin\npublic group Api\n    public contract Origin\npublic contract Child: " + parent);
        Assert.NotEmpty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Invalid, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Child").BindingState);
    }

    [Theory]
    [InlineData("Future<i32>")]
    [InlineData("::Future<i32>")]
    [InlineData("Api.Future<i32>")]
    public void MissingNamesDoNotMakeGenericParentsDeferrable(string parent)
    {
        var c = MinimalEmissionTest.Analyze("public group Api\npublic contract Child: " + parent);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.True(c.Binding.Bind(BindingMode.Provisional).InvalidCount > 0);
        Assert.Equal(BindingState.Invalid, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Child").BindingState);
    }

    [Fact]
    public void WarmRootQualifiedParentBindingAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("public group Api\n    public contract Origin\npublic contract Child: ::Api.Origin");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Qualified parent binding failed.");
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
}
