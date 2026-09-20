// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class LateConstraintEnvironmentBindingTest
{
    [Theory]
    [InlineData("string is Copy", false)]
    [InlineData("() -> bool is (()) -> bool", false)]
    [InlineData("i32 is string", false)]
    [InlineData("string is not Copy", true)]
    [InlineData("(i32) -> bool is (i32,) -> bool", true)]
    public void ContractConditionsControlExpandedConsumerPremises(string clause, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public contract Marker: Copy\n    " + clause + Consumers);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        AssertConsumers(c, valid);
        Assert.Equal(valid, c.Bind().IsComplete);
        AssertConsumers(c, valid);
        var restored = Reload(c);
        Assert.Equal(valid, restored.Bind().IsComplete);
        AssertConsumers(restored, valid);
    }

    private const string Consumers = "\ngroup G\n    func take<T>()\n        T is Copy\n        ()\n    func caller<T>(value?: T)\n        T is Marker\n        take<T>()";

    [Theory]
    [InlineData("struct", false)]
    [InlineData("struct", true)]
    [InlineData("enum", false)]
    [InlineData("enum", true)]
    public void InvalidPremisesRevokeGenericTypeCertificates(string kind, bool reverse)
    {
        const string marker = "public contract Marker: Copy\n    string is Copy\n";
        var target = "public " + kind + " Target<T>\n    T is Marker\n    Self is C\n" + (kind == "enum" ? "    A\n" : string.Empty);
        var c = MinimalEmissionTest.Analyze("public contract C\n" + (reverse ? target + marker : marker + target));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        var container = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Target");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        Assert.Equal(BindingState.Invalid, container.BindingState);
        Assert.False(c.Binding.GetConformanceDefinition(container.BoundType!, contract.BoundSymbol!)!.IsVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateFailureDoesNotInvalidateIndependentPremises(bool provisional)
    {
        var c = MinimalEmissionTest.Analyze("public contract Marker: Copy\n    string is Copy" + Consumers + "\n    func independent<T>(value?: T)\n        T is Copy\n        take<T>()");
        c.Binding.Bind(provisional ? BindingMode.Provisional : BindingMode.Final);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "independent");
        Assert.Equal(BindingState.Resolved, function.BindingState);
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(function.Parameters[0].Type.BoundType!, function));
        Assert.NotNull(Assert.IsType<InvocationKoto>(function.Body!.Items.Single()).BoundCall);
        var caller = Caller(c);
        Assert.Equal(ConstraintProof.Error, c.Binding.ProveCopy(caller.Parameters[0].Type.BoundType!, caller));
    }

    [Fact]
    public void RepairingTheContractRestoresItsConsumers()
    {
        const string source = "public contract Marker: Copy\n    string is Copy";
        var c = MinimalEmissionTest.Analyze(source + Consumers);
        var clause = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Marker").ConstraintNodes[0];
        var original = clause.Right;
        var donor = MinimalEmissionTest.Analyze(source.Replace("string is Copy", "string is not Copy", StringComparison.Ordinal) + Consumers);
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Marker").ConstraintNodes[0].Right;
        Assert.True(KotoHelper.Replace(clause, original, replacement));
        Assert.True(c.Bind().IsComplete);
        AssertConsumers(c, true);
        Assert.True(KotoHelper.Replace(clause, replacement, original));
        Assert.False(c.Bind().IsComplete);
        AssertConsumers(c, false);
    }

    [Fact]
    public void WarmEnvironmentRevalidationAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("public contract Marker: Copy\n    i32 is Copy" + Consumers);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Constraint environment revalidation failed.");
            }
        }));
    }

    private static FunctionKoto Caller(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "caller");

    private static void AssertConsumers(Compilation c, bool valid)
    {
        var caller = Caller(c);
        Assert.Equal(valid ? BindingState.Resolved : BindingState.Invalid, caller.BindingState);
        Assert.Equal(valid ? ConstraintProof.Proven : ConstraintProof.Error, c.Binding.ProveCopy(caller.Parameters[0].Type.BoundType!, caller));
        Assert.Equal(valid, Assert.IsType<InvocationKoto>(caller.Body!.Items.Single()).BoundCall is not null);
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
}
