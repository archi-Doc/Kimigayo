// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class AssociatedInferenceBoundaryTest
{
    [Theory]
    [InlineData("func read<length N>(value: [N of i32]) -> Self.Item", "public func read<length M>(value: [M of i32]) -> [M of i32] => value")]
    [InlineData("func read() -> [2 of Self.Item]", "public func read() -> [3 of i32] => [1, 2, 3]")]
    [InlineData("func read() -> Self.Item", "public func read() -> Box<string> => $abort(\"unused\")")]
    public void EscapingLengthsBodyResultsShapeAndFormationCannotSupplyValidEvidence(string requirement, string implementation)
    {
        var c = CompilationTestHelper.ParseSuccess("struct Box<T>\n    T is i32\ncontract C\n    associate Item\n    " + requirement + "\nstruct S\n    Self is C\n    " + implementation);
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
    }

    [Fact]
    public void FamiliesRequireSpecifications()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    associate Item(step) for ref/Self during step\n    func read(self: ref/Self during step) -> Self.Item(step)\nstruct S\n    Self is C\n    public func read(self: ref/Self) -> i32 => 1");
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
    }

    [Fact]
    public void SymbolicPublicProjectionsRemainComplete()
    {
        Bound("contract Source\n    associate Element\ncontract C\n    associate Item\n    func read() -> Self.Item\nstruct S<T>\n    T is Source\n    Self is C\n    public func read() -> [2 of T.(Source).Element] => $abort(\"unused\")");
    }

    [Fact]
    public void BadUnitBodyDoesNotChangeItsDeclaredResult()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    associate Item\n    func read() -> Self.Item\nstruct S\n    Self is C\n    public func read() => return 1");
        Assert.False(c.Bind().IsComplete);
        Assert.Same(BoundType.Unit, Assert.Single(Definition(c).AssociatedTypes).Value);
    }

    [Fact]
    public void IndependentWholeFunctionResultsAgree()
    {
        Bound("contract C\n    associate Item\n    func read() -> Self.Item\n    func other() -> Self.Item\nstruct S\n    Self is C\n    public func read() -> (ref/i32) -> ref/i32 => $abort(\"unused\")\n    public func other() -> (ref/i32) -> ref/i32 => $abort(\"unused\")");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitAndInferredNestedFunctionTypesUseTheSameIdentity(bool explicitBinding)
    {
        Bound("contract C\n    associate Item\n    func read() -> Self.Item\n    func other() -> Self.Item\nstruct S\n    Self is C\n" +
            (explicitBinding ? "    associate C.Item is Option<(ref/i32) -> ref/i32>\n" : string.Empty) +
            "    public func read() -> Option<(ref/i32) -> ref/i32> => .None\n    public func other() -> Option<(ref/i32) -> ref/i32> => .None");
    }

    [Fact]
    public void SourceModulesPublishCompletedBindings()
    {
        var c = ModuleBindingTest.Create("func read() -> Lib.S.(Lib.C).Item => Lib.S.read()\npublic func main() => ()", "public contract C\n    associate Item\n    func read() -> Self.Item\npublic struct S\n    Self is C\n    public func read() -> i32 => 42");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void DisplayLimitsKeepTheCauseAndExplicitSpecificationLocation()
    {
        var name = "Contract" + new string('x', 256);
        const string path = "bounded-inference.kimi";
        var source = "contract " + name + "\n    associate Item\n    func read<T>(value: T) -> Self.Item\nstruct S\n    Self is " + name + "\n    public func read<U>(value: U) -> U => value";
        var c = CompilationTestHelper.ParseSuccess(source, path);
        Assert.False(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(c.Diagnostics.Finalize(rejected: true).Diagnostics, static x => x.Code == "AssociatedTypeInferenceFailed_Kd");
        var identity = Assert.Single(error.Reason!, static x => x.Name == "associated");
        Assert.True(identity.Value.Length <= DiagnosticLimits.ValueLength);
        Assert.EndsWith(".Item", identity.Value, StringComparison.Ordinal);
        Assert.Contains(error.Reason!, static x => x.Name == "reason" && x.Value.Contains("outside the conformance scope", StringComparison.Ordinal));
        Assert.Contains(error.Related!, static x => x.Role == "associated");
        Assert.Contains(error.Related!, static x => x.Role == "binder");
        Assert.Equal(source.IndexOf("Self is", StringComparison.Ordinal), error.Span!.Value.Start);
    }

    [Fact]
    public void DistinctBoundContractsDoNotShareTheirResults()
    {
        var c = Bound("contract C<T>\n    associate Item\n    func read(value: T) -> Self.Item\nstruct S\n    Self is C<i32> and C<bool>\n    public func read(value: i32) -> i32 => value\n    public func read(value: bool) -> bool => value");
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var clause = Assert.Single(type.ConstraintNodes).BoundConstraint!;
        var left = c.Binding.GetConformanceDefinition(type.TypeOf()!, clause.Left!.Contract!)!;
        var right = c.Binding.GetConformanceDefinition(type.TypeOf()!, clause.Right!.Contract!)!;
        Assert.Equal("i32", Assert.Single(left.AssociatedTypes).Value.Name);
        Assert.Equal("bool", Assert.Single(right.AssociatedTypes).Value.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MergedFragmentsFreezeAllConformancesTogether(bool reverse)
    {
        const string contracts = "contract C\n    associate Item\n    func read() -> Self.Item\ncontract D\n    associate Output\n    func make() -> Self.Output\n";
        const string first = "struct S\n    Self is C\n    Self is D\n    public func read() -> i32 => 1\n";
        const string second = "struct S\n    public func make() -> Self.(C).Item => 1\n";
        var c = CompilationTestHelper.ParseSuccess(contracts);
        c.Kotonoha.AddSource(new(reverse ? "z.kimi" : "a.kimi", reverse ? second : first));
        c.Kotonoha.AddSource(new(reverse ? "a.kimi" : "z.kimi", reverse ? first : second));
        Assert.Empty(TestDiagnostics.Of(c));
        Assert.False(c.Bind().IsComplete);
        Assert.True(Definition(c).IsVerified);
        Assert.False(Definition(c, "D").IsVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InheritedMappingKeepsTheBaseTypeAndWitness(bool newConformance)
    {
        var c = Bound("contract C\n    associate Item\n    func read(self: ref/Self) -> Self.Item\nopen struct Base<T>\n" +
            (newConformance ? string.Empty : "    Self is C\n") +
            "    public func read(self: ref/Self) -> T => $abort(\"unused\")\nstruct S: Base<i32>\n" +
            (newConformance ? "    Self is C\n" : string.Empty));
        Assert.Equal("i32", Assert.Single(Definition(c).AssociatedTypes).Value.Name);
        Assert.NotEmpty(Definition(c).Paths.SelectMany(static x => x.InferenceEvidence));
    }

    [Fact]
    public void AFailedBaseWitnessCannotLeaveAnInheritedCertificate()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    associate Item\n    func read(self: ref/Self) -> Self.Item\n        effect confined\nopen struct Base\n    Self is C\n    public func read(self: ref/Self) -> i32\n        Console.writeLine(\"effect\")\n        return 1\nstruct S: Base");
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
        Assert.All(Definition(c).Paths, static x => Assert.False(x.IsVerified));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateEffectFailureInvalidatesSharedAndExternalEvidence(bool external)
    {
        const string contracts = "contract P\n    associate Item\ncontract C: P\n    func read() -> Self.Item\n        effect confined\n";
        var c = CompilationTestHelper.ParseSuccess(contracts + (external
            ? "struct Origin\n    Self is C\n    public func read() -> i32\n        Console.writeLine(\"effect\")\n        return 1\ncontract D\n    associate Value\n    func make() -> Self.Value\nstruct S\n    Self is D\n    public func make() -> Origin.(P).Item => 1"
            : "struct S\n    Self is C and P\n    public func read() -> i32\n        Console.writeLine(\"effect\")\n        return 1"));
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c, external ? "D" : "P").IsVerified);
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c, external ? "D" : "P").IsVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingExplicitEvidenceCannotReuseTheOldFrozenBasis(bool restore)
    {
        var c = Bound("contract C\n    associate Item\n    func read() -> Self.Item\ncontract D\n    associate Output\n    func make() -> Self.Output\nstruct S\n    Self is C and D\n    associate C.Item is i32\n    public func read() -> i32 => 1\n    public func make() -> Self.(C).Item => 1");
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var explicitClause = type.Members.OfType<IsKoto>().Single(x => x.IsAssociatedConstraint);
        var replacement = CompilationTestHelper.ParseSuccess("struct R\n    let unrelated: i32 = 1").Kotonoha.RootKoto.NestedContainers.Single().Members.OfType<VariableKoto>().Single();
        Assert.True(KotoHelper.Replace(type, explicitClause, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c, "D").IsVerified);
        if (restore)
        {
            Assert.True(KotoHelper.Replace(type, replacement, explicitClause));
            Assert.True(c.Bind().IsComplete, Describe(c));
            Assert.True(Definition(c, "D").IsVerified);
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RejectedWarmInferenceReusesStorage()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    associate Item\n    func read<T>(value: T) -> Self.Item\nstruct S\n    Self is C\n    public func read<U>(value: U) -> U => value");
        for (var i = 0; i < 100; i++)
        {
            Assert.False(c.Bind().IsComplete);
        }

        var storage = c.Binding.AssociatedMetrics;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (c.Bind().IsComplete || c.Binding.AssociatedMetrics != storage)
            {
                throw new InvalidOperationException("Rejected inference changed its outcome or storage.");
            }
        }));
    }

    private static Compilation Bound(string source)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        return c;
    }

    private static BoundConformance Definition(Compilation c, string contractName = "C")
    {
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == contractName);
        return c.Binding.GetConformanceDefinition(type.TypeOf()!, contract.BoundSymbol!)!;
    }

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
