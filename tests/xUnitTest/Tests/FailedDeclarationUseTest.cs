// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

// SPEC 23.1.2: a check that rests on a failed declaration is a derived problem. A call whose candidate or selected target is
// in a failed declaration, and the Property proof of a member whose declaration context failed, name that declaration as
// their prerequisite and are suppressed by its published Error, while independent problems stay visible.
public sealed class FailedDeclarationUseTest
{
    [Theory]
    [InlineData("func add(a: i32) -> i32 => a\nfunc add(a: i32) -> i32 => a\npublic func main()\n    let x = add(1)\n", nameof(DiagnosticCode.DuplicateBinding_Kd))]
    [InlineData("struct Kimi\n    public var v: i32 = 0\npublic func main()\n    let k = Kimi.init()\n", nameof(DiagnosticCode.DuplicateBinding_Kd))]
    [InlineData("open struct Base\n    public var a: i32 = 0\n    public struct Node\n        public var v: i32 = 0\nstruct Derived: Base\n    public var b: i32 = 0\n    public struct Node\n        public var w: i32 = 0\npublic func main()\n    let x: i32 = 1\n", nameof(DiagnosticCode.DuplicateBinding_Kd))]
    // A parameter's nested borrow without its Origin (SPEC 3.3.2) keeps the candidate pending at each call, which published
    // UnprovenConstraint_Kd at the call beside the declaration's MissingOriginBinding_Kd.
    [InlineData("func read(p: ref/uniq/i32) -> i32 => p@follow@follow\npublic func main()\n    var a = 1\n    let t = (a@uniq, 2)\n    let x = read(t.0@ref)\n", nameof(DiagnosticCode.MissingOriginBinding_Kd))]
    [InlineData("func total<V>(d: ref/Dictionary<i32, ref/V>) -> i32 => d.length\npublic func main()\n    var a = 1\n    let entries = [7: a@ref]\n    let n = total(entries@ref)\n", nameof(DiagnosticCode.MissingOriginBinding_Kd))]
    // m1's store without its clause: its twin (`==`) reports the store's append, which reads the failed clause's resting slot directly
    // in the program, so it rests there (DECISIONS Q13 rule W, PLAN G739).
    [InlineData("struct V<T> {source}\n    public let value: ref/T during source\n    public init(value: ref/T during source) => self.value = value\nfunc identity(x: V<i32>) -> V<i32>{r}\n    origin r.source = x.source\n    return x@move\nfunc user(y: V<i32>) -> i32\n    let z = identity(y@move)\n    let k: i32 = z.value@follow\n    return k\nfunc pass(y: V<i32>) -> V<i32>{q}\n    origin q.source == y.source\n    return identity(y@move)\nfunc store(y: V<i32>, sink: uniq/Array<V<i32>>) => sink@follow.append(identity(y@move))\npublic func main() => ()\n", nameof(DiagnosticCode.OriginRelationOperator_Kd))]
    public void UsesOfAFailedDeclarationRestOnIt(string source, string code)
    {
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.NotEmpty(records);
        Assert.All(records, x => Assert.Equal(code, x.Code));
    }

