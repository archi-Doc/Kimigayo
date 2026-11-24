// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// IMPL A.4 (DECISIONS U-D2's entry; the owner's O-80 (b)): no Type higher than H = 8,192 is formed. A Type that would pass it is one
// Resource record TypeHeightLimit_Kd at the site whose Type it would be, never a Language record or a crash; a node reading the refused
// one rests on it, and an independent record stays. `D_n<T>.Item` doubles per declaration (height 2^n, no expression), so D13 is
// exactly H. Fail-before: dev accepts the H + 1 forms, crashes its 64 MB stack at D15, and the first head (64d0a22d8) recorded
// `InvalidConstraint_Kd` (Language) where D15's associated Type reads D14's refused one.
public class TypeHeightLimitTest(ITestOutputHelper output)
{
    private const string Main = "public func main() => ()\n";

    // An override whose Self's base is a doubling Type (AValidationPassPastTheLimitIsRecordedAtItsFrame's last form).
    private const string OverrideForm = "open struct Base<T>\n    public virtual func f(self: objref/Self) -> i32\n        T is HasItem\n        T.Item is Sealed\n" +
        "        return 1\nstruct Derived : Base<D12<{0}>>\n    override func f(self: objref/Self) -> i32 => 2\n";

    private const string IndependentMismatch = "func q() -> ()\n    let z: i32 = true\n    return\n";

    // A requirement over the associated Type, and W, whose associated Type is D13<i32>.Item (= H); W's members follow.
    private const string Pair = "contract HasPair\n    associate Item\n    func take(self: ref/Self, x: (Self.Item, i32))\n" +
        "struct W\n    Self is HasPair\n    associate HasPair.Item is D13<i32>.Item\n";

    public static TheoryData<int> Chains => [15, 16, 17];

    // SPEC 23.1.2, 8.3.3 (Limits): every check that reads an associated Type the limit refused rests on its conformance's record, so a
    // chain past the limit (D15 over D14, and so on) gives only height records, with a use or without one.
    [Theory]
    [MemberData(nameof(Chains))]
    public void AChainPastTheLimitGivesOnlyHeightRecords(int n)
    {
        Assert.All(this.Compile(Doubling(n) + $"func p(x: ref/D{n}<i32>.Item) => ()\n" + Main).Records, AssertHeightCode);
        Assert.All(this.Compile(Doubling(n) + Main).Records, AssertHeightCode);
    }

