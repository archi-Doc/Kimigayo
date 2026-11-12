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
    [InlineData("1.5.format(writer@uniq)")]
    [InlineData("Text.utf8(\"text\").format(writer@uniq)")]
    [InlineData("((200@u8)@Wrapping<u8>).format(writer@uniq)")]
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

    // SPEC 22.10.2, 8.6.1, 3.1.1 (R8-S2a, R8-S2c, R8-S2d1): Utf8Format's requirement on bool, char, Unit, f32, f64, string and a wrapping
    // integer selects the witness table's group function, a body, where i32 still takes the built-in target (fail-before: Builtin for each).
    [Theory]
    [InlineData("bool", "BoolFormat")]
    [InlineData("char", "CharFormat")]
    [InlineData("()", "UnitFormat")]
    [InlineData("f32", "F32Format")]
    [InlineData("f64", "F64Format")]
    [InlineData("string", "String")]
    [InlineData("Wrapping<u8>", "WrappingFormat")]
    public void ATableTypesFormatRequirementSelectsItsGroupsFunction(string type, string group)
    {
        var c = MinimalEmissionTest.Analyze("let x = 1\n");
        var self = type switch
        {
            "bool" => BoundType.Boolean,
            "char" => BoundType.Char,
            "f32" => BoundType.F32,
            "f64" => BoundType.F64,
            "string" => BoundType.String,
            "Wrapping<u8>" => BoundType.WrappingOf(BoundType.Primitives["u8"]),
            _ => BoundType.Unit,
        };
        var contract = c.Library.GetSymbol(KimiDeclarationId.Utf8Format)!;
        var requirement = contract.Contract!.OwnRequirement()!;
        var target = c.Types.Witness(requirement, contract, c.Types.Synthesized(requirement, self, contract.Type!));
        Assert.Equal(RequirementTargetKind.Body, target.Kind);
        Assert.Same(c.Library.Members(Enum.Parse<WitnessMembers>(group)), target.Target!.Scope.Owner);
        var i32 = c.Types.Witness(requirement, contract, c.Types.Synthesized(requirement, BoundType.I32, contract.Type!));
        Assert.Equal(RequirementTargetKind.Builtin, i32.Kind);
    }

    // SPEC 3.1.1, 22.10.2 (R8-S2d1): an abstract Wrapping<T> over a proven PrimitiveInteger is Utf8Format by the witness table's
    // WrappingFormat row (no longer by a premise): written, interpolated, converted and passed as a `U is Utf8Format` argument, with no
    // record. Its invalid twin, Wrapping<T> over a T not proven PrimitiveInteger, keeps the records it had before R8-S2d1 (the parameter's
    // two and two at each use, all UnprovenConstraint_Kd; the same at S2c's head), none added.
    [Theory]
    [InlineData("    T is PrimitiveInteger\n", 0)]
    [InlineData("", 10)]
    public void AnAbstractWrappingIntegerIsTheTablesUtf8Format(string premise, int unproven)
    {
        var c = MinimalEmissionTest.Analyze(
            "func shown<U>(value: ref/U, writer: uniq/Utf8Writer) -> Result<(), BufferFull>\n    U is Utf8Format\n    return value.format(writer)\n" +
            "func emit<T>(writer: uniq/Utf8Writer, w: Wrapping<T>) -> Result<(), BufferFull>\n" + premise +
            "    _ = Text.toString(w)\n    _ = \"\\(w)\"\n    try shown(w, writer)\n    return writer.write(w)\n");
        Assert.Equal(Enumerable.Repeat("UnprovenConstraint_Kd", unproven), c.Binding.Issues.Select(static x => x.Code.ToString()));
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
