// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AssociatedInputFormationBindingTest
{
    [Theory]
    [InlineData("Source<string>.Origin.Item")]
    [InlineData("(Source<string>.Origin.Item, i32)")]
    [InlineData("[2 of Source<string>.Origin.Item]")]
    public void NormalizationCannotHideInvalidDefinitionInputs(string definition)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "contract C\n    associate Item\nstruct S\n    Self is C\n    associate C.Item is " + definition);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(Definition(c).IsVerified);
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.False(Definition(restored).IsVerified);
    }

    [Theory]
    [InlineData("Source<i32>.Origin.Item")]
    [InlineData("(Source<i32>.Origin.Item, i32)")]
    [InlineData("[2 of Source<i32>.Origin.Item]")]
    public void ValidNormalizedDefinitionsStillCertify(string definition)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "contract C\n    associate Item\nstruct S\n    Self is C\n    associate C.Item is " + definition);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Definition(c).IsVerified);
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.True(Definition(restored).IsVerified);
    }

    [Theory]
    [InlineData("contract C\n    associate Item is Source<string>.Origin.Item\nstruct S\n    Self is C")]
    [InlineData("contract Parent\n    associate Item is Source<string>.Origin.Item\ncontract C: Parent\nstruct S\n    Self is C")]
    [InlineData("contract C\n    associate Item\nstruct S<U>\n    Self is C when U is Copy\n        associate C.Item is Source<string>.Origin.Item")]
    public void FixedInheritedAndConditionalInputsAreChecked(string declarations)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + declarations);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.All(Definition(c).Paths, path => Assert.False(path.IsVerified));
    }

    [Theory]
    [InlineData("struct S<U>\n    U is i32\n    Self is C\n    associate C.Item is Source<U>.Origin.Item")]
    [InlineData("struct S<U>\n    Self is C when U is i32\n        associate C.Item is Source<U>.Origin.Item")]
    public void DependentProjectionInputsUsePathEvidence(string declarations)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "contract C\n    associate Item\n" + declarations);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.All(Definition(c).Paths, path => Assert.True(path.IsVerified));
    }

    [Fact]
    public void ReplacingProjectionInputRestoresSameCertificate()
    {
        const string source = Prefix + "contract C\n    associate Item\nstruct S\n    Self is C\n    associate C.Item is Source<i32>.Origin.Item";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        var definition = Definition(c);
        var clause = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").Members.OfType<IsKoto>().Single(x => x.IsAssociatedConstraint);
        var original = clause.Right;
        var donor = MinimalEmissionTest.Analyze(source.Replace("Source<i32>", "Source<string>", StringComparison.Ordinal));
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").Members.OfType<IsKoto>().Single(x => x.IsAssociatedConstraint).Right;
        Assert.True(KotoHelper.Replace(clause, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.False(definition.IsVerified);
        Assert.True(KotoHelper.Replace(clause, replacement, original));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(definition, Definition(c));
        Assert.True(definition.IsVerified);
    }

    [Fact]
    public void WarmInputChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "contract C\n    associate Item\nstruct S\n    Self is C\n    associate C.Item is Source<i32>.Origin.Item");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Associated projection input formation failed.");
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

    private static BoundConformance Definition(Compilation c)
    {
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        return Assert.IsType<BoundConformance>(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!));
    }
}
