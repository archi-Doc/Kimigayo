// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AssociatedOriginBindingTest
{
    [Theory]
    [InlineData("contract C\n    associate Item(a) is i32\nstruct S\n    Self is C")]
    [InlineData("contract C\n    associate Item(a)\nstruct S\n    Self is C\n    associate C.Item(b) is i32")]
    public void ConstantFamilyNormalizesAtApplication(string declarations)
    {
        var c = MinimalEmissionTest.Analyze(declarations + "\nfunc f(x: ref/i32 during source) -> S.(C).Item(source) => 42");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        Assert.Same(BoundType.I32, function.ReturnType!.BoundType);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("S.Item")]
    [InlineData("S.Item(source, source)")]
    [InlineData("S.Item(missing)")]
    [InlineData("S.Item(_)")]
    [InlineData("S(source)")]
    [InlineData("S.C.Item(source)")]
    public void InvalidApplicationsDoNotBind(string result)
    {
        var c = MinimalEmissionTest.Analyze("contract C\n    associate Item(a) is i32\nstruct S\n    Self is C\nfunc f(x: ref/i32 during source) -> " + result + " => 42");
        Assert.False(c.Binding.Result.IsComplete);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("contract C\n    associate Item(a, a)")]
    [InlineData("contract C\n    associate Item(a)\nstruct S\n    Self is C\n    associate C.Item is i32")]
    [InlineData("contract C\n    associate Item\nstruct S\n    Self is C\n    associate C.Item(a) is i32")]
    [InlineData("contract C\n    associate Item(a)\n    func f(self: ref/Self) -> Self.Item(a)")]
    [InlineData("contract C\n    associate Item(a) is i32\nstruct S\n    Self is C\n    associate C.Item(b, c) is i32")]
    public void InvalidBindersAndSpecificationsDoNotBind(string source)
        => Assert.False(MinimalEmissionTest.Analyze(source).Binding.Result.IsComplete);

    [Fact]
    public void EmitsConstantFamily()
    {
        const string source = "contract C\n    associate Item(a) is i32\nstruct S\n    Self is C\nfunc f(x: ref/i32 during source) -> S.(C).Item(source) => 42\nlet value = 1\nrequire f(value@ref) == 42 else => $abort(\"family\")\nConsole.writeLine(\"42\")";
        ScalarEmissionTest.EmitFixture("AssociatedOriginConstant", source, "42\n");
    }

    [Theory]
    [InlineData("Self.Item(a)")]
    [InlineData("Self.(C).Item(a)")]
    [InlineData("Item(a)")]
    public void MethodOriginsAreSeparateFromAssociatedBinders(string result)
    {
        var c = MinimalEmissionTest.Analyze("contract C\n    associate Item(a) is i32\n    func f(self: ref/Self during a) -> " + result + "\nstruct S\n    Self is C\n    public func f(self: ref/Self during b) -> i32 => 42");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var parameters = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<OriginApplicationKoto>().Select(x => x.ArgumentNodes.Single().BoundOrigin).ToArray();
        Assert.NotNull(parameters[0]);
        Assert.NotSame(parameters[0], parameters[1]);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("(a and b)")]
    [InlineData("static")]
    public void GenericConstantFamilyAcceptsExistingOrigins(string argument)
    {
        var c = MinimalEmissionTest.Analyze("contract C\n    associate Item(step) is i32\nfunc f<T>(x: ref/T during a, y: ref/T during b) -> T.(C).Item(" + argument + ")\n    T is C\n    return 42");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void SerializedFamilyRetainsBindingAndWarmIdentity()
    {
        var c = MinimalEmissionTest.Analyze("contract C\n    associate Item(a) is i32\nstruct S\n    Self is C\nfunc f(x: ref/i32 during a) -> S.Item(a) => 42");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.True(restored.Bind().IsComplete, MinimalEmissionTest.Describe(restored, null));
        for (var i = 0; i < 8; i++)
        {
            Assert.True(restored.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => restored.Bind()));
    }

    [Theory]
    [InlineData("contract C\n    associate Item(a)\n    func a(self: ref/Self)")]
    [InlineData("contract C<a>\n    associate Item(a)")]
    public void UnrelatedNamespacesDoNotHideOriginParameters(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void EnclosingOriginCannotBeHidden()
    {
        const string source = "struct Outer {a}\n    contract C\n        associate Item(a)";
        ParseTestHelper.ParseSuccess(source);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code.ToString() == "DuplicateBinding_Kd");
    }

    [Fact]
    public void EmitsGenericRequirementReturningAppliedFamily()
    {
        const string source = "contract C\n    associate Item(a) is i32\n    func get(self: ref/Self during a) -> Self.Item(a)\nstruct S\n    Self is C\n    public init() => ()\n    public func get(self: ref/Self during b) -> i32 => 42\nfunc read<T>(x: ref/T during a) -> T.(C).Item(a)\n    T is C\n    return x.get()\nlet value = S.init()\nrequire read(value@ref) == 42 else => $abort(\"generic family\")\nConsole.writeLine(\"generic family\")";
        ScalarEmissionTest.EmitFixture("AssociatedOriginGeneric", source, "generic family\n");
    }

    // SPEC 8.4.3: a universal family capability holds at every application, so a Copy of T.Item(b) is proven from it.
    [Fact]
    public void UniversalFamilyCapabilityHoldsAtEveryApplication()
    {
        const string source = "contract C\n    associate Item(step) is Copy\n    func get(self: ref/Self during step) -> Self.Item(step)\n" +
            "func twice<T>(x: ref/T during b) -> (T.(C).Item(b), T.(C).Item(b))\n    T is C\n    let v = x.get()\n    return (v, v)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n    origin a outlives b")]
    [InlineData("\n    T.Item(a) is i32")]
    public void UnknownFamilyDoesNotAssumeCovarianceOrUniversalIdentity(string condition)
    {
        var source = "contract C\n    associate Item(step)\nfunc f<T>(x: ref/i32 during a, y: ref/i32 during b, value: T.Item(a)) -> T.Item(b)\n    T is C" + condition + "\n    return value@move";
        ParseTestHelper.ParseSuccess(source);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
    }
}
