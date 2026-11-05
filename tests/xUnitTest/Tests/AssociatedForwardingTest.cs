// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AssociatedForwardingTest
{
    internal static string MilestoneSource => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/milestones/Milestone20.kimi")).Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void EmitsUnchangedMilestone()
        => ScalarEmissionTest.EmitFixture("AssociatedForwardingMilestone20", MilestoneSource, "Associated numbers are 21, 21.\nAssociated flags are true, true.\nContract forwarding finished.\n");

    [Fact]
    public void EmitsMilestoneWithAnInferredBinding()
        => ScalarEmissionTest.EmitFixture("AssociatedInferenceForwarding", MilestoneSource.Replace("    associate Source.Element is i32\n", string.Empty, StringComparison.Ordinal), "Associated numbers are 21, 21.\nAssociated flags are true, true.\nContract forwarding finished.\n");

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBindingRetainsAssociatedEvidenceWithoutAllocations()
    {
        // Measure the associated projection/proof path, separately from main's
        // tuple-pattern construction and whole-program setup.
        var source = MilestoneSource[..MilestoneSource.IndexOf("public func main", StringComparison.Ordinal)] +
            "func project(value: Wrapper<Wrapper<NumberSource>>.Source.Element) -> i32 => value\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    [Fact]
    public void RequirementInstancesRetainVerifiedMemberIdentities()
    {
        var c = MinimalEmissionTest.Analyze(MilestoneSource);
        Assert.True(c.Binding.Result.IsComplete);
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var requirement = nodes.OfType<InvocationKoto>().Select(x => x.CallOf()).First(x => x?.Target.Declaration is FunctionKoto { IsRequirement: true })!;
        var main = nodes.OfType<FunctionKoto>().Single(x => x.Name == "main");
        foreach (var invocation in KotoTree.Walk(main).OfType<InvocationKoto>())
        {
            if (invocation.CallOf() is { } call && call.Target.Name is "readTwice" or "readNumber")
            {
                Assert.Equal(ConstraintProof.Proven, c.Binding.ResolveConformance(call.TypeArguments[0]!, requirement.Target.Scope.Owner.SymbolOf()!, main, out var path));
                Assert.True(path!.IsVerified);
                var implementation = path.GetImplementation(new(requirement.Target, requirement.RequirementContract!));
                Assert.NotNull(implementation);
                Assert.Equal("Wrapper", implementation.Scope.Owner.SymbolOf()!.Name);
                Assert.NotNull(implementation.ConditionalDeclaration);
            }
        }
    }

    [Theory]
    [InlineData("Missing", "AssociatedTypeInferenceFailed_Kd")]
    [InlineData("Duplicate", "DuplicateBinding_Kd")]
    [InlineData("Contradictory", "InvalidAssociatedType_Kd")]
    public void InvalidMappingsCannotPublishIr(string mutation, string diagnostic)
    {
        var source = mutation switch
        {
            "Missing" => MilestoneSource.Replace("    associate Source.Element is i32\n", string.Empty, StringComparison.Ordinal)
                .Replace("public func read(self: ref/Self) -> i32", "public func read(self: ref/Self) -> Self.Element", StringComparison.Ordinal),
            "Duplicate" => MilestoneSource.Replace("    Self is Source\n", "    Self is Source\n    Self is Source\n", StringComparison.Ordinal),
            _ => MilestoneSource.Replace("    associate Source.Element is i32\n", "    associate Source.Element is i32\n    associate Source.Element is bool\n", StringComparison.Ordinal),
        };
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code.ToString() == diagnostic);
        using var writer = new StringWriter();
        Assert.False(c.Generate(writer));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void EmitsCompoundAssociatedResult()
    {
        const string source = "contract Source\n    associate Element is Copy\n    func read(self: ref/Self) -> Element\nstruct PairSource\n    Self is Source\n    associate Source.Element is (i32, bool)\n    let pair: (i32, bool)\n    public init(pair: (i32, bool)) => self.pair = pair\n    public func read(self: ref/Self) -> (i32, bool) => self.pair\nfunc readOne<T>(source: ref/T) -> T.Source.Element\n    T is Source\n    return source.read()\nlet value = PairSource.init((7, true))\nmatch readOne(value@ref)\n    (7, true) => Console.writeLine(\"Pair received.\")\n    _ => $abort(\"pair\")";
        ScalarEmissionTest.EmitFixture("AssociatedForwardingFieldSnapshot", source, "Pair received.\n");
        var constructed = source.Replace("let pair: (i32, bool)", "let value: i32", StringComparison.Ordinal)
            .Replace("public init(pair: (i32, bool)) => self.pair = pair", "public init(value: i32) => self.value = value", StringComparison.Ordinal)
            .Replace("=> self.pair", "=> (self.value, true)", StringComparison.Ordinal)
            .Replace("PairSource.init((7, true))", "PairSource.init(7)", StringComparison.Ordinal);
        ScalarEmissionTest.EmitFixture("AssociatedForwardingCompound", constructed, "Pair received.\n");
    }

    [Fact]
    public void MilestoneBinds()
    {
        var c = MinimalEmissionTest.Analyze(MilestoneSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void MilestoneOwnershipIsVerified()
    {
        var c = MinimalEmissionTest.Analyze(MilestoneSource);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("NumberSource", "i32")]
    [InlineData("Wrapper<NumberSource>", "i32")]
    [InlineData("FlagSource", "bool")]
    [InlineData("Wrapper<FlagSource>", "bool")]
    public void NormalizesAssociatedIdentityAfterEachContainerSubstitution(string inner, string result)
    {
        var source = MilestoneSource[..MilestoneSource.IndexOf("public func main", StringComparison.Ordinal)] +
            $"func project(value: Wrapper<{inner}>.Source.Element) -> {result} => value\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("wrongEquality")]
    [InlineData("missingPremise")]
    [InlineData("missingIndependentEvidence")]
    [InlineData("exclusiveReceiver")]
    [InlineData("missingDefinitionPremise")]
    public void RejectsInvalidEvidence(string mutation)
    {
        var source = MilestoneSource;
        source = mutation switch
        {
            "wrongEquality" => source.Replace("let nested =", "let invalid = readNumber(flag@ref)\n    let nested =", StringComparison.Ordinal),
            "missingPremise" => source.Replace("    Console.writeLine(\"Contract forwarding finished.\")", "    let invalid = readTwice(storageOnly@ref)", StringComparison.Ordinal),
            "missingIndependentEvidence" => source.Replace("    associate Source.Element is i32\n", string.Empty, StringComparison.Ordinal)
                .Replace("public func read(self: ref/Self) -> i32", "public func read(self: ref/Self) -> Self.Element", StringComparison.Ordinal),
            "exclusiveReceiver" => source.Replace("public func read(self: ref/Self) -> i32", "public func read(self: uniq/Self) -> i32", StringComparison.Ordinal),
            _ => source.Replace("    T is Source\n    let first", "    let first", StringComparison.Ordinal),
        };
        Assert.NotEqual(MilestoneSource, source);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
    }
}
