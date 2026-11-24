// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 7.1, 13.5.5, 13.7.2: a function lends existing storage through the reference it returns. The call yields that
/// reference as a value; `@follow` selects its referent without Copy or Move, which offers Read and, through uniq/T, Write but
/// never Take. A compound update through `E@follow` evaluates `E` once, after its right-hand side, and reads and writes the one
/// referent Place that evaluation designates.</summary>
public class DereferenceUpdateTest
{
    private const string Source =
        "func pick(r: uniq/i32, c: uniq/i32) -> uniq/i32 during r\n    c@follow += 1\n    return r@move\n" +
        "var x: i32 = 1\nvar count: i32 = 0\npick(x@uniq, count@uniq)@follow += 10\n" +
        "require x == 11 and count == 1 else => $abort(\"once\")\nlet target = x@uniq\ntarget@follow *= 2\ntarget@follow++\n" +
        "require x == 23 else => $abort(\"local\")\nConsole.writeLine(\"ok\")";

    private const string First = "func first<T>(values: ref/Array<T>) -> ref/T during values\n    return values[0]@ref\n";

    private const string Slot = "func slot(values: uniq/Array<i32>) -> uniq/i32 during values\n    return values[0]@uniq\n";

    private const string Cell =
        "struct Cell\n    public var count: i32\n    var name: string\n    public init(count: i32, name: string)\n        self.count = count\n        self.name = name@move\n" +
        "    public func counter(self: uniq/Self) -> uniq/i32 during self => self.count@uniq\n" +
        "    public func label(self) -> ref/string during self => self.name@ref\n";

    // A Copy referent is read by @follow or by a value read at a fixed read Type; the reference itself is kept or passed as is.
    private const string Read = First +
        "let values: Array<i32> = [42, 7]\nlet copied = first(values)@follow\nlet read: i32 = first(values)\nlet explicit = first(values)@follow@copy\n" +
        "require copied == 42 and read == 42 and explicit == 42 else => $abort(\"copy\")\nlet names: Array<string> = [\"first\", \"second\"]\nConsole.writeLine(first(names))\n" +
        "let view = first(names)@follow@ref\nConsole.writeLine(view)\nlet kept = first(names)\nConsole.writeLine(kept)\n_ = first(names)";

    private const string Write = Slot +
        "var values: Array<i32> = [1, 2]\nslot(values@uniq)@follow = 41\nslot(values@uniq)@follow += 1\nrequire values[0] == 42 and slot(values@uniq) == 42 else => $abort(\"write\")\n" +
        "let exclusive = slot(values@uniq)@follow@uniq\nexclusive@follow = 43\nrequire values[0] == 43 else => $abort(\"borrow\")\nConsole.writeLine(\"ok\")";

    private const string Receivers = Cell + First +
        "func make() -> Array<string> => [\"made\"]\nvar cell = Cell.init(1, \"cell\")\ncell.counter()@follow = 5\ncell.counter()@follow += 1\n" +
        "Console.writeLine(cell.label())\nrequire cell.counter() == 6 else => $abort(\"counter\")\nConsole.writeLine(first(make()))";

    private const string Forwarded = First + Slot +
        "func second<T>(values: ref/Array<T>) -> ref/T during values\n    return first(values)\nfunc view<T>(values: ref/Array<T>) -> ref/T during values => first(values)@follow@ref\n" +
        "func exclusive(values: uniq/Array<i32>) -> uniq/i32 during values => slot(values)@follow@uniq\nlet names: Array<string> = [\"only\"]\nConsole.writeLine(second(names))\n" +
        "Console.writeLine(view(names))\nvar values: Array<i32> = [1]\nexclusive(values@uniq)@follow = 42\nrequire values[0] == 42 else => $abort(\"forward\")";

    // SPEC 3.4.3, 7.7.3: member access, indexing, method receivers and calls of a Function value select through the reference.
    private const string Projections = Cell +
        "func pairOf(values: ref/Array<(i32, string)>) -> ref/(i32, string) during values => values[0]@ref\n" +
        "func rows(values: ref/Array<Array<i32>>) -> ref/Array<i32> during values => values[0]@ref\nfunc cellOf(values: uniq/Array<Cell>) -> uniq/Cell during values => values[0]@uniq\n" +
        "func inc(v: i32) -> i32 => v + 1\nfunc firstFn(values: ref/Array<(i32) -> i32>) -> ref/((i32) -> i32) during values => values[0]@ref\n" +
        "let pairs: Array<(i32, string)> = [(1, \"p\")]\nrequire pairOf(pairs).0 == 1 else => $abort(\"tuple\")\nConsole.writeLine(pairOf(pairs).1)\nlet grid: Array<Array<i32>> = [[5, 6]]\n" +
        "require rows(grid)[1] == 6 else => $abort(\"index\")\nvar cells: Array<Cell> = [Cell.init(2, \"c\")]\ncellOf(cells@uniq).count += 1\ncellOf(cells@uniq).counter()@follow += 1\n" +
        "require cells[0].count == 4 else => $abort(\"field\")\nlet f: (i32) -> i32 = inc\nvar fs: Array<(i32) -> i32> = []\nfs.append(f@move)\nrequire firstFn(fs@ref)(5) == 6 else => $abort(\"callee\")";

