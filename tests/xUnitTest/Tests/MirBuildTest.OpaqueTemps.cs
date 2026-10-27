// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>An Opaque temp of the MIR build is no storage (PLAN G247; DECISIONS R4-M149): no Ref, pair-case acquisition or case
/// adaptation lends a Place in a temp the builder holds an Opaque value in.</summary>
public sealed partial class MirBuildTest
{
    // SPEC 23.1.2, 3.6, 13.5.6 (PLAN G247; DECISIONS R4-M149, amending R4-M109 (a) and L1): a lending rvalue over a Place rooted at an
    // Opaque temp (the derived value of `w`, `h` or `b`, each use of which fails its Type's constraint: S2 is neither a Dictionary key nor
    // Equatable) is its consumer's derived value, an Opaque whose site names the temp. The forms: a Borrow of a Field chain, in
    // parentheses, of the rejected name itself, an address; an upcast's view; a runtime index's guard and its element's Borrow, also with
    // a write; a Slice's backing and first element; `.capacity`; a runtime Type test; an update's Writeback, also a getter-only Property's
    // standard side (whose getter takes its own derived value of the receiver located in the temp, DECISIONS R4-M109); a method's receiver;
    // a Subject; a pair-case acquisition; a case adaptation (fail-before: each a Ref, PairAcquire or Adapt of a Place in the temp). The
    // valid twin (an i32 key) lends the local each time and holds no Opaque value or construction record (R5's guard that a complete
    // compilation never reaches Unlent); a literal's Borrow, valid in both, lends its materialized temporary (implementation review 1's
    // B1). Each row: f's lending rvalues and derived values in order, a lent root by name, `opaque` for a temp an Opaque is assigned to,
    // else `temp`; `derived` for an Opaque naming one such temp.
    [Theory]
    [InlineData("let p = w.v@ref", "derived", "Shared w")]
    [InlineData("let p = w.inner.n@uniq", "derived", "Uniq w")]
    [InlineData("let p = (w.v)@ref", "derived", "Shared w")]
    [InlineData("let p = h@ref", "derived", "Shared h")]
    [InlineData("let p = w.v@raw", "derived", "Raw w")]
    [InlineData("let view = w.sq@objref/Shape", "derived", "Shared w")]
    [InlineData("let i = 1\n    let p = w.arr[i]@ref", "derived | derived", "Shared w | Shared w")]
    [InlineData("let p = w.xs[g()]@ref", "derived | derived", "Shared w | Shared w")]
    [InlineData("let i = 1\n    w.arr[i] = 3", "derived", "Shared w")]
    [InlineData("let s = w.xs[0..1]", "derived | derived", "Shared w | Raw w")]
    [InlineData("let n = w.xs.capacity", "derived", "Shared w")]
    [InlineData("let k = w.sq is Square", "derived", "Shared w")]
    [InlineData("w.v += 1", "derived", "TwoPhaseUniq w")]
    [InlineData("w.q += 1", "derived | derived", "TwoPhaseUniq w | Shared w")]
    [InlineData("w.inner.mutate()", "derived", "TwoPhaseUniq w")]
    [InlineData("let r = w.inner.pick()", "derived", "Shared w")]
    [InlineData("let r: ref/i32 = match w.inner\n        let x => x.pick()", "derived", "Shared w")]
    [InlineData("b.item", "derived", "acquire b")]
    [InlineData("let x = b.item", "derived", "acquire b")]
    [InlineData("let x = keepS(b.item)", "derived", "acquire b")]
    [InlineData("let r = w.v@s", "derived", "adapt w")]
    [InlineData("let p = 1@ref", "Shared temp", "Shared temp")]
    [InlineData("let p = 5@uniq", "Uniq temp", "Uniq temp")]
    [InlineData("let p = (1)@ref", "Shared temp", "Shared temp")]
    public void AnOpaqueTempLendsNothing(string statement, string failed, string valid)
    {
        foreach (var (key, expected) in new[] { ("S2", failed), ("i32", valid) })
        {
            var c = Compile(RejectedRoots(key) + "    " + statement + "\npublic func main() => ()\n");
            Assert.Equal(key == "S2", c.Binding.Issues.Any());
            var program = c.BuildMir();
            Assert.Empty(program.Findings);
            var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
            Assert.Equal(expected.Split(" | "), Lending(c, body, RejectedRoots(key).Length));
            if (key == "i32")
            {
                // A complete compilation holds no Opaque value and no construction record, so none of its rvalues reaches Unlent.
                Assert.DoesNotContain(program.Bodies.ToArray(), static x => x.HasOpaque);
                Assert.Empty(program.Records);
            }
        }
    }

