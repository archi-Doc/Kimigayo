// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 3.4, 15.1.5: one path computation decides writes, Moves and exclusive borrows; the diagnostic names the
/// layer that denies the capability.</summary>
public class PathCapabilityDiagnosticTest
{
    private const string S = "struct S\n    public var f: string\n    public var n: i32 = 0\n    public init(f: string) => self.f = f@move\n";

    // A stored exclusive reference below a shared layer: the path grants shared access only (SPEC 15.6.2).
    private const string Layers =
        "struct Inner\n    public var n: i32 = 0\nstruct Outer {a}\n    public let inner: uniq/Inner during a\n    public let slot: uniq/Option<i32> during a\n" +
        "    public init(inner: uniq/Inner during a, slot: uniq/Option<i32> during a)\n        self.inner = inner@move\n        self.slot = slot@move\n";

    [Theory]
    [InlineData("var values: Array<string> = [\"a\"]\nlet view = values[..]\nlet m = view[0]@move", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(S + "func f(r: uniq/S) -> string => r.f@move", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData("func f(r: uniq/string) -> string => r@deref@move", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData("func f(v: ref/i32) => v@deref = 3", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("func f(v: ref/i32) => v@deref += 3", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(S + "func f(r: ref/S) => r.n = 3", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(S + "func f(r: ref/S) -> uniq/i32 during r => r.n@uniq", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("func f(v: ref/i32) -> uniq/i32 during v => v@deref@uniq", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("var values: Array<i32> = [1]\nlet view = values[..]\nview[0] = 3", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(S + "func f(r: ref/S) -> string => r.f@move", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("func f(x: i32) => x = 2", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("func f(x: Option<i32>) -> i32 => match x\n    .Some(let value) if (value = 1) => value\n    _ => 0", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Layers + "func f(r: ref/Outer{o}) => r.inner.n = 3", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Layers + "func f(r: ref/Outer{o}) => r.inner.n += 3", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Layers + "func f(r: ref/Outer{o})\n    let u = r.inner.n@uniq", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Layers + "func f(r: ref/Outer{o})\n    match r.slot\n        .Some(let x) => x@deref += 1\n        .None => ()", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Layers + "func f(r: ref/Outer{o})\n    let s = r.slot@uniq", DiagnosticCode.SharedPathAccess_Kd)]
    public void TheDenyingLayerIsReported(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Code is DiagnosticCode.UnsupportedOwnership_Kd or DiagnosticCode.UninitializedPlace_Kd);
    }

    [Theory]
    [InlineData(S + "let o = S.init(\"a\")\nlet t = o.f@move")]
    [InlineData(S + "func f(r: uniq/S) => r.n = 3")]
    [InlineData("func f(r: uniq/i32) => r@deref += 1")]
    [InlineData("func f(r: uniq/string) -> uniq/string => r@move")]
    public void OwnedAndExclusivePathsKeepTheirCapabilities(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}
