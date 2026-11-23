// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>Default templates and their copies in the MIR build (plan 5.6.2, SPEC 7.2.3): template calls, Inlined scopes, the expansion limit.</summary>
public sealed partial class MirBuildTest
{
    // Generic defaults a call's substitution may not instantiate: an Option of T, a Tuple holding one and a call of h<T>; a Box to nest.
    private const string DefaultsOver = "func f<T>(x: ref/T, k: Option<T> = .None) => ()\nfunc f3<T>(x: ref/T, k: (i32, Option<T>) = (0, .None)) => ()\n" +
        "func h<T>() -> i32 => 0\nfunc f2<T>(x: ref/T, k: i32 = h<T>()) => ()\nstruct Bx<T>\n    public var v: T\n";

    // Callers of DefaultsOver (ACopyOverRejectedStorageIsBuilt, ACopyOverAnInstancePastBindingsBoundIsBuilt, MirBorrowsTest's
    // AnUninstantiableDefaultHidesNoRecord), each
    // holding a Loan across its omitting call that `v = 2` meets, over storage Binding rejected (Bad's Field) and its valid twin (Good).
    internal static string RejectedCopies => DefaultsOver + "struct Bad\n    public var m: Missing\nstruct Good\n    public var m: i32\n" +
        Caller("dep", "Bad", "f") + Caller("dep3", "Bad", "f3") + Caller("dep2", "Bad", "f2") + Caller("val", "Good", "f") + Caller("val2", "Good", "f2");

    // The same in a complete program over a Box nested 65 times, 63 times (under the Option, past the storage depth) and 30 times.
    internal static string LimitCopies => DefaultsOver + Caller("lim", Nested("Bx", 65), "f") + Caller("lim64", Nested("Bx", 63), "f") +
        Caller("n30", Nested("Bx", 30), "f");

    // Plan 5.6.2: a default whose template reaches itself through the defaults its calls omit, alone (tests/mir/defaults) or with another
    // (a component of the omission graph), is a call of its template wherever it is omitted, never a copy. A default Binding rejected fails
    // its template, and a call that omits it takes an Opaque value at the call, reading the earlier slots, with no record of its own.
    [Fact]
    public void RecursiveDefaultsAreTemplateCallsAndFailedOnesOpaque()
    {
        var c = Compile("func f(n: i32, value: i32 = if n == 0 => 1 else => g(n - 1)) -> i32 => value\n" +
            "func g(n: i32, value: i32 = if n == 0 => 2 else => f(n - 1)) -> i32 => value\nfunc h(x: i32, y: i32 = missing) -> i32 => y\n" +
            "let a = f(2)\nlet b = h(1)\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records(MirRecordClass.Construction));
        var bodies = program.Bodies.ToArray();
        Assert.Equal(["g#1"], Templates(bodies.Single(static x => x.Key is { Kind: MirBodyKind.DefaultTemplate, Decl.Name: "f" })));
        Assert.Equal(["f#1"], Templates(bodies.Single(static x => x.Key is { Kind: MirBodyKind.DefaultTemplate, Decl.Name: "g" })));
        Assert.True(bodies.Single(static x => x.Key is { Kind: MirBodyKind.DefaultTemplate, Decl.Name: "h" }).HasOpaque);
        var top = bodies.Single(static x => x.Key.Decl is null && x.Source.Module == 0);
        Assert.Equal(["f#1"], Templates(top));
        Assert.DoesNotContain(top.Scopes.ToArray(), static x => x.Kind == MirScopeKind.Inlined);
        var failed = Assert.Single(top.OpaqueSites.ToArray());
        Assert.Equal("h(1)", c.SyntaxOf(failed.Source).ToString());
        Assert.Equal(MirLocalRole.PendingSlot, top.Locals[Assert.Single(top.OpaqueUses.Slice(failed.Start, failed.Count).ToArray()).Root].Role);

        static string[] Templates(MirBody body) => body.Blocks.ToArray().Where(static x => x.Terminator is { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Fn, Arg: > 0 } })
            .Select(static x => $"{x.Terminator.Callee.Symbol!.Name}#{x.Terminator.Callee.Arg - 1}").ToArray();
    }

