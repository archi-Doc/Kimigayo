// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ClosedProjectionConstraintBindingTest
{
    private const string Head = "public contract Origin\n    associate Item\npublic struct Source\n    Self is Origin\n    associate Origin.Item is i32\n";

    [Theory]
    [InlineData("struct")]
    [InlineData("enum")]
    public void ConcreteProjectionSubjectIsProvedAtTheDeclaration(string kind)
    {
        var c = MinimalEmissionTest.Analyze(Source(kind, "Source.Origin.Item is Copy"));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Bind().IsComplete);
        Assert.True(Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("Source.Origin.Item is i32", true)]
    [InlineData("Source.Origin.Item is string", false)]
    [InlineData("Source.Origin.Item is not i32", false)]
    [InlineData("Source.Origin.Item is Copy and Owned", true)]
    [InlineData("Source.Origin.Item is string or Copy", true)]
    [InlineData("(Source.Origin.Item, bool) is Copy", true)]
    [InlineData("[2 of Source.Origin.Item] is Copy", true)]
    [InlineData("ref{static}/Source.Origin.Item is Copy", true)]
    [InlineData("[2 of Source.Origin.Item] is [3 of i32]", false)]
    public void ClosedProjectionIdentityAndCapabilitiesAreJudged(string clause, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Source("struct", clause));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid ? BindingState.Resolved : BindingState.Invalid, Target(c).BindingState);
        Assert.Equal(valid, Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QualifierInputConstraintsCannotBeErasedByProjectionNormalization(bool valid)
    {
        var source = Source("struct", "Source<" + (valid ? "i32" : "string") + ">.Origin.Item is i32")
            .Replace("struct Source\n", "struct Source<T>\n    T is Copy\n", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid ? BindingState.Resolved : BindingState.Invalid, Target(c).BindingState);
        Assert.Equal(valid, Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateInvalidConformanceRevokesOwnerAndProperty(bool reverse)
    {
        const string source = "public struct Source\n    string is Copy\n    Self is Origin\n    associate Origin.Item is i32\n";
        const string target = "public struct Target\n    Source.Origin.Item is i32\n    public computed value: i32\n        get(self: ref/Self) -> i32 => 1\n";
        var c = MinimalEmissionTest.Analyze("public contract Origin\n    associate Item\n" + (reverse ? target + source : source + target));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Invalid, Target(c).BindingState);
        Assert.False(Target(c).Members.OfType<PropertyKoto>().Single().BoundSymbol!.Property!.IsVerified);
    }

    [Theory]
    [InlineData("public", true)]
    [InlineData("internal", false)]
    public void ClosedProjectionSubjectsRetainTheirQualifierApiDomain(string access, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Source("struct", "Source.Origin.Item is Copy").Replace("public struct Source", access + " struct Source", StringComparison.Ordinal));
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        }
    }

    [Fact]
    public void ReplacingTheAssociatedSpecificationRechecksTheObligation()
    {
        var c = MinimalEmissionTest.Analyze(Source("struct", "Source.Origin.Item is Copy"));
        var clause = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<IsKoto>().Single();
        var original = clause.Right;
        var donor = MinimalEmissionTest.Analyze(Source("struct", "Source.Origin.Item is Copy").Replace("Item is i32", "Item is string", StringComparison.Ordinal));
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<IsKoto>().Single().Right;
        Assert.True(KotoHelper.Replace(clause, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(BindingState.Invalid, Target(c).BindingState);
        Assert.True(KotoHelper.Replace(clause, replacement, original));
        Assert.True(c.Bind().IsComplete);
    }

    [Fact]
    public void WarmClosedProjectionProofsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source("struct", "[2 of Source.Origin.Item] is Copy"));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Closed projection proof failed.");
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

    private static string Source(string kind, string clause)
        => Head + "public " + kind + " Target\n    " + clause + (kind == "enum" ? "\n    A" : string.Empty);

    private static DeclarationContainerKoto Target(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Target");
}
