// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 7.6.2: an environment holds bindings of any Type with slot storage: enums such as Option and Result, Arrays,
// Dictionaries, Slices and raw pointers, captured by Copy, Move or a slot borrow under the ordinary acquisition rules.
public class CollectionCaptureTest
{
    [Theory]
    [InlineData("ArrayView", "let a: Array<i32> = [1, 2, 3]\nlet view = a@ref\nlet f = func () => view.length\nrequire f() == 3 else => $abort(\"f\")")]
    [InlineData("ArrayBorrow", "let a: Array<i32> = [1, 2, 3]\nlet g = func [a@ref] () => a.length\nrequire g() == 3 and g() == 3 else => $abort(\"g\")")]
    [InlineData("ArrayMove", "let a: Array<i32> = [1, 2, 3]\nlet h = func [a@move] () => a.length\nrequire h() == 3 else => $abort(\"h\")")]
    [InlineData("ArrayUpdate", "let a: Array<i32> = [1]\nvar grow = func [var a@move] () -> i32\n    a.append(2)\n    return 1\nrequire grow() + grow() == 2 else => $abort(\"grow\")")]
    [InlineData("Dictionary", "let m: Dictionary<i32, i32> = [1: 5]\nlet f = func [m@move] () => m[1]\nrequire f() == 5 else => $abort(\"m\")")]
    [InlineData("Slice", "var a: Array<i32> = [1, 2, 3]\nlet s = a[1..]\nlet k = func [s] () => s.length\nrequire k() == 2 else => $abort(\"s\")")]
    [InlineData("OptionCopy", "let o: Option<i32> = .Some(7)\nlet g = func () -> i32\n    match o\n        .Some(let n) => return n\n        .None => return 0\nrequire g() == 7 else => $abort(\"o\")")]
    [InlineData("ResultMove", "let r: Result<i32, string> = .Ok(1)\nlet f = func [r@move] () -> i32\n    match r\n        .Ok(let n) => return n\n        .Err(let e) => return 0\nrequire f() == 1 else => $abort(\"r\")")]
    [InlineData("RawPointer", "var x: i32 = 1\nlet p = x@raw\nlet f = func () => p == null\nrequire f() == false else => $abort(\"p\")")]
    [InlineData("GenericOption", "func count<T>(value: Option<T>) -> i32\n    let f = func [value@move] () -> i32\n        match value\n            .Some(let v) => return 1\n            .None => return 0\n    return f()\nrequire count(Option<string>.Some(\"x\")) == 1 else => $abort(\"generic\")")]
    public void CollectionsAndEnumsAreCaptured(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("CollectionCapture" + name, source, string.Empty);
    }

    [Fact]
    public void ABorrowedArrayCannotGrowWhileTheClosureLives()
    {
        var c = MinimalEmissionTest.Analyze("var a: Array<i32> = [1, 2, 3]\nlet g = func [a@ref] () => a.length\na.append(4)\nrequire g() == 4 else => $abort(\"g\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
    }

    [Fact]
    public void AnOmittedListCapturesANonCopyEnumOnlyByCopy()
    {
        var c = MinimalEmissionTest.Analyze("enum Mode\n    Fast(i32)\n    Slow\nlet m: Mode = .Fast(3)\nlet f = func () -> i32\n    match m\n        .Fast(let n) => return n\n        .Slow => return 0\nlet r = f()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.TransferRequired);
    }
}
