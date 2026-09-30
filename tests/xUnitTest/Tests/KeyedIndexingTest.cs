// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.6.1, 4.6.4, 4.6.9: built-in element selection accepts a position of any Type satisfying Position and range
/// selection a range of any Type satisfying PositionRange, including closed and from-end range syntax; keys resolve against
/// the current length.</summary>
public class KeyedIndexingTest
{
    private const string IndexKeys =
        "let values: [4 of i32] = [10, 20, 30, 40]\nlet last = ^1\n" +
        "require values[last] == 40 and values[1@u8] == 20 and values[^4] == 10 and values[2@i64] == 30 else => $abort(\"fixed\")\n" +
        "let view = values[..]\nrequire view[last] == 40 and view[^2] == 30 and view[0@u64] == 10 else => $abort(\"slice\")\n" +
        "let dynamic: Array<i32> = [1, 2, 3]\nrequire dynamic[^1] == 3 and dynamic[1@i16] == 2 and dynamic[last] == 3 else => $abort(\"array\")\n" +
        "func at(items: ref/[4 of i32], position: FromEnd<i32>) -> i32 => items[position]\nrequire at(values, ^2) == 30 else => $abort(\"through reference\")\n" +
        "func pick<P>(items: Slice<i32>, position: P) -> i32\n    P is Position\n    return items[position]\nlet everything = ..\nrequire pick(values[..], ^1) == 40 and pick(values[..], 1@u8) == 20 and pick(values[..], everything.start) == 10 else => $abort(\"generic\")\n" +
        "let text: [2 of string] = [\"First text.\", \"Last text.\"]\nlet words = text[..]\nConsole.writeLine(words[last])\nConsole.writeLine(words[^2])\n" +
        "let borrowed = words[0@usize]@ref\nConsole.writeLine(borrowed)\nConsole.writeLine(\"ok\")";

    private const string RangeKeys =
        "let values: [5 of i32] = [1, 2, 3, 4, 5]\nlet saved = 1..^1\nlet middle = values[saved]\n" +
        "require middle.length == 3 and middle[0] == 2 and middle[^1] == 4 else => $abort(\"saved\")\n" +
        "let inclusive = values[1..=2]\nrequire inclusive.length == 2 and inclusive[0] == 2 and inclusive[1] == 3 else => $abort(\"inclusive\")\n" +
        "let tail = values[^2..]\nrequire tail.length == 2 and tail[0] == 4 else => $abort(\"from-end start\")\n" +
        "let head = values[..^1]\nrequire head.length == 4 and head[3] == 4 else => $abort(\"from-end end\")\n" +
        "let resolved = saved.resolve(values.length)\nlet again = values[resolved]\nrequire again.length == 3 and again[1] == 3 else => $abort(\"resolved\")\n" +
        "let empty = values[^0..]\nrequire empty.isEmpty else => $abort(\"empty\")\n" +
        "let whole = ..\nrequire values[whole].length == 5 and middle[whole].length == 3 else => $abort(\"whole\")\n" +
        "let nested = middle[1..]\nrequire nested.length == 2 and nested[0] == 3 else => $abort(\"nested\")\n" +
        "let direct = (1..3).resolve(3)\nlet applied = middle[direct]\nrequire applied.length == 2 and applied[0] == 3 else => $abort(\"direct\")\n" +
        "Console.writeLine(\"ok\")";

    // SPEC 4.6.5, 4.6.6: a view of a fixed-array element reached through a Slice retains the backing storage, not the
    // handles used to form it.
    private const string NestedViews =
        "let grid: [3 of [4 of i32]] = [[1, 2, 3, 4], [10, 20, 30, 40], [5, 6, 7, 8]]\nlet rows: Slice<[4 of i32]> = grid[1..]\nlet row = rows[0][..]\n" +
        "require row.length == 4 and row[1] == 20 else => $abort(\"row\")\n" +
        "let middle: Slice<i32> = label selection: do\n    let inner: Slice<[4 of i32]> = grid[1..]\n    let line = inner[0][..]\n    exit to selection line[1..3]\n" +
        "require middle.length == 2 and middle[0] == 20 and middle[1] == 30 else => $abort(\"middle\")\n" +
        "let last = rows[^1][1..]\nrequire last.length == 3 and last[0] == 6 else => $abort(\"last\")\n" +
        "let saved = 1..^1\nlet keyed = rows[0][saved]\nrequire keyed.length == 2 and keyed[0] == 20 else => $abort(\"keyed\")\n" +
        "func head(table: ref/[3 of [4 of i32]]) -> i32 => table[2][..][0]\nrequire head(grid) == 5 else => $abort(\"reference\")\nConsole.writeLine(\"ok\")";

