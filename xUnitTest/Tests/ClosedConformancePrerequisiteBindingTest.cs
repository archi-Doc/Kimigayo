// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ClosedConformancePrerequisiteBindingTest
{
    [Theory]
    [InlineData("struct", false)]
    [InlineData("struct", true)]
    [InlineData("enum", false)]
    [InlineData("enum", true)]
    public void ClosedDeclarationPrerequisitesNeedFiniteEvidence(string kind, bool cycle)
    {
        var extra = kind == "enum" ? "\n    Item" : string.Empty;
        var c = MinimalEmissionTest.Analyze("public contract C\npublic " + kind + " A\n    B is C\n    Self is C" + extra + "\npublic " + kind + " B\n    " + (cycle ? "A is C\n    " : string.Empty) + "Self is C" + extra);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(!cycle, c.Binding.Result.IsComplete);
        var contract = Container(c, "C");
        Assert.Equal(!cycle, c.Binding.GetConformanceDefinition(Container(c, "A").BoundType!, contract.BoundSymbol!)!.IsVerified);
        Assert.Equal(!cycle, c.Bind().IsComplete);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.Equal(!cycle, restored.Bind().IsComplete);
    }

    [Fact]
    public void NamedSelfPrerequisiteCannotCertifyItself()
    {
        var c = MinimalEmissionTest.Analyze("public contract C\npublic struct A\n    A is C\n    Self is C");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Binding.GetConformanceDefinition(Container(c, "A").BoundType!, Container(c, "C").BoundSymbol!)!.IsVerified);
    }

    [Theory]
    [InlineData("or", true)]
    [InlineData("and", false)]
    public void IndependentFiniteEvidenceCanDischargeARecursiveQuery(string operation, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public contract C\npublic struct A\n    B is C\n    Self is C\npublic struct B\n    A is C " + operation + " A\n    Self is C");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
    }

    [Fact]
    public void WarmFinitePrerequisiteVerificationAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("public contract C\npublic struct A\n    B is C\n    Self is C\npublic struct B\n    Self is C");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Finite conformance verification failed.");
            }
        }));
    }

    private static DeclarationContainerKoto Container(Compilation c, string name)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == name);
}
