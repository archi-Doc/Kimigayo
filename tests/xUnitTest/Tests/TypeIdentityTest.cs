// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 8.2, 8.8.1, 8.3: an available Type-identity premise `X is U` makes X and U one Type in its scope, a
/// function body or a conditional conformance's block, by substitution.</summary>
public partial class TypeIdentityTest
{
    private const string Pair =
        "contract Pick\n    func pick(self: ref/Self) -> bool\n" +
        "struct Pair<S, E>\n    S is PrimitiveInteger\n    E is PrimitiveInteger\n    let a: S\n    let b: E\n" +
        "    public init(a: S, b: E)\n        self.a = a\n        self.b = b\n" +
        "    Self is Pick when E is S\n        public func pick(self: ref/Self) -> bool\n            return self.a < self.b\n";

    private const string Uses =
        "func same<A, B>(a: A, b: B) -> A\n    B is A\n    return b@move\n" +
        "func check<P>(p: ref/P) -> bool\n    P is Pick\n    return p.pick()\n" +
        "let p = Pair<i32, i32>.init(1, 2)\n" +
        "require p.pick() and check(p@ref) and same(3, 4) == 4 else => $abort(\"identity\")\nConsole.writeLine(\"ok\")";

    private const string Getter = "contract Getter\n    func get(self: ref/Self) -> i32\n";