    // PLAN G206, rule W at the candidate's verdict: a parameter or result Type that failed to bind is a wildcard in every fit. A candidate
    // that would apply rests the call on that failure (it was CheckFaulted_Kd at a failed default: a function, generic or not, beside an
    // independent record, a method, generic or not, a generic constructor; a valid twin). One that another part refutes leaves the call to
    // another overload, which keeps its records: a typed argument, a Literal, an aggregate or the arity refutes it, at a failed default,
    // result or mapped parameter, a constructor's, a method's or a generic one's. The last row's failed overload is refuted by nothing.
    [Theory]
    [InlineData("func gref<T>(x: T, y: ref/T during x = x@ref) => ()\npublic func main()\n    gref(3)\n", "InvalidOriginBinding_Kd")]
    [InlineData("func gref(x: i64, y: ref/i64 during x = x@ref) => ()\npublic func main()\n    gref(3)\n", "InvalidOriginBinding_Kd")]
    [InlineData("func gref<T>(x: T, y: ref/T during x = x@ref) => ()\npublic func main()\n    gref(3)\n    let z: i32 = true\n", "InvalidOriginBinding_Kd; TypeMismatch_Kd")]
    [InlineData("struct S\n    var v: i64 = 0\n    public init() => ()\n    public func m<T>(self: ref/Self, x: T, y: ref/T during x = x@ref) => ()\npublic func main()\n    let s = S.init()\n    s.m(3)\n", "InvalidOriginBinding_Kd")]
    [InlineData("struct S\n    var v: i64 = 0\n    public init() => ()\n    public func m(self: ref/Self, x: i64, y: ref/i64 during x = x@ref) => ()\npublic func main()\n    let s = S.init()\n    s.m(3)\n", "InvalidOriginBinding_Kd")]
    [InlineData("struct S<T>\n    var v: i64 = 0\n    public init(x: T, y: ref/T during x = x@ref) => ()\npublic func main()\n    let s = S.init(3)\n", "InvalidOriginBinding_Kd")]
    [InlineData("func gref<T>(x: ref/T during a, y: ref/T during a = x) => ()\npublic func main()\n    let v = 3\n    gref(v@ref)\n", "")]
    [InlineData("func f(x: i64, y: ref/i64 during x = x@ref) -> i64 => 1\nfunc f(x: bool) -> bool => x\npublic func main()\n    let z = f(true)\n    let q: i64 = z\n", "InvalidOriginBinding_Kd; TypeMismatch_Kd")]
    [InlineData("func f(x: i64) -> Missing => 1\nfunc f(x: bool) -> bool => x\npublic func main()\n    let z = f(true)\n    let q: i64 = z\n", "TypeMismatch_Kd; UnresolvedBinding_Kd")]
    [InlineData("func f(x: i64, y: Missing) -> i64 => 1\nfunc f(x: bool, y: i64) -> bool => x\npublic func main()\n    let z = f(true, 1)\n    let q: i64 = z\n", "TypeMismatch_Kd; UnresolvedBinding_Kd")]
    [InlineData("func f(y: Missing, x: i64) -> i64 => 1\nfunc f(y: i64, x: bool) -> bool => x\npublic func main()\n    let z = f(1, true)\n    let q: i64 = z\n", "TypeMismatch_Kd; UnresolvedBinding_Kd")]
    [InlineData("struct S\n    var v: i64 = 0\n    public init(x: i64, y: ref/i64 during x = x@ref) => ()\n    public init(x: bool) => ()\npublic func main()\n    let s = S.init(true)\n    let q: i64 = s\n", "InvalidOriginBinding_Kd; TypeMismatch_Kd")]
    [InlineData("struct S\n    var v: i64 = 0\n    public init() => ()\n    public func m(self: ref/Self, x: i64, y: ref/i64 during x = x@ref) -> i64 => 1\n    public func m(self: ref/Self, x: bool) -> bool => x\npublic func main()\n    let s = S.init()\n    let z = s.m(true)\n    let q: i64 = z\n", "InvalidOriginBinding_Kd; TypeMismatch_Kd")]
    [InlineData("func f<T>(x: T, y: ref/T during x = x@ref, w: i64) -> T => x\nfunc f(x: bool) -> bool => x\npublic func main()\n    let z = f(true)\n    let q: i64 = z\n", "InvalidOriginBinding_Kd; TransferRequired_Kd; TypeMismatch_Kd")]
    [InlineData("enum Failure\n    Busy\nfunc f(x: i64) -> Missing => 1\nfunc f(x: bool) -> Result<i32, Failure> => .Ok(1)\npublic func main()\n    f(true)\n", "DiscardedResult_Kd; UnresolvedBinding_Kd")]
    [InlineData("func f(x: bool, y: ref/i64 during x = x@ref) -> i64 => 1\nfunc f(x: i64) -> bool => true\npublic func main()\n    let z = f(1)\n    let q: i64 = z\n", "InvalidOriginBinding_Kd; TypeMismatch_Kd")]
    [InlineData("func f(x: bool) -> Missing => 1\nfunc f(x: i64) -> bool => true\npublic func main()\n    let z = f(1)\n    let q: i64 = z\n", "TypeMismatch_Kd; UnresolvedBinding_Kd")]
    [InlineData("func f(x: bool, y: Missing) -> i64 => 1\nfunc f(x: i64, y: i64) -> bool => true\npublic func main()\n    let z = f(1, 2)\n    let q: i64 = z\n", "TypeMismatch_Kd; UnresolvedBinding_Kd")]
    [InlineData("func f(x: (bool, bool)) -> Missing => 1\nfunc f(x: (i64, i64)) -> bool => true\npublic func main()\n    let z = f((1, 2))\n    let q: i64 = z\n", "TypeMismatch_Kd; UnresolvedBinding_Kd")]
    [InlineData("enum Failure\n    Busy\nfunc f(x: bool) -> Missing => 1\nfunc f(x: i64) -> Result<i32, Failure> => .Ok(1)\npublic func main()\n    f(1)\n", "DiscardedResult_Kd; UnresolvedBinding_Kd")]
    [InlineData("func f(x: i64, y: Missing) -> i64 => 1\nfunc f(x: bool, y: i64) -> bool => x\npublic func main()\n    let z = f(1, 1)\n    let q: bool = z\n", "UnresolvedBinding_Kd")]
    public void ACandidateWithAFailedSignatureRestsTheCallUnlessAnotherPartRefutesIt(string source, string codes)
        => Assert.Equal(codes, string.Join("; ", DiagnosticCorpus.Check(source).Diagnostics.Select(static x => x.Code).Order(StringComparer.Ordinal)));

