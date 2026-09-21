// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AssociatedProjectionCertificateBindingTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LateInvalidWitnessCannotCertifyAssociatedDefinition(int form)
    {
        var c = MinimalEmissionTest.Analyze(Prefix("internal") + Consumer(form));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.All(Definition(c).Paths, path => Assert.False(path.IsVerified));
        Assert.False(c.Bind().IsComplete);
        Assert.All(Definition(c).Paths, path => Assert.False(path.IsVerified));
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.All(Definition(restored).Paths, path => Assert.False(path.IsVerified));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ValidNestedDefinitionsPreserveCertificates(int form)
    {
        var c = MinimalEmissionTest.Analyze(Prefix("public") + Consumer(form).Replace("Source.Origin.Item", "([2 of Source.Origin.Item], i32)", StringComparison.Ordinal));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.All(Definition(c).Paths, path => Assert.True(path.IsVerified));
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.All(Definition(restored).Paths, path => Assert.True(path.IsVerified));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ForwardWitnessDependenciesAlsoInvalidate(int form)
    {
        var c = MinimalEmissionTest.Analyze(Consumer(form) + "\n" + Prefix("internal"));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.All(Definition(c).Paths, path => Assert.False(path.IsVerified));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentAssociatedPathRemainsVerified(bool conditional)
    {
        var source = Prefix("internal") + "contract C\n    associate Item\ncontract D\n    associate Other\n" + (conditional
            ? "struct S<U>\n    Self is C when U is Copy\n        associate C.Item is Source.Origin.Item\n    Self is D when U is Owned\n        associate D.Other is i32"
            : "struct S\n    Self is C\n    associate C.Item is Source.Origin.Item\n    Self is D\n    associate D.Other is i32");
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.All(Definition(c).Paths, path => Assert.False(path.IsVerified));
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "D");
        Assert.All(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)!.Paths, path => Assert.True(path.IsVerified));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void RepairingWitnessRestoresAssociatedPath(int form)
    {
        var source = Prefix("internal") + Consumer(form);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.All(Definition(c).Paths, path => Assert.False(path.IsVerified));
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<FunctionKoto>().Single();
        var original = function.Parameters[1].Type;
        var donor = MinimalEmissionTest.Analyze(source.Replace("x: Local.Hidden.Item", "x: i32", StringComparison.Ordinal));
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<FunctionKoto>().Single().Parameters[1].Type;
        Assert.True(KotoHelper.Replace(function, original, replacement));
        Assert.True(c.Bind().IsComplete);
        Assert.All(Definition(c).Paths, path => Assert.True(path.IsVerified));
        Assert.True(KotoHelper.Replace(function, replacement, original));
        Assert.False(c.Bind().IsComplete);
        Assert.All(Definition(c).Paths, path => Assert.False(path.IsVerified));
    }

    [Fact]
    public void SelfProjectionPreservesAcyclicAssociatedEvidence()
    {
        var c = MinimalEmissionTest.Analyze("contract C\n    associate Item\n    associate Other\nstruct S\n    Self is C\n    associate C.Item is i32\n    associate C.Other is Self.C.Item");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Definition(c).IsVerified);
    }

    [Fact]
    public void WarmAssociatedProofsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Prefix("public") + Consumer(0));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Associated projection verification failed.");
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

    private static string Prefix(string access)
        => access + " contract Hidden\n    associate Item\npublic struct Local\n    Self is Hidden\n    associate Hidden.Item is i32\npublic contract Origin\n    associate Item\n    func f(self: ref/Self, x: i32) -> i32\npublic struct Source\n    Self is Origin\n    associate Origin.Item is i32\n    public func f(self: ref/Self, x: Local.Hidden.Item) -> i32 => x\n";

    private static string Consumer(int form) => form switch
    {
        1 => "contract C\n    associate Item is Source.Origin.Item\nstruct S\n    Self is C",
        2 => "contract Parent\n    associate Item is Source.Origin.Item\ncontract C: Parent\nstruct S\n    Self is C",
        3 => "contract C\n    associate Item\nstruct S<U>\n    Self is C when U is Copy\n        associate C.Item is Source.Origin.Item",
        _ => "contract C\n    associate Item\nstruct S\n    Self is C\n    associate C.Item is Source.Origin.Item",
    };

    private static BoundConformance Definition(Compilation c)
    {
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        return c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)!;
    }
}
