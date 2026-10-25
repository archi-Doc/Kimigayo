// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>A4 over storage Binding rejected (DECISIONS BU-36, BU-37, R4-M139): an instance is read as every reader reads one
/// (<c>TyCtxt.Readable</c>), and a part Binding rejected destroys nothing, never a limit.</summary>
public sealed partial class MirEffectsTest
{
    // A struct whose Field Binding rejects (MISSING) or binds (its repair), quiet or with a destructor that writes to the console.
    private const string Rejectable = "struct Bad\n    public var m: MISSING = 0\n    public init() => ()\n";

    // SPEC 8.11.2, 23.1.2 (DECISIONS R4-M139): an effect over storage Binding rejected is its repair's. A callee's Drop(T) atom composed
    // at a Rejected instance (`consume<Bad>`), a published row's destroyed parameter over one (`Array<Bad>.clear`, TyCtxt.Signature's
    // declared Type in the instance), and an Open Drop's part Binding rejected (a Field, a Case, null since BU-37) destroy what the repair
    // destroys: nothing over a quiet struct, the destructor's environment effect over a noisy one, which the repair also reports. Before,
    // each read the Rejected instance or the null part as a limit: Unsupported_Kd at the obligation, which also hid the destructor's record.
    [Theory]
    [InlineData("group H\n    public func consume<T>(x: T) => ()\n", "        let b = Bad.init()\n        H.consume(b@move)\n", false, "")]
    [InlineData("group H\n    public func consume<T>(x: T) => ()\n", "        let b = Bad.init()\n        H.consume(b@move)\n", true, Failed)]
    [InlineData("", "        var a: Array<Bad> = []\n        a.clear()\n", false, "")]
    [InlineData("", "        var a: Array<Bad> = []\n        a.clear()\n", true, Failed)]
    [InlineData("struct P\n    public var m: MISSING = 0\n    public var s: string = \"a\"\n    public init() => ()\n", "        var p = P.init()\n        let t = p.s@move\n", false, "")]
    [InlineData("enum E\n    A(MISSING)\n    B(string)\ngroup H\n    public func take(s: string) => ()\n", "        let e: E = .B(\"a\")\n        match e@move\n            .B(let s) => H.take(s@move)\n            _ => ()\n", false, "")]
    public void AnEffectOverRejectedStorageIsItsRepairs(string declarations, string body, bool noisy, string expected)
    {
        var bad = Rejectable + (noisy ? "    drop => Console.writeLine(\"bad\")\n" : string.Empty);
        foreach (var part in new[] { "Missing", "i32" })
        {
            var source = (Sink + Loud + bad + declarations + Witness + body + "        return .Ok(())\n").Replace("MISSING", part, StringComparison.Ordinal);
            Assert.Equal(Expected(expected), Problems(source));
        }
    }
}
