// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AssociatedSubjectConstraintBindingTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructedTypeMustSatisfyAssociatedSubjectConstraint(bool enumeration)
    {
        var c = MinimalEmissionTest.Analyze(Source(enumeration, "string", "i32"));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.UnsatisfiedConstraint);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
    }

    [Theory]
    [InlineData(false, "i32", "i32", true)]
    [InlineData(true, "i32", "i32", true)]
    [InlineData(false, "i32", "Copy", true)]
    [InlineData(true, "i32", "Copy", true)]
    [InlineData(false, "string", "Copy", false)]
    [InlineData(true, "string", "Copy", false)]
    [InlineData(false, "string", "i32 or string", true)]
    [InlineData(true, "string", "i32 or string", true)]
    [InlineData(false, "string", "not i32", true)]
    [InlineData(true, "string", "not i32", true)]
    public void SubstitutedSubjectUsesOrdinaryRequirementProofs(bool enumeration, string item, string requirement, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Source(enumeration, item, requirement));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        var restored = Reload(c);
        Assert.Equal(valid, restored.Bind().IsComplete);
    }

    [Theory]
    [InlineData("(Box<Source>, i32)")]
    [InlineData("[2 of Box<Source>]")]
    [InlineData("unsafe/Box<Source>")]
    public void NestedTypeFormationCannotBypassTheSubjectClause(string type)
    {
        var c = MinimalEmissionTest.Analyze(Source(false, "string", "i32").Replace("x: Box<Source>", "x: " + type, StringComparison.Ordinal));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.UnsatisfiedConstraint);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GenericCallArgumentsRequireTheAssociatedSubjectClause(bool enumeration, bool valid)
    {
        var source = Source(enumeration, valid ? "i32" : "string", "i32");
        source = source[..source.IndexOf("group Consumer", StringComparison.Ordinal)] + "group Consumer\n    func take<U>() => ()\n    func call() => take<Box<Source>>()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, Assert.IsType<InvocationKoto>(Function(c, "call").ExpressionBody).BoundCall is not null);
        var restored = Reload(c);
        Assert.Equal(valid, restored.Bind().IsComplete);
        Assert.Equal(valid, Assert.IsType<InvocationKoto>(Function(restored, "call").ExpressionBody).BoundCall is not null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnumConstructionRequiresTheAssociatedSubjectClause(bool valid)
    {
        var source = Source(true, valid ? "i32" : "string", "i32").Replace("func accept(x: Box<Source>) => ()", "func make() -> Box<Source> => .Empty", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, c.Binding.TryGetEnumConstruction(Function(c, "make").ExpressionBody!, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DependentUsesRetainCallerProjectionEvidence(bool enumeration)
    {
        var source = Source(enumeration, "string", "i32").Replace("func accept(x: Box<Source>) => ()", "func accept<U>(x: Box<U>)\n        U is Origin\n        U.Origin.Item is i32\n        ()", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void ChangingAConcreteInputInvalidatesAndRestoresTheSignature()
    {
        var source = Source(false, "i32", "i32") + "\nstruct Other\n    Self is Origin\n    associate Origin.Item is string";
        var c = MinimalEmissionTest.Analyze(source);
        var function = Function(c, "accept");
        var original = function.Parameters[0].Type;
        var donor = MinimalEmissionTest.Analyze(source.Replace("Box<Source>", "Box<Other>", StringComparison.Ordinal));
        var replacement = Function(donor, "accept").Parameters[0].Type;
        Assert.True(KotoHelper.Replace(function, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.True(KotoHelper.Replace(function, replacement, original));
        Assert.True(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmAssociatedSubjectChecksAllocateNothing(bool enumeration)
    {
        var c = MinimalEmissionTest.Analyze(Source(enumeration, "i32", "Copy and i32"));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Associated subject constraint check failed.");
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

    private static FunctionKoto Function(Compilation c, string name)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single(x => x.Name == name);

    private static string Source(bool enumeration, string item, string requirement)
        => "contract Origin\n    associate Item\nstruct Source\n    Self is Origin\n    associate Origin.Item is " + item + "\n" + (enumeration ? "enum" : "struct") + " Box<T>\n    T is Origin\n    T.Origin.Item is " + requirement + (enumeration ? "\n    Empty" : string.Empty) + "\ngroup Consumer\n    func accept(x: Box<Source>) => ()";
}
