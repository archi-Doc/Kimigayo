// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 3.5, 8.14, 8.15, 13.5.5: the remaining bare positions of a pair-layer Place. A bare argument at a by-value parameter and a
// bare result or arm are acquired by each Semantics case as the case Type is; a write through a pair element of a writable Tuple
// has the owner case's Write; a bare element whose case Type is an exclusive reference is accepted.
public sealed class PairBarePositionTest
{
    private const string Sink = "func takeOR<t/U>(y: t/U)\n    t is owner or ref\n    U is Copy\n    return\n" +
        "func takeOU<t/U>(y: t/U)\n    t is owner or uniq\n    U is Copy\n    return\n";

    private const string Main = "public func main() -> ()\n    var m: i32 = 1\n    f(m)\n";

    private const string Six = "func f<a/A, b/B, c/C, d/D, e/E, g/G>(va: a/A, vb: b/B, vc: c/C, vd: d/D, ve: e/E, vg: g/G) -> ()\n" +
        "    a is owner or ref\n    b is owner or ref\n    c is owner or ref\n    d is owner or ref\n    e is owner or ref\n    g is owner or {0}\n" +
        "    A is Copy\n    B is Copy\n    C is Copy\n    D is Copy\n    E is Copy\n    G is Copy\n    let t = (va, vg)\n    let u = t\n    let w = t\n" +
        "public func main() -> ()\n    return\n";

    private const string Receiver = "struct H<s/T>\n    s is owner or {0}\n    T is Copy\n    var item: (s/T, i32)\n    var other: (s/T, i32)\n" +
        "    public init(item: (s/T, i32))\n        self.item = item@move\n        self.other = self.item@move\n        self.item = self.other{1}\n" +
        "public func main() -> ()\n    return\n";

    [Theory]
    [InlineData("func keep<F>(f: F) -> F => f@move\nfunc f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n    let kept = keep(value)\n    _ = kept@follow\n")]
    [InlineData("func f<s/T>(value: s/T) -> s/T\n    s is owner or uniq\n    T is Copy\n    return value\n")]
    [InlineData("func f<s/T>(value: s/T, c: bool) -> s/T\n    s is owner or uniq\n    T is Copy\n    return if c => value else => value\n")]
    [InlineData("func f<s/T>(value: s/T, x: T) -> ()\n    s is owner or uniq\n    T is Copy\n    var p = (value@move, 1)\n    p.0@follow = x\n    _ = p.0@follow\n")]
    public void ABarePositionIsAcquiredPerCase(string definition)
    {
        var c = MinimalEmissionTest.Analyze(definition + "public func main() -> ()\n    var n: i32 = 1\n    _ = f(n" + (definition.Contains("x: T", StringComparison.Ordinal) ? ", 9" : definition.Contains("c: bool", StringComparison.Ordinal) ? ", true" : string.Empty) + ")\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    // The old analysis's located limit at the bare element (Unsupported_Kd at `arr[0]`) went with the MIR deciding acceptance (H5; 1a,
    // the owner 2026-11-03): check accepts the program with no record, and a Library build generates f's instance that main calls.
    [Fact]
    public void ABareElementOfAnExclusiveCaseIsAccepted()
    {
        var source = "func f<s/T>(value: s/T, other: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n    var arr: [2 of s/T] = [value@move, other@move]\n    let e = arr[0]\n    _ = e@follow\npublic func main() -> ()\n    var n: i32 = 1\n    var m: i32 = 2\n    f(n, m)\n";
        var check = DiagnosticCorpus.Check(source);
        Assert.True(check.Accepted && check.Diagnostics.Length == 0, string.Join("; ", check.Diagnostics.Select(static x => x.Code)));
        Assert.Contains("f", MinimalEmissionTest.GeneratedBodies(source, OutputKind.Library, accepted: true));
    }

    // SPEC 13.5.6: an exclusive short adaptation borrows the written slot, which must be writable.
    [Fact]
    public void AnExclusiveShorthandNeedsAWritableWrittenSlot()
    {
        var source = "func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n    let v = value@s\n    _ = v@move\npublic func main() -> ()\n    var n: i32 = 1\n    f(n)\n    f(n@uniq)\n";
        Assert.Contains(DiagnosticCorpus.Check(source).Diagnostics, static x => x.Code == nameof(DiagnosticCode.InvalidAssignment_Kd));
        var valid = source.Replace("let v = value@s", "var slot = value@move\n    let v = slot@s", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(valid);
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        NativeAllocationAudit.WriteFixture("GenericAdaptationWritableSlot", valid, 0, 0, 0);
    }

    // SPEC 8.14, 8.15.2 (G642): an acquisition's Copy evidence is the 8.3 proof of the complete Type in each Semantics case, at every
    // reader: a bare Place (a Tuple, a Closure), a Consuming call (7.7.3), `@copy` (13.5.3), a special receiver's part (6.3.4), and at
    // 64 cases, the bound, inclusive. Labels from SPEC: the first five are valid (owner or ref, T is Copy: `(T, i32)`, `(ref/T, i32)`
    // and the Closure over them are Copy); each other row is invalid in one case (a uniq case, 3.5.2) with its record.
    [Theory]
    [InlineData("func f<s/T>(value: s/T) -> ()\n    s is owner or ref\n    T is Copy\n    let t = (value, 1)\n    let u = t\n    let w = t\n", "")]
    [InlineData("func f<s/T>(value: s/T) -> ()\n    s is owner or ref\n    T is Copy\n    let c = func [value] () -> ()\n        takeOR(value@move)\n    let d = c\n    c()\n    c()\n", "")]
    [InlineData("func f<s/T>(value: s/T) -> ()\n    s is owner or ref\n    T is Copy\n    let t = (value, 1)\n    let u = t@copy\n    let w = value@copy\n", "")]
    [InlineData("receiver ref", "")]
    [InlineData("six ref", "")]
    [InlineData("func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n    let c = func [value] () -> ()\n        takeOU(value@move)\n    c()\n", nameof(DiagnosticCode.TransferRequired_Kd))]
    [InlineData("func f<s/T>(value: s/T) -> ()\n    s is owner or ref\n    let t = (value@move, 1)\n    let u = t\n", nameof(DiagnosticCode.TransferRequired_Kd))]
    [InlineData("func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n    let u = value@copy\n", nameof(DiagnosticCode.NonCopyOperand_Kd))]
    [InlineData("receiver uniq", nameof(DiagnosticCode.InvalidAssignment_Kd))]
    [InlineData("six uniq", nameof(DiagnosticCode.TransferRequired_Kd))]
    public void ACopyInEveryCaseIsTheAcquisitionsEvidence(string program, string code)
    {
        var source = program switch
        {
            "receiver ref" => string.Format(CultureInfo.InvariantCulture, Receiver, "ref", string.Empty),
            "receiver uniq" => string.Format(CultureInfo.InvariantCulture, Receiver, "uniq", "@move"),
            "six ref" => string.Format(CultureInfo.InvariantCulture, Six, "ref"),
            "six uniq" => string.Format(CultureInfo.InvariantCulture, Six, "uniq"),
            _ => Sink + program + Main,
        };
        var check = DiagnosticCorpus.Check(source);
        var codes = string.Join("; ", check.Diagnostics.Select(static x => x.Code));
        Assert.True(code.Length == 0 ? check.Accepted && check.Diagnostics.Length == 0 : !check.Accepted && codes.Contains(code, StringComparison.Ordinal), codes);
    }
}