    // SPEC 15.1.6: a followed referent is a bare Place Subject (Shared); the returned uniq/T value itself is an Exclusive Subject.
    private const string Subjects =
        "func pairOf(values: ref/Array<(i32, string)>) -> ref/(i32, string) during values => values[0]@ref\nfunc bins(values: uniq/Array<Array<i32>>) -> uniq/Array<i32> during values => values[0]@uniq\n" +
        "let pairs: Array<(i32, string)> = [(1, \"p\")]\nmatch pairOf(pairs)@follow\n    (let n, let s) => Console.writeLine(s)\nvar grid: Array<Array<i32>> = [[1, 2]]\nfor x in bins(grid@uniq)@follow\n" +
        "    Console.writeLine(\"\\(x)\")\nfor x in bins(grid@uniq)\n    x@follow += 1\nlet row = bins(grid@uniq)@follow@ref\nrequire row[0] == 2 and row[1] == 3 else => $abort(\"for\")";

    // Every callable path keeps the reference result: a Function Item, a concrete Closure, a Callable Constraint and an erased Function.
    private const string Callables = First + Slot +
        "func read<F>(f: ref/F, a: ref/Array<i32>) -> i32\n    F is Callable<(ref/Array<i32>) -> ref/i32>\n    return f(a)@follow\n" +
        "func write<F>(f: ref/F, a: uniq/Array<i32>)\n    F is Callable<(uniq/Array<i32>) -> uniq/i32>\n    f(a)@follow = 42\nlet item = first<i32>\nlet closure = func (a: ref/Array<i32>) -> ref/i32 => a[0]@ref\n" +
        "let erased: (ref/Array<i32>) -> ref/i32 = first\nlet a: Array<i32> = [42]\nrequire item(a) == 42 and closure(a) == 42 and erased(a) == 42 and read(first<i32>, a) == 42 else => $abort(\"read\")\n" +
        "let branch = func (v: ref/Array<i32>, choose: bool) -> ref/i32\n    if choose\n        return v[0]@ref\n    return v[0]@ref\nrequire branch(a, true) + branch(a, false) == 84 else => $abort(\"branch\")\n" +
        "var b: Array<i32> = [1]\nwrite(slot, b@uniq)\nrequire b[0] == 42 else => $abort(\"write\")\nConsole.writeLine(\"ok\")";

    private const string ZeroSize = First +
        "let f: (ref/Array<()>) -> ref/() = first\nlet a: Array<()> = [()]\nlet view = f(a)@follow@ref\nrequire view@follow == () else => $abort(\"unit\")\n" +
        "let g: (uniq/Array<()>) -> uniq/() = func (v) -> uniq/() => v[0]@uniq\nvar b: Array<()> = [()]\ng(b@uniq)@follow = ()\nlet r = g(b@uniq)@follow@uniq\nr@follow = ()\nConsole.writeLine(\"ok\")";

    private const string LengthItem =
        "func first<length N, T>(values: ref/[N of T]) -> ref/T during values => values[0]@ref\nlet f = first<2, i32>\nlet g: (ref/[2 of i32]) -> ref/i32 = first\n" +
        "let a: [2 of i32] = [21, 7]\nrequire f(a) + g(a) == 42 else => $abort(\"length\")\nConsole.writeLine(\"ok\")";

    private const string ErasedProjection =
        "let f: (uniq/Array<(i32, i32)>) -> uniq/(i32, i32) = func (v) -> uniq/(i32, i32) => v[0]@uniq\nvar a: Array<(i32, i32)> = [(1, 7)]\nf(a@uniq)@follow.0 = 40\n" +
        "f(a@uniq).0 += 2\nrequire a[0].0 == 42 else => $abort(\"projection\")\nConsole.writeLine(\"ok\")";

    private const string NestedReference = First +
        "func run(n: ref/i32 during a) -> i32\n    let values: Array<ref/i32 during a> = [n]\n" +
        "    let f: (ref/Array<ref/i32 during a>) -> ref/(ref/i32 during a) = func (v) -> ref/(ref/i32 during a) => v[0]@ref\n    let item = first<ref/i32 during a>\n" +
        "    let held = f(values)@follow@ref\n    let read = f(values)@follow\n    return held@follow@follow + read@follow + item(values)@follow@follow - n@follow\n" +
        "func same(n: ref/i32) -> ref/i32 => n\nfunc readVia<T, F>(f: ref/F, value: ref/T) -> T\n    T is Copy\n    F is Callable<(ref/T) -> ref/T>\n    F is Callable<(ref/i32) -> ref/i32>\n" +
        "    return f(value)@follow\nlet n = 21\nrequire run(n@ref) == 42 and readVia(same, n@ref) == 21 else => $abort(\"nested\")\nConsole.writeLine(\"ok\")";

