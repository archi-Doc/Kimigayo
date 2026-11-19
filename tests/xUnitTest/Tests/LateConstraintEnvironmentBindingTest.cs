// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class LateConstraintEnvironmentBindingTest
{
    [Theory]
    [InlineData("string is Copy", false)]
    [InlineData("i32 is string", false)]
    [InlineData("string is not Copy", true)]
    public void ContractConditionsControlExpandedConsumerPremises(string clause, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public contract Marker: Copy\n    " + clause + Consumers);
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        AssertConsumers(c, valid);
        Assert.Equal(valid, c.Bind().IsComplete);
        AssertConsumers(c, valid);
        var restored = CompilationTestHelper.Reload(c);
        Assert.Equal(valid, restored.Bind().IsComplete);
        AssertConsumers(restored, valid);
    }

    private const string Consumers = "\ngroup G\n    func take<T>()\n        T is Copy\n        ()\n    func caller<T>(value: T)\n        T is Marker\n        take<T>()";

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
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        var container = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Target");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        Assert.Equal(BindingState.Invalid, container.StateOf());
        Assert.False(c.Binding.GetConformanceDefinition(container.TypeOf()!, contract.SymbolOf()!)!.IsVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateFailureDoesNotInvalidateIndependentPremises(bool provisional)
    {
        var c = MinimalEmissionTest.Analyze("public contract Marker: Copy\n    string is Copy" + Consumers + "\n    func independent<T>(value: T)\n        T is Copy\n        take<T>()");
        c.Binding.Bind(provisional ? BindingMode.Provisional : BindingMode.Final);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "independent");
        Assert.Equal(BindingState.Resolved, function.StateOf());
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(function.Parameters[0].Type.TypeOf()!, function));
        Assert.NotNull(Assert.IsType<InvocationKoto>(function.Body!.Items.Single()).CallOf());
        var caller = Caller(c);
        Assert.Equal(ConstraintProof.Error, c.Binding.ProveCopy(caller.Parameters[0].Type.TypeOf()!, caller));
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
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(clause, original, replacement));
        Assert.True(c.Bind().IsComplete);
        AssertConsumers(c, true);
        Assert.True(KotoHelper.Replace(clause, replacement, original));
        Assert.False(c.Bind().IsComplete);
        AssertConsumers(c, false);
    }

    // SPEC 8.3.2 (Contract refinement, Error), 3.1.2 (`owner/string` is Non-Copy); PLAN G949 S1-1c: a late failure reaches its consumer
    // through a diamond chain of 32 refinements (M(i) refines M(i-1) and L(i-1), L(i) refines M(i-1)). M0's Constraint `string is Copy` is
    // Refuted, so `T is M32`, which supplies M0's facts, names an invalid declaration: an Error, `caller` invalid, which the certificate
    // round's judgment of its environment finds after the closed Constraint failed; with `string is not Copy` every declaration is valid.
    // A walk that followed each path would meet about 2^23 of them; the chain binds within the deadline either way.
    [Theory]
    [InlineData("string is Copy", false)]
    [InlineData("string is not Copy", true)]
    public async Task ALateFailureReachesItsConsumerThroughADiamondChain(string clause, bool valid)
    {
        const int n = 32;
        var source = "public contract M0: Copy\n    " + clause + "\npublic contract L0: M0\n" +
            string.Concat(Enumerable.Range(1, n).Select(static i => $"public contract M{i}: M{i - 1}, L{i - 1}\npublic contract L{i}: M{i - 1}\n")) +
            Consumers.Replace("T is Marker", $"T is M{n}", StringComparison.Ordinal);
        var cancel = TestContext.Current.CancellationToken;
        var run = Task.Run(() => MinimalEmissionTest.Analyze(source), cancel);
        Assert.Same(run, await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(60), cancel)));
        var c = await run;
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        AssertConsumers(c, valid);
        Assert.Equal(valid, c.Bind().IsComplete);
        AssertConsumers(c, valid);
    }

    // SPEC 8.3.2 (directly available `P` and `not P` is contradictory evidence, an Error); PLAN G949 S1-1c: each pass's first walk
    // judges every environment, so an edit that makes an environment contradictory, with nothing failed before the walk, is found on
    // the next Bind: valid with `T is Copy` and `T is Owned`, invalid once the second premise reads `T is not Copy`, valid again when
    // it is restored.
    [Fact]
    public void AnEditedEnvironmentIsJudgedOnTheNextBind()
    {
        const string source = "group G\n    func take<T>(value: T)\n        T is Copy\n        T is Owned\n        ()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var function = Take(c);
        var clause = Assert.IsType<IsKoto>(function.TypeConstraints[1]);
        var original = clause.Right!;
        var donor = MinimalEmissionTest.Analyze(source.Replace("T is Owned", "T is not Copy", StringComparison.Ordinal));
        var replacement = Assert.IsType<IsKoto>(Take(donor).TypeConstraints[1]).Right!;
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(clause, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(BindingState.Invalid, function.StateOf());
        Assert.True(KotoHelper.Replace(clause, replacement, original));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));

        static FunctionKoto Take(Compilation c)
            => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "take");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmEnvironmentRevalidationAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("public contract Marker: Copy\n    i32 is Copy" + Consumers);
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Constraint environment revalidation failed.");
            }
        }));
    }

    // R7-C3 C3-1b (F's C2 rulings 287-290; SPEC 3.1.2, 8.2.3, 8.3.1, 8.6.3, 8.7.1, 23.1.2): Marker's closed `string is Copy` is Refuted
    // (`owner/string` is Non-Copy), so Marker is an invalid declaration; a premise naming it is its declaration's premise error, reported
    // once there, never at uses (a call, a requirement, a conformance's otherwise-Proven verdict). A conformance's own failure keeps its
    // record (s.c: Target is not Copy; u.k: `U is i32` is not proven from `U is E`); u.k.mv is its control with Marker valid.
    [Theory]
    [InlineData("p1.call", Marker + "group G\n    func take<T>()\n        T is Copy\n        ()\n    func caller<T>(value: T)\n        T is Marker\n        take<T>()\n", "UnsatisfiedConstraint_Kd contract | UnsatisfiedConstraint_Kd string is Copy | InvalidConstraint_Kd caller<T>(value: T)")]
    [InlineData("p1.req", Marker + Needs, "UnsatisfiedConstraint_Kd contract | UnsatisfiedConstraint_Kd string is Copy | InvalidConstraint_Kd need<T>() | InvalidConstraint_Kd caller<T>(value: T)")]
    [InlineData("p1.bare", "public contract Marker\n    string is Copy\n" + Needs, "UnsatisfiedConstraint_Kd contract | UnsatisfiedConstraint_Kd string is Copy | InvalidConstraint_Kd need<T>() | InvalidConstraint_Kd caller<T>(value: T)")]
    [InlineData("p1.cond", "public contract C\n" + Marker + "struct W<T>\n    Self is C when T is Marker\n", "UnsatisfiedConstraint_Kd contract | UnsatisfiedConstraint_Kd string is Copy | InvalidConstraint_Kd Self is C when T is Marker")]
    [InlineData("p1.struct", "public contract C\n" + Marker + "public struct Target<T>\n    T is Marker\n    Self is C\n", "UnsatisfiedConstraint_Kd contract | UnsatisfiedConstraint_Kd string is Copy | InvalidConstraint_Kd struct")]
    [InlineData("s.c", "public contract C: Copy\n" + Marker + "public struct Target<T>\n    T is Marker\n    Self is C\n    public var s: string = \"\"\n", "UnsatisfiedConstraint_Kd contract | UnsatisfiedConstraint_Kd string is Copy | InvalidConstraint_Kd struct | UnsatisfiedConstraint_Kd Self is C")]
    [InlineData("u.k", Marker + Puts, "UnsatisfiedConstraint_Kd contract | UnsatisfiedConstraint_Kd string is Copy | InvalidConstraint_Kd struct | UnprovenConstraint_Kd Self is Put<E>")]
    [InlineData("u.k.mv", "public contract Marker: Copy\n    string is not Copy\n" + Puts, "UnprovenConstraint_Kd Self is Put<E>")]
    public void AnInvalidContractIsItsUsersDeclarationsPremiseErrorOnce(string id, string declarations, string records)
    {
        var source = declarations + "public func main() -> ()\n    return\n";
        Assert.Equal((id, records), (id, string.Join(" | ", DiagnosticCorpus.Check(source).Diagnostics.Select(x => $"{x.Code} {(x.Span is { } at ? source.Substring(at.Start, at.Length).Split('\n')[0] : string.Empty)}"))));
    }

    private const string Marker = "public contract Marker: Copy\n    string is Copy\n";

    private const string Needs = "group G\n    func need<T>()\n        T is Marker\n        ()\n    func caller<T>(value: T)\n        T is Marker\n        need<T>()\n";

    private const string Puts = "contract Put<T>\n    func put<U>(self: uniq/Self, u: U)\n        U is T\nstruct Box<E>\n    E is Marker\n    Self is Put<E>\n    public func put<U>(self: uniq/Self, u: U)\n        U is i32\n        return\n";

    private static FunctionKoto Caller(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "caller");

    private static void AssertConsumers(Compilation c, bool valid)
    {
        var caller = Caller(c);
        Assert.Equal(valid ? BindingState.Resolved : BindingState.Invalid, caller.StateOf());
        Assert.Equal(valid ? ConstraintProof.Proven : ConstraintProof.Error, c.Binding.ProveCopy(caller.Parameters[0].Type.TypeOf()!, caller));
        Assert.Equal(valid, Assert.IsType<InvocationKoto>(caller.Body!.Items.Single()).CallOf() is not null);
    }
}
