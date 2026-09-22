// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AssociatedForwardingTest
{
    internal static string MilestoneSource => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../milestones/Milestone19.kimi")).Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void EmitsUnchangedMilestone()
        => ScalarEmissionTest.EmitFixture("AssociatedForwardingMilestone19", MilestoneSource, "Associated numbers are 21, 21.\nAssociated flags are true, true.\nContract forwarding finished.\n");

    [Fact]
    public void WarmBindingRetainsAssociatedEvidenceWithoutAllocations()
    {
        // Measure the associated projection/proof path, separately from main's
        // tuple-pattern construction and whole-program setup.
        var source = MilestoneSource[..MilestoneSource.IndexOf("public func main", StringComparison.Ordinal)] +
            "func project(value: Wrapper<Wrapper<NumberSource>>.Source.Element) -> i32 => value\n()";
        var c = MinimalEmissionTest.Analyze(source);
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    [Fact]
    public void RequirementInstancesRetainVerifiedMemberIdentities()
    {
        var c = MinimalEmissionTest.Analyze(MilestoneSource);
        Assert.True(c.Binding.Result.IsComplete);
        var requirement = c.Ownership.Bodies.SelectMany(x => x.Operations).Select(x => x.Source).OfType<InvocationKoto>()
            .Select(x => x.BoundCall).First(x => x?.Target.Declaration is FunctionKoto { IsRequirement: true })!;
        var main = c.Ownership.Bodies.Single(x => x.Function.Name == "main");
        foreach (var operation in main.Operations)
        {
            if (operation.Source is InvocationKoto { BoundCall: { } call } && call.Target.Name is "readTwice" or "readNumber")
            {
                Assert.Equal(ConstraintProof.Proven, c.Binding.ResolveConformance(call.TypeArguments[0]!, requirement.Target.Scope.Owner.BoundSymbol!, main.Function, out var path));
                Assert.True(path!.IsVerified);
                var implementation = path.GetImplementation(requirement.Target);
                Assert.NotNull(implementation);
                Assert.Equal("Wrapper", implementation.Scope.Owner.BoundSymbol!.Name);
                Assert.NotNull(implementation.ConditionalDeclaration);
            }
        }
    }

    [Theory]
    [InlineData("Missing", "InvalidAssociatedType_Kd")]
    [InlineData("Duplicate", "DuplicateBinding_Kd")]
    [InlineData("Contradictory", "InvalidAssociatedType_Kd")]
    public void InvalidMappingsCannotPublishIr(string mutation, string diagnostic)
    {
        var source = mutation switch
        {
            "Missing" => MilestoneSource.Replace("    associate Source.Element is i32\n", string.Empty, StringComparison.Ordinal),
            "Duplicate" => MilestoneSource.Replace("    Self is Source\n", "    Self is Source\n    Self is Source\n", StringComparison.Ordinal),
            _ => MilestoneSource.Replace("    associate Source.Element is i32\n", "    associate Source.Element is i32\n    associate Source.Element is bool\n", StringComparison.Ordinal),
        };
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code.ToString() == diagnostic);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void EmitsCompoundAssociatedResult()
    {
        const string source = "contract Source\n    associate Element is Copy\n    func read(self: ref/Self) -> Element\nstruct PairSource\n    Self is Source\n    associate Source.Element is (i32, bool)\n    let pair: (i32, bool)\n    public init(pair: (i32, bool)) => self.pair = pair\n    public func read(self: ref/Self) -> (i32, bool) => self.pair\nfunc readOne<T>(source: ref/T) -> T.Source.Element\n    T is Source\n    return source.read()\nlet value = PairSource.init((7, true))\nmatch readOne(value@ref)\n    (7, true) => Console.writeLine(\"Pair received.\")\n    _ => $abort(\"pair\")";
        // Copying a whole aggregate field through a shared receiver is a separate,
        // currently rejected storage operation. Keep its rejection boundary explicit.
        var unsupported = MinimalEmissionTest.Analyze(source);
        Assert.False(unsupported.Ownership.Result.IsVerified);
        Assert.Contains(unsupported.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void MilestoneOwnershipIsVerified()
    {
        var c = MinimalEmissionTest.Analyze(MilestoneSource);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("wrongEquality")]
    [InlineData("missingPremise")]
    [InlineData("missingAssociated")]
    [InlineData("exclusiveReceiver")]
    [InlineData("missingDefinitionPremise")]
    public void RejectsInvalidEvidence(string mutation)
    {
        var source = MilestoneSource;
        source = mutation switch
        {
            "wrongEquality" => source.Replace("let nested =", "let invalid = readNumber(flag@ref)\n    let nested =", StringComparison.Ordinal),
            "missingPremise" => source.Replace("    Console.writeLine(\"Contract forwarding finished.\")", "    let invalid = readTwice(storageOnly@ref)", StringComparison.Ordinal),
            "missingAssociated" => source.Replace("    associate Source.Element is i32\n", string.Empty, StringComparison.Ordinal),
            "exclusiveReceiver" => source.Replace("public func read(self: ref/Self) -> i32", "public func read(self: uniq/Self) -> i32", StringComparison.Ordinal),
            _ => source.Replace("    T is Source\n    let first", "    let first", StringComparison.Ordinal),
        };
        Assert.NotEqual(MilestoneSource, source);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
    }
}