    // SPEC 8.15.3, plan 5.6.2 (PLAN G597): a default's keyed view is decided again in each copy, by its key in the call's instance
    // (Refirst; MirBuilder.First): at a key that starts at the storage the Storage alone; at one that displaces the Storage in a temp of
    // the copy, at the view's location and live for its two statements, and the proven Add; at a generic caller's own key the Storage
    // keyed by that key; the receiver is the copy's parameter, a borrow of the argument (Frame), so its Place has one more Deref. None
    // is a validator finding (fail-before: the copy kept its template's statement, a keyed Storage whose closed token the validator
    // rejects).
    [Fact]
    public void ACopysKeyedViewIsDecidedByItsFoldedKey()
    {
        var program = Compile(
            "func h<R>(v: ref/Array<i32>, r: R, n: isize = v[r].length) -> isize\n    R is PositionRange\n    return n\n" +
            "func whole(v: ref/Array<i32>) -> isize => h(v, ..)\nfunc tail(v: ref/Array<i32>) -> isize => h(v, 1..)\n" +
            "func forward<Q>(v: ref/Array<i32>, q: Q) -> isize\n    Q is PositionRange\n    return h(v, q)\n").BuildMir();
        Assert.Empty(program.Findings);
        Assert.Equal("_ = storage((*(*_)))", Views(program, "whole"));
        Assert.Equal("_ = storage((*(*_)))|_ = Add(copy _, copy _) proven", Views(program, "tail"));
        Assert.Equal("_ = storage((*(*_)), copy _, const type Q)", Views(program, "forward"));
        var tail = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "tail" });
        var statements = tail.Statements.ToArray();
        var at = Array.FindIndex(statements, static x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Storage });
        var (view, temp) = (statements[at], tail.Locals[statements[at].Place.Root]);
        Assert.Equal((MirLocalKind.Temp, view.Source.Syntax, view.Source.Scope), (temp.Kind, temp.Source.Syntax, temp.Source.Scope));
        Assert.Equal(MirScopeKind.Inlined, tail.Scopes[view.Source.Scope].Kind);
        Assert.Equal(
            [
                (MirStatementKind.StorageLive, view.Place),
                (MirStatementKind.Assign, view.Place),
                (MirStatementKind.Assign, statements[at + 1].Place),
                (MirStatementKind.StorageDead, view.Place),
            ],
            statements[(at - 1)..(at + 3)].Select(static x => (x.Kind, x.Place)));
    }

    // Plan 5.6.2, SPEC 23.1.2 (DECISIONS R5-M79): the defaults a template omits are those of the calls it builds. A closure Binding failed
    // to define, here after binding its body, is published Error and its body replaced (Syntax), so the call in that body is no edge: the
    // default is no call of its own template, and the call that omits it takes an Opaque value built at the call.
    [Fact]
    public void AFailedClosureInADefaultIsNoEdge()
    {
        var c = Compile("func id<T>(x: T) -> i32 => 1\nfunc run(n: i32, k: i32 = id(func (x: Missing) => run(1))) -> i32 => n + k\n" +
            "require run(3) == 4 else => $abort(\"x\")\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var top = program.Bodies.ToArray().Single(static x => x.Key.Decl is null && x.Source.Module == 0);
        Assert.DoesNotContain(top.Blocks.ToArray(), static x => x.Terminator is { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Fn, Arg: > 0 } });
        Assert.True(top.HasOpaque);
    }

    // Plan 5.6.2, SPEC 7.2.3 (DECISIONS R4-M90 D6): a template's prepared input p_i is located at its parameter, not at the function, so a
    // fact about the slot (A5's Anchor) names the parameter. A parameter has no syntax node of its own, so that is its Type node: the
    // written annotation, or the `self` shorthand's inferred Type on its name. The receiver, a parameter and a later default's own.
    [Fact]
    public void ATemplatesPreparedInputsAreLocatedAtTheirParameters()
    {
        var c = Compile("struct Box\n    var size: i32\n    public init(size: i32) => self.size = size\n" +
            "    public func grow(self, by: i32 = self.size, more: i32 = by) -> i32 => by + more\n" +
            "func scale(x: i32, y: i32 = x, z: i32 = x + y) -> i32 => z\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var inputs = 0;
        foreach (var template in program.Bodies.ToArray().Where(static x => x.Key.Kind == MirBodyKind.DefaultTemplate))
        {
            var function = (FunctionKoto)template.Key.Decl!.Declaration;
            for (var i = 1; i <= template.Key.Index; i++)
            {
                var local = template.Locals[i];
                Assert.Equal(MirLocalRole.PreparedInput, local.Role);
                Assert.Same(function.Parameters[i - 1].Type, Syntax(template, local.Source));
                Assert.Equal((0, false), (local.Source.Scope, local.Source.StatementStart));
                inputs++;
            }

            Assert.Same(function, Syntax(template, template.Locals[0].Source)); // The result stays the function's.
        }

        Assert.Equal(6, inputs); // grow: by (self), more (self, by); scale: y (x), z (x, y).

        Koto Syntax(MirBody body, in SourceInfo source) => c.SyntaxOf(body.Subject(source));
    }

    // Plan 5.6.2, SPEC 7.2.3: each statement and terminator of a copy is its template's, folded: at its template's location, in the
    // copy's scopes under the one Inlined at the omitting call; a PairAcquire too, which the instance's case acquires again (R4-M66):
    // Copied (concrete), Reborrowed (exclusive, a default the analysis rejects: DefaultArgumentBorrow_Kd in its uniq case), by the
    // caller's binder (generic) and as the input of a call in the template (called). A template's own copy (outer's of pick) is copied
    // into its nested Inlined scope (nested, nestedGeneric). Only the slots and the prologue's borrows are the caller's.
    [Theory]
    [InlineData("concrete", "pick")]
    [InlineData("exclusive", "grab")]
    [InlineData("generic", "pick")]
    [InlineData("called", "via")]
    [InlineData("nested", "outer pick")]
    [InlineData("nestedGeneric", "outer pick")]
    public void ACopyLiesInItsInlinedScopeAtItsTemplatesLocations(string caller, string templates)
    {
        var program = Compile("func pick<s/T>(a: s/T, b: s/T = a) -> i32\n    s is owner or ref\n    T is Copy\n    return 0\n" +
            "func grab<s/T>(a: s/T, b: s/T = a) -> i32\n    s is ref or uniq\n    return 0\nfunc id<s/T>(x: s/T) -> s/T\n    s is owner or ref\n" +
            "    T is Copy\n    return x\nfunc via<s/T>(a: s/T, b: s/T = id(a)) -> i32\n    s is owner or ref\n    T is Copy\n    return 0\n" +
            "func outer<s/T>(a: s/T, c: i32 = pick(a)) -> i32\n    s is owner or ref\n    T is Copy\n    return c\n" +
            "func concrete(r: ref/i32) -> i32 => pick(r)\nfunc generic<s/T>(x: s/T) -> i32\n    s is owner or ref\n    T is Copy\n    return pick(x)\n" +
            "func exclusive(u: uniq/i32) -> i32 => grab(u)\nfunc called(r: ref/i32) -> i32 => via(r)\nfunc nested(r: ref/i32) -> i32 => outer(r)\n" +
            "func nestedGeneric<s/T>(x: s/T) -> i32\n    s is owner or ref\n    T is Copy\n    return outer(x)\n").BuildMir();
        Assert.Empty(program.Findings);
        var bodies = program.Bodies.ToArray();
        var body = bodies.Single(x => x.Key.Kind == MirBodyKind.Fn && x.Key.Decl?.Name == caller);
        foreach (var callee in templates.Split(' '))
        {
            // The body's first copy, of the template given, then the template's own copy of the next.
            var template = bodies.Single(x => x.Key.Kind == MirBodyKind.DefaultTemplate && x.Key.Decl?.Name == callee);
            var inlined = Enumerable.Range(0, body.Scopes.Length).First(x => body.Scopes[x].Kind == MirScopeKind.Inlined);
            Assert.Equal(Sources(template, 0), Sources(body, inlined));
            body = template;
        }

        // The statements' kinds and the statements' and terminators' syntax and statement starts under a root scope, each after the kinds
        // of the scopes from the root to its own.
        static string[] Sources(MirBody body, int root) => body.Statements.ToArray().Select(static x => (x.Source, Kind: x.Kind.ToString()))
            .Concat(body.Blocks.ToArray().Select(static x => (x.Terminator.Source, Kind: "end")))
            .Select(x => (Path: Path(body, x.Source.Scope, root), x.Source, x.Kind)).Where(static x => x.Path is not null)
            .Select(static x => $"{x.Path}{x.Kind} {x.Source.Syntax} {x.Source.StatementStart}").Order(StringComparer.Ordinal).ToArray();

        static string? Path(MirBody body, int scope, int root)
        {
            var path = string.Empty;
            for (; scope > root; scope = body.Scopes[scope].Outer)
            {
                path = $"{body.Scopes[scope].Kind}/{path}";
            }

            return scope == root ? path : null;
        }
    }

    // IMPL 21.3.2, plan 5.6.2: a copy that would take a body past 65,536 MIR statements is not made. Defaults that copy the one below
    // twice at each level report ExpansionLimit_Kd once, at the omitting call where the limit is met; that template fails, so the
    // calls that omit it take an Opaque value without another record: the record marks the template it lies in alone, not a body whose
    // Opaque value rests on it (MirBody.HasConstructionRecord; GR-1a, fail-before: none).
    [Fact]
    public void DefaultExpansionStopsAtTheLimit()
    {
        var text = new StringBuilder("func f0(x: i32 = 1) -> i32 => x\n");
        for (var i = 1; i <= 16; i++)
        {
            text.Append("func f").Append(i).Append("(x: i32 = f").Append(i - 1).Append("() + f").Append(i - 1).Append("()) -> i32 => x\n");
        }

        var c = Compile(text.Append("let value = f16()\n").ToString());
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var record = Assert.Single(program.Records(MirRecordClass.Construction));
        Assert.Equal(DiagnosticCode.ExpansionLimit_Kd, record.Code);
        Assert.Matches(@"^f\d+\(\)$", c.SyntaxOf(record.Subject).ToString());
        Assert.All(program.Bodies.ToArray(), static x => Assert.InRange(x.Statements.Length, 0, 65_536));
        Assert.True(program.Bodies.ToArray().Single(static x => x.Key.Decl is null && x.Source.Module == 0).HasOpaque);
        Assert.Equal(MirBodyKind.DefaultTemplate, Assert.Single(program.Bodies.ToArray(), static x => x.HasConstructionRecord).Key.Kind);
    }

    // DECISIONS R5-M183's GR-1a line: a builder record marks the ordinary function body it lies in alone (MirBody.HasConstructionRecord).
    // f's Unbuilt selection records one located Unsupported_Kd at the selection with its Opaque value, and f is marked; g, calling f, is
    // not. Before PLAN G396 (3)'s B3 the row was a call omitting a default past Binding's storage bound, now built; before R7-T1 c4 the
    // deleted ATupleOperatorWithoutItsComparisonIsUnsupported held it on the tuple operator without its comparison (fail-before: none).
    [Fact]
    public void AConstructionRecordMarksItsOwnBodyAlone()
    {
        var c = Compile(Unbuilt.Declarations + $"func f<T>(x: ref/T) -> i32\n    {Unbuilt.Premise}\n    return x.0.a\n" +
            "func g(y: ref/(Pair, i32)) -> i32 => f(y)\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var record = Assert.Single(program.Records(MirRecordClass.Construction), static x => x.Subject.Module == 0);
        Assert.Equal((DiagnosticCode.Unsupported_Kd, "x.0"), (record.Code, c.SyntaxOf(record.Subject).ToString()));
        Assert.True(program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "f" }).HasOpaque);
        Assert.Equal(["f"], program.Bodies.ToArray().Where(static x => x.HasConstructionRecord && x.Source.Module == 0).Select(static x => x.Key.Decl?.Name));
    }

    // SPEC 7.2.3, 23.1.1 (DECISIONS R4-M127, R7-M54 m2; PLAN G396 (3)): a copy is decided before it is written, by the instance's
    // formation, as the written call forms it: over an instance past Binding's storage bound it is built. lim and lim64 (f's T at Bx^65
    // and Bx^63, past the storage depth under the Option), a literal default (f4's null at Bx^65) and a closed Type there under a library
    // group member's call (take's probe<Array^65<i32>>) are Inlined copies with no construction record, as n30 is, lim's copy holding
    // `.None` of Option<Bx^65<i32>> (fail-before: an Opaque value at the call reading the earlier slots, with the call's Unsupported_Kd,
    // B3). The collector admits the library copy with no record.
    [Fact]
    public void ACopyOverAnInstancePastBindingsBoundIsBuilt()
    {
        var complete = Compile(LimitCopies);
        Assert.True(complete.Binding.Result.IsComplete);
        Assert.Equal(["lim: Inlined f", "lim64: Inlined f", "n30: Inlined f"], Callers(complete, "lim", "lim64", "n30"));
        var lim = complete.BuildMir().Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "lim" });
        Assert.Contains(lim.Locals.ToArray(), static x => x.Type is { } type && TyCtxt.Name(type) == $"Option<{Nested("Bx", 65)}>");
        var literal = Compile(DefaultsOver + "func f4<T>(x: ref/T, k: raw/T = null) => ()\n" + Caller("lnull", Nested("Bx", 65), "f4"));
        Assert.Equal(["lnull: Inlined f4"], Callers(literal, "lnull"));
        var api = "public group Api\n    public func probe<T>(value: T) -> i32 => 0\n" +
            $"    public func take(x: i32, extra: i32 = probe<{Nested("Array", 65)}>([])) -> i32 => x + extra\n";
        var library = ModuleBindingTest.Create("public func main() -> ()\n    _ = Lib.Api.take(1)\n    return\n", api);
        Assert.True(library.Bind().IsComplete);
        Assert.True(library.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(["main: Inlined probe take"], Callers(library, "main"));
        var program = new MirBuilder(library).Build(SyntaxTable.LibraryModule + 1); // Uncollected: the compilation's own collection roots both modules.
        new MirAnalysis(library.Types).Run(program, 1); // The collector replays stepped bodies (its input contract, PLAN G951).
        Assert.Equal(program.Version, new InstanceCollector(library.Types, library.Layout).Run(program, 1).Version);
        Assert.Empty(program.Records(MirRecordClass.Collection));
    }

    // SPEC 7.2.3 (DECISIONS R7-M54 m2; PLAN G396 (3)): a program omitting a default over an instance past Binding's storage bound has its
    // written twin's records, by code (Layout's GenerationResourceLimit_Kd stays at its operation inside the copy, DIAGNOSTICS' copy
    // rule): take<Bx^65> (accepted), one default at Bx^65, i32 and Bx^65 in one body and through a generic caller at Bx^65 and i32 (the
    // copy's instances and compositions per copy), a closed copy in a closure literal, g's Option<Bx^65> (Layout's limit), a conforming
    // Self (T.zero() at Bx^65) and a declaring Type (Bx<T>.size() at Bx^64) composed (fail-before: B3's Unsupported_Kd at each omitting
    // call; the twins are the reviewers' r-two, r-clo and the Front's f-self, f-decl).
    [Theory]
    [InlineData("take<B65>(a)", "take<B65>(a, zero<B65>())")]
    [InlineData(
        "take<B65>(a) + take<i32>(a) + take<B65>(a) + outer<B65>(a) + outer<i32>(a)",
        "take<B65>(a, zero<B65>()) + take<i32>(a, zero<i32>()) + take<B65>(a, zero<B65>()) + outer<B65>(a) + outer<i32>(a)")]
    [InlineData("call(func (x: i32) -> i32 => take<B65>(x), a)", "call(func (x: i32) -> i32 => take<B65>(x, zero<B65>()), a)")]
    [InlineData("g<B65>(a)", "g<B65>(a, .None)")]
    [InlineData("takeSelf<B65>(a)", "takeSelf<B65>(a, B65.zero())")]
    [InlineData("takeDecl<B64>(a)", "takeDecl<B64>(a, Bx<B64>.size())")]
    public void AnOmittedDefaultPastTheBoundHasItsWrittenCallsRecords(string omitted, string written)
    {
        Assert.Equal(Errors(written), Errors(omitted));
        Assert.DoesNotContain(nameof(DiagnosticCode.Unsupported_Kd), Errors(omitted));

        static string[] Errors(string expression)
        {
            var source = "contract Zero\n    func zero() -> i32\nstruct Bx<T>\n    public var v: T\n    Self is Zero\n    public func zero() -> i32 => 0\n" +
                "    public func size() -> i32 => 0\nfunc zero<T>() -> i32 => 0\nfunc take<T>(a: i32, k: i32 = zero<T>()) -> i32 => a + k\n" +
                "func g<T>(a: i32, k: Option<T> = .None) -> i32 => a\nfunc outer<T>(a: i32) -> i32 => take<Option<T>>(a)\n" +
                "func takeSelf<T>(a: i32, k: i32 = T.zero()) -> i32\n    T is Zero\n    return a + k\n" +
                "func takeDecl<T>(a: i32, k: i32 = Bx<T>.size()) -> i32 => a + k\nfunc call(f: (i32) -> i32, a: i32) -> i32 => f(a)\n" +
                $"func same(a: i32) -> i32 => {expression.Replace("B65", Nested("Bx", 65), StringComparison.Ordinal).Replace("B64", Nested("Bx", 64), StringComparison.Ordinal)}\n" +
                "public func main() -> ()\n    _ = same(1)\n    return\n";
            return [.. TestDiagnostics.Of(MinimalEmissionTest.Check(source)).Where(static x => x.Severity == DiagnosticSeverity.Error).Select(static x => x.Code).Order(StringComparer.Ordinal)];
        }
    }

    // SPEC 7.2.3 (PLAN G396 (3); the Middle's review C1, an open issue): a default whose template reads a preceding parameter over an
    // instance past Binding's storage bound is built, but its copy reads the parameter through the prologue's shared borrow of the pending
    // slot, so A5 meets retention's limit (CallRetention.Limit, read as Deep, R4-M153) with a Loan held: OwnershipDepthLimit_Kd at the
    // copy's operation (fr's peek(x)) and at the omitting call, where the written twin fr(b, peek(b)) holds no Loan and is accepted
    // (fail-before: B3's Unsupported_Kd at the call). The difference is the open issue's (A5 over a copy's prologue Loan).
    [Fact]
    public void ACopyReadingAPrecedingParameterPastTheBoundMeetsA5sLimit()
    {
        var head = $"struct Bx<T>\n    public var v: T\nfunc peek<T>(r: ref/T) -> i32 => 0\nfunc fr<T>(x: ref/T, k: i32 = peek(x)) -> i32 => k\n" +
            $"public func main() => ()\nfunc same(b: ref/{Nested("Bx", 65)}) -> i32 => ";
        Assert.Equal(
            ["OwnershipDepthLimit_Kd@peek(x)", "OwnershipDepthLimit_Kd@fr(b)"],
            TestDiagnostics.Of(MinimalEmissionTest.Check(head + "fr(b)\n")).Where(static x => x.Severity == DiagnosticSeverity.Error).Select(static x => $"{x.Code}@{x.Text}"));
        Assert.DoesNotContain(TestDiagnostics.Of(MinimalEmissionTest.Check(head + "fr(b, peek(b))\n")), static x => x.Severity == DiagnosticSeverity.Error);
    }

    // IMPL A.4, 21.3.2 (PLAN G396 (3), G651): the copy forms its temp Option<W^k<W^8000<i32>>> after Binding's run. Within IMPL A.4's
    // height (copy191) it is built with its written twin's record codes (Layout's limit); past it (copy192) the copy's formation
    // (TyCtxt.Answered) owns the refusal and the builder's read of it marks the omitting call's node site: exactly TypeHeightLimit_Kd
    // there, no Unsupported_Kd and no site-less record (PLAN G651 U2b, K1; the written twin gets it at none(p40) in Binding's frame).
    // In a generic caller (outer192) the copy is built open and the collector's formation at the
    // instance passing the height records TypeHeightLimit_Kd at the copy's operation, as at the written twin's expression (the Back's
    // review N3). The built rows compare distinct codes: the copy and its twin form their Types at different sites, so their least-use
    // records (DECISIONS R5-M171, one per instance and declaration) differ in count (copy191: two GenerationResourceLimit_Kd, its
    // twin three), not in class. Fail-before: B3's Unsupported_Kd at the omitting call for copy191 and outer192; copy192, the arm,
    // holds on both.
    [Theory]
    [InlineData("copy191", "takeh(p40)", "takeh(p40, count(none(p40)))", null)]
    [InlineData("copy192", "takeh(p40)", "takeh(p40, count(none(p40)))", "TypeHeightLimit_Kd@takeh(p40)")]
    [InlineData("outer192", "outer(p40)", "outerw(p40)", null)]
    public void ACopyPastTheHeightAfterBindingsRunIsTheOneArmLeft(string shape, string omitted, string written, string? records)
    {
        var k = int.Parse(shape.AsSpan(shape.Length - 3), System.Globalization.CultureInfo.InvariantCulture);
        var errors = Errors(Lifted(k, omitted));
        if (records is not null)
        {
            Assert.Equal(records.Split(' '), errors);
            return;
        }

        Assert.Equal(Codes(Errors(Lifted(k, written))), Codes(errors));
        Assert.DoesNotContain(nameof(DiagnosticCode.Unsupported_Kd), Codes(errors));

        static string[] Codes(string[] errors) => [.. errors.Select(static x => x[..x.IndexOf('@', StringComparison.Ordinal)]).Distinct().Order(StringComparer.Ordinal)];

        static string[] Errors(string source)
            => [.. TestDiagnostics.Of(MinimalEmissionTest.Check(source)).Where(static x => x.Severity == DiagnosticSeverity.Error).Select(static x => $"{x.Code}@{x.Text ?? "-"}")];

        // p40: raw/W^8000<i32> by 40 lifts of 200 levels, each within the height.
        static string Lifted(int k, string statement)
            => $"struct W<T>\n    public var v: T\nfunc lift<T>(p: raw/T) -> raw/{W(200)} => null\nfunc count<U>(u: U) -> i32 => 0\n" +
                $"func none<T>(p: raw/T) -> Option<{W(k)}> => .None\nfunc takeh<T>(p: raw/T, k: i32 = count(Option<{W(k)}>.None)) => ()\n" +
                (statement.StartsWith("outer", StringComparison.Ordinal) ? "func outer<U>(p: raw/U) => takeh(p)\nfunc outerw<U>(p: raw/U) => takeh(p, count(none(p)))\n" : string.Empty) +
                "public func main() -> ()\n    let p0: raw/i32 = null\n" +
                string.Concat(Enumerable.Range(1, 40).Select(static i => $"    let p{i} = lift(p{i - 1})\n")) + $"    {statement}\n    return\n";

        static string W(int n) => string.Concat(Enumerable.Repeat("W<", n)) + "T" + new string('>', n);
    }

    // IMPL A.4, 21.3.2, SPEC 12 (interpolation; PLAN G651 U2b, U2c ruling 7 N3; the Front's C-503 lint1, lint0): an owning interpolation as a
    // default (takes' s = "\(G<W^k<T>>.init(null))", fmt its written form) copied at takes(p40) forms the write's borrow temp
    // ref/G<W^k<W^8000<i32>>> after Binding's run. Past IMPL A.4's height (k = 191: 8,193) its formation owns the refusal and the builder's
    // read marks the omitting call's node site: exactly TypeHeightLimit_Kd there beside A4's own depth limits in takes' and fmt's
    // interpolations; at the height (k = 190) A4's depth limits alone. Never InternalInvariant_Kd or Unsupported_Kd (fail-before for k = 191:
    // both, as copy192 before U2b).
    [Theory]
    [InlineData(191, "TypeHeightLimit_Kd@takes(p40)")]
    [InlineData(190, null)]
    public void AnInterpolationCopyPastTheHeightIsARecordAtItsCall(int k, string? record)
    {
        var source = "struct W<T>\n    public var v: T\nstruct G<T>\n    Self is Utf8Format\n    public var q: raw/T\n    public init(q: raw/T) => self.q = q\n" +
            "    public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => writer.write(\"g\")\n" +
            $"func lift<T>(p: raw/T) -> raw/{Nest(200)} => null\nfunc takes<T>(p: raw/T, s: string = \"\\(G<{Nest(k)}>.init(null))\") => ()\n" +
            $"func fmt<T>(p: raw/T) -> string => \"\\(G<{Nest(k)}>.init(null))\"\n" + "public func main() -> ()\n    let p0: raw/i32 = null\n" + string.Concat(Enumerable.Range(1, 40).Select(static i => $"    let p{i} = lift(p{i - 1})\n")) +
            "    takes(p40)\n    return\n";
        var errors = TestDiagnostics.Of(MinimalEmissionTest.Check(source)).Where(static x => x.Severity == DiagnosticSeverity.Error).Select(static x => $"{x.Code}@{x.Text ?? "-"}");
        string[] expected = record is null ? [] : [record];
        Assert.Equal(
            expected,
            errors.Where(static x => !x.StartsWith(nameof(DiagnosticCode.OwnershipDepthLimit_Kd), StringComparison.Ordinal)));
        Assert.Contains(errors, static x => x.StartsWith(nameof(DiagnosticCode.OwnershipDepthLimit_Kd), StringComparison.Ordinal));

        static string Nest(int n) => string.Concat(Enumerable.Repeat("W<", n)) + "T" + new string('>', n);
    }

    // SPEC 23.1.1, 23.1.2 (DECISIONS R4-M139; PLAN G396 (3)): a copy over storage Binding rejected is built in the call's instance, as the
    // written call forms it (TyCtxt.Apply) and as a non-generic template over that storage is: dep, dep3 and dep2 copy like their twins
    // with no builder record (BU-38's x3, `d(b)`, is dep's form); dep's copy holds `.None` of Option<Bad>, and dep2's calls `h<Bad>`.
    // Before, the Rejected instance read as a limit: an Opaque value and the call's Unsupported_Kd. Such a copy in a confined witness is
    // MirEffectsTest's ACopyInAConfinedWitnessOverRejectedStorageIsItsRepairs.
    [Fact]
    public void ACopyOverRejectedStorageIsBuilt()
    {
        var c = Compile(RejectedCopies);
        Assert.Equal(
            ["dep: Inlined f", "dep3: Inlined f3", "dep2: Inlined h f2", "val: Inlined f", "val2: Inlined h f2"],
            Callers(c, "dep", "dep3", "dep2", "val", "val2"));
        var bodies = c.BuildMir().Bodies.ToArray();
        Assert.Contains(bodies.Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "dep" }).Locals.ToArray(), static x => TyCtxt.Name(x.Type!) == "Option<Bad>");
        var h = bodies.Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "dep2" }).Blocks.ToArray().Single(static x => x.Terminator.Callee.Symbol?.Name == "h");
        Assert.Equal("Bad", h.Terminator.Callee.Subst!.Types[0]!.Symbol!.Name);
    }

    // A warm rebuild of copies over storage Binding rejected allocates nothing: Instantiates' maps keep their storage.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebuildOfRejectedCopiesAllocatesNothing()
    {
        var c = Compile(RejectedCopies);
        _ = c.BuildMir();
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            c.BuildMir();
        }));
    }

    // Each caller's Inlined scopes and callees, then each Opaque site's syntax and the roles of the locals it reads; then the records.
    private static string[] Callers(Compilation c, params string[] names)
    {
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        return [.. names.Select(name =>
        {
            var body = program.Bodies.ToArray().Single(x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == name);
            var scopes = body.Scopes.ToArray().Where(static x => x.Kind == MirScopeKind.Inlined).Select(static x => $"{x.Kind} ");
            var callees = body.Blocks.ToArray().Where(static x => x.Terminator.Kind == MirTerminatorKind.Call)
                .Select(static x => x.Terminator.Callee.Symbol?.Name);
            var sites = body.OpaqueSites.ToArray().Select(x =>
                $"; {c.SyntaxOf(x.Source)} reads {string.Join(" ", body.OpaqueUses.Slice(x.Start, x.Count).ToArray().Select(r => body.Locals[r.Root].Role))}");
            return $"{name}: {string.Concat(scopes)}{string.Join(" ", callees)}{string.Concat(sites)}";
        }), .. program.Records(MirRecordClass.Construction).Where(static x => x.Subject.Module == 0).Select(x => $"record {c.SyntaxOf(x.Subject)} {x.Code}")];
    }

    // A caller of a default of DefaultsOver: v's Loan held across the omitting call, which `v = 2` then meets.
    private static string Caller(string name, string parameter, string callee)
        => $"func {name}(b: ref/{parameter})\n    var v = 1\n    let r = v@ref\n    {callee}(b)\n    v = 2\n    let k = r@follow\n";

    // A generic Type nested n times around i32.
    private static string Nested(string type, int n) => string.Concat(Enumerable.Repeat(type + "<", n)) + "i32" + new string('>', n);

    public partial class AllocationTests
    {
        // V11, plan 5.6.2: N calls that omit a generic default whose template holds a copy of another grow the body by two Inlined scopes each,
        // the MIR and the cold build's allocation linearly with N, and a warm rebuild allocates nothing.
        [Fact]
        public void DefaultCopiesScaleLinearly()
        {
            var (sizes, work, compilations, bodies) = (new[] { 16, 32, 64 }, new long[3], new Compilation[3], new MirBody[3]);
            var allocated = AllocationMeasurement.MeasureSeries(sizes.Length, i =>
            {
                var text = new StringBuilder("func next(x: i32, y: i32 = x + 1) -> i32 => y\nfunc scale<T>(x: T, factor: i32 = next(2)) -> i32\n    T is Copy\n    return factor\n");
                text.Append("func work() -> i32\n    var total = 0\n");
                for (var n = 0; n < sizes[i]; n++)
                {
                    text.Append("    total += scale(").Append(n).Append(")\n");
                }

                var c = compilations[i] = Compile(text.Append("    return total\n").ToString());
                return () => bodies[i] = c.BuildMir().Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "work" });
            });
            for (var i = 0; i < sizes.Length; i++)
            {
                work[i] = bodies[i].Blocks.Length + bodies[i].Statements.Length;
                Assert.Equal(sizes[i] * 2, bodies[i].Scopes.ToArray().Count(static x => x.Kind == MirScopeKind.Inlined));
            }

            output.WriteLine($"N {string.Join(", ", sizes)}: MIR size {string.Join(", ", work)}, cold build allocation {string.Join(", ", allocated)}");
            Assert.True(work[2] - work[1] <= (2 * (work[1] - work[0])) + 8, $"MIR size {string.Join(", ", work)}");
            Assert.True(allocated[2] - allocated[1] <= 3 * (allocated[1] - allocated[0]), $"cold build allocation {string.Join(", ", allocated)}");
            var last = compilations[^1];
            Assert.Equal(0, AllocationMeasurement.Measure(() =>
            {
                last.Binding.Bind(BindingMode.Final);
                last.BuildMir();
            }));
        }
    }
}