    // SPEC 4.6.4: a key resolves against the length of the receiver the selection evaluated, so a call receiver runs once.
    private const string CallReceivers =
        "func make(count: uniq/i32) -> [3 of i32]\n    count@follow += 1\n    return [1, 2, 3]\n" +
        "func makeArray(count: uniq/i32) -> Array<i32>\n    count@follow += 1\n    return [4, 5, 6]\n" +
        "var calls: i32 = 0\nlet last = ^1\n" +
        "require make(calls@uniq)[^1] == 3 and make(calls@uniq)[last] == 3 and calls == 2 else => $abort(\"fixed\")\n" +
        "require makeArray(calls@uniq)[^2] == 5 and calls == 3 else => $abort(\"array\")\n" +
        "let middle = make(calls@uniq)[1..^1].length\nrequire middle == 1 and calls == 4 else => $abort(\"range\")\nConsole.writeLine(\"ok\")";

    // SPEC 4.6.4, 4.6.9: saved positions and ranges apply to a receiver of any form, which is located once, and a key read
    // from the receiver itself keeps no borrow of it.
    private const string ReceiverForms =
        "func make() -> [4 of i32] => [1, 2, 3, 4]\nfunc makeArray() -> Array<i32> => [5, 6, 7, 8]\n" +
        "struct Holder\n    public var values: [4 of i32]\n    public init() => self.values = [1, 2, 3, 4]\n" +
        "let saved = 1..^1\nlet closed = 0..=2\nlet last = ^1\nlet whole = ..\nlet flag = true\nlet a: [4 of i32] = [1, 2, 3, 4]\nlet b: [4 of i32] = [5, 6, 7, 8]\nvar holder = Holder.init()\n" +
        "require make()[saved].length == 2 and make()[closed].length == 3 else => $abort(\"call\")\n" +
        "require makeArray()[saved][0] == 6 and makeArray()[last] == 8 else => $abort(\"array call\")\n" +
        "require (if flag => a else => b)[saved][0] == 2 and (if flag => a else => b)[last] == 4 else => $abort(\"selection\")\n" +
        "require holder.values[saved].length == 2 and holder.values[whole].length == 4 else => $abort(\"field\")\n" +
        "holder.values[last] = 9\nrequire holder.values[3] == 9 else => $abort(\"field write\")\n" +
        "var keys: [3 of i32] = [2, 0, 1]\nkeys[keys[0]] = 7\nrequire keys[2] == 7 else => $abort(\"own key\")\n" +
        "var items: Array<i32> = [1, 0, 5]\nlet removed = items.remove(items[0])\nrequire removed == 0 and items.length == 2 and items[1] == 5 else => $abort(\"own position\")\n" +
        "Console.writeLine(\"ok\")";

    // SPEC 4.6.6: the read operations of fixed arrays and Array mean the same operation on their whole-range Slice, for
    // positions of every form, valid or not.
    private const string ReadApis =
        "func value(o: Option<ref/i32 during a>) -> i32\n    match o\n        .Some(let x) => return x\n        .None => return -1\n" +
        "func size(o: Option<Slice<i32>>) -> isize\n    match o\n        .Some(let s)\n            let handle = s@follow\n            return handle.length\n        .None => return -1\n" +
        "func split(o: Option<(Slice<i32>, Slice<i32>)>) -> isize\n    match o\n        .Some(let parts) => return parts.0.length\n        .None => return -1\n" +
        "func position<P>(fixed: ref/[4 of i32], array: ref/Array<i32>, p: P) -> bool\n    P is Position\n" +
        "    let a = value(fixed.tryGet(p))\n    return a == value(fixed[..].tryGet(p)) and a == value(array.tryGet(p)) and a == value(array[..].tryGet(p)) and " +
        "split(fixed.trySplitAt(p)) == split(fixed[..].trySplitAt(p)) and split(array.trySplitAt(p)) == split(array[..].trySplitAt(p)) and split(fixed.trySplitAt(p)) == split(array.trySplitAt(p))\n" +
        "func range<R>(fixed: ref/[4 of i32], array: ref/Array<i32>, r: R) -> bool\n    R is PositionRange\n" +
        "    let a = size(fixed.trySlice(r))\n    return a == size(fixed[..].trySlice(r)) and a == size(array.trySlice(r)) and a == size(array[..].trySlice(r))\n" +
        "let fixed: [4 of i32] = [10, 20, 30, 40]\nlet array: Array<i32> = [10, 20, 30, 40]\n" +
        "require position(fixed, array, 0) and position(fixed, array, 3) and position(fixed, array, ^1) and position(fixed, array, ^0) else => $abort(\"positions\")\n" +
        "require position(fixed, array, 5) and position(fixed, array, -1) and position(fixed, array, 1@u8) and position(fixed, array, ^(2@i64)) and position(fixed, array, ^5) else => $abort(\"more positions\")\n" +
        "require range(fixed, array, 1..^1) and range(fixed, array, ..=3) and range(fixed, array, 2..) and range(fixed, array, 3..1) and range(fixed, array, ^5..) and range(fixed, array, 0..=4) else => $abort(\"ranges\")\n" +
        "require fixed.splitAt(^1).0.length == fixed[..].splitAt(^1).0.length and array.splitAt(1@u8).1.length == array[..].splitAt(1@u8).1.length else => $abort(\"split\")\n" +
        "require value(fixed.tryGet(^1)) == 40 and size(array.trySlice(1..^1)) == 2 else => $abort(\"values\")\nConsole.writeLine(\"ok\")";

