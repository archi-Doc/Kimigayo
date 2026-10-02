// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.6.2–4.6.4: FromEnd&lt;T&gt;, Start, End, Range&lt;S, E&gt;, ClosedRange&lt;S, E&gt; and ResolvedRange are
/// library structs. Range syntax constructs the Type of its shape from independently typed boundaries, construction checks
/// nothing, and resolution against a length produces or refuses a ResolvedRange.</summary>
public class RangeValueTest
{
    private const string Resolve =
        "let values: [5 of i32] = [1, 2, 3, 4, 5]\nlet half = 1..3\nlet closed = 1..=3\nlet whole = ..\nlet tail = 2..\nlet head = ..^1\n" +
        "let a = half.resolve(values.length)\nlet b = closed.resolve(values.length)\nlet c = whole.resolve(values.length)\nlet d = tail.resolve(values.length)\nlet e = head.resolve(values.length)\n" +
        "require a.start == 1 and a.end == 3 and a.length == 2 and a.isEmpty == false else => $abort(\"half\")\n" +
        "require b.start == 1 and b.end == 4 and c.start == 0 and c.end == 5 else => $abort(\"closed or open\")\n" +
        "require d.start == 2 and d.end == 5 and e.start == 0 and e.end == 4 else => $abort(\"open sides\")\n" +
        "require half == (1..3) and half != (1..4) and whole == (..) and tail == (2..) and head == (..^1) and closed == (1..=3) else => $abort(\"equality\")\n" +
        "require ^1 == ^1 and ^1 != ^2 and (^0).resolve(4) == 4 and (^1).resolve(4) == 3 else => $abort(\"positions\")\n" +
        "let empty = (3..3).resolve(3)\nrequire empty.isEmpty and empty.length == 0 else => $abort(\"empty\")\n" +
        "let direct = (2..5).resolve(5)\nrequire direct.length == 3 and direct == (2..^0).resolve(5) and direct != values.indices else => $abort(\"direct\")\n" +
        "var sum: isize = 0\nfor i in direct\n    sum = sum + i\nrequire sum == 9 else => $abort(\"iteration\")\n" +
        "var count: isize = 0\nfor i in values.indices\n    count = count + 1\nrequire count == 5 else => $abort(\"indices\")\n" +
        "match (3..2).tryResolve(5)\n    .Some(_) => $abort(\"reversed\")\n    .None => Console.writeLine(\"Reversed refused.\")\n" +
        "match (1..=4).tryResolve(4)\n    .Some(_) => $abort(\"closed end\")\n    .None => Console.writeLine(\"Closed end refused.\")\n" +
        "match (^6..).tryResolve(5)\n    .Some(_) => $abort(\"from-end start\")\n    .None => Console.writeLine(\"Long from-end refused.\")\n" +
        "match (..=^0).tryResolve(5)\n    .Some(_) => $abort(\"end element\")\n    .None => Console.writeLine(\"End element refused.\")\n" +
        "match (^3).tryResolve(2)\n    .Some(_) => $abort(\"position\")\n    .None => Console.writeLine(\"Position refused.\")\n" +
        "match (..).tryResolve(-1)\n    .Some(_) => $abort(\"negative length\")\n    .None => Console.writeLine(\"Negative length refused.\")\n" +
        "match (2..5).tryResolve(5)\n    .Some(let r) => require r.start == 2 and r.end == 5 else => $abort(\"some\")\n    .None => $abort(\"none\")\nConsole.writeLine(\"ok\")";

    private const string Order =
        "func first() -> isize\n    Console.writeLine(\"Start evaluated.\")\n    return 1\nfunc second() -> FromEnd<isize>\n    Console.writeLine(\"End evaluated.\")\n    return ^1\n" +
        "let bounds = first()..second()\nrequire bounds.start == 1 and bounds.end == ^1 and bounds.end.offset == 1 else => $abort(\"bounds\")\n" +
        "let saved = bounds\nlet resolved = saved.resolve(4)\nrequire resolved.start == 1 and resolved.end == 3 else => $abort(\"saved\")\n" +
        "let reversed = 3..1\nlet negative = ^(-1)\nrequire reversed.start == 3 and negative.offset == -1 else => $abort(\"unchecked\")\n" +
        "Console.writeLine(\"\\(bounds) \\(resolved) \\(..=^2) \\(2..) \\(..)\")\nConsole.writeLine(\"ok\")";

    // SPEC utf8-formatting §4.2: `^` and the offset, nothing for Start and End, and each boundary of a range as written.
    private const string Formats =
        "let whole = ..\nlet n: u8 = 7\nConsole.writeLine(\"\\(^2) [\\(whole.start)] [\\(whole.end)] \\(0..=3) \\(..^1) \\(^n..) \\(..=^(0@i64)) \\((1..4).resolve(9))\")";

    // SPEC 4.6.3.4: an entry copies the boundaries, so a returned iterator keeps no borrow of its local range.
    private const string Entries =
        "func makeNumbers() -> RangeIterator<i32>\n    let numbers = 0..3\n    return numbers.iterate()\n" +
        "func makeClosed() -> ClosedRangeIterator<u8>\n    let numbers = 250@u8..=255\n    return numbers.intoIterator()\n" +
        "var total = 0\nfor number in Kimi.Iteration.owning(makeNumbers())\n    total += number\n" +
        "var count = 0\nfor b in Kimi.Iteration.owning(makeClosed())\n    count += 1\nrequire total == 3 and count == 6 else => $abort(\"entries\")\nConsole.writeLine(\"ok\")";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "Formats", Formats, "^2 [] [] 0..=3 ..^1 ^7.. ..=^0 1..4\n" },
        { "Entries", Entries, "ok\n" },
        { "Resolve", Resolve, "Reversed refused.\nClosed end refused.\nLong from-end refused.\nEnd element refused.\nPosition refused.\nNegative length refused.\nok\n" },
        { "Order", Order, "Start evaluated.\nEnd evaluated.\n1..^1 1..3 ..=^2 2.. ..\nok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("RangeValue" + name, source, stdout);

    [Theory]
    [InlineData("for i in 1..^1\n    ()")]
    [InlineData("let inner = 1..^1\nfor i in inner => ()")]
    [InlineData("for i in ..3 => ()")]
    [InlineData("let tail = 0..\nfor i in tail => ()")]
    [InlineData("for i in 0@i32..3@i64 => ()")]
    public void RangeWithoutOneIntegerTypeIsNotIterable(string source)
    {
        // SPEC 4.6.3.4, 14.6.2: the conditional entry conformances need `S is PrimitiveInteger` and `E is S`; a refuted
        // condition supplies no conformance, so the Subject refutes the loop's requirement.
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var issue = Assert.Single(c.Binding.Issues);
        Assert.Equal(DiagnosticCode.UnsatisfiedConstraint_Kd, issue.Code);
        Assert.Same(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ForKoto>().Single().Iterable, issue.Node);
    }

    [Fact]
    public void InclusiveEndCannotBeOmitted()
    {
        var c = MinimalEmissionTest.Analyze("let r = 1..=");
        Assert.NotEmpty(TestDiagnostics.Of(c, "Hello.kimi"));
    }
}
