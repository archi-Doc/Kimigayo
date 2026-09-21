// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ContainerFragmentBindingTest
{
    [Theory]
    [InlineData("group S\nstruct S {}")]
    [InlineData("struct S {}\ngroup S")]
    [InlineData("public group G\nprivate group G")]
    [InlineData("internal struct S {}\nstruct S {}")]
    [InlineData("open struct S {}\nstruct S {}")]
    [InlineData("struct S {}\nopen struct S {}")]
    [InlineData("contract C\ncontract C")]
    [InlineData("group C\ncontract C")]
    [InlineData("contract C\nrootgroup C")]
    [InlineData("struct G {}\nrootgroup G.Child")]
    [InlineData("rootgroup G.Child\nstruct G {}")]
    [InlineData("public rootgroup G\nrootgroup G")]
    [InlineData("enum E\n    A\nenum E\n    B")]
    [InlineData("open struct B {}\nstruct S {}: B\nstruct S {}: B")]
    [InlineData("open struct B {}\nopen struct C {}\nstruct S {}: B\nstruct S {}: C")]
    public void IncompatibleFragmentsAreDeclarationErrors(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Duplicate);
    }

    [Theory]
    [InlineData("group G\nprivate group G")]
    [InlineData("private struct S {}\nstruct S {}")]
    [InlineData("public group G\npublic group G")]
    [InlineData("open struct S {}\nopen struct S {}")]
    public void MatchingHeadersMerge(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Single(c.Kotonoha.RootKoto.NestedContainers);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OmittedBaseSharesTheSingleDefiningFragment(bool baseFirst)
    {
        const string Prefix = "open struct B {}\n    public func value() -> i32 => 7\n";
        var c = MinimalEmissionTest.Analyze(Prefix + (baseFirst ? "struct S {}: B\nstruct S {}\n" : "struct S {}\nstruct S {}: B\n") + "func use() -> i32 => S.value()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var derived = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        Assert.Single(derived.Bases);
        Assert.True(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitGroupControlsSynthesizedPathAccessibility(bool explicitFirst)
    {
        const string Explicit = "public group G\n";
        const string Path = "public rootgroup G.Child\n    public struct Value {}\n";
        var c = MinimalEmissionTest.Analyze((explicitFirst ? Explicit + Path : Path + Explicit) + "public group Api\n    public func use(x: G.Child.Value) => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(ModifierKind.Public, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Modifier);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BaseUsesItsDefiningSourceEnvironmentAfterReload(bool baseFirst)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("types.kimi", "group Left\n    public open struct B {}\n        public func value() -> i32 => 7\ngroup Right\n    public open struct B {}\n        public func value() -> string => \"wrong\""));
        var withBase = new SourceDocument("base.kimi", "alias Left\nstruct S {}: B");
        var withoutBase = new SourceDocument("other.kimi", "alias Right\nstruct S {}\nfunc use() -> i32 => S.value()");
        c.Kotonoha.AddSource(baseFirst ? withBase : withoutBase);
        c.Kotonoha.AddSource(baseFirst ? withoutBase : withBase);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete, MinimalEmissionTest.Describe(restored, null));
        Assert.True(restored.Bind().IsComplete);
    }

    [Theory]
    [InlineData("public group G", "group G")]
    [InlineData("contract C", "contract C")]
    [InlineData("open struct B {}\nstruct S {}: B", "struct S {}: B")]
    public void ReloadRetainsConflictingFragmentDiagnostics(string first, string second)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("first.kimi", first));
        Assert.True(c.Bind().IsComplete);
        c.Kotonoha.AddSource(new SourceDocument("second.kimi", second));
        Assert.False(c.Bind().IsComplete);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Contains(restored.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Duplicate);
    }

    [Fact]
    public void WarmMergedBindingAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("open struct B {}\n    public func value() -> i32 => 7\nstruct S {}: B\nstruct S {}\nfunc use() -> i32 => S.value()");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Merged declaration Binding failed.");
            }
        }));
    }

    private static Compilation Reload(Compilation c)
    {
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(restored);
        return restored;
    }
}
