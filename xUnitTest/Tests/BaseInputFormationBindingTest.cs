// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class BaseInputFormationBindingTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalizedBaseCannotHideInvalidQualifier(bool reverseOrder)
    {
        const string middle = "open struct Middle: Source<string>.Origin.Item\n";
        const string child = "struct S: Middle\n    Self is C\n";
        var c = MinimalEmissionTest.Analyze(Prefix + "contract C\n" + (reverseOrder ? child + middle : middle + child));
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
    [InlineData("Source<string>.Origin.Item", false)]
    [InlineData("(Source<string>.Origin.Item, i32)", false)]
    [InlineData("[2 of Source<string>.Origin.Item]", false)]
    [InlineData("Source<i32>.Origin.Item", true)]
    [InlineData("(Source<i32>.Origin.Item, i32)", true)]
    [InlineData("[2 of Source<i32>.Origin.Item]", true)]
    public void NestedBaseProjectionArgumentsRetainValidity(string argument, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "open struct Generic<T>\ncontract C\nstruct S: Generic<" + argument + ">\n    Self is C");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, Definition(c).IsVerified);
    }

    [Theory]
    [InlineData("struct S: Source<i32>.Origin.Item\n    Self is C")]
    [InlineData("struct S<U>: Source<U>.Origin.Item\n    U is i32\n    Self is C")]
    public void ValidBaseInputsUseDeclarationEvidence(string declaration)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "contract C\n" + declaration);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Definition(c).IsVerified);
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.True(Definition(restored).IsVerified);
    }

    [Fact]
    public void ReplacingBaseQualifierRestoresSameCertificate()
    {
        const string source = Prefix + "contract C\nstruct S: Source<i32>.Origin.Item\n    Self is C";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        var definition = Definition(c);
        var structure = Type(c);
        var original = structure.Bases[0];
        var donor = MinimalEmissionTest.Analyze(source.Replace("Source<i32>", "Source<string>", StringComparison.Ordinal));
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
    public void WarmBaseInputChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "contract C\nstruct S: Source<i32>.Origin.Item\n    Self is C");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Base input formation failed.");
            }
        }));
    }

    private const string Prefix = "open struct Base\ncontract Origin\n    associate Item\nstruct Source<T>\n    T is i32\n    Self is Origin\n    associate Origin.Item is Base\n";

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
