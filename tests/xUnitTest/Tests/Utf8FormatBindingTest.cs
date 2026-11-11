// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class Utf8FormatBindingTest
{
    private const string Prefix = "var buffer = Text.heap(0)\nvar writer = Text.writer(buffer@uniq)\n";

    [Theory]
    [InlineData("0@i8")]
    [InlineData("0@u8")]
    [InlineData("0@i16")]
    [InlineData("0@u16")]
    [InlineData("0@i32")]
    [InlineData("0@u32")]
    [InlineData("0@i64")]
    [InlineData("0@u64")]
    [InlineData("0@i128")]
    [InlineData("0@u128")]
    [InlineData("0@isize")]
    [InlineData("0@usize")]
    [InlineData("0@f32")]
    [InlineData("0@f64")]
    [InlineData("true")]
    [InlineData("'a'")]
    [InlineData("()")]
    [InlineData("\"text\"")]
    [InlineData("Text.utf8(\"text\")")]
    public void BuiltinsUseTheFormattingContract(string value)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "_ = (writer@uniq).write(" + value + ")");
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("(42).format(writer@uniq)")]
    [InlineData("\"text\".format(writer@uniq)")]
    [InlineData("true.format(writer@uniq)")]
    public void DirectBuiltinRequirementSelection(string expression)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "_ = " + expression);
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("(1, 2)")]
    [InlineData("[2 of 1]")]
    [InlineData("null@raw/i32")]
    public void UnspecifiedTypesDoNotAcquireImplicitFormatting(string value)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "_ = (writer@uniq).write(" + value + ")");
        Assert.False(c.Binding.Result.IsComplete);
    }

    // SPEC 22.10.2, 8.6.1 (R8-S2a): Utf8Format's requirement on bool, char and Unit selects the witness table's group function, a body, where
    // i32 still takes the built-in target (fail-before: Builtin for each).
    [Theory]
    [InlineData("bool", "BoolFormat")]
    [InlineData("char", "CharFormat")]
    [InlineData("()", "UnitFormat")]
    public void ATableTypesFormatRequirementSelectsItsGroupsFunction(string type, string group)
    {
        var c = MinimalEmissionTest.Analyze("let x = 1\n");
        var self = type switch { "bool" => BoundType.Boolean, "char" => BoundType.Char, _ => BoundType.Unit };
        var contract = c.Library.GetSymbol(KimiDeclarationId.Utf8Format)!;
        var requirement = contract.Contract!.OwnRequirement()!;
        var target = c.Types.Witness(requirement, contract, c.Types.Synthesized(requirement, self, contract.Type!));
        Assert.Equal(RequirementTargetKind.Body, target.Kind);
        Assert.Same(c.Library.Members(Enum.Parse<WitnessMembers>(group)), target.Target!.Scope.Owner);
        var i32 = c.Types.Witness(requirement, contract, c.Types.Synthesized(requirement, BoundType.I32, contract.Type!));
        Assert.Equal(RequirementTargetKind.Builtin, i32.Kind);
    }

    // SPEC 22.10.1 (R8-S2a's invalid twin): a user Type that states no Utf8Format conformance is still rejected, with its record.
    [Fact]
    public void AUserTypeWithoutTheFormattingContractIsRejected()
    {
        var c = MinimalEmissionTest.Analyze("struct Plain\n    public init() => ()\n" + Prefix + "_ = (writer@uniq).write(Plain.init())");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(["NoApplicableOverload_Kd"], c.Binding.Issues.Select(static x => x.Code.ToString()));
    }

    private static string Describe(Compilation c)
        => MinimalEmissionTest.Describe(c) + string.Join("\n", c.Binding.Issues.Select(x => x.Code + ": " + x.Node));
}
