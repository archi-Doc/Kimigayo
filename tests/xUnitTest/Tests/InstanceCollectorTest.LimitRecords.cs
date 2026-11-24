// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

// IMPL 21.3.2, DIAGNOSTICS rule 6 (DECISIONS R5-M171, PLAN G153): a Type over a layout limit is one record per
// declaration that uses it, at the least of that declaration's uses, whatever the collection's order.
public sealed partial class InstanceCollectorTest
{
    private const string Separate =
        "func g1<T>() -> i32\n    let p: Option<T> = .None\n    return 1\nfunc g2<T>() -> i32\n    let q: Option<T> = .None\n    return 2\n";

    private const string Within = "func g<T>() -> i32\n    let a: Option<T> = .None\n    let b: Option<[2147483648 of u8]> = .None\n    return 1\n";

    // DECISIONS R5-M171: one record per declaration (two functions, a function and an anonymous function in it, a constructor and its
    // creation), at the declaration's least use whichever instance is admitted first (a call routed through a generic helper changes that
    // order), each subject a parsed node, also where Binding synthesizes a call (an index, an iteration, an object creation).
    [Theory]
    [InlineData("func a(x: [2147483648 of u8]) -> i32 => 1\nfunc b(y: [2147483648 of u8]) -> i32 => 2\n", "[2147483648 of u8]|[2147483648 of u8]")]
    [InlineData(
        Separate + "func r1() -> i32 => g2<[2147483648 of u8]>()\nfunc r2() -> i32 => g1<[2147483648 of u8]>()\n",
        "let p: Option<T> = .None|let q: Option<T> = .None")]
    [InlineData(
        Separate + "func w<U>() -> i32 => g2<[2147483648 of u8]>()\nfunc r1() -> i32 => w<i32>()\nfunc r2() -> i32 => g1<[2147483648 of u8]>()\n",
        "let p: Option<T> = .None|let q: Option<T> = .None")]
    [InlineData(Within + "func r1() -> i32 => g<i32>()\nfunc r2() -> i32 => g<[2147483648 of u8]>()\n", "let a: Option<T> = .None")]
    [InlineData(
        Within + "func w<U>() -> i32 => g<i32>()\nfunc r1() -> i32 => w<i32>()\nfunc r2() -> i32 => g<[2147483648 of u8]>()\n", "let a: Option<T> = .None")]
    [InlineData(
        "func host(big: [2147483648 of u8]) -> i32\n    let f = func (x: [2147483648 of u8]) -> i32 => 1\n    return 1\n",
        "[2147483648 of u8]|[2147483648 of u8]")]
    [InlineData("func f(values: ref/Array<(i32, [2147483648 of u8])>) -> i32 => values[0].0\n", "values[0].0")]
    [InlineData(
        "struct Holder<T>\n    public var v: Option<T>\n    public init() => self.v = .None\n" +
        "func f() -> i32\n    let o = Holder<[2147483648 of u8]>.init()@obj\n    return 1\n",
        "self.v|Holder<[2147483648 of u8]>.init()@obj")]
    [InlineData(BigStruct + "func f(values: ref/Array<Big>) -> i32\n    for v in values\n        _ = v.n\n    return 1\n", "for v in values")]
    public void ALayoutLimitIsARecordPerDeclarationAtItsLeastUse(string source, string subjects)
    {
        var c = MirBuildTest.Compile(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Equal(subjects, string.Join("|", c.BuildMir().Records(MirRecordClass.Collection).Select(x => c.SyntaxOf(x.Subject).ToString().Split('\n')[0])));
        Assert.All(c.BuildMir().Records(MirRecordClass.Collection), x => Assert.Equal(DiagnosticCode.GenerationResourceLimit_Kd, x.Code));
        Assert.All(c.BuildMir().Records(MirRecordClass.Collection), x => Assert.True(c.SyntaxOf(x.Subject) is var node && node.SyntaxId <= node.SyntaxOwner!.ParsedCount, "Synthesized."));
    }

    // DECISIONS R5-M171: a type-driven key whose Type passes the height cap is a limit of its erased Type, not a refused key: one record per
    // declaration at its least request (two functions, two records; two locals of one function, one). Known limit: a shared type-driven
    // item keeps its first admitter's record (Option<S>'s glue, reaching an Array of a Type over the size limit, requested from d and e:
    // one record, at d; at e once d is gone). Subjects up to their first colon.
    [Theory]
    [InlineData("twoFunctions", "let v|let v")]
    [InlineData("oneFunction", "let v")]
    [InlineData("shared", "let o")]
    [InlineData("sharedAlone", "let p")]
    public void AHeightCapIsATypeLimitAndASharedItemKeepsItsFirstRecord(string shape, string subjects)
    {
        var deep = "Option<obj/" + string.Concat(Enumerable.Repeat("W<", 130)) + "i32" + new string('>', 130) + "> = .None\n";
        var nest = "struct W<T>\n    public var next: Option<obj/W<T>>\n";
        var shared = "struct S\n    public var a: Array<[2147483648 of u8]>\n";
        var e = "func e() -> i32\n    let p: Option<S> = .None\n    return 2\n";
        var c = MirBuildTest.Compile(shape switch
        {
            "twoFunctions" => nest + $"func f() -> ()\n    let v: {deep}    return\nfunc g() -> ()\n    let v: {deep}    return\n",
            "oneFunction" => nest + $"func f() -> ()\n    let v: {deep}    let u: {deep}    return\n",
            "shared" => shared + "func d() -> i32\n    let o: Option<S> = .None\n    return 1\n" + e,
            _ => shared + e,
        });
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Equal(subjects, string.Join("|", c.BuildMir().Records(MirRecordClass.Collection).Select(x => c.SyntaxOf(x.Subject).ToString().Split(':')[0])));
        Assert.All(c.BuildMir().Records(MirRecordClass.Collection), x => Assert.Equal(DiagnosticCode.GenerationResourceLimit_Kd, x.Code));
    }

    // PLAN G651 U2b R8 Rev 2, R11 OP1 Rev 2 (IMPL A.4, 21.3.2; DECISIONS R5-M171): A5's read of a function's own contract after the run
    // forms the referent's storage at the input's written Type, a node site there, so a refused instance there is one TypeHeightLimit_Kd
    // per declaration at its parameter: inlg (p's Outer<..> stores D12's Item past H), ictoru (Heir's base part), hfv41 (L41<i32>'s layers,
    // L0 conforming to nothing: no record in the run, ruling 15's C-R2-1); a second declaration reading it has its own record at its own
    // parameter in either order (q), a caller's call of the function its own at its parameter, never inside the callee (m). Records in
    // report order as code@declaration:text. Fail-before (base f227e33ce): InternalInvariant_Kd at the project alone.
    [Theory]
    [InlineData("inlg", "", "", "p:ref/Outer<(D12<i32>.Item, i32)>")]
    [InlineData("inlg", "", "func q(y: ref/Outer<(D12<i32>.Item, i32)>) => ()\n", "p:ref/Outer<(D12<i32>.Item, i32)> q:ref/Outer<(D12<i32>.Item, i32)>")]
    [InlineData("inlg", "func q(y: ref/Outer<(D12<i32>.Item, i32)>) => ()\n", "", "q:ref/Outer<(D12<i32>.Item, i32)> p:ref/Outer<(D12<i32>.Item, i32)>")]
    [InlineData("ictoru", "", "", "use:ref/Heir")]
    [InlineData("ictoru", "", "func m(r: ref/Heir) => use(r)\n", "use:ref/Heir m:ref/Heir")]
    [InlineData("ictoru", "func m(r: ref/Heir) => use(r)\n", "", "m:ref/Heir use:ref/Heir")]
    [InlineData("hfv41", "", "", "t:ref/L41<i32>")]
    public void AnOwnContractsReadOfARefusedInstanceIsARecordAtItsParameter(string shape, string before, string after, string records)
    {
        var source = shape switch
        {
            "inlg" => TypeHeightLimitTest.Doubling(12) + "struct Box<T>\n    var v: D12<T>.Item\nstruct Outer<T>\n    var b: Box<T>\n" +
                before + "func p(x: ref/Outer<(D12<i32>.Item, i32)>) => ()\n" + after,
            "ictoru" => TypeHeightLimitTest.Doubling(12) + "open struct Base<T>\n    var v: D12<T>.Item\nstruct Heir : Base<(D12<i32>.Item, i32)>\n" +
                before + "func use(h: ref/Heir) => ()\n" + after,
            _ => Layers41 + "func t(v: ref/L41<i32>) => ()\n",
        };
        Assert.Equal(records, HeightRecords(source + "public func main() => ()\n"));
    }

    // PLAN G651 U2b ruling 18 (SPEC 8.11.4, 15.6.4, 22.4.1; IMPL A.4, 21.3.2): A4 judges a selected Callable binding's confined bound on its
    // callee's summary at the use (keep's bound on h<L41<i32>>, whose destruction walks L41<i32>'s storage past H) in the site of its
    // premise definition's body, a node site at the argument: one TypeHeightLimit_Kd there, in main or a member function (the body's own
    // evaluation's fallback merges into it: a fallback never beats a use) or in a module's top-level statements, beside A4's own depth
    // limit there and h's record at its parameter; never InternalInvariant_Kd (fail-before: the obligation's read had no site,
    // InternalInvariant_Kd at the project and main's fallback). A Container's body sees no document-local function (SPEC 6.1.1), so a
    // member's struct, h and keep lie in one group.
    // Known limit (rulings 21 and 28, PLAN's open issue on the Callable use's body): the use's site is keyed by its premise definition's
    // body (MirProgram.Callee), not by the body holding the use. A use inside a Closure is keyed by main's body, so the Closure body's own
    // read keeps its record at the literal beside the use's; a computed getter's body is keyed by its accessor function (SPEC 11.3), which
    // the row's definition does not name, so the getter's own read keeps its record at the getter beside the use's (a #Test body likewise:
    // the lookup does not answer its kind). The row changes when Binding publishes the use's body. It also guards the after-run throw (U2b
    // R13.1): no throw and no InternalInvariant_Kd for a Closure or getter use.
    [Theory]
    [InlineData("public func main() -> ()\n    keep(h<L41<i32>>)\n    return\n", "h:T OwnershipDepthLimit_Kd@keep:h<L41<i32>> keep:h<L41<i32>>")]
    [InlineData("keep(h<L41<i32>>)\n", "h:T OwnershipDepthLimit_Kd@keep:h<L41<i32>> keep:h<L41<i32>>")]
    [InlineData(
        "public func main() -> ()\n    let c = func () -> () => keep(h<L41<i32>>)\n    c()\n    return\n",
        "h:T let:func () -> () => keep(h<L41<i32>>) OwnershipDepthLimit_Kd@let:h<L41<i32>> let:h<L41<i32>>")]
    [InlineData(
        "struct S\n    public var raw: i32\n    public func k(self: ref/Self) -> i32\n        keep(h<L41<i32>>)\n        return self.raw\n",
        "h:T OwnershipDepthLimit_Kd@keep:h<L41<i32>> keep:h<L41<i32>>")]
    [InlineData(
        "struct S\n    public var raw: i32\n    public computed k: i32\n        get() -> i32\n            keep(h<L41<i32>>)\n            return self.raw\n",
        "h:T get:get() -> i32\n                keep(h<L41<i32>>)\n                return self.raw OwnershipDepthLimit_Kd@keep:h<L41<i32>> keep:h<L41<i32>>")]
    public void ACallableBindingsObligationReadsAtItsUse(string use, string records)
    {
        var source = "func h<T>(x: T)\n    return\nfunc keep<F>(f: F)\n    F is Callable<owner, (L41<i32>) -> ()>\n        effect confined\n    return\n" + use;
        if (use.StartsWith("struct ", StringComparison.Ordinal))
        {
            source = "group G\n" + string.Concat(source.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(static x => $"    {x}\n")) + "public func main() -> ()\n    return\n";
        }

        Assert.Equal(records, HeightRecords(Layers41 + source));
    }

    // hfv41-u's layers (the Front's C8 probes): L(k)<X> : L(k-1)<W^200<X>> from an L0 conforming to nothing, so L41<i32> is formed within H
    // in the run and its storage walk passes H after it.
    private static string Layers41 => "struct W<T>\n    var v: T\nopen struct L0<X>\n    var v: X\n    public virtual func f(self: objref/Self) -> i32 => 1\n" +
        string.Concat(Enumerable.Range(1, 41).Select(static i => $"open struct L{i}<X> : L{i - 1}<{Nest("W", 200, "X")}>\n"));

    // A program's records in report order, each owner:text, the owner the declaration on its line (else the line's first word; - without
    // a location), a code other than TypeHeightLimit_Kd before it.
    private static string HeightRecords(string source)
    {
        var c = MinimalEmissionTest.Check(source);
        return string.Join(' ', TestDiagnostics.Of(c).Select(x =>
        {
            var line = source[(source.LastIndexOf('\n', Math.Max(0, x.Span.Start - 1)) + 1)..].Split('\n')[0].Trim();
            var owner = x.Text is null ? "-" : line.StartsWith("func ", StringComparison.Ordinal) ? line[5..line.IndexOfAny(['<', '('])] : line[..line.IndexOfAny([' ', '('])];
            return (x.Code == nameof(DiagnosticCode.TypeHeightLimit_Kd) ? string.Empty : x.Code + "@") + owner + ":" + x.Text;
        }));
    }
}
