// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 15.3.2: a struct or enum declares its own Origin slots in its header alone. A storage name that no declaration matches,
// and a projection of a slot its Type does not declare, are each reported once at the name, relating the header.
public class DeclaredOriginSlotTest
{
    private const string Main = "public func main() => ()\n";

    private const string SlotW = "struct W {s}\n    public var items: Array<ref/i32 during s>\n    public init(items: Array<ref/i32 during s>) => self.items = items@move\n";

    private const string Item = "contract C\n    associate Item(a) is i32\nstruct S\n    Self is C\n";

    [Theory]
    [InlineData("public struct View<T> {source}\n    private let data: ref/T during buffer\n", "buffer", "header", "{source}")]
    [InlineData("struct Pair {source}\n    let first: ref/i32 during source\n    let second: ref/i32 during souce\n", "souce", "header", "{source}")]
    [InlineData("struct Holder<T>\n    let value: ref/T during source\n", "source", "type", "Holder")]
    [InlineData("struct Outer<T> {source}\n    struct Inner\n        let value: ref/T during sauce\n", "sauce", "type", "Inner")]
    [InlineData("enum Choice<T> {source}\n    Some(ref/T during sorce)\n    None\n", "sorce", "header", "{source}")]
    public void AnUndeclaredStorageNameIsReportedOnceAtTheName(string declarations, string name, string role, string related)
    {
        var source = declarations + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.MissingOriginBinding_Kd), error.Code);
        Assert.Equal(name, Text(source, error.Span));
        Assert.Equal($"{name} is not a declared Origin slot", error.Label);
        Assert.Contains("declares its own Origin slots only in its header", error.Note);
        var location = Assert.Single(error.Related!);
        Assert.Equal(role, location.Role);
        Assert.Equal(related, Text(source, location.Span));
    }

    // SPEC 11.6: `during self` in storage is explained by the storage rule, not as an undeclared slot.
    [Fact]
    public void SelfInStorageKeepsTheStorageRule()
    {
        var source = "struct Selfish\n    let value: ref/i32 during self\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.MissingOriginBinding_Kd), error.Code);
        Assert.Equal("self is not a storage Origin", error.Label);
        Assert.Contains("SPEC 11.6", error.Note);
    }

    // A name of another role keeps its role error rather than the undeclared-slot record.
    [Fact]
    public void AFieldLocalSetUsedAsAScalarKeepsItsRoleError()
    {
        var source = "struct View<T> {source}\n    public let value: ref/T during source\nstruct Wrapper<T> {source}\n    let pair: (View<T>{inner}, ref/T during inner)\n        origin inner.source == source\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.InvalidOriginBinding_Kd), error.Code);
        Assert.Null(error.Label);
    }

    [Fact]
    public void ValidHeadersAndNestedTypesAreAccepted()
    {
        var source = "public struct View<T> {source}\n    private let data: ref/T during source\nstruct Outer<T> {source}\n    struct Inner\n        let value: ref/T during source\n" +
            "enum Choice<T> {source}\n    Some(ref/T during source)\n    None\n" + Main;
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);

        // The projection of a declared slot binds (its ownership is checked by the projection tests).
        var c = MinimalEmissionTest.Analyze("public struct View<T> {source}\n    private let data: ref/T during source\nfunc forward<T>(value: View<T>) -> View<T>{result}\n    origin result.source == value.source\n    return value@move\n" + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 15.3.2: after a slot is renamed, each projection of the old name is one record at the slot name, and the unbound result slot
    // and the mismatch of identically spelled Types that follow from it are not reported as independent problems.
    [Fact]
    public void AProjectionOfAnAbsentSlotIsReportedOncePerProjection()
    {
        var source = "public struct Renamed<T> {buffer}\n    private let data: ref/T during buffer\nfunc forward<T>(value: Renamed<T>) -> Renamed<T>{result}\n    origin result.source == value.source\n    return value\n" + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal(2, errors.Length);
        Assert.All(errors, error =>
        {
            Assert.Equal(nameof(DiagnosticCode.InvalidOriginBinding_Kd), error.Code);
            Assert.Equal("source", Text(source, error.Span));
            Assert.Equal("Renamed has no Origin slot source", error.Label);
            Assert.Equal("The Origin slots of Renamed are buffer", error.Note);
            var header = Assert.Single(error.Related!);
            Assert.Equal(("header", "{buffer}"), (header.Role, Text(source, header.Span)));
        });
        Assert.NotEqual(errors[0].Span, errors[1].Span);
    }

    // An independent failure in the same function stays visible.
    [Fact]
    public void AnIndependentMismatchBesideAnAbsentSlotIsReported()
    {
        var source = "public struct Renamed<T> {buffer}\n    private let data: ref/T during buffer\nfunc wrong<T>(value: Renamed<T>) -> Renamed<T>{result}\n    origin result.buffer == value.sorce\n    return 1\n" + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal([nameof(DiagnosticCode.InvalidOriginBinding_Kd), nameof(DiagnosticCode.TypeMismatch_Kd)], errors.Select(static x => x.Code).ToArray());
    }

    // SPEC 23.1.2, 15.3.1 (PLAN G312): a parenthesis or meet whose operand failed, and an associated Type application whose Origin
    // argument failed, rest on that operand, so a written Origin reports each cause once, at the operand: a non-borrow name, an absent
    // slot (also nested, and in a clause), BU-56's located Unsupported_Kd and a missing Name; a meet whose two operands fail keeps both
    // records, and the valid twins bind.
    [Theory]
    [InlineData("func f(a: ref/i32, n: i32, b: ref/i32 during (n and a)) => ()", "InvalidOriginBinding_Kd: n")]
    [InlineData(SlotW + "func f(w: ref/W, b: ref/i32 during (w.zz)) => ()", "InvalidOriginBinding_Kd: zz")]
    [InlineData(SlotW + "func f(w: ref/W, b: ref/i32 during ((w.zz))) => ()", "InvalidOriginBinding_Kd: zz")]
    [InlineData(SlotW + "func f(a: ref/i32, w: ref/W, b: ref/i32 during (w.zz and a)) => ()", "InvalidOriginBinding_Kd: zz")]
    [InlineData(SlotW + "func f(a: ref/i32, w: ref/W)\n    origin (w.zz) outlives a\n    ()", "InvalidOriginBinding_Kd: zz")]
    [InlineData(
        SlotW + "func leak(a: ref/i32) -> i32\n    var arr: Array<ref/i32> = []\n    arr.append(a)\n    var w: W{x} = W.init(items: arr@move)\n" +
            "    func put(slot: uniq/Array<ref/i32 during (x.s)>, v: ref/i32) => slot@follow.append(v)\n    return 0",
        "Unsupported_Kd: x.s")]
    [InlineData(SlotW + "func f(n: i32, w: ref/W, b: ref/i32 during (n and w.zz)) => ()", "InvalidOriginBinding_Kd: n", "InvalidOriginBinding_Kd: zz")]
    [InlineData(Item + "func f(x: ref/i32 during source) -> S.Item((missing)) => 42", "MissingOriginBinding_Kd: missing")]
    [InlineData(SlotW + "func f(a: ref/i32, w: ref/W, b: ref/i32 during ((a and w.s))) => ()")]
    [InlineData(Item + "func f(x: ref/i32 during source) -> S.Item((source)) => 42")]
    public void AWrittenOriginReportsEachCauseOnceAtItsOperand(string declarations, params string[] records)
        => Assert.Equal(records, RawPointerSyntaxTest.Records(declarations));

    // A warm rebind of those parents reuses the prerequisite storage.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ParentsRestingOnAFailedOperandRebindWithoutAllocating()
    {
        var c = MinimalEmissionTest.Analyze(SlotW + Item + "func f(n: i32, w: ref/W, b: ref/i32 during ((n and w.zz))) -> S.Item((missing)) => 42\n" + Main);
        var invalid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Bind();
            invalid &= !c.Binding.Result.IsComplete;
        }));
        Assert.True(invalid);
    }

    // SPEC 5.7, 15.3.5: a raw layer's Origin is no use of a slot for Loan requirements, so a slot only a raw layer states is Phantom and
    // the declaration invalid, with the slot related; a Loan<T> Field over the slot keeps it. Phantom status is per admitted case of the
    // declaration's pair binders, its own and an enclosing declaration's, and a generic definition is valid only when every case is
    // (SPEC 8.15.2; DECISIONS BU-44): a slot only a conditional pair slot or a nested slot uses is Phantom where those cases store no
    // borrow (the owner case; `s = owner, t = owner`; Inner<i32>'s and an owner-only binder's every case), and kept where every case does
    // (`ref or uniq`; a `uniq` binder beside an `owner or ref` one) or another Field keeps it.
    [Theory]
    [InlineData("struct W<T> {s}\n    let p: raw/T during s", "InvalidOriginBinding_Kd: raw/T during s")]
    [InlineData("struct W<T> {s}\n    let keep: Loan<ref/T during s>\n    let p: raw/T during s", null)]
    [InlineData(PairAnnotationBindingTest.HolderOwnerUniq + "    let p: raw/i32 during a", "InvalidOriginBinding_Kd: raw/i32 during a")]
    [InlineData(PairAnnotationBindingTest.HolderRefUniq + "    let p: raw/i32 during a", null)]
    [InlineData(PairAnnotationBindingTest.HolderOwnerUniq + "    let r: ref/i32 during a\n    let p: raw/i32 during a", null)]
    [InlineData("struct M<s/T, t/U> {a}\n    s is uniq\n    t is owner or ref\n    let x: s/T during a\n    let y: t/U during a\n    let p: raw/i32 during a", null)]
    [InlineData("struct M<s/T, t/U> {a}\n    s is owner or uniq\n    t is owner or ref\n    let x: s/T during a\n    let y: t/U during a\n    let p: raw/i32 during a", "InvalidOriginBinding_Kd: raw/i32 during a")]
    [InlineData("struct Outer<s/T>\n    s is owner or uniq\n    struct Inner {a}\n        let x: s/T during a\n        let p: raw/i32 during a", "InvalidOriginBinding_Kd: raw/i32 during a")]
    [InlineData("struct Outer<s/T>\n    s is ref or uniq\n    struct Inner {a}\n        let x: s/T during a\n        let p: raw/i32 during a", null)]
    [InlineData(PairAnnotationBindingTest.InnerOwnerUniq + "struct Outer {a}\n    let j: Inner<i32> during a\n    let p: raw/i32 during a", "InvalidOriginBinding_Kd: raw/i32 during a")]
    [InlineData(PairAnnotationBindingTest.InnerOwnerUniq + "struct Outer<q/U> {a}\n    q is owner\n    let j: Inner<q/U> during a\n    let p: raw/i32 during a", "InvalidOriginBinding_Kd: raw/i32 during a")]
    [InlineData(PairAnnotationBindingTest.InnerOwnerUniq + "struct Outer<q/U> {a}\n    q is owner or uniq\n    let j: Inner<q/U> during a\n    let p: raw/i32 during a", "InvalidOriginBinding_Kd: raw/i32 during a")]
    public void ARawLayerStatesNoPhantomSlot(string declarations, string? record)
        => Assert.Equal(record is null ? [] : [record], RawPointerSyntaxTest.Records(declarations));

    // SPEC 23.1.2, 15.3.5 (DECISIONS BU-44): a slot's record claims its failure only in the Semantics cases it holds in: a Phantom slot
    // in the product of each open binder's non-borrow cases, a `uniq` slot bound to `static` in the union of their `uniq` cases; a
    // binder failing in all its cases, and a concrete instance, name none, as the concrete twin's record does.
    [Theory]
    [InlineData(PairAnnotationBindingTest.HolderOwnerUniq + "    let p: raw/i32 during a", "Origin slot a is Phantom in the Semantics case s = owner")]
    [InlineData(
        "struct M<s/T, t/U> {a}\n    s is owner or uniq\n    t is owner or ref\n    let x: s/T during a\n    let y: t/U during a\n    let p: raw/i32 during a",
        "Origin slot a is Phantom in the Semantics case s = owner, t = owner")]
    [InlineData(PairAnnotationBindingTest.HolderOwnerUniq + "func f<q/U>(h: ref/Holder<q/U>, p: raw/i32 during h.a)\n    q is owner or uniq\n    ()", "Origin slot a is Phantom in the Semantics case q = owner")]
    [InlineData(PairAnnotationBindingTest.HolderOwnerUniq + "func g(r: uniq/i32) -> Holder<uniq/i32 during r> during static => $abort(\"u\")", "static cannot supply uniq for Origin slot a")]
    [InlineData(
        PairAnnotationBindingTest.HolderOwnerUniq + "func g<q/U>(x: q/U) -> Holder<q/U> during static\n    q is owner or uniq\n    $abort(\"u\")",
        "static cannot supply uniq for Origin slot a in the Semantics case q = uniq")]
    [InlineData(
        "struct M<s/T, t/U> {a}\n    s is owner or uniq\n    t is owner or uniq\n    let x: s/T during a\n    let y: t/U during a\n" +
        "func g<q/U, p/V>(x: q/U, y: p/V) -> M<q/U, p/V> during static\n    q is owner or uniq\n    p is owner or uniq\n    $abort(\"u\")",
        "static cannot supply uniq for Origin slot a in the Semantics cases q = uniq; p = uniq")]
    public void AConditionalSlotsRecordNamesItsCase(string declarations, string label)
        => Assert.Equal(label, Assert.Single(DiagnosticCorpus.Check(declarations + "\n" + Main).Diagnostics).Label);

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
}
