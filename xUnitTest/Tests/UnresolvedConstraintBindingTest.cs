// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class UnresolvedConstraintBindingTest
{
    [Theory]
    [InlineData("public struct Target\n    Future is Copy", "public struct Future\n    Self is Copy", true)]
    [InlineData("public contract Target\n    Future is Copy", "public struct Future\n    Self is Copy", true)]
    [InlineData("public enum Target\n    Future is Copy\n    A", "public struct Future\n    Self is Copy", true)]
    [InlineData("public struct Target\n    i32 is Future", "public struct Future", false)]
    [InlineData("public struct Target<T>\n    T is Future", "public contract Future", true)]
    [InlineData("group G\n    func take<T>()\n        T is Future\n        ()", "public contract Future", true)]
    [InlineData("public struct Target\n    [2 of Future] is Copy", "public struct Future\n    Self is Copy", true)]
    [InlineData("public struct Target\n    (Future, i32) is Copy", "public struct Future\n    Self is Copy", true)]
    [InlineData("public struct Target\n    (Future) -> i32 is Owned", "public struct Future", true)]
    [InlineData("public struct Target<T>\n    T is [2 of Future]", "public struct Future", true)]
    [InlineData("public struct Target\n    Self is Future", "public contract Future", true)]
    [InlineData("public struct Target<T>\n    T is not Future", "public contract Future", true)]
    public void MissingDeclarationsRemainUnresolvedUntilFinalBinding(string source, string generated, bool valid)
    {
        var c = Parse(source);
        var provisional = c.Binding.Bind(BindingMode.Provisional);
        Assert.Equal(0, provisional.InvalidCount);
        Assert.True(provisional.UnresolvedCount > 0);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", generated));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Generated.kimi").GetArray());
        Assert.Equal(valid, c.Bind().IsComplete);
        Assert.Equal(valid, Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("public struct Target\n    Future is Copy")]
    [InlineData("public contract Target\n    Future is Copy")]
    [InlineData("public enum Target\n    Future is Copy\n    A")]
    [InlineData("public struct Target<T>\n    T is Future")]
    [InlineData("group G\n    func take<T>()\n        T is Future\n        ()")]
    public void FinalBindingRejectsNamesThatRemainMissing(string source)
    {
        var c = Parse(source);
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure is BindingFailure.MissingType or BindingFailure.InvalidConstraint);
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.False(Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("public group Future\npublic struct Target\n    i32 is Future")]
    [InlineData("public group Invalid\npublic struct Target\n    (Future, Invalid) is Copy")]
    [InlineData("public group Invalid\npublic struct Target<T>\n    T is Future or Invalid")]
    [InlineData("public struct Target<s/T>\n    s is Future")]
    public void AvailableViolationsRemainInvalidInProvisionalBinding(string source)
    {
        var c = Parse(source);
        Assert.True(c.Binding.Bind(BindingMode.Provisional).InvalidCount > 0);
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("Copy or Future")]
    [InlineData("Future or Copy")]
    [InlineData("not ((not Copy) and Future)")]
    public void ATrueBooleanBranchDoesNotCompleteAnUnformedRequirement(string requirement)
    {
        var c = Parse("public contract Marker\npublic struct Target\n    i32 is " + requirement + "\n    Self is Marker");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var target = Container(c, "Target");
        Assert.Equal(BindingState.Unresolved, target.BindingState);
        Assert.False(c.Binding.GetConformanceDefinition(target.BoundType!, Container(c, "Marker").BoundSymbol!)!.IsVerified);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public contract Future"));
        Assert.True(c.Bind().IsComplete, string.Join(", ", c.Binding.Issues.Select(x => $"{x.Node.Akind}:{x.Node.BindingFailure}:{x.Node.Span}")));
        Assert.True(Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("Future", false)]
    [InlineData("Future or Copy", true)]
    public void MissingFunctionRequirementsCannotPublishCompletedCalls(string requirement, bool valid)
    {
        var c = Parse("group G\n    func take<T>()\n        T is " + requirement + "\n        ()\n    func call() => take<i32>()");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var function = Container(c, "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "take");
        Assert.Equal(BindingState.Unresolved, function.BindingState);
        var call = Assert.IsType<InvocationKoto>(Container(c, "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "call").ExpressionBody);
        Assert.Null(call.BoundCall);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public contract Future"));
        Assert.Equal(valid, c.Bind().IsComplete);
        Assert.Equal(valid, call.BoundCall is not null);
    }

    [Fact]
    public void MissingPropositionsAreNotAddedAsAssumptions()
    {
        var c = Parse("public struct Target<T>\n    T is Future\n    T is not Other");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var target = Container(c, "Target");
        foreach (var clause in target.ConstraintNodes)
        {
            Assert.Equal(ConstraintProof.Unknown, c.Binding.Prove(clause.BoundConstraint!, target));
        }

        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public contract Future\npublic contract Other"));
        Assert.True(c.Bind().IsComplete);
    }

    [Fact]
    public void FunctionSubjectRestrictionsRemainDiagnostics()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", "group G\n    func take<T>()\n        Future is Copy\n        ()"));
        Assert.NotEmpty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.True(c.Binding.Bind(BindingMode.Provisional).InvalidCount > 0);
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void WarmMissingNamePassesAllocateNothing()
    {
        var c = Parse("public struct Target\n    [2 of Future] is Copy");
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (c.Binding.Bind(BindingMode.Provisional).InvalidCount != 0)
            {
                throw new InvalidOperationException("Missing constraint declaration became invalid.");
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

    private static DeclarationContainerKoto Container(Compilation c, string name)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == name);

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        return c;
    }
}
