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
    [InlineData("((200@u8)@Wrapping<u8>).format(writer@uniq)")]
    [InlineData("(7@u8).format(writer@uniq)")]
    [InlineData("(7@i128).format(writer@uniq)")]
    public void DirectBuiltinRequirementSelection(string expression)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "_ = " + expression);
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
    }

    // SPEC 22.10.2, 22.9.3: the view's Utf8Format conformance is built in and adds no member, so its `format` is reached only through a
    // proven `T is Utf8Format`; a direct call, a call through a borrow and the Type-qualified Function Item each name no member of the
    // view, which has `length` and `bytes()` only.
    [Theory]
    [InlineData(Prefix + "_ = Text.utf8(\"a\").format(writer@uniq)\n", "Text.utf8(\"a\").format")]
    [InlineData("func put(r: ref/Text.Utf8Slice, w: uniq/Utf8Writer) -> Result<(), BufferFull> => r.format(w)\n" + Prefix, "r.format")]
    [InlineData("let show = Text.Utf8Slice.format\n", "Text.Utf8Slice.format")]
    public void AViewsFormatIsNoMember(string source, string at)
    {
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(("UnresolvedBinding_Kd", at), (record.Code, source.Substring(record.Span!.Value.Start, record.Span.Value.Length)));
        Assert.Equal(source.IndexOf(at, StringComparison.Ordinal), record.Span.Value.Start);
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

    // SPEC 22.10.2, 8.6.1, 3.1.1 (R8-S2a, R8-S2c, R8-S2d1, R8-S2b1, R8-S2b2): Utf8Format's requirement on bool, char, Unit, f32, f64,
    // string, a view, a wrapping integer and every integer selects the witness table's group function, a body, i128's and u128's the
    // generic one, whose specialization runs (SPEC 8.13.3) (fail-before: Builtin for each, the view's its declared member; i128 and u128
    // until R8-S2b2, DECISIONS R8-M30's temporary 7).
    [Theory]
    [InlineData("bool", "BoolFormat")]
    [InlineData("char", "CharFormat")]
    [InlineData("()", "UnitFormat")]
    [InlineData("f32", "F32Format")]
    [InlineData("f64", "F64Format")]
    [InlineData("string", "String")]
    [InlineData("Text.Utf8Slice", "Utf8SliceFormat")]
    [InlineData("Wrapping<u8>", "WrappingFormat")]
    [InlineData("i32", "IntegerFormat")]
    [InlineData("u8", "IntegerFormat")]
    [InlineData("i64", "IntegerFormat")]
    [InlineData("isize", "IntegerFormat")]
    [InlineData("usize", "IntegerFormat")]
    [InlineData("i128", "IntegerFormat")]
    [InlineData("u128", "IntegerFormat")]
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
            "Text.Utf8Slice" => c.Library.GetSymbol(KimiDeclarationId.Utf8Slice)!.Type!,
            "Wrapping<u8>" => BoundType.WrappingOf(BoundType.Primitives["u8"]),
            "()" => BoundType.Unit,
            _ => BoundType.Primitives[type],
        };
        var contract = c.Library.GetSymbol(KimiDeclarationId.Utf8Format)!;
        var requirement = contract.Contract!.OwnRequirement()!;
        var target = c.Types.Witness(requirement, contract, c.Types.Synthesized(requirement, self, contract.Type!));
        Assert.Equal(RequirementTargetKind.Body, target.Kind);
        Assert.Same(c.Library.Members(Enum.Parse<WitnessMembers>(group)), target.Target!.Scope.Owner);
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

    // SPEC 3.1.1, 8.3, 23.1.2 (R8-S2d1's move (a), pinned by R8-S2b1): a header that applies `Holder<E>` (`E is Utf8Format`) to a
    // Wrapping<T> over a T not proven PrimitiveInteger reports the Type's two formation records and no verdict of Holder's own over that
    // failed child: the WrappingFormat row's construction arm proves the conformance, and the formation is always checked (fail-before:
    // a third UnprovenConstraint_Kd, Holder's, at S2c's head).
    [Fact]
    public void AHolderOverAnIllFormedWrappingReportsOnlyItsFormation()
    {
        var c = MinimalEmissionTest.Analyze(
            "struct Holder<E>\n    E is Utf8Format\n    public var items: Array<E> = []\n    public init() => ()\n" +
            "func take<T>(h: ref/Holder<Wrapping<T>>) -> isize\n    return h.items.length\n");
        Assert.Equal(["UnprovenConstraint_Kd", "UnprovenConstraint_Kd"], c.Binding.Issues.Select(static x => x.Code.ToString()));
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
