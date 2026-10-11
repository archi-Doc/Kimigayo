// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>SPEC 8.1.1, 8.4.3.1: a family may borrow the target `T` of a pair `s/T` stored in `Self`; the formation condition
/// `for ref/Self during a` proves the stored pair's dependencies, and so the target's, outlive `a`.</summary>
public class AssociatedPairFormationTest
{
    private const string Holder =
        "struct Holder<s/T>\n    s is value or valueborrow\n    Self is Peek\n    associate Peek.Item(a) is ref/T during a\n    var item: s/T\n" +
        "    public init(item: s/T) => self.item = item@move\n    public func peek(self: ref/Self during a) -> ref/T during a\n        return self.item@follow@ref\n";

    private const string Formed = "contract Peek\n    associate Item(a) for ref/Self during a\n    func peek(self: ref/Self during a) -> Self.Item(a)\n";

    private const string Uses =
        "func peekOf<C>(c: ref/C during b) -> C.(Peek).Item(b)\n    C is Peek\n    return c.peek()\n" +
        "let h = Holder<i32>.init(7)\nrequire h.peek() == 7 and peekOf(h@ref) == 7 else => $abort(\"peek\")\nConsole.writeLine(\"ok\")";

    private const string BorrowUses =
        "func peekOf<C>(c: ref/C during b) -> C.(Peek).Item(b)\n    C is Peek\n    return c.peek()\n" +
        "let n: i32 = 7\nvar m: i32 = 8\nlet h = Holder<ref/i32>.init(n@ref)\nlet u = Holder<uniq/i32>.init(m@uniq)\n" +
        "require h.peek() == 7 and peekOf(h@ref) == 7 and u.peek() == 8 and peekOf(u@ref) == 8 else => $abort(\"peek\")\nConsole.writeLine(\"ok\")";

    [Fact]
    public void PairTargetFamilyExecutes()
        => ScalarEmissionTest.EmitFixture("AssociatedPairTarget", Formed + Holder + Uses, "ok\n");

    // The family over a borrowed target: the stored reference's own Origin outlives the borrow of the Holder.
    [Fact]
    public void PairTargetFamilyExecutesForBorrowInstances()
        => ScalarEmissionTest.EmitFixture("AssociatedPairTargetBorrows", Formed + Holder + BorrowUses, "ok\n");

    // Without the formation condition nothing proves that the stored target outlives `a`.
    [Fact]
    public void PairTargetFamilyNeedsItsFormationCondition()
    {
        var c = MinimalEmissionTest.Analyze("contract Peek\n    associate Item(a)\n    func peek(self: ref/Self during a) -> Self.Item(a)\n" + Holder + "()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.InvalidOriginBinding_Kd);
    }

    // SPEC 8.1.2, 8.4.3: a family over a pair layer, annotated or applied, is a conditional borrow of its target: `s/U during a` and
    // `s/T during a` return the stored application and the stored `W` in owner and ref instances alike (A.15). Each header slot is
    // anchored by a Field in every instance, and the ref instance is inferred, since a written Type argument cannot bind its own
    // omitted Origin yet (PLAN G10).
    [Fact]
    public void PairLayerFamiliesExecute()
    {
        const string Source = Formed + "contract Pick\n    associate Item(a) for ref/Self during a\n    func pick(self: ref/Self during a) -> Self.Item(a)\n" +
            "struct Pair<s/T, U> {x}\n    s is owner or ref\n    T is Copy\n    U is Copy\n" +
            "    Self is Peek\n    associate Peek.Item(a) is s/U during a\n    Self is Pick\n    associate Pick.Item(a) is s/T during a\n" +
            "    public let anchor: ref/i32 during x\n    public let first: s/T during x\n    public let other: s/U during x\n" +
            "    public init(anchor: ref/i32 during x, first: s/T during x, other: s/U during x)\n" +
            "        self.anchor = anchor\n        self.first = first@move\n        self.other = other@move\n" +
            "    public func peek(self: ref/Self during a) -> s/U during a\n        return self.other\n" +
            "    public func pick(self: ref/Self during a) -> s/T during a\n        return self.first\n" +
            "func peekOf<C>(c: ref/C during b) -> C.(Peek).Item(b)\n    C is Peek\n    return c.peek()\n" +
            "func pickOf<C>(c: ref/C during b) -> C.(Pick).Item(b)\n    C is Pick\n    return c.pick()\n" +
            "let k: i32 = 9\nlet y: i64 = 7\nlet h = Pair.init(k@ref, k@ref, y@ref)\nlet g = Pair<i32, i64>.init(k@ref, 5, 6)\n" +
            "Console.writeLine(\"\\(peekOf(h@ref)) \\(peekOf(g@ref)) \\(pickOf(h@ref)) \\(pickOf(g@ref))\")";
        ScalarEmissionTest.EmitFixture("AssociatedPairLayers", Source, "7 6 9 5\n");
    }

    // SPEC 8.1.2, 8.4.3, 23.3.6.4: pair-layer families are formed like borrows whose slot exists in the binder's borrow cases. A missing
    // formation condition, a static slot under an admitted `uniq` and a slot that the target's Origins do not provably outlive are each
    // one record, as for the concrete twin; a binder admitting an object Semantics stays Unsupported, beside its own unproven Object
    // Target.
    [Theory]
    [InlineData(Formed + "struct H<s/T>\n    s is owner or ref\n    T is Copy\n    Self is Peek\n    associate Peek.Item(a) is s/T during a\n    var item: s/T\n    public init(item: s/T) => self.item = item@move\n    public func peek(self: ref/Self during a) -> s/T during a\n        return self.item\n", new string[0])]
    [InlineData("contract Peek\n    associate Item(a) for ref/Self during a\nstruct H<s/T, U>\n    s is value or valueborrow\n    Self is Peek\n    associate Peek.Item(a) is Option<s/U during a>\n    var item: s/T\n    var other: U\n", new string[0])]
    [InlineData("contract Peek<s/T, U>\n    s is value or valueborrow\n    associate Item(a) is s/U during a\n", new string[0])]
    [InlineData("contract Peek<s/T, U>\n    s is value or valueborrow\n    associate Item(a) for s/U during a\n", new string[0])]
    [InlineData("contract Peek\n    associate Item(a)\nstruct H<s/T, U>\n    s is value or valueborrow\n    Self is Peek\n    associate Peek.Item(a) is s/U during a\n    var item: s/T\n", new[] { "InvalidOriginBinding_Kd" })]
    [InlineData("contract Peek\n    associate Item(a) for ref/Self during a\nstruct H<s/T, U>\n    s is owner or uniq\n    Self is Peek\n    associate Peek.Item(a) is s/U during static\n    var item: s/T\n", new[] { "InvalidOriginBinding_Kd" })]
    [InlineData("contract Peek\n    associate Item(a) for ref/Self during a\nstruct H<s/T, U> {b}\n    s is owner or ref\n    Self is Peek\n    associate Peek.Item(a) is s/(ref/U during b) during a\n    var item: s/T\n    var other: ref/U during b\n", new[] { "InvalidOriginBinding_Kd" })]
    [InlineData("contract Peek\n    associate Item(a) for ref/Self during a\nstruct H<s/T, U>\n    s is owner or obj\n    Self is Peek\n    associate Peek.Item(a) is s/U during a\n    var item: s/T\n", new[] { "UnprovenConstraint_Kd", "Unsupported_Kd" })]
    public void PairLayerFamiliesAreFormedLikeBorrows(string source, string[] codes)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(codes.Order(), c.Binding.Issues.Select(static x => x.Code.ToString()).Order());
    }
}
