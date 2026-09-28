// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class EnumContextBindingTest
{
    [Theory]
    [InlineData("#Unknown", "A", "Invalid.E.A")]
    [InlineData("#Layout(\"C\")", "A", "Invalid.E.A")]
    [InlineData("#Unknown", "A(i32)", "Invalid.E.A(1)")]
    [InlineData("#Unknown", "A(i32)", ".A(1)")]
    [InlineData("#Unknown", "A", ".A")]
    public void InvalidOwnerContextCannotPublishConstruction(string attribute, string @case, string expression)
    {
        var c = MinimalEmissionTest.Analyze(attribute + "\ngroup Invalid\n    public enum E\n        " + @case + "\ngroup Consumer\n    func make() -> Invalid.E => " + expression);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        var use = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single().ExpressionBody!;
        Assert.False(c.Binding.TryGetEnumConstruction(use, out _));
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Binding.TryGetEnumConstruction(use, out _));
    }

    [Fact]
    public void EarlyPropertyInitializerCannotRetainInvalidConstruction()
    {
        var c = MinimalEmissionTest.Analyze("#Unknown\ngroup Invalid\n    public enum E\n        A(i32)\ngroup Consumer\n    let value = Invalid.E.A(1)");
        Assert.False(c.Binding.Result.IsComplete);
        var property = Assert.IsType<PropertyKoto>(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.Single());
        Assert.False(c.Binding.TryGetEnumConstruction(property.InitializerKoto!, out _));
    }

    [Fact]
    public void RebindingChangedOwnerContextInvalidatesAndRestoresPlans()
    {
        var c = MinimalEmissionTest.Analyze("group Invalid\n    public enum E\n        A(i32)\ngroup Consumer\n    func make() -> Invalid.E => Invalid.E.A(1)");
        var use = Use(c);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Binding.TryGetEnumConstruction(use, out var initial));
        c.Kotonoha.AddSource(new SourceDocument("marker.kimi", "#Unknown\ngroup Invalid"));
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Binding.TryGetEnumConstruction(use, out _));
        var owner = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Invalid");
        Assert.True(owner.RemoveAttribute(Assert.IsType<AttributeKoto>(owner.AttributeChain)));
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.TryGetEnumConstruction(use, out var restored));
        Assert.Same(initial, restored);
    }

    [Fact]
    public void ReloadRetainsInvalidConstructedEnumOwner()
    {
        var c = MinimalEmissionTest.Analyze("#Unknown\ngroup Invalid\n    public enum E<T>\n        A(T)\ngroup Consumer\n    func make() -> Invalid.E<i32> => .A(1)");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Binding.TryGetEnumConstruction(Use(c), out _));
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.False(restored.Bind().IsComplete);
        Assert.False(restored.Binding.TryGetEnumConstruction(Use(restored), out _));
    }

    [Fact]
    public void UnrelatedInvalidGroupDoesNotDiscardValidPlans()
    {
        var c = MinimalEmissionTest.Analyze("#Unknown\ngroup Invalid\nenum E\n    A\ngroup Consumer\n    func make() -> E => .A");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.True(c.Binding.TryGetEnumConstruction(Use(c), out _));
    }

    [Fact]
    public void WarmOwnerContextChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("group Valid\n    public enum E\n        A(i32)\ngroup Consumer\n    func make() -> Valid.E => .A(1)");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Enum owner context Binding failed.");
            }
        }));
    }

    private static Koto Use(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single().ExpressionBody!;
}