    [Fact]
    public void AMemberOfAStructWithAnInvalidAttributeAddsNoConstraintRecord()
    {
        var records = DiagnosticCorpus.Check("#Layout(\"c\")\nstruct P\n    public var x: i32 = 0\npublic func main()\n    let p = P.init()\n    let y = p.x\n").Diagnostics;
        Assert.Contains(records, x => x.Code == nameof(DiagnosticCode.InvalidLayoutAttribute_Kd));
        Assert.DoesNotContain(records, x => x.Code == nameof(DiagnosticCode.InvalidConstraint_Kd));
    }

    [Fact]
    public void AnIndependentProblemNextToAFailedDeclarationStaysVisible()
    {
        var records = DiagnosticCorpus.Check("func add(a: i32) -> i32 => a\nfunc add(a: i32) -> i32 => a\npublic func main()\n    let x = add(1)\n    let y: i32 = true\n").Diagnostics;
        Assert.Contains(records, x => x.Code == nameof(DiagnosticCode.DuplicateBinding_Kd));
        Assert.Contains(records, x => x.Code == nameof(DiagnosticCode.TypeMismatch_Kd));
        Assert.DoesNotContain(records, x => x.Code == nameof(DiagnosticCode.InvalidConstraint_Kd));
    }

    // DECISIONS G52-1 (G449): a container's or a Property's verdict reads no binding result of the declarations it contains, so a member or
    // accessor resting on a syntax recovery (a guessed relation operator) leaves the container's own record: the form is its valid-clause twin
    // plus the clause's record. A header duplicate, a nested Type's redeclared slot, an inline cycle, a Property's initializer mismatch.
    [Theory]
    [InlineData("struct Holder<E> {h, h}\n    func f(self: ref/Self, x: ref/E during a, y: ref/E during b) -> i32\n        origin a OP b\n        return 0\n", nameof(DiagnosticCode.DuplicateBinding_Kd))]
    [InlineData("struct Outer {h}\n    let v: i32\n    struct Holder<E> {h}\n        func f(self: ref/Self, x: ref/E during a, y: ref/E during b) -> i32\n            origin a OP b\n            return 0\n", nameof(DiagnosticCode.DuplicateBinding_Kd))]
    [InlineData("struct Node\n    var next: Option<Node>\n    func f(self: ref/Self, x: ref/i32 during a, y: ref/i32 during b) -> i32\n        origin a OP b\n        return 0\n", nameof(DiagnosticCode.InvalidInlineLayout_Kd))]
    [InlineData("struct S {s, t}\n    let p: ref/i32 during s\n    let q: ref/i32 during t\n    let v: i32 = true\n        get(self: ref/Self) -> i32\n            origin s OP t\n            return 0\n", nameof(DiagnosticCode.TypeMismatch_Kd))]
    public void AContainedDeclarationsRecoveryLeavesItsContainersRecord(string declarations, string code)
    {
        var twin = Codes(declarations.Replace("OP", "==", StringComparison.Ordinal));
        Assert.Contains(code, twin);
        Assert.Equal([.. twin.Append(nameof(DiagnosticCode.OriginRelationOperator_Kd)).Order(StringComparer.Ordinal)], Codes(declarations.Replace("OP", "=", StringComparison.Ordinal)));
    }