    // SPEC 4.6.1, 15.1.3: after a Partial Move through a literal static path the remaining elements stay accessible by
    // literal paths, and positions select through chained arrays.
    private const string PartialMoveAndChains =
        "var names: [3 of string] = [\"a\", \"b\", \"c\"]\nlet taken = names[2]@move\nConsole.writeLine(taken)\nConsole.writeLine(names[1])\nnames[2] = \"d\"\nConsole.writeLine(names[2])\n" +
        "var matrix: [2 of [3 of i32]] = [[1, 2, 3], [4, 5, 6]]\nmatrix[^1][^1] = 9\nrequire matrix[1][2] == 9 and matrix[^2][..^1].length == 2 and matrix[1][1@u8..][0] == 5 else => $abort(\"chain\")\nConsole.writeLine(\"ok\")";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "ReceiverForms", ReceiverForms, "ok\n" },
        { "ReadApis", ReadApis, "ok\n" },
        { "PartialMoveAndChains", PartialMoveAndChains, "c\nb\nd\nok\n" },
        { "IndexKeys", IndexKeys, "Last text.\nFirst text.\nFirst text.\nok\n" },
        { "RangeKeys", RangeKeys, "ok\n" },
        { "NestedViews", NestedViews, "ok\n" },
        { "CallReceivers", CallReceivers, "ok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("KeyedIndexing" + name, source, stdout);

    [Theory]
    [InlineData("let values: [3 of i32] = [1, 2, 3]\nlet i = 1.5\nlet v = values[i]", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let values: [3 of i32] = [1, 2, 3]\nlet v = values[true]", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let values: [3 of i32] = [1, 2, 3]\nlet v = values[^1.5]", DiagnosticCode.TypeMismatch_Kd)]
    public void RejectsAtBinding(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }

    // SPEC 4.6.1, 4.6.6, 15.1.3: a position other than an integer literal is no static Move Path, like a runtime isize;
    // a dynamic position may select a moved element; and an Array read result keeps the receiver shared-borrowed.
    [Theory]
    [InlineData("var names: [3 of string] = [\"a\", \"b\", \"c\"]\nlet taken = names[^1]@move", DiagnosticCode.UnsupportedOwnership_Kd)]
    [InlineData("var names: [3 of string] = [\"a\", \"b\", \"c\"]\nlet i: isize = 2\nlet taken = names[i]@move", DiagnosticCode.UnsupportedOwnership_Kd)]
    [InlineData("var names: [3 of string] = [\"a\", \"b\", \"c\"]\nlet taken = names[0]@move\nConsole.writeLine(names[^1])", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("var array: Array<i32> = [1, 2]\nlet first = array.tryGet(0)\narray.append(3)\nlet again = first", DiagnosticCode.CallActivationConflict_Kd)]
    [InlineData("var array: Array<i32> = [1, 2]\nlet part = array.trySlice(..1)\narray.append(3)\nlet again = part", DiagnosticCode.CallActivationConflict_Kd)]
    public void RejectsAtOwnership(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Code == code);
    }
}
