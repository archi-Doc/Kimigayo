// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class OwnedPathBorrowTest
{
    private const string Pair = "struct Counter\n    public var value: i32 = 1\nstruct Pair\n    public var left: Counter = Counter.init()\n    public var right: Counter = Counter.init()\n";

    private const string Outer = "struct Counter\n    public var value: i32 = 1\nstruct Inner\n    public var c: Counter = Counter.init()\n    public var d: Counter = Counter.init()\nstruct Outer\n    public var tag: i32 = 7\n    public var inner: Inner = Inner.init()\n";

    private const string Calls = "struct Counter\n    public var value: i32 = 1\n    public func bump(self: uniq/Self)\n        self.value += 1\nstruct Pair\n    public var left: Counter = Counter.init()\n    public var right: Counter = Counter.init()\nfunc g(c?: uniq/Counter)\n    c.value += 1\nfunc h(a?: uniq/Counter, b?: uniq/Counter)\n    a.value += b.value\nfunc r(a?: ref/Counter, b?: ref/Counter) -> i32 => a.value + b.value\n";

    // SPEC 15.6: explicit borrows of inline parts of owned locals and parameters.
    [Theory]
    [InlineData("Exclusive", Pair + "var pair = Pair.init()\nlet a = pair.left@uniq\na.value += 1\nrequire pair.left.value == 2 and pair.right.value == 1 else => $abort(\"value\")")]
    [InlineData("Siblings", Pair + "var pair = Pair.init()\nlet a = pair.left@uniq\nlet b = pair.right@uniq\nb.value += 2\na.value += b.value\nrequire pair.left.value == 4 and pair.right.value == 3 else => $abort(\"value\")")]
    [InlineData("SiblingWrite", Pair + "var pair = Pair.init()\nlet a = pair.left@uniq\npair.right.value = 5\na.value += pair.right.value\nrequire pair.left.value == 6 else => $abort(\"value\")")]
    [InlineData("Shared", Pair + "let pair = Pair.init()\nlet a = pair.left@ref\nlet b = pair.left@ref\nrequire a.value + b.value + pair.left.value == 3 else => $abort(\"value\")")]
    [InlineData("Nested", Outer + "var o = Outer.init()\nlet a = o.inner.c@uniq\nlet b = o.inner.d@uniq\no.tag += 1\nb.value += 1\na.value += b.value + o.tag\nrequire o.inner.c.value == 11 and o.inner.d.value == 2 and o.tag == 8 else => $abort(\"value\")")]
    [InlineData("Tuple", Pair + "var t: (i32, (Counter, Counter)) = (5, (Counter.init(), Counter.init()))\nlet a = t.1.0@uniq\nt.0 += 1\na.value += t.0\nrequire t.1.0.value == 7 and t.1.1.value == 1 else => $abort(\"value\")")]
    [InlineData("Parameter", Pair + "func f(p?: Pair) -> i32\n    let a = p.left@ref\n    let b = p.right@ref\n    return a.value + b.value\nrequire f(Pair.init()) == 2 else => $abort(\"value\")")]
    [InlineData("Release", Pair + "var pair = Pair.init()\nlet a = pair.left@uniq\na.value += 1\npair.left.value += 1\nlet whole = pair@ref\nrequire whole.left.value == 3 else => $abort(\"value\")")]
    [InlineData("Arguments", Calls + "var pair = Pair.init()\ng(pair.left@uniq)\ng(pair.left)\nrequire pair.left.value == 3 else => $abort(\"value\")")]
    [InlineData("SiblingArguments", Calls + "var pair = Pair.init()\nh(pair.left, pair.right)\nrequire pair.left.value == 2 and pair.right.value == 1 else => $abort(\"value\")")]
    [InlineData("SharedArguments", Calls + "let pair = Pair.init()\nrequire r(pair.left, pair.left) == 2 else => $abort(\"value\")")]
    [InlineData("Receiver", Calls + "var pair = Pair.init()\npair.left.bump()\nrequire pair.left.value == 2 else => $abort(\"value\")")]
    [InlineData("SiblingCall", Calls + "var pair = Pair.init()\nlet a = pair.left@uniq\ng(pair.right)\na.value += pair.right.value\nrequire pair.left.value == 3 else => $abort(\"value\")")]
    public void ExecutesOwnedPathBorrows(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Ownership.Result.IsVerified, string.Join('\n', c.Ownership.Issues));
        ScalarEmissionTest.EmitFixture("OwnedPathBorrow" + name, source, string.Empty);
    }

    [Theory]
    [InlineData(Pair + "var pair = Pair.init()\nlet a = pair@uniq\nlet x = pair.left.value\na.left.value += x")]
    [InlineData(Pair + "var pair = Pair.init()\nlet a = pair.left@uniq\nlet x = pair.left.value\na.value += x")]
    [InlineData(Pair + "var pair = Pair.init()\nlet a = pair.left@uniq\nlet b = pair.left@uniq\na.value += b.value")]
    [InlineData(Pair + "var pair = Pair.init()\nlet a = pair.left@uniq\nlet b = pair@ref\na.value += b.right.value")]
    [InlineData(Pair + "var pair = Pair.init()\nlet a = pair.left@ref\npair = Pair.init()\nlet x = a.value")]
    [InlineData(Pair + "var pair = Pair.init()\nlet a = pair.left@ref\npair.left.value = 3\nlet x = a.value")]
    [InlineData(Pair + "var pair = Pair.init()\nlet a = pair.left@ref\nlet m = pair.left\nlet x = a.value")]
    [InlineData(Outer + "var o = Outer.init()\nlet a = o.inner.c@uniq\nlet b = o.inner@ref\na.value += b.d.value")]
    [InlineData(Outer + "var o = Outer.init()\nlet a = o.inner@ref\no.inner.d.value += 1\nlet x = a.c.value")]
    [InlineData(Calls + "var pair = Pair.init()\nh(pair.left, pair.left)")]
    [InlineData(Calls + "var pair = Pair.init()\nlet a = pair.left@uniq\ng(pair.left)\na.value += 1")]
    public void RejectsOverlappingOwnedPathBorrows(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == Kimi.Compiler.OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void RejectsExclusiveBorrowOfImmutableOwner()
    {
        var c = MinimalEmissionTest.Analyze(Pair + "let pair = Pair.init()\nlet a = pair.left@uniq\na.value += 1");
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
