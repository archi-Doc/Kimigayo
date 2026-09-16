// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class BaseTypeFormationBindingTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidBaseInputCannotCertifyDerivedConformance(bool reverseOrder)
    {
        const string middle = "open struct Middle: Base<string>\n";
        const string derived = "struct S: Middle\n    Self is C\n";
        var c = MinimalEmissionTest.Analyze("open struct Base<T>\n    T is i32\ncontract C\n" + (reverseOrder ? derived + middle : middle + derived));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Invalid, Type(c).BindingState);
        Assert.False(Definition(c).IsVerified);
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Equal(BindingState.Invalid, Type(restored).BindingState);
        Assert.False(Definition(restored).IsVerified);
    }

    [Theory]
    [InlineData("Box<string>")]
    [InlineData("(Box<string>, i32)")]
    [InlineData("[2 of Box<string>]")]
    public void NestedBaseArgumentsMustBeValid(string argument)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\nopen struct Base<T>\ncontract C\nstruct S: Base<" + argument + ">\n    Self is C");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Invalid, Type(c).BindingState);
        Assert.False(Definition(c).IsVerified);
    }

    [Theory]
    [InlineData("struct S: Base<i32>\n    Self is C")]
    [InlineData("struct S<T>: Base<T>\n    T is i32\n    Self is C")]
    [InlineData("open struct Middle<T>: Base<T>\n    T is i32\nstruct S<U>: Middle<U>\n    U is i32\n    Self is C")]
    public void ValidBasesUseDeclarationEvidence(string declaration)
    {
        var c = MinimalEmissionTest.Analyze("open struct Base<T>\n    T is i32\ncontract C\n" + declaration);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Definition(c).IsVerified);
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.True(Definition(restored).IsVerified);
    }

    [Fact]
    public void InvalidSiblingDoesNotPoisonValidBase()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base<T>\n    T is i32\ncontract C\nstruct Invalid: Base<string>\nstruct S: Base<i32>\n    Self is C");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Resolved, Type(c).BindingState);
        Assert.True(Definition(c).IsVerified);
    }

    [Fact]
    public void ProvisionalBindingRejectsKnownInvalidBase()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base<T>\n    T is i32\ncontract C\nstruct S: Base<string>\n    Self is C");
        Assert.False(c.Binding.Bind(BindingMode.Provisional).IsComplete);
        Assert.Equal(BindingState.Invalid, Type(c).BindingState);
        Assert.False(Definition(c).IsVerified);
    }

    [Fact]
    public void ReplacingBaseRestoresSameCertificate()
    {
        const string source = "open struct Base<T>\n    T is i32\ncontract C\nstruct S: Base<i32>\n    Self is C";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        var definition = Definition(c);
        var structure = Type(c);
        var original = structure.Bases[0];
        var donor = MinimalEmissionTest.Analyze(source.Replace("Base<i32>", "Base<string>", StringComparison.Ordinal));
        var replacement = Type(donor).Bases[0];
        Assert.True(KotoHelper.Replace(structure, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.False(definition.IsVerified);
        Assert.True(KotoHelper.Replace(structure, replacement, original));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(definition, Definition(c));
        Assert.True(definition.IsVerified);
    }

    [Fact]
    public void WarmBaseChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base<T>\n    T is i32\ncontract C\nopen struct Middle<T>: Base<T>\n    T is i32\nstruct S<U>: Middle<U>\n    U is i32\n    Self is C");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Base Type formation failed.");
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

    private static StructKoto Type(Compilation c)
        => Assert.IsType<StructKoto>(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S"));

    private static BoundConformance Definition(Compilation c)
    {
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        return Assert.IsType<BoundConformance>(c.Binding.GetConformanceDefinition(Type(c).BoundType!, contract.BoundSymbol!));
    }
}
