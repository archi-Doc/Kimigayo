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
}
