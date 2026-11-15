// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public partial class TypeDagScalingTest
{
    // R7-C3a (design review 1, B2): the identity classes are linear in the premises' distinct parts. A Tuple tree of depth d (d premises,
    // 2^d paths) and a chain of n identities cost nodes proportional to d and n; a cycle of n steps is found with no depth bound and
    // recorded once at the declaration (SPEC 8.2.3 Cycles).
    [Theory]
    [InlineData("tree")]
    [InlineData("chain")]
    public void IdentityClassesAreLinearInThePremises(string shape)
    {
        var (small, large) = (Work(shape, 16), Work(shape, 32));
        Assert.True(large <= 2 * small, $"{shape}: {small} at 16, {large} at 32");

        static int Work(string shape, int n)
        {
            var c = MinimalEmissionTest.Analyze(IdentitySource(shape, n));
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
            return c.Binding.IdentityClassWork;
        }
    }

    [Fact]
    public void AnIdentityCycleIsFoundWithoutADepthBound()
        => Assert.Equal("1*InvalidConstraint_Kd", DiagnosticCorpus.RecordProfile(IdentitySource("cycle", 70)));

    // R7-C3a (the provisional path's bound, Binding.TypeIdentity's contract): a table read under a Provisional availability (a Contract's
    // supplied identity) is rebuilt at each read, so its work is the reads times the view's premise nodes: linear in the uses, and in
    // the premises their own clauses' reads times their nodes.
    [Theory]
    [InlineData("uses", 2)]
    [InlineData("premises", 4)]
    public void ProvisionalIdentityWorkIsReadsTimesPremises(string grown, int factor)
    {
        var (small, large) = (Work(8, 8), grown == "uses" ? Work(16, 8) : Work(8, 16));
        Assert.True(large <= factor * small, $"{grown}: {small} at 8, {large} at 16");

        static int Work(int uses, int premises)
        {
            var parameters = string.Concat(Enumerable.Range(0, premises).Select(static i => $", U{i}"));
            var source = "contract K1\n    associate A\n    associate C\n    Self.(K1).A is Option<Self.(K1).C>\n" +
                $"func f<T{parameters}>(c: T.(K1).C) -> i32\n    T is K1\n    T.(K1).C is i32\n" +
                string.Concat(Enumerable.Range(0, premises).Select(static i => $"    U{i} is i32\n")) +
                string.Concat(Enumerable.Range(0, uses).Select(static i => $"    let x{i}: T.(K1).C = c\n")) + "    return 0\n";
            var c = MinimalEmissionTest.Analyze(source + "public func main() -> ()\n    return\n");
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
            return c.Binding.IdentityClassWork;
        }
    }

    // R7-C2c2 c2-1 (design review 2, D): the premise supply's class keys cost one key per distinct Type a table compares and one scan per
    // class, n = 8, 32, 128. Linear in a chain of n parameter identities (one class of n + 1 members) and in the same class with a
    // Contract fact on each member (each member keyed, the class scanned once: 214, 286, 574; 342, 1,398, 17,142 while Representative
    // scanned per Type). A demand n projections deep under `U is T` is quadratic, the bound asserted (276, 912, 9,216): each prefix's fill moves the view's
    // serial, and the table and the view's entry are rebuilt at each serial, re-keying every fact the request indexes; R7-C2c3's fills
    // remove that term, and R7-C2e2 e2-1's kept entry does not: the same 276, 912 and 9,216 with it
    // (cubic while ClassKey kept no key: 11,285 at 8, 11,849,545 at 128). The programs are valid (SPEC 8.2.3, 8.3.2): CopyTree supplies Copy.
    [Theory]
    [InlineData("parameters", 4)]
    [InlineData("members", 4)]
    [InlineData("depth", 16)]
    public void ClassKeyWorkIsAKeyPerComparedType(string shape, int factor)
    {
        var (w8, w32, w128) = (Work(shape, 8), Work(shape, 32), Work(shape, 128));
        Assert.True(w32 <= factor * w8 && w128 <= factor * w32, $"{shape}: {w8} at 8, {w32} at 32, {w128} at 128");

        static long Work(string shape, int n)
        {
            var c = MinimalEmissionTest.Analyze(KeySource(shape, n));
            Assert.True(CompilationTestHelper.PublishedErrors(c).Length == 0, MinimalEmissionTest.Describe(c));
            return c.Binding.IdentityKeyWork;
        }
    }

    // R7-C2e2-2 (implementation review N3): a closure validator's read forms its view's entry over the scope's whole chain, once per
    // read. The agreement (SPEC 8.9.2) reads each scope with a projection identity premise: a struct with m Contract premises and n
    // methods, each with `T.(C).E is i32`, forms n + 1 entries of about m pairs, linear in n and in m (173, 333, 653 at n = 8, 16, 32;
    // 173, 309, 581 at m = 8, 16, 32). Disjointness (SPEC 8.9.1) reads b's view once per pair of 4 paths whose conditions name
    // distinct Types, over m identity premises: linear in m (94, 174, 334); an entry formed per fact of a's chain is quadratic in m.
    // The pairs over the library's; the programs are valid (SPEC 8.2.3, 8.9.1).
    [Theory]
    [InlineData("methods")]
    [InlineData("premises")]
    [InlineData("paths")]
    public void AClosureValidatorsReadIsLinearInItsChain(string grown)
    {
        var (w8, w16, w32) = (Work(grown, 8), Work(grown, 16), Work(grown, 32));
        Assert.True(w16 <= 2 * w8 && w32 <= 2 * w16, $"{grown}: {w8} at 8, {w16} at 16, {w32} at 32");

        static int Work(string grown, int n)
        {
            var (methods, premises) = grown == "methods" ? (n, 8) : (8, n);
            var parameters = string.Concat(Enumerable.Range(0, premises).Select(static i => $", A{i}"));
            var chain = string.Concat(Enumerable.Range(0, premises).Select(i => $"    A{i} is {(grown == "paths" ? "i32" : "C")}\n"));
            var body = grown == "paths" ?
                string.Concat(new[] { "i32", "string", "bool", "u8" }.Select(static (x, i) => $"    Self is L{i} when T is {x}\n")) +
                "    public func read(self: ref/Self, value: ref/i32) -> i32 => 1\n" :
                "    T is C\n" + string.Concat(Enumerable.Range(0, methods).Select(static i => $"    public func f{i}(self: ref/Self) -> ()\n        T.(C).E is i32\n        ()\n"));
            return Closure("contract C\n    associate E\ncontract P<X>\n    func read(self: ref/Self, value: ref/X) -> i32\n" +
                string.Concat(Enumerable.Range(0, 4).Select(static i => $"contract L{i}: P<i32>\n")) +
                $"struct W<T{parameters}>\n{chain}    var v: T\n{body}") - Closure(string.Empty);
        }

        static int Closure(string source)
        {
            var c = MinimalEmissionTest.Analyze(source + "public func main() -> ()\n    return\n");
            Assert.True(CompilationTestHelper.PublishedErrors(c).Length == 0, MinimalEmissionTest.Describe(c));
            return c.Binding.ClosureIndexWork;
        }
    }

    // R7-C2c2 c2-2 (design review N1): an extension round per identity row a walk records, the key changing between rounds. M3's clause
    // `Self.(Tree).Child.(Tree).Alias is Self.(Tree).Child`, supplied at every `T.(Tree).Child...` link, joins each `.(Tree).Alias` of the
    // demand `T` + n times `.(Tree).Child.(Tree).Alias` to a shorter chain, so each round keys the demand to a new Type and walks its
    // links: at most one round per `.(Tree).Child.(Tree).Alias` of the demand (measured n / 2: 2, 4, 8). Valid (SPEC 8.2.3 grounds, 8.3.2): the
    // demand is `T` + n times `.(Tree).Child`, which is M3, so Copy.
    [Fact]
    public void ExtensionRoundsAreLinearInTheKeyChanges()
    {
        var (w4, w8, w16) = (Rounds(4), Rounds(8), Rounds(16));
        Assert.True(w4 <= 4 && w8 <= 8 && w16 <= 16 && w16 <= 4 * w4, $"{w4} at 4, {w8} at 8, {w16} at 16");

        static long Rounds(int n)
        {
            var demand = "T" + string.Concat(Enumerable.Repeat(".(Tree).Child.(Tree).Alias", n));
            var c = MinimalEmissionTest.Analyze("contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract M3: Tree, Copy\n" +
                "    Self.(Tree).Child is M3\n    Self.(Tree).Child.(Tree).Alias is Self.(Tree).Child\n" +
                $"func twice<T>(value: {demand}) -> ({demand}, {demand})\n    T is M3\n    return (value, value)\npublic func main() -> ()\n    return\n");
            Assert.True(CompilationTestHelper.PublishedErrors(c).Length == 0, MinimalEmissionTest.Describe(c));
            return c.Binding.IdentityRounds;
        }
    }

    // PLAN G881 with G881b (the owner's O-321 (b); SPEC 8.3.3 Limits, 23.1.1): the extension rounds of one request end at
    // Binding.PremiseClosureLimit. Pq's path equation `Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up` (allowed) supplies one deeper
    // identity a round through row (5)'s mate, so with Inner supplied at `T` + 2k times `.(Pq).Next` + `.(Pq).Up` the request for
    // `T.(Pq).Up is Inner` is proven after k - 1 rounds (valid: SPEC 8.3.2, 8.2.3; the call is judged twice, so 6, 14, 30 rounds at
    // k = 4, 8, 16) and its mate walks grow with the cube of k (rounds x mates x a round's walks; asserted within x16 per x2). A
    // request is proven through k = limit + 1. One step deeper (valid), or with nothing supplying Inner (invalid), it is refused
    // after limit rounds: the limit's one Resource record at the use, naming the limit, and no Proof or Language record.
    [Fact]
    public void ExtensionRoundsEndAtTheRoundLimit()
    {
        const int Limit = Kimi.Compiler.Binding.PremiseClosureLimit;
        var (p4, p8, p16, edge) = (Proven(4), Proven(8), Proven(16), Proven(Limit + 1));
        Assert.True(p4.Rounds == 6 && p8.Rounds == 14 && p16.Rounds == 30 && edge.Rounds == 2 * Limit, $"rounds {p4.Rounds}, {p8.Rounds}, {p16.Rounds}; {edge.Rounds} at the edge");
        Assert.True(p8.Work <= 16 * p4.Work && p16.Work <= 16 * p8.Work, $"mate work {p4.Work}, {p8.Work}, {p16.Work} at 4, 8, 16; {edge.Work} at the edge");
        Refused(Source(Limit + 2));
        Refused(Source(0));

        static (long Rounds, long Work) Proven(int k)
        {
            var c = MinimalEmissionTest.Analyze(Source(k));
            Assert.True(CompilationTestHelper.PublishedErrors(c).Length == 0, MinimalEmissionTest.Describe(c));
            return (c.Binding.IdentityRounds, c.Binding.IdentityMateWork);
        }

        static void Refused(string source)
        {
            var c = MinimalEmissionTest.Analyze(source);
            var record = Assert.Single(CompilationTestHelper.PublishedErrors(c));
            Assert.Equal(nameof(Kimi.DiagnosticCode.PremiseClosureLimit_Kd), record.Code);
            Assert.Contains(Limit.ToString(System.Globalization.CultureInfo.InvariantCulture), record.Message, StringComparison.Ordinal);
            Assert.Equal(source.IndexOf("need(x)", StringComparison.Ordinal), record.Span.Start);
            Assert.True(Kimi.DiagnosticEntries.TryGet(Kimi.DiagnosticCode.PremiseClosureLimit_Kd, out var entry) && entry.Category == Kimi.Diagnostics.DiagnosticCategory.Resource);
            Assert.Equal(Limit, c.Binding.IdentityRounds);
        }

        // Inner supplied at depth 2k of Pq's chain; k = 0: nowhere.
        static string Source(int k)
            => "contract Inner\ncontract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\n" +
                "func need<X>(x: ref/X)\n    X is Inner\n    return\nfunc f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n" +
                (k == 0 ? string.Empty : $"    T{string.Concat(Enumerable.Repeat(".(Pq).Next", 2 * k))}.(Pq).Up is Inner\n") +
                "    need(x)\npublic func main() -> ()\n    return\n";
    }

    // PLAN G881 (design review B3): ContractSolver's row (5) work (IdentityMateWork: a class's members scanned once per table, mates
    // walked) over c2-1's `members` shape (one class of n + 1 parameters, no projection mate), n projection mates of one parameter
    // (`Ti.(Outer3).Next.(Mid).Next is U`) and n chained two-link identities (`V{i-1}.(R).A.(R2).B is Vi`, each link's rows from its
    // mate, whose root's from its own: the walk nests n deep). Valid (SPEC 8.2.3, 8.3.2): U is Inner, Vn is R. A parameter-only class
    // costs one scan per table (linear). With projection mates the measured term is quadratic (mates 158, 1,154, 16,658; chained 126,
    // 642, 8,466 at 4, 16, 64): each of the O(n) requests through the class walks its n mates, since before the capability phase a
    // request keeps no rows; R7-C2e2's Closure(scope), which holds a view's rows across requests, removes the requests factor.
    [Theory]
    [InlineData("members", 4)]
    [InlineData("mates", 16)]
    [InlineData("chained", 16)]
    public void MateWorkIsLinearInTheClassAndQuadraticInMatesAndChains(string shape, int factor)
    {
        var (w4, w16, w64) = (Work(shape, 4), Work(shape, 16), Work(shape, 64));
        Assert.True(w16 <= factor * w4 && w64 <= factor * w16, $"{shape}: {w4} at 4, {w16} at 16, {w64} at 64");

        static long Work(string shape, int n)
        {
            var c = MinimalEmissionTest.Analyze(MateSource(shape, n));
            Assert.True(CompilationTestHelper.PublishedErrors(c).Length == 0, MinimalEmissionTest.Describe(c));
            return c.Binding.IdentityMateWork;
        }
    }

    // PLAN G881b: the rounds of ContractSolver's fixpoint (EnumerateRounds) and its mate walks over a ladder of n cycles through one
    // parameter, each needing the last one's row (`T is L0`, `T.(L{i-1}).X.(M{i}).Next is T`: T is Li only once it is L{i-1}), so a
    // request for T walks about n rounds. Valid (SPEC 8.2.3 Cycles and grounds, 8.3.2): T is Ln. The rounds are linear in n (measured
    // 12, 20, 36 at 2, 4, 8); the mate walks are rounds x mates x a round's walks, asserted at x8 per x2 (measured 102, 234, 1,218).
    // The library and a chain of identities walk no second round; projection mates of one class defer each other (each is open
    // while it walks the others), a constant two rounds whatever their number (measured 2 at 4 and at 16 mates: the first
    // request's rows are kept).
    [Fact]
    public void CycleRoundsAreBoundedByTheRowsThroughTheCycle()
    {
        var (r2, r4, r8) = (Rounds(LadderSource(2)), Rounds(LadderSource(4)), Rounds(LadderSource(8)));
        Assert.True(r4.Rounds <= 2 * r2.Rounds && r8.Rounds <= 2 * r4.Rounds && r4.Work <= 8 * r2.Work && r8.Work <= 8 * r4.Work, $"rounds {r2.Rounds}, {r4.Rounds}, {r8.Rounds}; mate work {r2.Work}, {r4.Work}, {r8.Work} at 2, 4, 8");
        var (m4, m16, c16, e) = (Rounds(MateSource("mates", 4)).Rounds, Rounds(MateSource("mates", 16)).Rounds, Rounds(MateSource("chained", 16)).Rounds, Rounds(string.Empty).Rounds);
        Assert.True(e == 0 && c16 == 0 && m16 == m4, $"mates {m4}, {m16}; chained {c16}; empty {e}");

        static (long Rounds, long Work) Rounds(string source)
        {
            var c = MinimalEmissionTest.Analyze(source.Length == 0 ? "public func main() -> ()\n    return\n" : source);
            Assert.True(CompilationTestHelper.PublishedErrors(c).Length == 0, MinimalEmissionTest.Describe(c));
            return (c.Binding.EnumerateRounds, c.Binding.IdentityMateWork);
        }

        static string LadderSource(int n)
            => string.Concat(Enumerable.Range(0, n).Select(static i => $"contract L{i}\n    associate X is M{i + 1}\ncontract M{i + 1}\n    associate Next is L{i + 1}\n")) +
                $"contract L{n}\nfunc needL<X>(x: ref/X)\n    X is L{n}\n    return\nfunc f<T>(t: ref/T)\n    T is L0\n" +
                string.Concat(Enumerable.Range(0, n).Select(static i => $"    T.(L{i}).X.(M{i + 1}).Next is T\n")) +
                "    needL(t)\npublic func main() -> ()\n    return\n";
    }

    // R7-C2c2 c2-4a (design RB3; rulings 151, 156): the identity table's work along a chain of binding grounds is linear in the chain:
    // `T is B<U1>`, ..., `Un is L`, `V is T.(C).X` grounds `T.(C).X` through n uses of B's binding, one ground per projection node.
    // Measured at n = 4 and 8: the base's walk below it (the nested well-formedness the descent reaches, its own unit) is exponential
    // past about 16, so the row stays where the base allows; the closure limit (PremiseClosureLimit) waits for that fix.
    [Fact]
    public void AGroundChainsTableWorkIsLinearInTheChain()
    {
        var (small, large) = (Work(4), Work(8));
        Assert.True(large <= 2 * small, $"{small} at 4, {large} at 8");

        static int Work(int n)
        {
            var c = MinimalEmissionTest.Analyze(GroundChainSource(n));
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
            return c.Binding.IdentityClassWork;
        }
    }

    private static string MateSource(string shape, int n)
    {
        if (shape == "members")
        {
            return KeySource(shape, n);
        }

        if (shape == "mates")
        {
            var roots = string.Join(", ", Enumerable.Range(0, n).Select(static i => $"T{i}"));
            var premises = string.Concat(Enumerable.Range(0, n).Select(static i => $"    T{i} is Outer3\n    T{i}.(Outer3).Next.(Mid).Next is U\n"));
            return "contract Inner\n    associate F\ncontract Mid\n    associate Next is Inner\ncontract Outer3\n    associate Next is Mid\n" +
                "func need<X>(x: ref/X)\n    X is Inner\n    return\n" +
                $"func f<{roots}, U>(u: ref/U)\n{premises}    need(u)\npublic func main() -> ()\n    return\n";
        }

        var parameters = string.Join(", ", Enumerable.Range(0, n + 1).Select(static i => $"V{i}"));
        var chain = string.Concat(Enumerable.Range(1, n).Select(static i => $"    V{i - 1}.(R).A.(R2).B is V{i}\n"));
        return "contract R\n    associate A is R2\ncontract R2\n    associate B is R\nfunc needR<X>(x: ref/X)\n    X is R\n    return\n" +
            $"func f<{parameters}>(v: ref/V{n})\n    V0 is R\n{chain}    needR(v)\npublic func main() -> ()\n    return\n";
    }

    private static string KeySource(string shape, int n)
    {
        const string Copies = "contract Tree\n    associate Child is Tree\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\n";
        if (shape is "parameters" or "members")
        {
            var parameters = string.Join(", ", Enumerable.Range(0, n + 1).Select(static i => $"T{i}"));
            var chain = string.Concat(Enumerable.Range(1, n).Select(static i => $"    T{i} is T{i - 1}\n"));
            var facts = shape == "members" ? string.Concat(Enumerable.Range(1, n).Select(static i => $"    T{i} is Tree\n")) : string.Empty;
            return Copies + $"func twice<{parameters}>(value: T{n}.(Tree).Child) -> (T{n}.(Tree).Child, T{n}.(Tree).Child)\n" + chain + facts +
                "    T0 is CopyTree\n    return (value, value)\npublic func main() -> ()\n    return\n";
        }

        var demand = "U" + string.Concat(Enumerable.Repeat(".(Tree).Child", n));
        return Copies + $"func twice<T, U>(value: {demand}) -> ({demand}, {demand})\n    U is T\n    T is CopyTree\n    return (value, value)\n" +
            "public func main() -> ()\n    return\n";
    }

    private static string IdentitySource(string shape, int n)
    {
        var parameters = string.Join(", ", Enumerable.Range(0, n + 1).Select(static i => $"T{i}"));
        var premises = shape switch
        {
            "tree" => Enumerable.Range(0, n).Select(static i => $"    T{i} is (T{i + 1}, T{i + 1})\n"),
            "chain" => Enumerable.Range(0, n).Select(static i => $"    T{i} is T{i + 1}\n").Append($"    T{n} is i32\n"),
            _ => Enumerable.Range(0, n).Select(static i => $"    T{i} is Option<T{i + 1}>\n").Append($"    T{n} is Option<T0>\n"),
        };

        return $"func f<{parameters}>(v: T0) -> i32\n" + string.Concat(premises) + "    return 0\npublic func main() -> ()\n    return\n";
    }

    private static string GroundChainSource(int n)
    {
        var parameters = string.Join(", ", Enumerable.Range(1, n).Select(static i => $"U{i}"));
        var links = string.Concat(Enumerable.Range(1, n - 1).Select(static i => $"    U{i} is B<U{i + 1}>\n"));
        return "contract C\n    associate X\nstruct L\n    Self is C\n    associate C.X is i32\n    public var v: i32\n" +
            "struct B<A>\n    A is C\n    Self is C\n    associate C.X is A.(C).X\n    public var a: A\n" +
            $"func f<T, {parameters}, V>(x: ref/V) -> i32\n    T is B<U1>\n{links}    U{n} is L\n    V is T.(C).X\n    return 0\n" +
            "public func main() -> ()\n    return\n";
    }
}
