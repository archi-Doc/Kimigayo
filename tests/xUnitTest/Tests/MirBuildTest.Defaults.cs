// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>Default templates and their copies in the MIR build (plan 5.6.2, SPEC 7.2.3): template calls, Inlined scopes, the expansion limit.</summary>
public sealed partial class MirBuildTest
{
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
        Assert.Empty(program.Records);
        var bodies = program.Bodies.ToArray();
        Assert.Equal(["g#1"], Templates(bodies.Single(static x => x.Key is { Kind: MirBodyKind.DefaultTemplate, Decl.Name: "f" })));
        Assert.Equal(["f#1"], Templates(bodies.Single(static x => x.Key is { Kind: MirBodyKind.DefaultTemplate, Decl.Name: "g" })));
        Assert.True(bodies.Single(static x => x.Key is { Kind: MirBodyKind.DefaultTemplate, Decl.Name: "h" }).HasOpaque);
        var top = bodies.Single(static x => x.Key.Decl is null && x.Source.Module == 0);
        Assert.Equal(["f#1"], Templates(top));
        Assert.DoesNotContain(top.Scopes.ToArray(), static x => x.Kind == MirScopeKind.Inlined);
        var failed = Assert.Single(top.OpaqueSites.ToArray());
        Assert.Equal("h(1)", c.SyntaxOf(failed.Source).ToString());
        Assert.Equal(MirLocalRole.PendingSlot, top.Locals[Assert.Single(top.OpaqueRoots.Slice(failed.Start, failed.Count).ToArray())].Role);

        static string[] Templates(MirBody body) => body.Blocks.ToArray().Where(static x => x.Terminator is { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Fn, Arg: > 0 } })
            .Select(static x => $"{x.Terminator.Callee.Symbol!.Name}#{x.Terminator.Callee.Arg - 1}").ToArray();
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
    // twice at each level report DefaultExpansionLimit_Kd once, at the omitting call where the limit is met; that template fails, so the
    // calls that omit it take an Opaque value without another record.
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
        var record = Assert.Single(program.Records);
        Assert.Equal(DiagnosticCode.DefaultExpansionLimit_Kd, record.Code);
        Assert.Matches(@"^f\d+\(\)$", c.SyntaxOf(record.Subject).ToString());
        Assert.All(program.Bodies.ToArray(), static x => Assert.InRange(x.Statements.Length, 0, 65_536));
        Assert.True(program.Bodies.ToArray().Single(static x => x.Key.Decl is null && x.Source.Module == 0).HasOpaque);
    }

    // V11, plan 5.6.2: N calls that omit a generic default whose template holds a copy of another grow the body by two Inlined scopes each,
    // the MIR and the cold build's allocation linearly with N, and a warm rebuild allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void DefaultCopiesScaleLinearly()
    {
        var (sizes, work, allocated) = (new[] { 16, 32, 64 }, new long[3], new long[3]);
        Compilation c = null!;
        for (var i = 0; i < sizes.Length; i++)
        {
            var text = new StringBuilder("func next(x: i32, y: i32 = x + 1) -> i32 => y\nfunc scale<T>(x: T, factor: i32 = next(2)) -> i32\n    T is Copy\n    return factor\n");
            text.Append("func work() -> i32\n    var total = 0\n");
            for (var n = 0; n < sizes[i]; n++)
            {
                text.Append("    total += scale(").Append(n).Append(")\n");
            }

            c = Compile(text.Append("    return total\n").ToString());
            var before = GC.GetAllocatedBytesForCurrentThread();
            var body = c.BuildMir().Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "work" });
            allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            work[i] = body.Blocks.Length + body.Statements.Length;
            Assert.Equal(sizes[i] * 2, body.Scopes.ToArray().Count(static x => x.Kind == MirScopeKind.Inlined));
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: MIR size {string.Join(", ", work)}, cold build allocation {string.Join(", ", allocated)}");
        Assert.True(work[2] - work[1] <= (2 * (work[1] - work[0])) + 8, $"MIR size {string.Join(", ", work)}");
        Assert.True(allocated[2] - allocated[1] <= 3 * (allocated[1] - allocated[0]), $"cold build allocation {string.Join(", ", allocated)}");
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            c.BuildMir();
        }));
    }
}
