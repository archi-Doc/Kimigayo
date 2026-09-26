// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.6.6: the Slice operations tryGet, trySlice, splitAt and trySplitAt are written in Kimigayo; the try-prefixed
/// forms convert only their own bounds failure to None, and every result retains the backing source.</summary>
public class SliceOperationsTest
{
    private const string Operations =
        "let values: [4 of i32] = [10, 20, 30, 40]\nlet s = values[1..]\n" +
        "match s.tryGet(10)\n    .Some(_) => $abort(\"beyond\")\n    .None => Console.writeLine(\"None for 10.\")\n" +
        "match s.tryGet(^1)\n    .Some(let last)\n        require last == 40 else => $abort(\"last\")\n    .None => $abort(\"none\")\n" +
        "match s.tryGet(^0)\n    .Some(_) => $abort(\"end boundary\")\n    .None => Console.writeLine(\"End boundary is not an element.\")\n" +
        "let parts = s.splitAt(1)\nrequire parts.0.length == 1 and parts.0[0] == 20 and parts.1.length == 2 and parts.1[0] == 30 else => $abort(\"split\")\n" +
        "let ends = s.splitAt(^0)\nrequire ends.0.length == 3 and ends.1.isEmpty else => $abort(\"split at end\")\n" +
        "match s.trySplitAt(5)\n    .Some(_) => $abort(\"beyond split\")\n    .None => Console.writeLine(\"None for split 5.\")\n" +
        "match s.trySplitAt(Index.init(0))\n    .Some(let pair)\n        require pair.0.isEmpty and pair.1.length == 3 else => $abort(\"split 0\")\n    .None => $abort(\"split none\")\n" +
        "match s.trySlice(1..)\n    .Some(let tail)\n        require tail.length == 2 and tail[0] == 30 else => $abort(\"tail\")\n    .None => $abort(\"tail none\")\n" +
        "match s.trySlice(2..=5)\n    .Some(_) => $abort(\"beyond slice\")\n    .None => Console.writeLine(\"None for 2..=5.\")\n" +
        "let r = ResolvedRange.init(start: 1, end: 3)\nmatch s.trySlice(r)\n    .Some(let inner)\n        require inner.length == 2 and inner[1] == 40 else => $abort(\"resolved\")\n    .None => $abort(\"resolved none\")\n" +
        "match s.trySlice(ResolvedRange.init(start: 2, end: 5))\n    .Some(_) => $abort(\"beyond resolved\")\n    .None => Console.writeLine(\"None for resolved.\")\n" +
        "let text: [2 of string] = [\"First text.\", \"Last text.\"]\nlet words = text[..]\n" +
        "match words.tryGet(1)\n    .Some(let word) => Console.writeLine(word)\n    .None => $abort(\"word\")\n" +
        "func tryTail<T>(items: Slice<T>) -> Option<Slice<T> during items.source>\n    return items.trySlice(1..)\n" +
        "match tryTail(words)\n    .Some(let rest) => Console.writeLine(rest[0])\n    .None => $abort(\"generic tail\")\n" +
        "match tryTail(words[1..])\n    .Some(let rest)\n        require rest.isEmpty else => $abort(\"tail of one\")\n    .None => $abort(\"tail of one none\")\n" +
        "match tryTail(words[2..])\n    .Some(_) => $abort(\"empty tail\")\n    .None => Console.writeLine(\"None for an empty tail.\")\n" +
        "Console.writeLine(\"ok\")";

    [Fact]
    public void Executes()
        => ScalarEmissionTest.EmitFixture("SliceOperations", Operations, "None for 10.\nEnd boundary is not an element.\nNone for split 5.\nNone for 2..=5.\nNone for resolved.\nLast text.\nLast text.\nNone for an empty tail.\nok\n");
}
