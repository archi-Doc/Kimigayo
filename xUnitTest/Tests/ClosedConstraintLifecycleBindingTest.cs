// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ClosedConstraintLifecycleBindingTest
{
    [Theory]
    [InlineData("struct", false)]
    [InlineData("struct", true)]
    [InlineData("enum", false)]
    [InlineData("enum", true)]
    public void UnresolvedClosedConditionsCannotVerifyConformance(string kind, bool negative)
    {
        var c = Parse("public contract Origin\npublic contract Marker\npublic struct Source {}\npublic " + kind + " Target\n    Source is " + (negative ? "not " : string.Empty) + "Origin\n    Self is Marker" + (kind == "enum" ? "\n    A" : string.Empty));
        var provisional = c.Binding.Bind(BindingMode.Provisional);
        Assert.Equal(0, provisional.InvalidCount);
        Assert.Equal(BindingState.Unresolved, Target(c).BindingState);
        Assert.False(Certificate(c));
        AppendSourceConformance(c);
        Assert.Equal(!negative, c.Bind().IsComplete);
        Assert.Equal(!negative, Certificate(c));
        Assert.Equal(!negative, Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("struct", false)]
    [InlineData("struct", true)]
    [InlineData("enum", false)]
    [InlineData("enum", true)]
    public void FinalBindingDeterminesAbsenceWithoutUsingProvisionalEvidence(string kind, bool negative)
    {
        var c = Parse("public contract Origin\npublic contract Marker\npublic struct Source {}\npublic " + kind + " Target\n    Source is " + (negative ? "not " : string.Empty) + "Origin\n    Self is Marker" + (kind == "enum" ? "\n    A" : string.Empty));
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.False(Certificate(c));
        Assert.Equal(negative, c.Bind().IsComplete);
        Assert.Equal(negative, Certificate(c));
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.False(Certificate(c));
        Assert.Equal(negative, c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("i32 is Copy", true)]
    [InlineData("string is Copy", false)]
    [InlineData("(i32) -> bool is (i32,) -> bool", true)]
    [InlineData("() -> bool is (()) -> bool", false)]
    public void DeterminedConditionsKeepTheirStateAcrossPasses(string clause, bool valid)
    {
        var c = Parse("public contract Marker\npublic struct Target {}\n    " + clause + "\n    Self is Marker");
        Assert.Equal(valid, c.Binding.Bind(BindingMode.Provisional).InvalidCount == 0);
        Assert.Equal(valid ? BindingState.Resolved : BindingState.Invalid, Target(c).BindingState);
        Assert.Equal(valid, Certificate(c));
        Assert.Equal(valid, c.Bind().IsComplete);
        Assert.Equal(valid, Certificate(c));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingOwnersWithholdPropertyAndCallCompletion(bool reverse)
    {
        const string source = "public struct Source {}\n";
        const string target = "public struct Target {}\n    Source is Origin\n    Self is Marker\n    public computed value: i32\n        get(self: ref/Self) -> i32 => 1\n    public func read() -> i32 => 1\n";
        var c = Parse("public contract Origin\npublic contract Marker\n" + (reverse ? target + source : source + target) + "group G\n    func call() -> i32 => Target.read()");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.False(Certificate(c));
        var property = Target(c).Members.OfType<PropertyKoto>().Single().BoundSymbol!.Property!;
        var call = Assert.IsType<InvocationKoto>(Container(c, "G").Members.OfType<FunctionKoto>().Single().ExpressionBody);
        Assert.False(property.IsVerified);
        Assert.Equal(BindingState.Unresolved, call.BindingState);
        AppendSourceConformance(c);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Certificate(c));
        Assert.True(property.IsVerified);
        Assert.NotNull(call.BoundCall);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingContractConditionsPropagateToConformance(bool append)
    {
        var c = Parse("public contract Origin\npublic struct Source {}\npublic contract Marker\n    Source is Origin\npublic struct Target {}\n    Self is Marker");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.Equal(BindingState.Unresolved, Container(c, "Marker").BindingState);
        Assert.False(Certificate(c));
        if (append)
        {
            AppendSourceConformance(c);
        }

        Assert.Equal(append, c.Bind().IsComplete);
        Assert.Equal(append, Certificate(c));
    }

    [Fact]
    public void AppendedClosedObligationsInvalidatePreviouslyVerifiedOwners()
    {
        var c = Parse("public contract Marker\npublic struct Target {}\n    public computed value: i32\n        get(self: ref/Self) -> i32 => 1");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var property = Target(c).Members.OfType<PropertyKoto>().Single().BoundSymbol!.Property!;
        Assert.True(property.IsVerified);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public struct Target {}\n    string is Copy\n    Self is Marker"));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Generated.kimi").GetArray());
        Assert.False(c.Bind().IsComplete);
        Assert.False(property.IsVerified);
        Assert.False(Certificate(c));
        Assert.False(Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void WarmProvisionalAndFinalTransitionsAllocateNothing()
    {
        var c = Parse("public contract Marker\npublic struct Target {}\n    i32 is Copy\n    Self is Marker");
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (c.Binding.Bind(BindingMode.Provisional).InvalidCount != 0 || !c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Closed condition lifecycle failed.");
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

    private static void AppendSourceConformance(Compilation c)
    {
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public struct Source {}\n    Self is Origin"));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Generated.kimi").GetArray());
    }

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        return c;
    }

    private static DeclarationContainerKoto Container(Compilation c, string name)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == name);

    private static DeclarationContainerKoto Target(Compilation c) => Container(c, "Target");

    private static bool Certificate(Compilation c)
        => c.Binding.GetConformanceDefinition(Target(c).BoundType!, Container(c, "Marker").BoundSymbol!)!.IsVerified;
}
