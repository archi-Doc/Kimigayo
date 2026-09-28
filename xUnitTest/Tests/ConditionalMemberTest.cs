// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 7.4 conditional members: a member function or constructor constrains its declaring Type's parameters, exists
/// only for receiver or constructed Types whose arguments satisfy the clauses, and its body relies on them.</summary>
public class ConditionalMemberTest
{
    private const string Box =
        "struct Box<E>\n    public var item: E\n    public init(item: E) => self.item = item@move\n" +
        "    public func same(self, other: ref/Self) -> bool\n        E is Equatable\n        return self.item == other.item\n" +
        "    public init(! copying: ref/E)\n        E is Copy\n        self.item = copying@follow\n" +
        "struct Point\n    public var x: i32\n    public init(x: i32) => self.x = x\n";

    private const string Program = Box +
        "func same<T>(b: ref/Box<T>, other: ref/Box<T>) -> bool\n    T is Equatable\n    return b.same(other)\n" +
        "public func main()\n    let a = Box<i32>.init(3)\n    let c = Box<i32>.init(copying: 3)\n    let d = Box<i32>.init(4)\n" +
        "    require a.same(c) and not a.same(d) and same(a@ref, c@ref) else => $abort(\"same\")\n    Console.writeLine(\"Conditional.\")\n";

    // SPEC 4.6.6: the Slice search members exist for Equatable elements and compare through shared references.
    private const string Search =
        "struct Tag\n    Self is Equatable\n    public var id: i32\n    public init(id: i32) => self.id = id\n" +
        "    public func equals(self: ref/Self, other: ref/Self) -> bool => self.id == other.id\n" +
        "public func main()\n    let tags: Array<Tag> = [Tag.init(1), Tag.init(2), Tag.init(2)]\n    let two = Tag.init(2)\n    let nine = Tag.init(9)\n" +
        "    require tags[..].contains(two@ref) and not tags[..].contains(nine@ref) else => $abort(\"contains\")\n" +
        "    match tags[1..].firstIndex(of: two@ref)\n        .Some(let i) => require i == 0 else => $abort(\"index\")\n        .None => $abort(\"missing\")\n" +
        "    match tags[..].firstIndex(of: nine@ref)\n        .Some(_) => $abort(\"none\")\n        .None => Console.writeLine(\"Search.\")\n";

    [Fact]
    public void Executes()
        => ScalarEmissionTest.EmitFixture("ConditionalMember", Program, "Conditional.\n");

    [Fact]
    public void SliceSearchExecutes()
        => ScalarEmissionTest.EmitFixture("ConditionalMemberSearch", Search, "Search.\n");

    [Theory]
    [InlineData("public func main()\n    let a = Box<Point>.init(Point.init(1))\n    let r = a.same(a)", DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData("func f<T>(b: ref/Box<T>) -> bool\n    return b.same(b)", DiagnosticCode.UnprovenConstraint_Kd)]
    [InlineData("public func main()\n    let p = Point.init(1)\n    let a = Box<Point>.init(copying: p@ref)", DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData("public func main()\n    let points: Array<Point> = [Point.init(1)]\n    let p = Point.init(1)\n    let r = points[..].contains(p@ref)", DiagnosticCode.NoApplicableOverload_Kd)]
    public void RefutedMembersAreNotCandidates(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(Box + source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }

    // A clause subject must still be a parameter of the function or of its declaring Type.
    [Fact]
    public void ForeignSubjectsAreRejected()
    {
        var c = MinimalEmissionTest.Analyze("struct Other<F>\n    var f: F\nstruct Box<E>\n    public var item: E\n    public func bad(self) -> bool\n        F is Equatable\n        return true\n");
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}
