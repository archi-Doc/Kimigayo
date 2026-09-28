// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.6.2–4.6.4: Index, Range and ResolvedRange are library structs. Range syntax outside an index position
/// constructs a Range, boundaries evaluate in order before their signs are checked, and resolution against a length
/// produces or refuses a ResolvedRange.</summary>
public class RangeValueTest
{
    private const string Resolve =
        "let values: [5 of i32] = [1, 2, 3, 4, 5]\nlet half = 1..3\nlet inclusive = 1..=3\nlet whole = ..\nlet tail = 2..\nlet head = ..^1\n" +
        "let a = half.resolve(values.length)\nlet b = inclusive.resolve(values.length)\nlet c = whole.resolve(values.length)\nlet d = tail.resolve(values.length)\nlet e = head.resolve(values.length)\n" +
        "require a.start == 1 and a.end == 3 and a.length == 2 and a.isEmpty == false else => $abort(\"half\")\n" +
        "require b.start == 1 and b.end == 4 and c.start == 0 and c.end == 5 else => $abort(\"inclusive or open\")\n" +
        "require d.start == 2 and d.end == 5 and e.start == 0 and e.end == 4 else => $abort(\"open sides\")\n" +
        "require half == (1..3) and half != inclusive and whole == (0..^0) and (1..=2) != (1..3) else => $abort(\"equality\")\n" +
        "require Index.init(1) == Index.init(1) and ^1 != Index.init(1) and (^0).resolve(4) == 4 and Index.init(2).resolve(2) == 2 else => $abort(\"index\")\n" +
        "let empty = (3..3).resolve(3)\nrequire empty.isEmpty and empty.length == 0 else => $abort(\"empty\")\n" +
        "let direct = ResolvedRange.init(start: 2, end: 5)\nrequire direct.length == 3 and direct == ResolvedRange.init(start: 2, end: 5) else => $abort(\"direct\")\n" +
        "var sum: isize = 0\nfor i in direct\n    sum = sum + i\nrequire sum == 9 else => $abort(\"iteration\")\n" +
        "var count: isize = 0\nfor i in values.indices\n    count = count + 1\nrequire count == 5 else => $abort(\"indices\")\n" +
        "match (3..2).tryResolve(5)\n    .Some(_) => $abort(\"reversed\")\n    .None => Console.writeLine(\"Reversed refused.\")\n" +
        "match (1..=4).tryResolve(4)\n    .Some(_) => $abort(\"inclusive end\")\n    .None => Console.writeLine(\"Inclusive end refused.\")\n" +
        "match (^6..).tryResolve(5)\n    .Some(_) => $abort(\"from-end start\")\n    .None => Console.writeLine(\"Long from-end refused.\")\n" +
        "match Index.init(3).tryResolve(2)\n    .Some(_) => $abort(\"index\")\n    .None => Console.writeLine(\"Index refused.\")\n" +
        "match (2..5).tryResolve(5)\n    .Some(let r) => require r.start == 2 and r.end == 5 else => $abort(\"some\")\n    .None => $abort(\"none\")\nConsole.writeLine(\"ok\")";

    private const string Order =
        "func first() -> isize\n    Console.writeLine(\"Start evaluated.\")\n    return 1\nfunc second() -> Index\n    Console.writeLine(\"End evaluated.\")\n    return ^1\n" +
        "let bounds: Range = first()..second()\nrequire bounds.start.offset == 1 and bounds.start.isFromEnd == false and bounds.end.isFromEnd and bounds.isInclusive == false else => $abort(\"bounds\")\n" +
        "let saved = bounds\nlet resolved = saved.resolve(4)\nrequire resolved.start == 1 and resolved.end == 3 else => $abort(\"saved\")\nConsole.writeLine(\"ok\")";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "Resolve", Resolve, "Reversed refused.\nInclusive end refused.\nLong from-end refused.\nIndex refused.\nok\n" },
        { "Order", Order, "Start evaluated.\nEnd evaluated.\nok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("RangeValue" + name, source, stdout);

    [Fact]
    public void RangeIsNotIterable()
    {
        var c = MinimalEmissionTest.Analyze("for i in 0..3\n    ()");
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnsupportedBinding_Kd);
    }

    [Fact]
    public void InclusiveEndCannotBeOmitted()
    {
        var c = MinimalEmissionTest.Analyze("let r: Range = 1..=");
        Assert.NotEmpty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
    }
}
