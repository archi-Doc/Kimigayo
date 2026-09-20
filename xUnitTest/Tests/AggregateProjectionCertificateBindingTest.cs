// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AggregateProjectionCertificateBindingTest
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void LateInvalidWitnessCannotCertifyAggregate(int form, bool reverse)
    {
        var c = MinimalEmissionTest.Analyze(Source(form, reverse, "internal"));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(Definition(c).IsVerified);
        CheckConstruction(c, form, false);
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.False(Definition(restored).IsVerified);
        CheckConstruction(restored, form, false);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void ValidWitnessPreservesAggregate(int form, bool reverse)
    {
        var c = MinimalEmissionTest.Analyze(Source(form, reverse, "public"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Definition(c).IsVerified);
        CheckConstruction(c, form, true);
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.True(Definition(restored).IsVerified);
        CheckConstruction(restored, form, true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RepairingWitnessRestoresAggregate(int form)
    {
        var source = Source(form, false, "internal");
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(Definition(c).IsVerified);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<FunctionKoto>().Single();
        var original = function.Parameters[1].Type;
        var donor = MinimalEmissionTest.Analyze(source.Replace("x?: Local.Hidden.Item", "x?: i32", StringComparison.Ordinal));
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<FunctionKoto>().Single().Parameters[1].Type;
        Assert.True(KotoHelper.Replace(function, original, replacement));
        Assert.True(c.Bind().IsComplete);
        Assert.True(Definition(c).IsVerified);
        CheckConstruction(c, form, true);
        Assert.True(KotoHelper.Replace(function, replacement, original));
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
        CheckConstruction(c, form, false);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidProjectedBasePropagatesToDescendants(int form)
    {
        var c = MinimalEmissionTest.Analyze(Source(form, false, "internal").Replace("struct S:", "open struct S:", StringComparison.Ordinal) + "contract D\nstruct Child: S\n    Self is D");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Child");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "D");
        Assert.False(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)!.IsVerified);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void WarmAggregateProofsAllocateNothing(int form)
    {
        var c = MinimalEmissionTest.Analyze(Source(form, false, "public"));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Aggregate projection verification failed.");
            }
        }));
    }

    private static void CheckConstruction(Compilation c, int form, bool valid)
    {
        if (form == 0)
        {
            var expression = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single().ExpressionBody!;
            Assert.Equal(valid, c.Binding.TryGetEnumConstruction(expression, out _));
        }
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

    private static string Source(int form, bool reverse, string access)
    {
        var prefix = access + " contract Hidden\n    associate Item\npublic struct Local\n    Self is Hidden\n    associate Hidden.Item is i32\npublic open struct Base<T>\npublic contract Origin\n    associate Item\n    func f(self: ref/Self, x?: i32) -> i32\npublic struct Source\n    Self is Origin\n    associate Origin.Item is " + (form == 2 ? "Base<i32>" : "i32") + "\n    public func f(self: ref/Self, x?: Local.Hidden.Item) -> i32 => x\n";
        var consumer = "contract C\n" + (form == 0
            ? "enum S\n    A(Source.Origin.Item)\n    Self is C\ngroup G\n    func make() -> S => S.A(1)\n"
            : "struct S: " + (form == 1 ? "Base<Source.Origin.Item>" : "Source.Origin.Item") + "\n    Self is C\n");
        return reverse ? consumer + prefix : prefix + consumer;
    }

    private static BoundConformance Definition(Compilation c)
    {
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        return c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)!;
    }
}