    // A function `f<s/T>(value: s/T)` whose locals `w` (a struct), `h` (a Dictionary) and `b` (a struct of a pair-typed item) take a key
    // Type: S2, neither a Dictionary key nor Equatable, so that each use of them fails its Type's constraint, or i32, the valid twin; `t`
    // is a valid string the independent-error rows move. Its statements follow (AnOpaqueTempLendsNothing, MirBorrowsTest's
    // AnOpaqueTempKeepsTheIndependentRecords).
    internal static string RejectedRoots(string key) => $$"""
        open struct Shape
            public var size: i32
            protected init(size: i32) => self.size = size
        struct Square : Shape
            public init(size: i32) : base(size) => ()
        struct S2
            public var v: i32 = 0
            public init() => ()
        struct In
            public var n: i32 = 0
            public init() => ()
            public func mutate(self: uniq/Self) => ()
            public func pick(self: ref/Self) -> ref/i32 during self => self.n@ref
            public func put(self: uniq/Self, k: i32) => ()
        struct W<K>
            K is Equatable
            public var v: i32 = 0
            public var inner: In = In.init()
            public var arr: [2 of i32] = [0, 0]
            public var xs: Array<i32> = []
            public var d: Dictionary<i32, i32> = [:]
            public var sq: obj/Square = Kimi.Intrinsics.makeObj<Square>(Square.init(3))
            public var q: i32 = 0
                get() -> i32 => storage
            public init() => ()
        struct B<K, T>
            K is Equatable
            public var item: T
            public init(item: T) => self.item = item
        func see(p: ref/i32) => ()
        func hold(value: ref/i32) -> ref/i32 during value => value
        func keepS<s/T>(x: s/T) -> s/T => x
        func g() -> i32 => 0
        func take(x: string) -> i32 => 5
        func use(x: string) => ()
        func f<s/T>(value: s/T)
            s is owner or uniq
            T is Copy
            var w: W<{{key}}> = W<{{key}}>.init()
            var h: Dictionary<{{key}}, i32> = [:]
            var b: B<{{key}}, s/T> = B<{{key}}, s/T>.init(item: value)
            var t = "t"

        """;

    // A body's lending rvalues (a Ref, a pair-case acquisition, a case adaptation) and derived values in order, of the statements at or
    // after a source offset: `{kind} {root}` for a lending one, its root by name, `opaque` for a temp an Opaque is assigned to, else
    // `temp`; `derived` for an Opaque whose site names only such a temp.
    private static string[] Lending(Compilation c, MirBody body, int start)
    {
        var statements = body.Statements.ToArray();
        var opaque = statements.Where(x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Opaque } && body.Locals[x.Place.Root].Name is null)
            .Select(static x => x.Place.Root).ToHashSet();
        string Root(int root) => root < 0 ? "static" : body.Locals[root].Name ?? (opaque.Contains(root) ? "opaque" : "temp");
        return [.. statements.Where(x => x.Kind == MirStatementKind.Assign && c.SyntaxOf(x.Source.Syntax).Span.Start >= start).Select(x => x.Rvalue switch
        {
            { Kind: MirRvalueKind.Ref } borrow => $"{(MirRefKind)borrow.Op} {Root(borrow.Place.Root)}",
            { Kind: MirRvalueKind.Adapt } adapt => $"adapt {Root(adapt.Place.Root)}",
            { Kind: MirRvalueKind.Use } use when body.Operands[use.Start] is { Kind: MirOperandKind.PairAcquire } operand =>
                $"acquire {Root(operand.Place.Root)}",
            { Kind: MirRvalueKind.Opaque } derived when body.OpaqueSites[derived.Arg] is { Count: 1 } site &&
                Root(body.OpaqueRoots[site.Start]) == "opaque" => "derived",
            _ => null,
        }).OfType<string>()];
    }
}
