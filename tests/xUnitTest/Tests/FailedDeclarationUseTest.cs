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
    [InlineData("func f<T>(x: T, y: ref/T during x = x@ref, w: i64) -> T => x\nfunc f(x: bool) -> bool => x\npublic func main()\n    let z = f(true)\n    let q: i64 = z\n", "InvalidOriginBinding_Kd; TypeMismatch_Kd")]
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
}
