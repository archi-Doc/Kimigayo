// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>A5 on the assumed continuation of a Never form (SPEC 14.10.3, 15.6; PLAN G305, DECISIONS R4-M158): the continuation continues
/// from the state after the form's operands, where every check applies, so a value it uses is live back across the form; a P1 join takes no
/// assumed state, so a use after the join keeps nothing live on a continuation that flows into it.</summary>
public sealed partial class MirBorrowsTest
{
    // The continuation shapes: a Never callee, a consumer, a counted callee, a callee with a shared parameter and a receiver with an
    // exclusive `self`, with a Never callee of its Type.
    private const string ContinuationShapes = """
        func take(t: string) -> Never => $abort("t")
        func consume(t: string) => ()
        func cnt(t: string) -> i32 => 0
        func pair(a: ref/string, b: isize) -> i32 => 0
        func lose(c: Counter) -> Never => $abort("t")
        struct Counter
            var count: i32
            public init() => self.count = 0
            public func bump(self: uniq/Self, by: i32) => self.count = self.count + by

        """;

    // A Move or write before a Never form, in its operands or earlier, conflicts with a Loan a use on its continuation keeps (a Never
    // call, a return, `loop`, `exit`; a placement's secured right side, a shared argument, a later statement, a reservation's activation);
    // without a conflicting access (ad4m, r3) or with the use after a P1 join (s4, s5, s6) the program is valid. Each row is a body of
    // `func <name>(c: bool) -> i32` after `var x = "x"`, returning 0, and its one problem or none.
    [Theory]
    [InlineData("ad4", "    let y = \"y\"\n    var us: [1 of ref/string] = [y@ref]\n    us[take(x@move)] = x@ref\n", "x")]
    [InlineData("ad4r", "    let y = \"y\"\n    var us: [1 of ref/string] = [y@ref]\n    us[(return cnt(x@move))] = x@ref\n", "x")]
    [InlineData("ad4m", "    let y = \"y\"\n    var us: [1 of ref/string] = [y@ref]\n    us[take(\"t\")] = x@ref\n", "")]
    [InlineData("c1", "    _ = pair(x@ref, take(x@move))\n", "x")]
    [InlineData("s2", "    let r = x@ref\n    take(x@move)\n    inspect(r)\n", "x")]
    [InlineData("s3", "    let r = x@ref\n    x = \"z\"\n    take(\"t\")\n    inspect(r)\n", "x = \"z\"")]
    [InlineData("s7", "    let r = x@ref\n    x = \"z\"\n    loop => ()\n    inspect(r)\n", "x = \"z\"")]
    [InlineData("s9", "    let r = x@ref\n    while c\n        x = \"z\"\n        exit\n        inspect(r)\n", "x = \"z\"")]
    [InlineData("s4", "    let r = x@ref\n    if c\n        take(\"t\")\n        consume(x@move)\n    inspect(r)\n", "")]
    [InlineData("s5", "    let r = x@ref\n    if c\n        take(x@move)\n    inspect(r)\n", "")]
    [InlineData("s6", "    let r = x@ref\n    if c\n        consume(x@move)\n        return 1\n    inspect(r)\n", "")]
    [InlineData("r2", "    var k = Counter.init()\n    k.bump(lose(k@move))\n", "k CallReservationConflict_Kd")]
    [InlineData("r3", "    var k = Counter.init()\n    k.bump((return 1))\n", "")]
    public void ALoanStaysLiveIntoANeverFormsContinuation(string name, string body, string problem)
        => Assert.Equal(
            problem.Length == 0 ? [] : [$"{name}: {problem}"],
            Problems($"{ContinuationShapes}func {name}(c: bool) -> i32\n    var x = \"x\"\n{body}    return 0\n"));
}
