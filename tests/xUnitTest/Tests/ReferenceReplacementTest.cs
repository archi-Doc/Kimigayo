// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>SPEC 13.7, 7.1.1, 13.5.5.1, 4.6.9: an assignment through a Place call result, a followed uniq reference or a
/// field reached through a reference is one replacement at that address: the old value is destroyed by its Type's plan
/// and the new value moves in, for any stored Type, also inside generic bodies. Each literal is destroyed exactly once.</summary>
public class ReferenceReplacementTest
{
    private const string Pair =
        "struct Pair<T>\n    Self is UniqIndexable<isize>\n    associate Element is T\n    var first: T\n    var second: T\n" +
        "    public init(first: T, second: T)\n        self.first = first@move\n        self.second = second@move\n" +
        "    public func index(self, key: ref/isize) -> place ref/T during self\n        if key == 0 => return self.first\n        return self.second\n" +
        "    public func indexUniq(self: uniq/Self, key: ref/isize) -> place uniq/T during self\n        if key == 0 => return self.first\n        return self.second\n";

    private const string Box = "struct Box<T>\n    public var value: T\n    public init(value: T) => self.value = value@move\n";

    // Each fixture prints the replaced value (a borrow, not a copy) before "ok"; comparisons use no replaced literal.
    public static TheoryData<string, string, string, string> Fixtures => new()
    {
        { "PlaceCall", Pair + "var pair = Pair<string>.init(\"old\", \"sibling\")\npair[0] = \"new\"\nConsole.writeLine(pair[0])\nConsole.writeLine(\"ok\")", "new\nok\n", "old=1;sibling=1;new=1;ok=1" },
        { "PlaceCallAggregate", Pair + "var pair = Pair<(string, i32)>.init((\"old\", 1), (\"sibling\", 2))\npair[1] = (\"new\", 40)\nrequire pair[1].1 == 40 and pair[0].1 == 1 else => $abort(\"tuple\")\nConsole.writeLine(pair[1].0)\nConsole.writeLine(\"ok\")", "new\nok\n", "old=1;sibling=1;new=1;tuple=0;ok=1" },
        { "Referent", "var text = \"old\"\nlet exclusive = text@uniq\nexclusive@follow = \"new\"\nConsole.writeLine(text)\nConsole.writeLine(\"ok\")", "new\nok\n", "old=1;new=1;ok=1" },
        { "BorrowedField", Box + "func set(box: uniq/Box<string>) => box.value = \"new\"\nvar box = Box<string>.init(\"old\")\nset(box@uniq)\nConsole.writeLine(box.value)\nConsole.writeLine(\"ok\")", "new\nok\n", "old=1;new=1;ok=1" },
        { "GenericPlaceCall", Pair + "func put<S>(items: uniq/S, value: S.Element)\n    S is UniqIndexable<isize>\n    items[1] = value@move\nvar pair = Pair<string>.init(\"sibling\", \"old\")\nput(pair@uniq, \"new\")\nConsole.writeLine(pair[1])\nConsole.writeLine(\"ok\")", "new\nok\n", "sibling=1;old=1;new=1;ok=1" },
        { "GenericReferent", "func put<T>(target: uniq/T, value: T) => target@follow = value@move\nvar text = \"old\"\nput(text@uniq, \"new\")\nvar pair = (\"first\", 1)\nput(pair@uniq, (\"second\", 2))\nrequire pair.1 == 2 else => $abort(\"generic referent\")\nConsole.writeLine(text)\nConsole.writeLine(pair.0)\nConsole.writeLine(\"ok\")", "new\nsecond\nok\n", "old=1;new=1;first=1;second=1;generic referent=0;ok=1" },
        { "GenericField", Box + "func put<T>(box: uniq/Box<T>, value: T) => box.value = value@move\nvar box = Box<string>.init(\"old\")\nput(box@uniq, \"new\")\nConsole.writeLine(box.value)\nConsole.writeLine(\"ok\")", "new\nok\n", "old=1;new=1;ok=1" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ReplacesThroughTheAddress(string name, string source, string stdout, string destructions)
    {
        var fixture = "ReferenceReplacement" + name;
        var ir = ScalarEmissionTest.EmitFixture(fixture, source, stdout);
        StringEmissionTest.WriteAuditedFixture(fixture, source, ir, stdout, destructions);
    }
}