    // DECISIONS G52-1 (G449): a Field's syntax recovery leaves its struct's header duplicate, with no unavailable prerequisite in its place.
    [Fact]
    public void AFieldsRecoveryLeavesItsStructsHeaderDuplicate()
        => Assert.Equal([nameof(DiagnosticCode.DuplicateBinding_Kd), nameof(DiagnosticCode.ExpectedSyntax_Kd)], Codes("struct Holder {h, h}\n    let v: [2 i32]\n"));

    // SPEC 15.3.3, 23.1.2 (H11b-0 B1): a failed Origin clause fails itself, not its owner or the declaration's other clauses, so a
    // program's records are its twin's (the clause repaired or removed) beside the clause's own: each check of the owner, its members and
    // its uses that rested on the owner's failure is reported (PLAN G611, G612, G613), and a slot only the clause names rests on it.
    [Theory]
    // G613's form: a struct's redeclared inherited Field (ml2 / ml2n).
    [InlineData("open struct Base\n    public var n: i32 = 1\n    public init() => ()\nstruct Leaf {s, t}: Base\n    origin s = t\n    public var n: i32 = 2\n    public init() => ()\n", "    origin s = t\n", "")]
    // G612's form: an override's result mismatch (vo1 / vo1n).
    [InlineData("open struct Base\n    public init() => ()\n    public virtual func score(self: objref/Self) -> i32 => 1\nstruct Leaf {s, t}: Base\n    origin s = t\n    public init() => ()\n    override func score(self: objref/Self) -> bool => true\n", "    origin s = t\n", "")]
    // G612's form through a derived struct (vo2b / vo2bn).
    [InlineData("open struct Base {a}\n    origin a = a\n    public init() => ()\n    public virtual func score(self: objref/Self) -> i32 => 1\nstruct Leaf {b}: Base during b\n    public init() => ()\n    override func score(self: objref/Self) -> bool => true\n", "    origin a = a\n", "")]
    // a member's unsatisfied constraint (q7 / q7r).
    [InlineData("contract Marked\nstruct V<T> {source}\n    T is Marked\n    public let item: ref/T during source\nstruct S {a}\n    public let item: V<i32>{v}\n        origin v.source = a\n\npublic func main() => ()\n", "        origin v.source = a\n", "        origin v.source == a\n")]
    // a callee's effect bound (e1_caller_clause / e1n_caller_noclause).
    [InlineData("group State\n    public var count: i32 = 0\nfunc apply<F>(f: ref/F) -> i32\n    F is Callable<() -> i32>\n        effect confined\n    return f()\nfunc touch(x: ref/i32 during a, y: ref/i32 during b) -> i32\n    origin a = b\n    State.count = 3\n    return 0\npublic func main() => ()\nfunc entry() -> i32\n    let f = func [] () -> i32\n        let one = 1\n        return touch(one@ref, one@ref)\n    return apply(f@ref)\n", "    origin a = b\n", "")]
    // a call's Type constraint (cc1 / cc1n).
    [InlineData("contract Marker\nfunc f<T>(x: ref/T during a, y: ref/T during b) -> i32\n    T is Marker\n    origin a = b\n    return 0\nfunc use() -> i32\n    let one = 1\n    return f(one@ref, one@ref)\nlet wrong: i32 = true\n", "    origin a = b\n", "")]
    // a result slot named only by the failed clause, borrowed, returned and stored (m1 / m1r); the store states its clause, since a use
    // its twin rejects reads the resting slot directly in the program, whose record rests (DECISIONS Q13 rule W, PLAN G739).
    [InlineData("struct V<T> {source}\n    public let value: ref/T during source\n    public init(value: ref/T during source) => self.value = value\nfunc identity(x: V<i32>) -> V<i32>{r}\n    origin r.source = x.source\n    return x@move\nfunc user(y: V<i32>) -> i32\n    let z = identity(y@move)\n    let k: i32 = z.value@follow\n    return k\nfunc pass(y: V<i32>) -> V<i32>{q}\n    origin q.source == y.source\n    return identity(y@move)\nfunc store(y: V<i32>{p}, sink: uniq/Array<V<i32>{q}>)\n    origin p.source outlives q.source\n    sink@follow.append(identity(y@move))\npublic func main() => ()\n", "    origin r.source = x.source\n", "    origin r.source == x.source\n")]
    // a Function value read out of a result slot into an annotated local, moved (fn4 / fn4t) and not (fn3, whose twin's TransferRequired_Kd
    // it keeps): the local's fit holds by the resting slot and answers so (PLAN G739; InternalInvariant_Kd at the local before).
    [InlineData("struct C {s}\n    public let call: (ref/i32 during s) -> i32\n    public init(call: (ref/i32 during s) -> i32) => self.call = call@move\nfunc pickC(c: C) -> C{r}\n    origin r.s == zz\n    return c@move\nfunc use(a: ref/i32, g0: (ref/i32 during a) -> i32) -> i32\n    let h = pickC(C.init(g0@move))\n    let g: (ref/i32 during a) -> i32 = h.call@move\n    return g(a)\npublic func main() => ()\n", "    origin r.s == zz\n", "    origin r.s == c.s\n")]
    [InlineData("struct C {s}\n    public let call: (ref/i32 during s) -> i32\n    public init(call: (ref/i32 during s) -> i32) => self.call = call@move\nfunc pickC(c: C) -> C{r}\n    origin r.s == zz\n    return c@move\nfunc use(a: ref/i32, g0: (ref/i32 during a) -> i32) -> i32\n    let h = pickC(C.init(g0@move))\n    let g: (ref/i32 during a) -> i32 = h.call\n    return g(a)\npublic func main() => ()\n", "    origin r.s == zz\n", "    origin r.s == c.s\n")]
    // an enum's payload in its slot, constructed and matched (e1 / e1n).
    [InlineData("enum Box {s, t}\n    origin s = t\n    Value(ref/i32 during s)\n    Empty\nfunc f(x: ref/i32) -> i32\n    let b: Box = .Value(x)\n    match b\n        .Value(let n) => return n@follow\n        .Empty => return 0\npublic func main() => ()\n", "    origin s = t\n", "")]
    // a C-layout struct held by value and in a fixed array (b1 / b1n).
    [InlineData("#Layout(\"C\")\nstruct S\n    origin static = static\n    public let v: i32\n    public init(v: i32) => self.v = v\n#Layout(\"C\")\nstruct Outer\n    public let inner: S\n    public init(inner: S) => self.inner = inner@move\nfunc make() -> Outer => Outer.init(inner: S.init(v: 1))\nfunc arr() -> i32\n    let a: [2 of S] = [S.init(v: 1), S.init(v: 2)]\n    return a[0].v\nlet wrong: i32 = true\n", "    origin static = static\n", "")]
    // A5's record on a newly published body: a local's borrow returned `during static` (c01 / its `==` twin).
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during b) -> ref/i32 during static\n    origin a = b\n    let local = 1\n    return local@ref\nlet wrong: i32 = true\n", "    origin a = b\n", "    origin a == b\n")]
    public void AFailedOriginClauseFailsOnlyItself(string source, string written, string repair)
    {
        // A record whose span holds the clause shows the twin's text in its place.
        var start = source.IndexOf(written, StringComparison.Ordinal);
        var records = Records(source, start, written.Length).Select(x => x.Replace(written, repair, StringComparison.Ordinal)
            .Replace(written.TrimEnd('\n'), repair.TrimEnd('\n'), StringComparison.Ordinal)).Order(StringComparer.Ordinal);
        Assert.Equal(Records(source.Replace(written, repair, StringComparison.Ordinal), start, repair.Length), records);
    }

    private static string[] Codes(string declarations)
        => [.. DiagnosticCorpus.Check(declarations + "public func main()\n    let x: i32 = 1\n").Diagnostics.Select(static x => x.Code).Order(StringComparer.Ordinal)];

    // A program's records as code and shown text, in order, but those located in its clause's line (`start`, `length`).
    private static string[] Records(string source, int start, int length)
        => [.. DiagnosticCorpus.Check(source).Diagnostics
            .Where(x => x.Span is not { } span || span.Start < start || span.Start >= start + length)
            .Select(x => x.Code + " " + (x.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty))
            .Order(StringComparer.Ordinal)];
}
