// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class RuntimeTestFormationBindingTest
{
    [Theory]
    [InlineData("objref/Box<i32>", "Box<string>")]
    [InlineData("objref/Box<string>", "Box<i32>")]
    public void InvalidInputConstraintsCannotRetainRuntimeTest(string operand, string target)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ngroup G\n    func f(x?: " + operand + ") -> bool => x is " + target);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Binding.TypeSystem.IsBoundRuntimeTypeTest(Test(c)));
        Assert.Null(Test(c).BoundRuntimeTest);
        Assert.False(c.Bind().IsComplete);
        Assert.Null(Test(c).BoundRuntimeTest);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Null(Test(restored).BoundRuntimeTest);
    }

    [Theory]
    [InlineData("Box<string>")]
    [InlineData("(Box<string>, i32)")]
    [InlineData("[2 of Box<string>]")]
    public void NestedTargetArgumentsCannotRetainRuntimeTest(string argument)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\nstruct Target<T>\ngroup G\n    func f(x?: objref/Target<i32>) -> bool => x is Target<" + argument + ">");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Null(Test(c).BoundRuntimeTest);
    }

    [Theory]
    [InlineData("#Unknown\nstruct Invalid", "Invalid")]
    [InlineData("struct Hidden\npublic struct Invalid<T>\n    T is Hidden", "Invalid<Hidden>")]
    [InlineData("open struct Base<T>\n    T is i32\nstruct Invalid: Base<string>", "Invalid")]
    public void LateInvalidDeclarationsCannotRetainRuntimeTest(string declarations, string target)
    {
        var c = MinimalEmissionTest.Analyze("struct Valid\n" + declarations + "\ngroup G\n    func f(x?: objref/Valid) -> bool => x is " + target);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Binding.TypeSystem.IsBoundRuntimeTypeTest(Test(c)));
        Assert.Null(Test(c).BoundRuntimeTest);
    }

    [Theory]
    [InlineData("Box<i32>")]
    [InlineData("(Box<i32>, i32)")]
    [InlineData("[2 of Box<i32>]")]
    public void ValidNestedTargetsStillCertify(string argument)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\nstruct Target<T>\ngroup G\n    func f(x?: objref/Target<i32>) -> bool => x is Target<" + argument + ">");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Binding.TypeSystem.IsBoundRuntimeTypeTest(Test(c)));
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.True(restored.Binding.TypeSystem.IsBoundRuntimeTypeTest(Test(restored)));
    }

    [Fact]
    public void NeverDoesNotSkipTargetFormation()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ngroup G\n    func f() -> bool => (return true) is Box<string>");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Null(Test(c).BoundRuntimeTest);
    }

    [Fact]
    public void IndependentMemberErrorDoesNotInvalidateRuntimeType()
    {
        var c = MinimalEmissionTest.Analyze("struct Hidden\npublic struct Target\n    public let field: Hidden\ngroup G\n    func f(x?: objref/Target) -> bool => x is Target");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.True(c.Binding.TypeSystem.IsBoundRuntimeTypeTest(Test(c)));
        Assert.NotNull(Test(c).BoundRuntimeTest);
    }

    [Fact]
    public void ReplacingTargetRestoresRuntimePlan()
    {
        const string source = "struct Box<T>\n    T is i32\ngroup G\n    func f(x?: objref/Box<i32>) -> bool => x is Box<i32>";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        var test = Test(c);
        var target = test.BoundRuntimeTest!.Value.TargetType;
        var original = test.Right;
        var donor = MinimalEmissionTest.Analyze(source.Replace("is Box<i32>", "is Box<string>", StringComparison.Ordinal));
        var replacement = Test(donor).Right;
        Assert.True(KotoHelper.Replace(test, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.Null(test.BoundRuntimeTest);
        Assert.True(KotoHelper.Replace(test, replacement, original));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(target, test.BoundRuntimeTest!.Value.TargetType);
    }

    [Fact]
    public void WarmFormationChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ngroup G\n    func f(x?: objref/Box<i32>) -> bool => x is Box<i32>");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Runtime Type formation failed.");
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

    private static IsKoto Test(Compilation c)
        => Assert.IsType<IsKoto>(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single().ExpressionBody);
}