    // The invalid twins past the limit (a written parameter Type refused: gap 2 for a written Type) keep q's independent mismatch; p's
    // mismatch reads the refused parameter and rests.
    [Theory]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    public void AnInvalidTwinPastTheLimitKeepsOnlyItsIndependentRecord(int n)
    {
        var source = Doubling(n) + $"func p(x: D{n}<i32>.Item) -> ()\n    let y: i64 = x@move\n    return\n" + IndependentMismatch + Main;
        var records = this.Compile(source).Records;
        var mismatch = Assert.Single(records, static x => x.Code != nameof(DiagnosticCode.TypeHeightLimit_Kd));
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), mismatch.Code);
        Assert.True(mismatch.Span.Start > source.IndexOf("func q", StringComparison.Ordinal));
    }

    [Fact]
    public void AWrittenTypeAtTheLimitIsKeptAndOneLayerMoreIsRecordedAtIt()
    {
        // The instance collector caps a type-driven key's height at 128 (R5-M170): a Resource record of its own, not this limit's.
        var kept = this.Compile(Doubling(13) + "func p(x: D13<i32>.Item) => ()\n" + Main);
        Assert.Equal([nameof(DiagnosticCode.GenerationResourceLimit_Kd)], kept.Records.Select(static x => x.Code));

        var source = Doubling(13) + "func p(x: ref/D13<i32>.Item) => ()\n" + Main;
        var record = Assert.Single(this.Compile(source).Records);
        AssertHeightRecord(record, source, "ref/D13<i32>.Item");
    }

    [Fact]
    public void AnExpressionPastTheLimitIsRecordedAtItsNode()
    {
        // A local of a Type this deep may still meet the instance collector's own Resource limits; no height record and no Language one.
        var at = this.Compile(Doubling(13) + "func p(x: D13<i32>.Item) -> ()\n    let y = x@move\n    return\n" + Main);
        Assert.All(at.Records, static x => Assert.Equal(DiagnosticCategory.Resource, x.Category));
        Assert.DoesNotContain(at.Records, static x => x.Code == nameof(DiagnosticCode.TypeHeightLimit_Kd));

        var source = Doubling(13) + "func p(x: D13<i32>.Item) -> ()\n    let y = (x@move, 1)\n    return\n" + Main;
        var record = Assert.Single(this.Compile(source).Records);
        AssertHeightRecord(record, source, "(x@move, 1)");
    }

    // The declaration phase's site: D14's associated Type would have height 16,384 when its conformance is prepared.
    [Fact]
    public void AnAssociatedTypePastTheLimitIsRecordedAtItsConformance()
    {
        var source = Doubling(14) + Main;
        var record = Assert.Single(this.Compile(source).Records);
        AssertHeightRecord(record, source, "Self is HasItem", source.IndexOf("struct D14", StringComparison.Ordinal));

        // A use of the refused associated Type is its own formation past the limit, recorded at its written Type.
        var use = Doubling(14) + "func p(x: ref/D14<i32>.Item) => ()\n" + Main;
        var records = this.Compile(use).Records;
        Assert.All(records, AssertHeightCode);
        Assert.True(records.Length >= 2, "the conformance's record and the use's own");
    }

    // An invalid twin keeps its independent record (q) beside the height record; the mismatch that reads the refused `y` rests on it.
    // Under the limit both mismatches are reported (a shallow twin: the adapter's Type text of a deep Type is outside this unit).
    [Fact]
    public void TheIndependentRecordStaysAndTheDependentOneRests()
    {
        const string q = IndependentMismatch;
        var under = this.Compile(Doubling(4) + "func p(x: D4<i32>.Item) -> ()\n    let y = x@move\n    let b: i64 = y@move\n    return\n" + q + Main);
        Assert.Equal([nameof(DiagnosticCode.TypeMismatch_Kd), nameof(DiagnosticCode.TypeMismatch_Kd)], under.Records.Select(static x => x.Code));

        var source = Doubling(13) + "func p(x: D13<i32>.Item) -> ()\n    let y = (x@move, 1)\n    let b: i64 = y@move\n    return\n" + q + Main;
        var over = this.Compile(source).Records;
        Assert.Equal([nameof(DiagnosticCode.TypeHeightLimit_Kd), nameof(DiagnosticCode.TypeMismatch_Kd)], over.Select(static x => x.Code));
        AssertHeightRecord(over[0], source, "(x@move, 1)");
        Assert.True(over[1].Span.Start > source.IndexOf("func q", StringComparison.Ordinal));
    }

    // SPEC 8.3.3 (Limits): a conformance whose member's written Type passes the limit rests on that member's record, whether the proof
    // would end Unproven or Invalid; the requirement takes (Self.Item, i32), height 8,193 over D13 (review 2's `req-13`, `req-13-self`).
    [Theory]
    [InlineData("(D13<i32>.Item, i32)")]
    [InlineData("(Self.Item, i32)")]
    public void AConformanceWhoseMemberPassesTheLimitGivesOnlyHeightRecords(string parameter)
    {
        var records = this.Compile(Doubling(13) + Pair + $"    public func take(self: ref/Self, x: {parameter}) => ()\n" + Main).Records;
        Assert.NotEmpty(records);
        Assert.All(records, AssertHeightCode);
    }

    // SPEC 23.1.2: an independent Language failure beside a member past the limit is reported beside the height record.
    [Fact]
    public void AnIndependentFailureBesideAMemberPastTheLimitIsKept()
    {
        var source = Doubling(13) + Pair + "    public func take(self: ref/Self, x: (D13<i32>.Item, i32)) => ()\n    public func bad(self) -> i32 => true\n" + Main;
        var records = this.Compile(source).Records;
        var mismatch = Assert.Single(records, static x => x.Code != nameof(DiagnosticCode.TypeHeightLimit_Kd));
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), mismatch.Code);
        Assert.True(mismatch.Span.Start > source.IndexOf("func bad", StringComparison.Ordinal));
        Assert.Contains(records, static x => x.Code == nameof(DiagnosticCode.TypeHeightLimit_Kd));
    }

    // SPEC 8.6.2, 8.8.2 (PLAN G665; its review's `h41-u`, `hgt-u41`, `hgt-u44`): L(k)'s declaring layer L0<W^(200k)<X>> passes the limit
    // from k = 41. IMPL 21.3.2 (PLAN G651 a2, F's C8 rulings 16 (a) and 18; DECISIONS Q13): L41's base clause forms the inherited declared
    // Self past H, so the one record is there (in the declaration's frame, F1), in either declaration order; the use reads L41, failed by
    // that record, and rests (SPEC 23.1.2): exactly one TypeHeightLimit_Kd, never a Language record, a fault or InternalInvariant_Kd.
    // Moved from "at the use" (dev's site-less refusal had no site of its own; BU-148's refused-composition bullet is superseded).
    [Theory]
    [InlineData(41, 41)]
    [InlineData(44, 41)]
    [InlineData(44, 44)]
    public void AUseThroughAnInheritedLayerPastTheLimitRestsOnItsClause(int n, int k)
    {
        var source = Heirs("W", n, "Self is U") + $"func t(v: ref/L{k}<i32>) => useU(v)\n" + Main;
        AssertHeightRecord(Assert.Single(this.Compile(source).Records), source, "L40<", source.IndexOf("struct L41", StringComparison.Ordinal));
    }

    // IMPL 21.3.2 (PLAN G651 a2, F1; the design review's B3, q1-in/q1-rev/q1-41/q1-41-I): the layer whose clause forms the Type past H is
    // refused at that clause whatever the order its heirs are declared in, and the limit stops no independent check.
    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(false, "func bad() -> string => 1\n")]
    public void AnInheritedLayerPastTheLimitIsRefusedAtItsClauseInEitherOrder(bool reversed, string independent)
    {
        var source = Heirs("W", 40, "Self is U") + (reversed ? HeirLayer(42) + HeirLayer(41) : HeirLayer(41) + HeirLayer(42)) + independent + Main;
        var records = this.Compile(source).Records;
        AssertHeightRecord(Assert.Single(records, static x => x.Category == DiagnosticCategory.Resource), source, "L40<", source.IndexOf("struct L41", StringComparison.Ordinal));
        string[] want = independent.Length == 0 ? [] : [nameof(DiagnosticCode.TypeMismatch_Kd)];
        Assert.Equal(want, records.Where(static x => x.Category != DiagnosticCategory.Resource).Select(static x => x.Code));
    }

    // SPEC 8.8.2 (PLAN G665; the Front's ruling 3, item 2): a path with premises states them at its generic declaring layer, so where that
    // layer passes the limit the definition is refused at the first such heir's base clause, valid (`oc41-u`, `ocown-191`: Option<T> is
    // Copy) or not (`h41-c`, `hgt-c41`, `hgt-c44`: W<T> is not); never a fault (fail-before: dev faults on all five). IMPL 21.3.2, SPEC
    // 23.1.2 (PLAN G651 a2, F's C8 rulings 16 (a), 18, 21 and 26; DECISIONS Q13): a use reading the heir the limit made unavailable rests
    // (moved from "and at the use" in the first four rows); M keeps its premise-error context (SPEC 8.2.3, the owner's rule), so its use is
    // judged and forms its own instance past H, refused at its site (BU-148's reader rule): `atUse`.
    [Theory]
    [InlineData("W", 41, 0, "L41<i32>", "struct L41", false)]
    [InlineData("W", 44, 0, "L41<i32>", "struct L41", false)]
    [InlineData("W", 44, 0, "L44<i32>", "struct L41", false)]
    [InlineData("Option", 41, 0, "L41<i32>", "struct L41", false)]
    [InlineData("Option", 40, 191, "M<i32>", "struct M", true)]
    public void APathWithPremisesPastTheLimitIsRecordedAtItsClause(string wrapper, int n, int owner, string use, string refused, bool atUse)
    {
        var heir = owner > 0 ? $"open struct M<s/T> : L40<s/{Nest(wrapper, owner, "T")}>\n    s is owner\n" : string.Empty;
        var source = Heirs(wrapper, n, "Self is U when X is Copy") + heir + $"func t(v: ref/{use}) => useU(v)\n" + Main;
        var records = this.Compile(source).Records.Where(static x => x.Code == nameof(DiagnosticCode.TypeHeightLimit_Kd)).ToArray();
        var at = source.IndexOf(refused, StringComparison.Ordinal);
        AssertHeightRecord(Assert.Single(records, x => x.Span.Start > at && x.Span.Start < source.IndexOf('\n', at)), source, "L40<", at);
        var call = source.IndexOf("useU(v)", StringComparison.Ordinal);
        Assert.Equal(atUse ? 1 : 0, records.Count(x => x.Span.Start >= call));
    }

    // IMPL 21.3.2, SPEC 23.1.2 (PLAN G651 a2: F1, B1's frame, form M, R13.2, the base statement and its readers, B4, B5; F's C8 rulings 18,
    // 20, 21, 26 and 31): L(n)'s inherited path premise at its declared Self, P<W^200<W^8000<X>>> for n = 40, passes the limit, so L40's
    // base clause is refused (one TypeHeightLimit_Kd), L40 is unavailable and every reader of it rests, while each independent check stays
    // judged; n = 39 (8,000 + 201 + 1 short of it) is the control, its own records only. Rows: an inherited Field read (pisff: of a failed
    // slot, C8 ruling 33: FieldOwner names no stored slot past H, so the read rests; n = 39 reads the Field Place of no Type), write, Move and
    // borrow, a nonvirtual and a virtual inherited call, an override, a base call, the explicit and the omitted base constructor call (with
    // their arity records), a refuted step-5 clause of an inherited callee, L40's own member, a mismatch beside the limit. B6 (C8 ruling
    // 31): a refutation in L40's own member reads L40's declaration context, refused, and rests (a named dependent move; n = 39 keeps it).
    [Theory]
    [InlineData("pisf", FieldL0, "", "func t(v: ref/L{0}<i32>) -> i32 => v.n\n", "", "")]
    [InlineData("pisff", "    public var n: Missing\n", "", "func t(v: ref/L{0}<i32>) -> i32 => v.n\n", "UnresolvedBinding_Kd", "UnresolvedBinding_Kd")]
    [InlineData("pisw", FieldL0, "", "func t(v: uniq/L{0}<i32>)\n    v.n = 1\n", "", "")]
    [InlineData("x-pisw", FieldL0, "", "func t(v: uniq/L{0}<i32>)\n    v.n = \"s\"\n", "TypeMismatch_Kd", "TypeMismatch_Kd")]
    [InlineData("pism", "    public var s: string = \"\"\n", "", "func t(v: L{0}<i32>) -> string => v.s@move\n", "", "")]
    [InlineData("pisp", FieldL0, "", "func t(v: ref/L{0}<i32>) -> ref/i32 => v.n\n", "", "")]
    [InlineData("x-pisf", FieldL0, "", "func t(v: ref/L{0}<i32>) -> string => v.n\n", "TypeMismatch_Kd", "TypeMismatch_Kd")]
    [InlineData("pisg", FieldL0 + "    public func get(self: ref/Self) -> i32 => self.n\n", "", "func t(v: ref/L{0}<i32>) -> i32 => v.get()\n", "", "")]
    [InlineData("pisv-t", Virtual, "", "func t(v: objref/L{0}<i32>) -> i32 => v.f()\n", "", "")]
    [InlineData("pisv-o", Virtual, "    override func f(self: objref/Self) -> i32 => 2\n", "", "", "")]
    [InlineData("pisb", "    public func f(self: ref/Self) -> i32 => 1\n", "    public func g(self: ref/Self) -> i32 => base.f()\n", "", "", "")]
    [InlineData("pisx", Init0, "    public init() : base() => ()\n", "", "", "")]
    [InlineData("pisx-b", Init0, "    public init() : base(1) => ()\n", "", "NoApplicableOverload_Kd", "NoApplicableOverload_Kd")]
    [InlineData("x-pisg", Need, "", "func t(v: ref/L{0}<i32>) -> i32 => v.need(1)\n", "UnsatisfiedConstraint_Kd", "UnsatisfiedConstraint_Kd")]
    [InlineData("pisown", "", "    public func own(self: ref/Self) -> i32 => 1\n", "func t(v: ref/L{0}<i32>) -> i32 => v.own()\n", "", "")]
    [InlineData("x-pisown", "", Need, "func t(v: ref/L{0}<i32>) -> i32 => v.need(1)\n", "", "UnsatisfiedConstraint_Kd")]
    [InlineData("x-pism", "", "    func g(self: ref/Self) -> string => 1\n", "", "TypeMismatch_Kd", "TypeMismatch_Kd")]
    public void AnInheritedPathPremisePastTheLimitIsRecordedAtTheHeirsClause(string row, string l0Body, string lastBody, string use, string past, string control)
    {
        foreach (var n in (int[])[40, 39])
        {
            output.WriteLine($"{row} n={n}");
            var source = HeirsWithPremise(n, l0Body, lastBody) + string.Format(System.Globalization.CultureInfo.InvariantCulture, use, n) + Main;
            var records = this.Compile(source).Records;
            var expected = n == 40 ? past : control;
            string[] want = expected.Length == 0 ? [] : [expected];
            Assert.Equal(want, records.Where(static x => x.Category != DiagnosticCategory.Resource).Select(static x => x.Code));
            if (n == 40)
            {
                AssertHeightRecord(Assert.Single(records, static x => x.Category == DiagnosticCategory.Resource), source, "L39<", source.IndexOf("struct L40", StringComparison.Ordinal));
            }
            else
            {
                Assert.DoesNotContain(records, static x => x.Category == DiagnosticCategory.Resource);
            }
        }
    }

    // The omitted base constructor call (SPEC 6.3.3, 6.3.6; the Rev 2e delta read's pisx-40o-I): L(n-1) declares only `init(v: i32)`, so
    // L(n)'s `init()` with no base clause selects none: an arity record past the limit (IMPL 21.3.2) as within it.
    [Fact]
    public void AnOmittedBaseCallPastTheLimitKeepsItsOwnRecord()
    {
        foreach (var n in (int[])[40, 39])
        {
            var source = HeirsWithPremise(n, Init0, Init0, (n - 1, "    public init(v: i32) => ()\n")) + Main;
            var records = this.Compile(source).Records;
            Assert.Equal([nameof(DiagnosticCode.NoApplicableOverload_Kd)], records.Where(static x => x.Category != DiagnosticCategory.Resource).Select(static x => x.Code));
            Assert.Equal(n == 40 ? 1 : 0, records.Count(static x => x.Category == DiagnosticCategory.Resource));
        }
    }

    // SPEC 8.13.1, 23.1.2, IMPL 21.3.2 (PLAN G651 a2-2c, its implementation review's B1): a specialization's restated input past the
    // limit is refused at its written Type alone; the match reads the refused input and rests, so the original's input is not formed
    // again at the specialization (one Type, one record). One layer less is accepted.
    [Fact]
    public void ASpecializationInputPastTheLimitIsRecordedOnceAtItsWrittenType()
    {
        const string form = "func g<T>(value: D12<T>.Item) -> i32 => 1\nspecialize func g<{0}>(value: D12<{0}>.Item) -> i32 => 2\n";
        var (within, accepted) = this.Compile(Doubling(12) + form.Replace("{0}", "D12<i32>.Item", StringComparison.Ordinal) + Main);
        Assert.Empty(within);
        Assert.True(accepted);

        var source = Doubling(12) + form.Replace("{0}", "(D12<i32>.Item, i32)", StringComparison.Ordinal) + Main;
        var record = Assert.Single(this.Compile(source).Records);
        AssertHeightRecord(record, source, "D12<(D12<i32>.Item, i32)>.Item", source.IndexOf("specialize", StringComparison.Ordinal));
    }

    // PLAN G651 U1 (SPEC 23.1.2, 8.3.3 Limits): a validation pass forms its Types in a frame at the node its record names: a judgment's use
    // (a constructed Type node's Constraints, a Constraint environment's owner, a closed clause and its owner, an agreement's first path)
    // or a pass element that publishes facts (a source state's declaration, an override's premises). A Type past the limit formed there
    // is TypeHeightLimit_Kd at that node, never InternalInvariant_Kd or a Language record (an agreement's first path is `Self is Named`);
    // `{0}` one layer less is accepted, `Derived : Base<D12<D12<i32>.Item>>` too (labelled V by B's R7-T1 ruling 14: a valid override
    // within the limit; before R7-T1 the collector's storage-nesting gap, Unsupported_Kd; ADerivedOverAValidDoublingExecutes runs it).
    // Fail-before: dev gives InternalInvariant_Kd. The
    // `T is V` row reaches the class's Member through V, a node no premise rewrites (ContractTypeCore's (E) arm); fail-before: c2-4a's
    // b5c260990 formed its Type past the limit unrefused and accepted the program.
    // R7-C3 (the design review's C1; F's C2 ruling 257 (2)): struct E's clause is spelled at the limit (`D12<{0}>` written), so E's
    // validation pass forms the Type past the limit in its own frame; the forms as first written are
    // AClauseValidatedAsWrittenStaysWithinTheLimit's rows.
    [Theory]
    [InlineData("struct G<T>\n    D12<T>.Item is Sealed\n    var v: i32\nfunc p(x: ref/G<{0}>) => ()\n", "G<(")]
    [InlineData("struct E<T>\n    T is {0}\n    D12<{0}>.Item is Sealed\n    var v: i32\n", "struct E")]
    [InlineData("struct E<T>\n    T is {0}\n    D12<{0}>.Item is Sealed\n    var v: i32\n    public func get(self: ref/Self) -> i32\n        return self.v\n", "struct E")]
    [InlineData("func f<T>(x: i32) -> i32\n    T is D12<{0}>\n    T is HasItem\n    T.Item is Sealed\n    return x\n", "func f")]
    [InlineData("func f<T, V>(x: i32) -> i32\n    T is D12<{0}>\n    T is V\n    V is HasItem\n    V.Item is Sealed\n    return x\n", "func f")]
    [InlineData("struct G<T>\n    D12<T>.Item is Sealed\n    Self is HasItem\n    associate HasItem.Item is i32\nstruct S\n    G<{0}>.Item is Sealed\n    var v: i32\n", "struct S")]
    [InlineData("contract Named\ncontract Tagged: Named\nstruct Tag<T>\n    var value: i32\n    Self is Named when T is HasItem, T.Item is Sealed\n    Self is Tagged when T is D12<{0}>\n", "Self is Named")]
    [InlineData(OverrideForm, "override func f")]
    public void AValidationPassPastTheLimitIsRecordedAtItsFrame(string form, string at)
    {
        Assert.Empty(this.Compile(Doubling(12) + form.Replace("{0}", "D12<i32>.Item", StringComparison.Ordinal) + Main).Records);

        var source = Doubling(12) + form.Replace("{0}", "(D12<i32>.Item, i32)", StringComparison.Ordinal) + Main;
        var records = this.Compile(source).Records;
        Assert.All(records, AssertHeightCode);
        var start = source.IndexOf(at, StringComparison.Ordinal);
        Assert.Contains(records, x => x.Code == nameof(DiagnosticCode.TypeHeightLimit_Kd) && x.Span.Start >= start && x.Span.Start < start + at.Length);
    }

    // R7-T1 (B's ruling 14; IMPL 21.3.1; the Middle's RP17): the valid override over a doubling Type one layer within the limit
    // (Derived : Base<D12<D12<i32>.Item>>, an instance since the collector reads no storage verdict) is generated and runs.
    [Fact]
    public void ADerivedOverAValidDoublingExecutes()
        => ScalarEmissionTest.EmitFixture(
            "TypeHeightDerivedOverride", Doubling(12) + OverrideForm.Replace("{0}", "D12<i32>.Item", StringComparison.Ordinal) + Main, string.Empty);

    // SPEC 23.1.2: an environment past the limit keeps an independent record beside its height record (r2's `envs-13-twin`).
    // R7-C3 (C1; ruling 257 (2)): the clause spelled at the limit; the Type is formed past it in the closed clause's frame and its
    // owner's (the frames above), one height record each, and `bad`'s record stays.
    [Fact]
    public void AnEnvironmentPastTheLimitKeepsAnIndependentRecord()
    {
        var source = Doubling(12) + "struct E<T>\n    T is (D12<i32>.Item, i32)\n    D12<(D12<i32>.Item, i32)>.Item is Sealed\n    var v: i32\nfunc bad() -> i32\n    return true\n" + Main;
        var records = this.Compile(source).Records;
        Assert.Equal([nameof(DiagnosticCode.TypeHeightLimit_Kd), nameof(DiagnosticCode.TypeHeightLimit_Kd), nameof(DiagnosticCode.TypeMismatch_Kd)], records.Select(static x => x.Code));
        AssertHeightRecord(records[0], source, "struct E");
    }

    // R7-C3 (the design review's C-e and C1; F's C2 ruling 257 (2); SPEC 8.3.3 Limits, IMPL A.4): a clause is validated as written, so
    // `D12<T>.Item is Sealed` under `T is {0}` forms no Type past the limit and is accepted; dev rewrote `D12<T>` to `D12<{0}>` and
    // reached the limit (TypeHeightLimit_Kd at struct E). 8.3.3 and IMPL A.4 permit both verdicts; C3-1 removes the rewrite (its
    // commit (2)), a named move. Within and past the limit's spelling of `{0}` alike.
    [Theory]
    [InlineData("struct E<T>\n    T is {0}\n    D12<T>.Item is Sealed\n    var v: i32\n")]
    [InlineData("struct E<T>\n    T is {0}\n    D12<T>.Item is Sealed\n    var v: i32\n    public func get(self: ref/Self) -> i32\n        return self.v\n")]
    public void AClauseValidatedAsWrittenStaysWithinTheLimit(string form)
    {
        foreach (var argument in (string[])["D12<i32>.Item", "(D12<i32>.Item, i32)"])
        {
            var (records, accepted) = this.Compile(Doubling(12) + form.Replace("{0}", argument, StringComparison.Ordinal) + Main);
            Assert.Empty(records);
            Assert.True(accepted);
        }
    }

    // SPEC 8.8 (review 2's `ovcond`): an override condition over an associated Type within the limit is accepted (its pass is framed by
    // PLAN G651 a1, so no form past the limit is a row here).
    [Fact]
    public void AnOverrideConditionWithinTheLimitIsAccepted()
    {
        var (records, accepted) = this.Compile("contract HasItem\n    associate Item\nstruct Box<T>\n    Self is HasItem\n    associate HasItem.Item is (T, i32)\n" +
            "contract Marker\nopen struct Base<T>\n    public virtual func read(self: objref/Self) -> i32 => 1\nstruct Derived<U> : Base<U>\n    U is Box<i32>\n" +
            "    U is HasItem\n    Self is Marker when U is HasItem, U.Item is Copy\n        override func read(self: objref/Self) -> i32 => 2\n" + Main);
        Assert.Empty(records);
        Assert.True(accepted);
    }

    // DECISIONS BU-153: a Type's display does not recurse on the Type, so a record naming a Type under H, or a closure whose
    // display nests a chain of closure signatures (an edge the height does not count, BU-71), is reported on the caller's stack. The
    // CLI runs the display in tier-0 code: dev's recursion overflowed its 1.5 MB stack from about 820 layers (1,856 B a frame). This
    // host compiles optimized code (QuickJit off), whose frames hold H, so each row runs alone in a fresh process in tier-0 code.
    [Theory]
    [InlineData(nameof(ARecordNamingADeepTypeIsReported))]
    [InlineData(nameof(ARecordNamingAClosureChainIsReported))]
    public Task TierZeroCodeDisplaysADeepType(string row) => FreshProcess.RunExplicit(typeof(TypeHeightLimitTest), row, ("DOTNET_TC_QuickJit", "1"));

    [Fact(Explicit = true)]
    public void ARecordNamingADeepTypeIsReported()
    {
        var chain = Chain(8190, "i32");
        var mismatch = Assert.Single(this.Compile(Doubling(12) + $"func p() -> ()\n    let x: {chain} = true\n    return\n" + Main).Records);
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), mismatch.Code);
        Assert.StartsWith("expected ((((", mismatch.Label, StringComparison.Ordinal); // A record elides a long text in its middle.
        Assert.EndsWith(", i32), i32), found bool", mismatch.Label, StringComparison.Ordinal);

        // The valid twin, and the Origin relation that fails at the outer layer of the same Type (its facts name both Types).
        var (c, d) = ($"ref/({chain}) during c", $"ref/({chain}) during d");
        Assert.Empty(this.Compile(Doubling(12) + $"func f(x: {c}, y: {d}) -> {c}\n    return x@move\n" + Main).Records);
        var relation = Assert.Single(this.Compile(Doubling(12) + $"func f(x: {c}, y: {d}) -> {d}\n    return x@move\n" + Main).Records);
        Assert.Equal(nameof(DiagnosticCode.UnprovenOriginRelation_Kd), relation.Code);
    }

    // A closure's display shows its signature (SPEC 7.7): a closure whose anonymous function takes the previous closure nests the
    // display four frames deeper at a height under 8.
    [Fact(Explicit = true)]
    public void ARecordNamingAClosureChainIsReported()
    {
        var source = new System.Text.StringBuilder("func pass<F, G>(f: ref/F, g: G) -> G\n    G is Callable<(ref/F) -> i32>\n    return g@move\n");
        source.Append("public func main()\n    let c0 = func [] () -> i32 => 1\n");
        for (var i = 1; i <= 512; i++)
        {
            source.Append($"    let c{i} = pass(c{i - 1}@ref, func [] (x) -> i32 => 1)\n");
        }

        var mismatch = Assert.Single(this.Compile(source.Append("    let z: i32 = c512\n").ToString()).Records);
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), mismatch.Code);
        Assert.StartsWith("expected i32, found closure (ref/closure (ref/closure (", mismatch.Label, StringComparison.Ordinal);
        Assert.EndsWith(" -> i32) -> i32) -> i32", mismatch.Label, StringComparison.Ordinal);
    }

    // D0's Item is (T, i32); each D_i's is D_{i-1}<D_{i-1}<T>.Item>.Item, so D_n<i32>.Item has height 2^n.
    internal static string Doubling(int n)
    {
        var s = "contract HasItem\n    associate Item\nstruct D0<T>\n    Self is HasItem\n    associate HasItem.Item is (T, i32)\n";
        for (var i = 1; i <= n; i++)
        {
            s += $"struct D{i}<T>\n    Self is HasItem\n    associate HasItem.Item is D{i - 1}<D{i - 1}<T>.Item>.Item\n";
        }

        return s;
    }

    // A Tuple chain of the given height over `bottom`: D_n<T>.Item adds 2^n layers.
    private static string Chain(int height, string bottom)
    {
        for (var n = 0; n < 13; n++)
        {
            if ((height >> n & 1) != 0)
            {
                bottom = $"D{n}<{bottom}>.Item";
            }
        }

        return bottom;
    }

    private const string FieldL0 = "    public var n: i32 = 0\n";

    private const string Virtual = "    public virtual func f(self: objref/Self) -> i32 => 1\n";

    private const string Init0 = "    public init() => ()\n";

    private const string Need = "    public func need<T>(self: ref/Self, x: T) -> i32\n        T is Flag\n        return 0\n";

    private static string HeirLayer(int i) => $"open struct L{i}<X> : L{i - 1}<{Nest("W", 200, "X")}>\n";

    // L0's marker `Self is U when X is P<W^200<X>>` inherited through L(i)<X> : L(i-1)<W^200<X>>, i = 1..n: L(n)'s inherited path premise at
    // its declared Self has height 200(n + 1) + 2, past the limit from n = 40 (`heirsP` of PLAN G651's rows), L0's body first, `body` at L(n)
    // (and `extra` at the layer it names).
    private static string HeirsWithPremise(int n, string l0Body, string body, (int Layer, string Body) extra = default)
    {
        var s = new System.Text.StringBuilder("contract Flag\ncontract U\ncontract P<V>\nfunc useU<T>(value: ref/T)\n    T is U\n    ()\nstruct W<T>\n    var v: T\n");
        s.Append($"open struct L0<X>\n    Self is U when X is P<{Nest("W", 200, "X")}>\n").Append(l0Body);
        for (var i = 1; i <= n; i++)
        {
            s.Append(HeirLayer(i)).Append(i == n ? body : i == extra.Layer ? extra.Body : string.Empty);
        }

        return s.ToString();
    }

    // A marker conformance of L0 inherited through L(i)<X> : L(i-1)<wrapper^200<X>>, i = 1..n.
    private static string Heirs(string wrapper, int n, string marker)
    {
        var s = new System.Text.StringBuilder($"contract U\nfunc useU<T>(value: ref/T)\n    T is U\n    ()\nstruct W<T>\n    var v: T\nopen struct L0<X>\n    {marker}\n");
        for (var i = 1; i <= n; i++)
        {
            s.Append($"open struct L{i}<X> : L{i - 1}<{Nest(wrapper, 200, "X")}>\n");
        }

        return s.ToString();
    }

    private static string Nest(string wrapper, int k, string inner) => string.Concat(Enumerable.Repeat(wrapper + "<", k)) + inner + new string('>', k);

    private static void AssertHeightCode(DiagnosticEntryView record) => Assert.Equal(nameof(DiagnosticCode.TypeHeightLimit_Kd), record.Code);

    private static void AssertHeightRecord(DiagnosticEntryView record, string source, string at, int from = 0)
    {
        Assert.Equal((nameof(DiagnosticCode.TypeHeightLimit_Kd), DiagnosticCategory.Resource), (record.Code, record.Category));
        Assert.Contains("8192", record.Message, StringComparison.Ordinal);
        var start = source.IndexOf(at, from, StringComparison.Ordinal);
        Assert.InRange(record.Span.Start, start, start + at.Length - 1);
    }

    private (DiagnosticEntryView[] Records, bool Accepted) Compile(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("Deep.kimi", source));
        var checkedAll = c.Bind().IsComplete && c.Binding.CheckStartup(OutputKind.Application).IsComplete && c.Analyze().IsVerified;
        c.Binding.ReportDiagnostics();
        c.ReportAnalysis();
        var accepted = checkedAll && c.Accepts;
        var records = c.Diagnostics.Finalize(rejected: !accepted).Diagnostics
            .Select(static x => new DiagnosticEntryView(x.Code, x.Category, x.Message, x.Span ?? default, x.Label)).ToArray();
        output.WriteLine(string.Join(", ", records.Select(static x => $"{x.Code} {x.Span}")));
        return (records, accepted);
    }

    private readonly record struct DiagnosticEntryView(string Code, DiagnosticCategory Category, string Message, SourceSpan Span, string? Label);
}
