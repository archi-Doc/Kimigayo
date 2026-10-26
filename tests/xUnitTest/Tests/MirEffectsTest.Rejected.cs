// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>A4 over storage Binding rejected (DECISIONS BU-36, BU-37, R4-M139): an instance is read as every reader reads one
/// (<c>TyCtxt.ReadableInstance</c>), and a part Binding rejected destroys nothing, never a limit.</summary>
public sealed partial class MirEffectsTest
{
    // A struct whose Field Binding rejects (MISSING) or binds (its repair), quiet or with a destructor that writes to the console.
    private const string Rejectable = "struct Bad\n    public var m: MISSING = 0\n    public init() => ()\n";

    // SPEC 8.11.2, 23.1.2 (DECISIONS R4-M139): an effect over storage Binding rejected is its repair's. A callee's Drop(T) atom composed
    // at a Rejected instance (`consume<Bad>`), a published row's destroyed parameter over one (`Array<Bad>.clear`, TyCtxt.Signature's
    // declared Type in the instance), and an Open Drop's part Binding rejected (a Field, a Case, null since BU-37) destroy what the repair
    // destroys: nothing over a quiet struct, the destructor's environment effect over a noisy one, which the repair also reports. Before,
    // each read the Rejected instance or the null part as a limit: Unsupported_Kd at the obligation, which also hid the destructor's record.
    // The null part ends no Open walk: a Loud sibling of the rejected Field, or a Loud Case beside the rejected one, keeps its own record.
    [Theory]
    [InlineData("group H\n    public func consume<T>(x: T) => ()\n", "        let b = Bad.init()\n        H.consume(b@move)\n", false, "")]
    [InlineData("group H\n    public func consume<T>(x: T) => ()\n", "        let b = Bad.init()\n        H.consume(b@move)\n", true, Failed)]
    [InlineData("", "        var a: Array<Bad> = []\n        a.clear()\n", false, "")]
    [InlineData("", "        var a: Array<Bad> = []\n        a.clear()\n", true, Failed)]
    [InlineData("struct P\n    public var m: MISSING = 0\n    public var s: string = \"a\"\n    public init() => ()\n", "        var p = P.init()\n        let t = p.s@move\n", false, "")]
    [InlineData("enum E\n    A(MISSING)\n    B(string)\ngroup H\n    public func take(s: string) => ()\n", "        let e: E = .B(\"a\")\n        match e@move\n            .B(let s) => H.take(s@move)\n            _ => ()\n", false, "")]
    [InlineData("struct P\n    public var m: MISSING = 0\n    public var l: Loud = Loud.init()\n    public var s: string = \"a\"\n    public init() => ()\n", "        var p = P.init()\n        let t = p.s@move\n", false, Failed)]
    [InlineData("enum E\n    A(MISSING)\n    B(Loud)\n    C(string)\ngroup H\n    public func take(s: string) => ()\n", "        let e: E = .C(\"a\")\n        match e@move\n            .C(let s) => H.take(s@move)\n            _ => ()\n", false, Failed)]
    public void AnEffectOverRejectedStorageIsItsRepairs(string declarations, string body, bool noisy, string expected)
    {
        var bad = Rejectable + (noisy ? "    drop => Console.writeLine(\"bad\")\n" : string.Empty);
        foreach (var part in new[] { "Missing", "i32" })
        {
            var source = (Sink + Loud + bad + declarations + Witness + body + "        return .Ok(())\n").Replace("MISSING", part, StringComparison.Ordinal);
            Assert.Equal(Expected(expected), Problems(source));
        }
    }

    // SPEC 23.1.2 (DECISIONS R4-M139): a copy over storage Binding rejected inside a confined witness (Holder.reset's `U.d(self.item@ref)`
    // omits `k: Option<T> = .None`) is built, and A4 reads the copy's Rejected locals, as its repair: no builder record and no A4 problem,
    // as MirBuildTest.ACopyOverRejectedStorageIsBuilt builds dep, dep3 and dep2. Before, the copy's Rejected instance read as a limit: the
    // call's Unsupported_Kd record and A4's Unsupported_Kd at `Self is Reset`.
    [Fact]
    public void ACopyInAConfinedWitnessOverRejectedStorageIsItsRepairs()
    {
        foreach (var part in new[] { "Missing", "i32" })
        {
            var c = MirBuildTest.Compile(("contract Reset\n    func reset(self: uniq/Self)\n        effect confined\nstruct Bad\n    public var m: MISSING\n" +
                "struct Holder\n    Self is Reset\n    var item: Bad\n    public func reset(self: uniq/Self)\n        let n = U.d(self.item@ref)\n" +
                "group U\n    public func d<T>(x: ref/T, k: Option<T> = .None) -> i32 => 0\npublic func main() => ()\n").Replace("MISSING", part, StringComparison.Ordinal));
            var program = c.BuildMir();
            Assert.DoesNotContain(program.Records, static x => x.Subject.Module == 0);
            Assert.Empty(Problems(c, program));
        }
    }
}