    [Theory]
    [InlineData("func same<A, B>(a: A, b: B) -> A\n    B is A\n    return b@move")]
    [InlineData("func same<A, B>(a: A, b: B) -> A\n    B is A\n    let x: A = b@move\n    return x@move")]
    [InlineData("func same<A, B>(a: A, b: B) -> B\n    B is A\n    let y: B = a@move\n    return y@move")]
    [InlineData("func less<A, B>(a: A, b: B) -> bool\n    A is PrimitiveInteger\n    B is A\n    return a < b")]
    [InlineData("func take<T>(x: T, y: T) -> () => ()\nfunc pass<A, B>(a: A, b: B) -> ()\n    B is A\n    A is Copy\n    take(a, b)")]
    [InlineData("func same<A, B>(b: B) -> A\n    A is B\n    B is A\n    return b@move")]
    [InlineData("func same<A, B, C>(b: B) -> C\n    A is B\n    A is C\n    return b@move")]
    [InlineData("func less<A, B, C>(b: B, c: C) -> bool\n    A is B\n    A is C\n    C is PrimitiveInteger\n    return b < c")]
    [InlineData("func less<A, B, C>(b: B, c: C) -> bool\n    A is C\n    A is B\n    B is PrimitiveInteger\n    return b < c")]
    [InlineData("func same<A, B, C>(b: Array<B>) -> Array<C>\n    A is B\n    A is C\n    return b@move")]
    public void PremiseMakesOneTypeInItsFunction(string source)
    {
        var c = MinimalEmissionTest.Analyze(source + "\n()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    // SPEC 8.2.3, 3.4.3, 8.5.2, 8.6.4: `U is S` makes S one Type with U, so name lookup on S reads U's Contracts (identity Constraints
    // provide declarations): beside S's own premise a second same-named associated Type or requirement is ambiguous (r18, r21: accepted
    // before R7-C2c2 c2-3a's name discovery read Enumerate's rows), whichever side the identity names first (r19) and for two
    // instances of one generic Contract (n-r18-other-n: SPEC 8.5.1); alone U's requirement is S's (x1, the counterpart).
    [Theory]
    [InlineData("contract Named2\n    associate Element\nfunc g<S, U>(items: ref/S, e: ref/S.Element) -> i32\n    U is Indexable<isize>\n    U is S\n    S is Named2\n    return 0", "AmbiguousBinding_Kd")]
    [InlineData(Getter + "contract Getter2\n    func get(self: ref/Self) -> i32\nfunc g<S, U>(v: ref/S) -> i32\n    U is Getter\n    U is S\n    S is Getter2\n    return v.get()", "AmbiguousBinding_Kd")]
    [InlineData(Getter + "func g<S, U>(v: ref/S) -> i32\n    U is Getter\n    U is S\n    return v.get()", "")]
    [InlineData("contract Named2\n    associate Element\nfunc g<S, U>(items: ref/S, e: ref/S.Element) -> i32\n    U is Indexable<isize>\n    S is U\n    S is Named2\n    return 0", "AmbiguousBinding_Kd")]
    [InlineData("func g<S, U>(items: ref/S, e: ref/S.Element) -> i32\n    U is Indexable<isize>\n    U is S\n    S is Indexable<i32>\n    return 0", "AmbiguousBinding_Kd")]
    public void NameLookupReadsTheIdenticalTypesContracts(string source, string codes)
    {
        var c = MinimalEmissionTest.Check(source + "\n()");
        Assert.Equal(codes, string.Join(" ", TestDiagnostics.Of(c).Select(static x => x.Code)));
    }

    // PLAN G881 (SPEC 8.2.3, 8.3.2): a premise's supply along a projection's chain is evidence for every Type identical with the
    // projection, however deep and whatever the identity's side or order, so `need(u)` proves `U is Inner` at one, two and three
    // links, through a chain of one-link identities and where the class key is a shallower projection (nkp). Fails on 0d79855e3
    // (ContractSolver's row (5) missing; `UnprovenConstraint_Kd`): m2r, m2ro, m3r, mchr, the identities written with U last (with U
    // first the proof reads the projection itself). Each invalid twin reaches Inner0, not Inner.
    [Theory]
    [InlineData("m1", "U", "T is Outer1\n    U is T.(Outer1).Next", "")]
    [InlineData("m1r", "U", "T is Outer1\n    T.(Outer1).Next is U", "")]
    [InlineData("m2", "U", "T is Outer3\n    U is T.(Outer3).Next.(Mid).Next", "")]
    [InlineData("m2r", "U", "T is Outer3\n    T.(Outer3).Next.(Mid).Next is U", "")]
    [InlineData("m2ro", "U", "T.(Outer3).Next.(Mid).Next is U\n    T is Outer3", "")]
    [InlineData("m3r", "U", "T is Top\n    T.(Top).Next.(Outer3).Next.(Mid).Next is U", "")]
    [InlineData("mchr", "U", "T is Outer3\n    T.(Outer3).Next is V\n    V.(Mid).Next is U", "")]
    [InlineData("nkp", "T.(K).Next", "T is K\n    V is Outer3\n    T.(K).Next is V.(Outer3).Next.(Mid).Next", "")]
    [InlineData("m0", "U", "T is Outer30\n    U is T.(Outer30).Next.(Mid0).Next", "UnprovenConstraint_Kd")]
    [InlineData("m0r", "U", "T is Outer30\n    T.(Outer30).Next.(Mid0).Next is U", "UnprovenConstraint_Kd")]
    [InlineData("mch0r", "U", "T is Outer30\n    T.(Outer30).Next is V\n    V.(Mid0).Next is U", "UnprovenConstraint_Kd")]
    public void AProjectionsSupplyIsEvidenceForItsIdenticalTypes(string row, string subject, string premises, string codes)
    {
        const string Chain = "contract Inner\n    associate F\ncontract Inner0\n    associate F\ncontract Mid\n    associate Next is Inner\n" +
            "contract Mid0\n    associate Next is Inner0\ncontract Outer1\n    associate Next is Inner\ncontract Outer3\n    associate Next is Mid\n" +
            "contract Outer30\n    associate Next is Mid0\ncontract Top\n    associate Next is Outer3\ncontract K\n    associate Next\n" +
            "func need<X>(x: ref/X)\n    X is Inner\n    return\n";
        var c = MinimalEmissionTest.Check(Chain + $"func f<T, V, U>(u: ref/{subject})\n    {premises}\n    need(u)\n()");
        Assert.True(codes == string.Join(" ", TestDiagnostics.Of(c).Select(static x => x.Code)), row + ": " + MinimalEmissionTest.Describe(c));
    }

    // PLAN G881 (design review rows R21-R26, R28, R30; SPEC 8.2.3, 8.3.2, 8.5.2, 8.10, 8.11, 15.6.4): ContractSolver's row (5) over whole
    // programs. A mate of a prefix three links below the query (up3*), two projection mates with different clauses (two*), discovery
    // across a mate (amb, ambf: AmbiguousBinding_Kd, accepted on c2-3), a generic Contract and an Origin-applied projection behind the
    // identity (genm, ori), c2-3b's kd2 and ebd2 behind it (kdi, ebi), mutual two-link identities (mu; hmu, hmuu accepted on c2-3) and
    // a projection node keyed by a parameter (keyw*: the request's key walk). The invalid twins keep their records. A cycle through a
    // projection is ACycleThroughAProjectionHasItsLeastFixpointRows (PLAN G881b).
    [Theory]
    [InlineData("up3r", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\ncontract A3\ncontract A2\n    associate N is A3\ncontract A1\n    associate N is A2\ncontract InnerD\n    associate N is A1\ncontract InnerD0\n    associate N\ncontract Mid2\n    associate Next is InnerD\ncontract Mid20\n    associate Next is InnerD0\ncontract Outer32\n    associate Next is Mid2\ncontract Outer320\n    associate Next is Mid20\nfunc needA3<X>(x: ref/X)\n    X is A3\n    return\nfunc f<T, U>(x: ref/U.(InnerD).N.(A1).N.(A2).N)\n    T is Outer32\n    T.(Outer32).Next.(Mid2).Next is U\n    needA3(x)\npublic func main() => ()\n", "")]
    [InlineData("up3f", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\ncontract A3\ncontract A2\n    associate N is A3\ncontract A1\n    associate N is A2\ncontract InnerD\n    associate N is A1\ncontract InnerD0\n    associate N\ncontract Mid2\n    associate Next is InnerD\ncontract Mid20\n    associate Next is InnerD0\ncontract Outer32\n    associate Next is Mid2\ncontract Outer320\n    associate Next is Mid20\nfunc needA3<X>(x: ref/X)\n    X is A3\n    return\nfunc f<T, U>(x: ref/U.(InnerD).N.(A1).N.(A2).N)\n    T is Outer32\n    U is T.(Outer32).Next.(Mid2).Next\n    needA3(x)\npublic func main() => ()\n", "")]
    [InlineData("up3n", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\ncontract A3\ncontract A2\n    associate N is A3\ncontract A1\n    associate N is A2\ncontract InnerD\n    associate N is A1\ncontract InnerD0\n    associate N\ncontract Mid2\n    associate Next is InnerD\ncontract Mid20\n    associate Next is InnerD0\ncontract Outer32\n    associate Next is Mid2\ncontract Outer320\n    associate Next is Mid20\nfunc needA3<X>(x: ref/X)\n    X is A3\n    return\nfunc f<T, U>(x: ref/U.(InnerD).N.(A1).N.(A2).N)\n    T is Outer320\n    T.(Outer320).Next.(Mid20).Next is U\n    needA3(x)\npublic func main() => ()\n", "UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd")]
    [InlineData("two", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\nfunc f<T, S, U>(u: ref/U) -> i32\n    T is Outer3\n    S is OuterO\n    T.(Outer3).Next.(Mid).Next is U\n    S.(OuterO).Next.(MidO).Next is U\n    need(u)\n    needO(u)\n    let g = u.get()\n    return u.put()\npublic func main() => ()\n", "")]
    [InlineData("twof", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\nfunc f<T, S, U>(u: ref/U) -> i32\n    T is Outer3\n    S is OuterO\n    U is T.(Outer3).Next.(Mid).Next\n    U is S.(OuterO).Next.(MidO).Next\n    need(u)\n    needO(u)\n    let g = u.get()\n    return u.put()\npublic func main() => ()\n", "")]
    [InlineData("twon", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\nfunc f<T, S, U>(u: ref/U)\n    T is Outer3\n    S is OuterO0\n    T.(Outer3).Next.(Mid).Next is U\n    S.(OuterO0).Next.(MidO0).Next is U\n    need(u)\n    needO(u)\npublic func main() => ()\n", "UnprovenConstraint_Kd")]
    [InlineData("amb", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\ncontract NamedF\n    associate F\ncontract NamedG\n    associate G\nfunc f<T, U>(u: ref/U, e: ref/U.F)\n    T is Outer3\n    U is NamedF\n    T.(Outer3).Next.(Mid).Next is U\n    return\npublic func main() => ()\n", "AmbiguousBinding_Kd")]
    [InlineData("ambf", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\ncontract NamedF\n    associate F\ncontract NamedG\n    associate G\nfunc f<T, U>(u: ref/U, e: ref/U.F)\n    T is Outer3\n    U is NamedF\n    U is T.(Outer3).Next.(Mid).Next\n    return\npublic func main() => ()\n", "AmbiguousBinding_Kd")]
    [InlineData("twoa", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\nfunc f<T, S, U>(u: ref/U, e: ref/U.F)\n    T is Outer3\n    S is Outer30\n    T.(Outer3).Next.(Mid).Next is U\n    S.(Outer30).Next.(Mid0).Next is U\n    return\npublic func main() => ()\n", "AmbiguousBinding_Kd")]
    [InlineData("ambv", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\ncontract NamedF\n    associate F\ncontract NamedG\n    associate G\nfunc f<T, U>(u: ref/U, e: ref/U.F)\n    T is Outer3\n    U is NamedG\n    T.(Outer3).Next.(Mid).Next is U\n    return\npublic func main() => ()\n", "")]
    [InlineData("ambs", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\ncontract NamedF\n    associate F\ncontract NamedG\n    associate G\nfunc f<T, U>(u: ref/U, e: ref/U.F)\n    T is Outer3\n    U is Inner\n    T.(Outer3).Next.(Mid).Next is U\n    return\npublic func main() => ()\n", "")]
    [InlineData("genm", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\ncontract Same<X>\n    func same(self: ref/Self, other: ref/X) -> i32\ncontract MidS\n    associate Next\n    Self.(MidS).Next is Same<Self.(MidS).Next>\ncontract MidS0\n    associate Next\ncontract OuterS\n    associate Next is MidS\ncontract OuterS0\n    associate Next is MidS0\nfunc needS<X>(x: ref/X)\n    X is Same<X>\n    return\nfunc f<T, U>(u: ref/U) -> i32\n    T is OuterS\n    T.(OuterS).Next.(MidS).Next is U\n    return u.same(u)\npublic func main() => ()\n", "")]
    [InlineData("genn", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\ncontract Same<X>\n    func same(self: ref/Self, other: ref/X) -> i32\ncontract MidS\n    associate Next\n    Self.(MidS).Next is Same<Self.(MidS).Next>\ncontract MidS0\n    associate Next\ncontract OuterS\n    associate Next is MidS\ncontract OuterS0\n    associate Next is MidS0\nfunc needS<X>(x: ref/X)\n    X is Same<X>\n    return\nfunc f<T, U>(u: ref/U)\n    T is OuterS0\n    T.(OuterS0).Next.(MidS0).Next is U\n    needS(u)\npublic func main() => ()\n", "UnprovenConstraint_Kd")]
    [InlineData("ori", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\ncontract MidL\n    associate L(a) is Inner\ncontract MidL0\n    associate L(a) is Inner0\ncontract OuterL\n    associate Next is MidL\ncontract OuterL0\n    associate Next is MidL0\nfunc f<T, U>(t: ref/T during a, u: ref/U)\n    T is OuterL\n    T.(OuterL).Next.(MidL).L(a) is U\n    need(u)\npublic func main() => ()\n", "")]
    [InlineData("orin", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\ncontract MidL\n    associate L(a) is Inner\ncontract MidL0\n    associate L(a) is Inner0\ncontract OuterL\n    associate Next is MidL\ncontract OuterL0\n    associate Next is MidL0\nfunc f<T, U>(t: ref/T during a, u: ref/U)\n    T is OuterL0\n    T.(OuterL0).Next.(MidL0).L(a) is U\n    need(u)\npublic func main() => ()\n", "UnprovenConstraint_Kd")]
    [InlineData("kdi", "contract Holder\n    associate Next is Holder\n    associate Other\n    associate F\n    Self.(Holder).F is Callable<(i32) -> i32>\n    func deep(self: ref/Self) -> ref/Self.(Holder).Next.(Holder).F\ncontract Holder0\n    associate Next is Holder0\n    associate F\n    func deep(self: ref/Self) -> ref/Self.(Holder0).Next.(Holder0).F\ncontract MidH\n    associate Next is Holder\ncontract MidH0\n    associate Next is Holder0\ncontract OuterH\n    associate Next is MidH\ncontract OuterH0\n    associate Next is MidH0\nfunc probe<T, U>(u: ref/U) -> i32\n    T is OuterH\n    T.(OuterH).Next.(MidH).Next is U\n    let g = u.deep()\n    return g(1)\npublic func main() => ()\n", "")]
    [InlineData("kdin", "contract Holder\n    associate Next is Holder\n    associate Other\n    associate F\n    Self.(Holder).F is Callable<(i32) -> i32>\n    func deep(self: ref/Self) -> ref/Self.(Holder).Next.(Holder).F\ncontract Holder0\n    associate Next is Holder0\n    associate F\n    func deep(self: ref/Self) -> ref/Self.(Holder0).Next.(Holder0).F\ncontract MidH\n    associate Next is Holder\ncontract MidH0\n    associate Next is Holder0\ncontract OuterH\n    associate Next is MidH\ncontract OuterH0\n    associate Next is MidH0\nfunc probe<T, U>(u: ref/U) -> i32\n    T is OuterH0\n    T.(OuterH0).Next.(MidH0).Next is U\n    let g = u.deep()\n    return g(1)\npublic func main() => ()\n", "NotCallable_Kd")]
    [InlineData("ebi", "contract Quiet\n    func ping(self: ref/Self) -> i32\n        effect confined\ncontract Loud\n    func ping(self: ref/Self) -> i32\ncontract Node\n    associate Next is Node\n    associate Item is Quiet\n    func deep(self: ref/Self) -> ref/Self.(Node).Next.(Node).Item\n        effect confined\ncontract NodeL\n    associate Next is NodeL\n    associate Item is Loud\n    func deep(self: ref/Self) -> ref/Self.(NodeL).Next.(NodeL).Item\n        effect confined\ncontract MidN\n    associate Next is Node\ncontract MidNL\n    associate Next is NodeL\ncontract OuterN\n    associate Next is MidN\ncontract OuterNL\n    associate Next is MidNL\ncontract Report\n    func report(self: ref/Self) -> i32\n        effect confined\nstruct Meter<T, U>\n    T is OuterN\n    T.(OuterN).Next.(MidN).Next is U\n    Self is Report\n    var reading: U\n    public func report(self: ref/Self) -> i32\n        return self.reading.deep().ping()\npublic func main() => ()\n", "")]
    [InlineData("ebin", "contract Quiet\n    func ping(self: ref/Self) -> i32\n        effect confined\ncontract Loud\n    func ping(self: ref/Self) -> i32\ncontract Node\n    associate Next is Node\n    associate Item is Quiet\n    func deep(self: ref/Self) -> ref/Self.(Node).Next.(Node).Item\n        effect confined\ncontract NodeL\n    associate Next is NodeL\n    associate Item is Loud\n    func deep(self: ref/Self) -> ref/Self.(NodeL).Next.(NodeL).Item\n        effect confined\ncontract MidN\n    associate Next is Node\ncontract MidNL\n    associate Next is NodeL\ncontract OuterN\n    associate Next is MidN\ncontract OuterNL\n    associate Next is MidNL\ncontract Report\n    func report(self: ref/Self) -> i32\n        effect confined\nstruct Meter<T, U>\n    T is OuterNL\n    T.(OuterNL).Next.(MidNL).Next is U\n    Self is Report\n    var reading: U\n    public func report(self: ref/Self) -> i32\n        return self.reading.deep().ping()\npublic func main() => ()\n", "IncompatibleContractImplementation_Kd")]
    [InlineData("mu", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\ncontract Ca\n    associate X is Mid\ncontract Cb\n    associate Y is MidO\ncontract Cb0\n    associate Y is MidO0\nfunc f<T, U>(t: ref/T, u: ref/U)\n    T is Ca\n    U is Cb\n    T.(Ca).X.(Mid).Next is U\n    U.(Cb).Y.(MidO).Next is T\n    need(u)\n    needO(t)\npublic func main() => ()\n", "")]
    [InlineData("mun", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\ncontract Ca\n    associate X is Mid\ncontract Cb\n    associate Y is MidO\ncontract Cb0\n    associate Y is MidO0\nfunc f<T, U>(t: ref/T, u: ref/U)\n    T is Ca\n    U is Cb0\n    T.(Ca).X.(Mid).Next is U\n    U.(Cb0).Y.(MidO0).Next is T\n    need(u)\n    needO(t)\npublic func main() => ()\n", "UnprovenConstraint_Kd")]
    [InlineData("hmu", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\ncontract Ca\n    associate X is Mid\ncontract Cb\n    associate Y is MidO\ncontract Cb0\n    associate Y is MidO0\nfunc fjm<T, U>(dst: uniq/U, p: raw/u8)\n    T is Ca\n    U is Cb\n    T.(Ca).X.(Mid).Next is U\n    U.(Cb).Y.(MidO).Next is T\n    return\nfunc fjmBad<T, U>(x: U) -> ()\n    T is Ca\n    U is Cb\n    T.(Ca).X.(Mid).Next is U\n    U.(Cb).Y.(MidO).Next is T\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fjm<T, U>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\npublic func main() => ()\n", "ComparisonLoanConflict_Kd")]
    [InlineData("hmuu", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\ncontract Ca\n    associate X is Mid\ncontract Cb\n    associate Y is MidO\ncontract Cb0\n    associate Y is MidO0\nfunc fjm<T, U>(dst: uniq/U, p: raw/u8)\n    T is Ca\n    U is Cb\n    T.(Ca).X.(Mid).Next is U\n    U.(Cb).Y.(MidO).Next is T\n    return\nfunc fjmBad<T, U>(x: T.(Ca).X.(Mid).Next) -> ()\n    T is Ca\n    U is Cb\n    T.(Ca).X.(Mid).Next is U\n    U.(Cb).Y.(MidO).Next is T\n    var t = x@move\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    fjm<T, T.(Ca).X.(Mid).Next>(t@uniq, w@raw@raw/u8)\n    y = 5\n    consume(t@move)\npublic func main() => ()\n", "ComparisonLoanConflict_Kd")]
    [InlineData("keyw", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\nfunc f<T, U>(x: ref/T.(Outer30).Next.(Mid0).Next)\n    T is Outer30\n    U is Inner\n    T.(Outer30).Next.(Mid0).Next is U\n    need(x)\npublic func main() => ()\n", "")]
    [InlineData("keywf", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\nfunc f<T, U>(x: ref/T.(Outer30).Next.(Mid0).Next)\n    T is Outer30\n    U is Inner\n    U is T.(Outer30).Next.(Mid0).Next\n    need(x)\npublic func main() => ()\n", "")]
    [InlineData("keywd", "func consume<X>(x: X) => ()\ncontract Inner\n    associate F\n    Self.(Inner).F is Callable<(raw/u8) -> Self>\n    func get(self: ref/Self) -> ref/Self.(Inner).F\ncontract Inner0\n    associate F\n    func get(self: ref/Self) -> ref/Self.(Inner0).F\ncontract Mid\n    associate Next is Inner\ncontract Mid0\n    associate Next is Inner0\ncontract Outer3\n    associate Next is Mid\ncontract Outer30\n    associate Next is Mid0\ncontract Other\n    associate G\n    func put(self: ref/Self) -> i32\ncontract Other0\n    associate G\ncontract MidO\n    associate Next is Other\ncontract MidO0\n    associate Next is Other0\ncontract OuterO\n    associate Next is MidO\ncontract OuterO0\n    associate Next is MidO0\nfunc need<X>(x: ref/X)\n    X is Inner\n    return\nfunc needO<X>(x: ref/X)\n    X is Other\n    return\nfunc f<T, U>(x: ref/T.(Outer30).Next.(Mid0).Next.(Other).G)\n    T is Outer30\n    U is Other\n    T.(Outer30).Next.(Mid0).Next is U\n    return\npublic func main() => ()\n", "")]
    public void AnIdentityThroughAProjectionHasTheProjectionsRows(string row, string source, string codes)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var found = string.Join(" ", CompilationTestHelper.PublishedErrors(c).Select(static x => x.Code).Order(StringComparer.Ordinal));
        Assert.True(codes == found, row + ": " + found);
    }

    // SPEC 8.2.3, 8.5.1, 8.6.4: identity is closed under replacing a Type inside Contract arguments, so under `U is S` the references
    // `Getter<U>` and `Getter<S>` are one and name one requirement or associated Type (v2, v3; v1 the non-generic control); `Boxed<i32>`
    // and `Boxed<i64>` stay two (v11: accepted before R7-C2c2 c2-3a). So are two written spellings under `U is i32` (w1, w2), a
    // projection argument a premise completes (w3, fa1, ar15) and arguments a decomposed identity joins (z1): PLAN G302's false
    // AmbiguousBinding_Kd before c2-3a published one candidate per Contract (ContractSolver's PremiseRows).
    [Theory]
    [InlineData("contract Getter<X>\n    func get(self: ref/Self) -> i32\nfunc g<S, U>(v: ref/S) -> i32\n    U is Getter<U>\n    U is S\n    S is Getter<S>\n    return v.get()", "")]
    [InlineData("contract Boxed<X>\n    associate Item\nfunc g<S, U>(items: ref/S, e: ref/S.Item) -> i32\n    U is Boxed<U>\n    U is S\n    S is Boxed<S>\n    return 0", "")]
    [InlineData(Getter + "func g<S, U>(v: ref/S) -> i32\n    U is Getter\n    U is S\n    S is Getter\n    return v.get()", "")]
    [InlineData("contract Boxed<X>\n    associate Item\nfunc g<S, U>(items: ref/S, e: ref/S.Item) -> i32\n    U is Boxed<i32>\n    U is S\n    S is Boxed<i64>\n    return 0", "AmbiguousBinding_Kd")]
    [InlineData("contract Getter<X>\n    func get(self: ref/Self) -> i32\nfunc g<T, U>(v: ref/T) -> i32\n    T is Getter<U>\n    U is i32\n    T is Getter<i32>\n    return v.get()", "")]
    [InlineData("contract Boxed<X>\n    associate Item\nfunc g<T, U>(e: ref/T.Item) -> i32\n    T is Boxed<U>\n    U is i32\n    T is Boxed<i32>\n    return 0", "")]
    [InlineData("contract Getter<X>\n    func get(self: ref/Self) -> i32\ncontract K\n    associate Y\nfunc g<S, U>(v: ref/S) -> i32\n    U is Getter<i32>\n    U is S\n    S is K\n    S.(K).Y is i32\n    S is Getter<S.(K).Y>\n    return v.get()", "")]
    [InlineData("contract Getter<X>\n    func get(self: ref/Self) -> i32\nfunc g<T, U, S>(first: ref/U during a, second: ref/S during b, v: ref/S) -> i32\n    origin a == b\n    T is ref/U during a\n    T is ref/S during b\n    S is Getter<U>\n    S is Getter<S>\n    return v.get()", "")]
    [InlineData("contract HasItem\n    associate Item\ncontract Iter<T>\n    associate Tag\nfunc f<T, U>(x: ref/T, t: T.Tag) -> ()\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Iter<i32>\n    T is Iter<U.(HasItem).Item>\n    ()", "")]
    [InlineData("contract HasItem\n    associate Item\nfunc f<T, U>(a: T, b: i32) -> T.Output\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Addable<i32>\n    T is Addable<U.(HasItem).Item>\n    return a + b", "")]
    public void IdenticalContractReferencesNameOneCandidate(string source, string codes)
    {
        var c = MinimalEmissionTest.Check(source + "\n()");
        Assert.Equal(codes, string.Join(" ", TestDiagnostics.Of(c).Select(static x => x.Code)));
    }

    [Fact]
    public void DifferentParametersStayDifferentWithoutAPremise()
    {
        var c = MinimalEmissionTest.Analyze("func same<A, B>(a: A, b: B) -> A\n    return b@move\n()");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.TypeMismatch_Kd);
    }

    [Fact]
    public void ConditionalConformanceUsesItsIdentityCondition()
        => ScalarEmissionTest.EmitFixture("TypeIdentityConformance", Pair + Uses, "ok\n");

    [Fact]
    public void EquivalentBoundaryTypesKeepTheirIntegerCapability()
    {
        const string Source = "func total<A, B, C>(a: B, b: C) -> B\n" +
            "    A is B\n    A is C\n    C is PrimitiveInteger\n" +
            "    var sum: B = a\n    for i in a..=b\n        sum = sum + i\n    return sum\n" +
            "require total<i32, i32, i32>(0, 3) == 6 else => $abort(\"equivalent boundaries\")\nConsole.writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("TypeIdentityRange", Source, "ok\n");
    }

    // SPEC 8.2: an expression Type with an identity premise substituted keeps its stored fields, so a field of a substituted
    // range keeps its substituted Type in every instance.
    [Fact]
    public void SubstitutedTypesKeepTheirFields()
    {
        const string Source = "func last<A, B>(r: Range<A, B>) -> B\n    A is PrimitiveInteger\n    B is A\n    return r.end\n" +
            "func first<A, B>(r: ClosedRange<A, B>) -> A\n    A is PrimitiveInteger\n    B is A\n    let copy = r\n    return copy.start\n" +
            "require last(1..4) == 4 and last(2@u8..9@u8) == 9 and first(3@i64..=5) == 3 else => $abort(\"fields\")\nConsole.writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("TypeIdentityFields", Source, "ok\n");
    }

    [Fact]
    public void IdentityChainsAreNotLimitedToSixteenSteps()
    {
        var parameters = string.Join(", ", Enumerable.Range(0, 24).Select(i => $"T{i}"));
        var premises = string.Join("\n", Enumerable.Range(0, 23).Select(i => $"    T{i} is T{i + 1}"));
        var c = MinimalEmissionTest.Analyze($"func same<{parameters}>(x: T0) -> T23\n{premises}\n    return x@move\n()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void IdentityClosureReusesItsScratchStorage()
    {
        var c = CompilationTestHelper.ParseSuccess("func same<A, B, C>(b: B) -> C\n    A is B\n    A is C\n    return b@move\n()");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    // SPEC 8.2.3, DECISIONS BU-72: a member or element access whose receiver's Type, through its Semantics layers, is a Type parameter
    // that the premises make a complete Type publishes that identity, which a warm rebind publishes again; an access without an identity
    // premise, or whose receiver's Type has the parameter only inside it, publishes none.
    [Theory]
    [InlineData("func f<T>(s: ref/T) -> i32\n    T is Pair\n    return s.a", "s.a", BoundTypeKind.Nominal)]
    [InlineData("func f<T>(s: T) -> i32\n    T is Pair\n    return (s).a", "(s).a", BoundTypeKind.Nominal)]
    [InlineData("func f<T, U>(s: ref/T) -> i32\n    T is U\n    U is Pair\n    return s.a", "s.a", BoundTypeKind.Nominal)]
    [InlineData("func f<T>(s: ref/T, i: isize) -> i32\n    T is Array<Pair>\n    return s[i].a", "s[i]", BoundTypeKind.Array)]
    [InlineData("func f<T>(s: ref/T) -> i32\n    T is (Pair, i32)\n    return s.0.a", "s.0", BoundTypeKind.Tuple)]
    [InlineData("func f(s: ref/Pair) -> i32\n    return s.a", "s.a", null)]
    [InlineData("contract Pick\n    func pick(self: ref/Self) -> bool\nfunc f<T>(s: ref/T) -> bool\n    T is Pick\n    return s.pick()", "s.pick", null)]
    [InlineData("func f<T>(xs: ref/Array<T>, i: isize) -> i32\n    T is Pair\n    return xs[i].a", "xs[i]", null)]
    [InlineData("func f<T>(xs: ref/Array<T>, i: isize) -> i32\n    T is Pair\n    return xs[i].a", "xs[i].a", null)]
    public void AnAccessPublishesTheIdentityItsReceiverWasResolvedThrough(string source, string access, BoundTypeKind? identical)
    {
        var c = CompilationTestHelper.BindSuccess("struct Pair\n    public var a: i32 = 1\n    public init() => ()\n" + source + "\n()");
        var node = (BinaryKoto)KotoTree.Walk(c.Kotonoha.RootKoto).Single(x => x is MemberAccessKoto or IndexKoto && x.ToString() == access);
        var premise = HirTables.IdentityPremise(node);
        if (identical is null)
        {
            Assert.Null(premise);
            return;
        }

        var receiver = HirTables.TypeOf(KotoHelper.UnwrapParentheses(node.Left));
        while (receiver is { Kind: BoundTypeKind.Semantics, Components.Count: 1 })
        {
            receiver = receiver.Components[0];
        }

        Assert.Equal(ConstraintKind.TypeIdentity, premise?.Kind);
        Assert.Equal((BoundTypeKind.Parameter, "T"), (premise!.Subject!.Kind, premise.Subject.Name));
        Assert.Equal(identical, premise.RequiredType!.Kind);
        Assert.Same(receiver, premise.RequiredType);
        Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete);
        Assert.Same(premise, HirTables.IdentityPremise(node));
    }

    // DECISIONS BU-72: a warm rebind publishes an access's identity premise again without allocating, a chain's derived one included.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebindRepublishesAnAccesssIdentityWithoutAllocating()
    {
        var c = CompilationTestHelper.BindSuccess(
            "struct Pair\n    public var a: i32 = 1\n    public init() => ()\n" +
            "func f<T, U>(s: ref/T, i: isize) -> i32\n    T is U\n    U is Array<Pair>\n    return s[i].a\n()");
        var node = KotoTree.Walk(c.Kotonoha.RootKoto).Single(x => x is IndexKoto && x.ToString() == "s[i]");
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
        Assert.Equal(BoundTypeKind.Array, HirTables.IdentityPremise(node)?.RequiredType?.Kind);
    }

    [Fact]
    public void RefutedIdentityLeavesTheMemberInapplicable()
    {
        var c = MinimalEmissionTest.Analyze(Pair + "let p = Pair<i32, i64>.init(1, 2)\nlet r = p.pick()");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NoApplicableOverload_Kd);
    }
}
