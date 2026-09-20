// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class SignatureInputFormationBindingTest
{
    [Theory]
    [InlineData("string", false, false)]
    [InlineData("string", true, false)]
    [InlineData("i32", false, true)]
    [InlineData("i32", true, true)]
    public void NormalizedFunctionInputsControlCallValidity(string argument, bool result, bool valid)
    {
        var projected = "Source<" + argument + ">.Origin.Item";
        var c = MinimalEmissionTest.Analyze(Prefix + "group G\n    func take(x?: " + (result ? "i32" : projected) + ") -> " + (result ? projected : "i32") + " => x\n    func call() -> i32 => take(1)");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, Call(c).BoundCall is not null);
        Assert.Equal(valid, c.Bind().IsComplete);
        var restored = Reload(c);
        Assert.Equal(valid, restored.Bind().IsComplete);
        Assert.Equal(valid, Call(restored).BoundCall is not null);
    }

    [Theory]
    [InlineData("Source<string>.Origin.Item", false)]
    [InlineData("(Source<string>.Origin.Item, i32)", false)]
    [InlineData("[2 of Source<string>.Origin.Item]", false)]
    [InlineData("Source<i32>.Origin.Item", true)]
    [InlineData("(Source<i32>.Origin.Item, i32)", true)]
    [InlineData("[2 of Source<i32>.Origin.Item]", true)]
    public void NormalizedPropertyInputsControlCertificateValidity(string type, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "struct S\n    var value: " + type);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, Property(c).IsVerified);
        Assert.Equal(valid, c.Bind().IsComplete);
        var restored = Reload(c);
        Assert.Equal(valid, restored.Bind().IsComplete);
        Assert.Equal(valid, Property(restored).IsVerified);
    }

    [Theory]
    [InlineData("get() -> Source<string>.Origin.Item => 1", false)]
    [InlineData("get() -> i32 => 1\n        set(value: Source<string>.Origin.Item) -> () => ()", false)]
    [InlineData("get() -> Source<i32>.Origin.Item => 1", true)]
    [InlineData("get() -> i32 => 1\n        set(value: Source<i32>.Origin.Item) -> () => ()", true)]
    public void AdditionalAccessorInputsAreChecked(string accessors, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "group S\n    computed value: i32\n        " + accessors);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, Property(c).IsVerified);
    }

    [Theory]
    [InlineData("func f(x?: i32) -> i32", "public func f(x?: Source<string>.Origin.Item) -> i32 => x")]
    [InlineData("property value: i32 has get", "public var value: Source<string>.Origin.Item")]
    public void InvalidSignatureInputsCannotSupplyConformanceWitnesses(string requirement, string implementation)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "contract C\n    " + requirement + "\nstruct S\n    Self is C\n    " + implementation);
        Assert.False(c.Binding.Result.IsComplete);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        Assert.False(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)!.IsVerified);
    }

    [Theory]
    [InlineData("struct S<U>\n    U is i32\n    var value: Source<U>.Origin.Item")]
    [InlineData("group G\n    func take<U>(x?: Source<U>.Origin.Item) -> i32\n        U is i32\n        return x\n    func call() -> i32 => take<i32>(1)")]
    public void DependentInputsUseTheDefiningScope(string declaration)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + declaration);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void ReplacingInputRestoresSameCallPlan()
    {
        const string source = Prefix + "group G\n    func take(x?: Source<i32>.Origin.Item) -> i32 => x\n    func call() -> i32 => take(1)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        var plan = Call(c).BoundCall;
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "take");
        var original = function.Parameters[0].Type;
        var donor = MinimalEmissionTest.Analyze(source.Replace("Source<i32>", "Source<string>", StringComparison.Ordinal));
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "take").Parameters[0].Type;
        Assert.True(KotoHelper.Replace(function, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.Null(Call(c).BoundCall);
        Assert.True(KotoHelper.Replace(function, replacement, original));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(plan, Call(c).BoundCall);
    }

    [Fact]
    public void WarmSignatureInputChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "struct S\n    var value: Source<i32>.Origin.Item\ngroup G\n    func take(x?: Source<i32>.Origin.Item) -> i32 => x\n    func call() -> i32 => take(1)");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Signature input formation failed.");
            }
        }));
    }

    private const string Prefix = "contract Origin\n    associate Item\nstruct Source<T>\n    T is i32\n    Self is Origin\n    associate Origin.Item is i32\n";

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

    private static InvocationKoto Call(Compilation c)
        => Assert.IsType<InvocationKoto>(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "call").ExpressionBody);

    private static BoundProperty Property(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").Members.OfType<PropertyKoto>().Single().BoundSymbol!.Property!;
}