    // SPEC 13.7.2: the right-hand side is secured before the call that locates the target.
    private const string UpdateOrder =
        "func slot(a: uniq/Array<i32>) -> uniq/i32 during a\n    Console.writeLine(\"call\")\n    return a[0]@uniq\nfunc rhs() -> i32\n    Console.writeLine(\"rhs\")\n    return 2\n" +
        "let f: (uniq/Array<i32>) -> uniq/i32 = slot\nvar a: Array<i32> = [40]\nf(a@uniq)@follow += rhs()\nrequire a[0] == 42 else => $abort(\"once\")";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "Read", Read, "first\nfirst\nfirst\n" },
        { "Write", Write, "ok\n" },
        { "Receivers", Receivers, "cell\nmade\n" },
        { "Forwarded", Forwarded, "only\nonly\n" },
        { "Projections", Projections, "p\n" },
        { "Subjects", Subjects, "p\n1\n2\n" },
        { "Callables", Callables, "ok\n" },
        { "ZeroSize", ZeroSize, "ok\n" },
        { "LengthItem", LengthItem, "ok\n" },
        { "ErasedProjection", ErasedProjection, "ok\n" },
        { "NestedReference", NestedReference, "ok\n" },
        { "UpdateOrder", UpdateOrder, "rhs\ncall\n" },
    };

    [Fact]
    public void TheReferenceIsEvaluatedOnce()
        => ScalarEmissionTest.EmitFixture("DereferenceUpdateOnce", Source, "ok\n");

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ReturnedReferencesAreFollowed(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("ReturnedReference" + name, source, stdout);

    // A followed referent keeps the capability of the returned reference and never offers Take; an assignment target is never
    // redirected to the referent (SPEC 3.5.3).
    [Theory]
    [InlineData(First + "let names: Array<string> = [\"a\"]\nlet taken = first(names)@follow@move", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Slot + "var values: Array<i32> = [1]\nlet taken = slot(values@uniq)@follow@move", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData(First + "var names: Array<string> = [\"a\"]\nfirst(names)@follow = \"b\"", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(First + "var names: Array<string> = [\"a\"]\nlet exclusive = first(names)@follow@uniq", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Slot + "var values: Array<i32> = [1]\nlet n: i32 = 2\nslot(values@uniq) = n", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData(First + "let names: Array<string> = [\"a\"]\nlet bare = first(names)@follow", DiagnosticCode.TransferRequired_Kd)]
    public void RejectsInvalidReferentUses(string source, DiagnosticCode code)
    {
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Contains(errors, x => x.Code == code.ToString());
        Assert.DoesNotContain(errors, x => x.Category == DiagnosticCategory.Unsupported);
    }

    // SPEC 10.2, 10.3: a result-only Type argument of a returned reference is never derived backward from a value read; an explicit
    // argument or an expected reference Type fixes it.
    [Theory]
    [InlineData("let a = only<i32>()", true)]
    [InlineData("let a: ref/i32 = only()", true)]
    [InlineData("let a: i32 = only()", false)]
    public void AResultOnlyTypeArgumentNeedsExplicitOrReferenceEvidence(string use, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("func only<T>() -> ref/T during static => $abort(\"never\")\n" + use);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, !c.Binding.Issues.Any(x => x.Code == DiagnosticCode.NoApplicableOverload_Kd));
    }

    // SPEC 15.6.3: a Reborrow of the referent keeps the input Loan of the call that returned the reference.
    [Theory]
    [InlineData("let f = slot")]
    [InlineData("let f: (uniq/Array<i32>) -> uniq/i32 = slot")]
    [InlineData("let f = func (a: uniq/Array<i32>) -> uniq/i32 => a[0]@uniq")]
    public void AReborrowKeepsTheInputLoan(string declaration)
    {
        var source = Slot + declaration + "\nvar a: Array<i32> = [1]\nlet view = f(a@uniq)@follow@ref\nf(a@uniq)@follow = 42\nConsole.writeLine(\"\\(view@follow)\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict());
        Assert.DoesNotContain(c.Problems, x => x.Code == DiagnosticCode.Unsupported_Kd);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Theory]
    [Trait("Purpose", "Allocation")]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmReferentUpdatesReuseAllPhases(bool erased)
    {
        var declaration = erased ? "let f: (uniq/Array<i32>) -> uniq/i32 = func (a) -> uniq/i32 => a[0]@uniq" : "let f = slot";
        var c = MinimalEmissionTest.Analyze(Slot + declaration + "\nvar a: Array<i32> = [1]\nf(a@uniq)@follow += 2");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }
}
