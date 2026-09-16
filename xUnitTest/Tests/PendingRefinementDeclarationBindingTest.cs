// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class PendingRefinementDeclarationBindingTest
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PendingAncestorWithholdsChildCompletion(bool reverse, bool missing)
    {
        const string child = "public contract Child: Marker\n";
        var marker = "public contract Marker\n    Source is " + (missing ? "Future" : "Origin") + "\n";
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", "public contract Origin\npublic struct Source\n" + (reverse ? child + marker : marker + child) + "group G\n    func take<T>()\n        T is Child\n        ()\n    func inspect<T>(value: T)\n        T is Child\n        take<T>()"));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.Equal(BindingState.Unresolved, Container(c, "Child").BindingState);
        var function = Container(c, "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "inspect");
        Assert.Equal(ConstraintProof.Unknown, c.Binding.Prove(Assert.IsType<IsKoto>(function.TypeConstraints[0]).BoundConstraint!, function));
        Assert.Null(Assert.IsType<InvocationKoto>(function.Body!.Items.Single()).BoundCall);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", (missing ? "public contract Future\n" : string.Empty) + "public struct Source\n    Self is " + (missing ? "Future" : "Origin")));
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(BindingState.Resolved, Container(c, "Child").BindingState);
        Assert.NotNull(Assert.IsType<InvocationKoto>(function.Body!.Items.Single()).BoundCall);
        Assert.True(Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiamondRefinementsPropagatePendingState(bool reverse)
    {
        var c = Parse("public contract Origin\npublic struct Source\npublic contract Marker\n    Source is Origin\npublic contract Left: Marker\npublic contract Right: Marker\npublic contract Child: " + (reverse ? "Right, Left" : "Left, Right") + "\npublic contract Ready");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.Equal(BindingState.Unresolved, Container(c, "Child").BindingState);
        Assert.Equal(BindingState.Resolved, Container(c, "Ready").BindingState);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public struct Source\n    Self is Origin"));
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(BindingState.Resolved, Container(c, "Child").BindingState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalAbsenceFailsAndProvisionalRebindingRestoresPendingState(bool missing)
    {
        var c = Parse("public contract Origin\npublic struct Source\npublic contract Marker\n    Source is " + (missing ? "Future" : "Origin") + "\npublic contract Child: Marker");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(BindingState.Invalid, Container(c, "Child").BindingState);
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.Equal(BindingState.Unresolved, Container(c, "Child").BindingState);
        var restored = Reload(c);
        Assert.Equal(0, restored.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.Equal(BindingState.Unresolved, Container(restored, "Child").BindingState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidAncestorsTakePriorityOverPendingAncestors(bool reverse)
    {
        var c = Parse("public contract Origin\npublic struct Source\npublic contract Pending\n    Source is Origin\npublic contract Broken\n    i32 is string\npublic contract Child: " + (reverse ? "Broken, Pending" : "Pending, Broken"));
        Assert.True(c.Binding.Bind(BindingMode.Provisional).InvalidCount > 0);
        Assert.Equal(BindingState.Invalid, Container(c, "Child").BindingState);
    }

    [Theory]
    [InlineData("struct")]
    [InlineData("enum")]
    public void ChildConformanceWaitsForInheritedPrerequisites(string kind)
    {
        var c = Parse("public contract Origin\npublic struct Source\npublic contract Marker\n    Source is Origin\npublic contract Child: Marker\npublic " + kind + " Target\n    Self is Child" + (kind == "enum" ? "\n    Item" : string.Empty));
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var child = Container(c, "Child");
        var target = Container(c, "Target");
        Assert.False(c.Binding.GetConformanceDefinition(target.BoundType!, child.BoundSymbol!)!.IsVerified);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public struct Source\n    Self is Origin"));
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.GetConformanceDefinition(target.BoundType!, child.BoundSymbol!)!.IsVerified);
    }

    [Fact]
    public void WarmAncestorPropagationAllocatesNothing()
    {
        var c = Parse("public contract Origin\npublic struct Source\npublic contract Marker\n    Source is Origin\npublic contract Child: Marker");
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (c.Binding.Bind(BindingMode.Provisional).InvalidCount != 0)
            {
                throw new InvalidOperationException("Pending ancestor became invalid.");
            }
        }));
    }

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        return c;
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
}
