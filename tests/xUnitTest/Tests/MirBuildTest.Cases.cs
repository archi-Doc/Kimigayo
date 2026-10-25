// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The MIR build's Semantics case views (plan 5.6.1, SPEC 8.14, 8.15): pair layers, acquisitions and adaptations resolved per view, and the
/// case bound.</summary>
public sealed partial class MirBuildTest
{
    // Plan 5.6.1, SPEC 8.14, 8.15, 13.5.5: one body serves every Semantics case. Under each view of tests/mir/cases (one binder, two
    // binders, a Closure, which has its enclosing function's binders, a default template and its copy, which names the omitting caller's
    // binder), a PairLayer step is its Place itself in the owner case and the referent of the case's reference otherwise, and a
    // PairAcquire Reborrows in the uniq case and Copies in the others.
    [Fact]
    public void ViewsResolvePairLayersAndAcquisitions()
    {
        var c = Compile(File.ReadAllText(DiagnosticCorpus.RepositoryPath("tests", "mir", "cases.kimi")));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var cases = new MirCases(c.Types);
        var lines = new List<string>();
        foreach (var body in program.Bodies)
        {
            for (var view = 0L; body.Binders.Count > 0 && view < MirCases.Count(body); view++)
            {
                var (resolved, selected) = (new List<string>(), cases.Select(body, view).ToArray());
                var resolver = new Resolver(body.Projections.ToArray(), cases, resolved);
                for (var block = 0; block < body.Blocks.Length; block++)
                {
                    for (var i = 0; i <= body.Blocks[block].Count; i++)
                    {
                        MirUses.ForEach(body, new(block, i), ref resolver);
                    }
                }

                var name = body.Key.Decl?.Name is { Length: > 0 } declared ? $"{body.Key.Kind} {declared}" : body.Key.Kind.ToString();
                lines.Add($"{name} {view} [{string.Join(", ", selected.Select(static x => x.Semantics))}]: {string.Join(", ", resolved.Distinct())}");
            }
        }

        output.WriteLine(string.Join('\n', lines));
        Assert.Equal(
            [
                "DefaultTemplate peek 0 [Owner]: @b0 itself: U",
                "DefaultTemplate peek 1 [Uniq]: @b0 *uniq/U: U",
                "Fn keep 0 [Owner]: acquire _3 copy, acquire _4 copy, @b0 itself: T",
                "Fn keep 1 [Uniq]: acquire _3 reborrow, acquire _4 reborrow, @b0 *uniq/T: T",
                "Fn both 0 [Owner, Owner]: @b0 itself: T, acquire _2 copy, acquire _7 copy",
                "Fn both 1 [Owner, Ref]: @b0 itself: T, acquire _2 copy, acquire _7 copy",
                "Fn both 2 [Uniq, Owner]: @b0 *uniq/T: T, acquire _2 copy, acquire _7 copy",
                "Fn both 3 [Uniq, Ref]: @b0 *uniq/T: T, acquire _2 copy, acquire _7 copy",
                "Closure 0 [Owner, Owner]: @b1 itself: U",
                "Closure 1 [Owner, Ref]: @b1 *ref/U: U",
                "Closure 2 [Uniq, Owner]: @b1 itself: U",
                "Closure 3 [Uniq, Ref]: @b1 *ref/U: U",
                "Fn peek 0 [Owner]: ",
                "Fn peek 1 [Uniq]: ",
                "Fn caller 0 [Owner]: acquire _1 copy, @b0 itself: T",
                "Fn caller 1 [Uniq]: acquire _1 reborrow, @b0 *uniq/T: T",
            ],
            lines);
    }

    // SPEC 8.14, 8.15, 13.5.3: an explicit case adaptation is one Adapt, which each Semantics case view of tests/mir/adaptations resolves to
    // the operation Binding selects in that case, as the operation's ordinary rvalue: a Place Copied, a temporary moved or an object
    // created from either (Use), an address or a borrow of the operand's Place, or for an object view of the whole object below its pair
    // layer (Ref; an objuniq upcast's Reborrow too), an owning or objref upcast and a pointer conversion (Cast); in a Closure under its
    // enclosing function's binder, in a default template under its own and in the template's copy under the omitting caller's. An
    // adaptation Binding rejected is a derived Opaque, without an Adapt or a record.
    [Fact]
    public void ViewsResolveAdaptations()
    {
        var c = Compile(File.ReadAllText(DiagnosticCorpus.RepositoryPath("tests", "mir", "adaptations.kimi")));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records);
        var cases = new MirCases(c.Types);
        var lines = new List<string>();
        foreach (var body in program.Bodies)
        {
            var name = body.Key.Decl?.Name is { Length: > 0 } declared ? $"{body.Key.Kind} {declared}" : body.Key.Kind.ToString();
            foreach (var adapt in body.Statements.ToArray().Select(static x => x.Rvalue).Where(static x => x.Kind == MirRvalueKind.Adapt))
            {
                for (var view = 0L; view < MirCases.Count(body); view++)
                {
                    var selected = cases.Select(body, view).ToArray();
                    var resolved = cases.Adapt(adapt, out var operation);
                    lines.Add($"{name} [{string.Join(", ", selected.Select(static x => x.Semantics))}]: {operation} {Form(body, resolved)}");
                }
            }
        }

        output.WriteLine(string.Join('\n', lines));
        Assert.Equal(
            [
                "DefaultTemplate take [Owner]: Identity Move _1",
                "DefaultTemplate take [Obj]: ObjectCreation Move _1",
                "DefaultTemplate take [Rc]: ObjectCreation Move _1",
                "DefaultTemplate take [Arc]: ObjectCreation Move _1",
                "Fn discard [Owner]: Identity Copy _1",
                "Fn discard [Ref]: Borrow Shared _1: ref/i32",
                "Fn discard [Obj]: ObjectCreation Copy _1",
                "Fn discard [Rc]: ObjectCreation Copy _1",
                "Fn discard [Arc]: ObjectCreation Copy _1",
                "Fn address [Obj]: ObjectCreation Copy _1",
                "Fn address [Raw]: Address Raw _1: raw/i32",
                "Fn widen [Obj]: ObjectUpcast Upcast: obj/Base",
                "Fn widen [Rc]: ObjectUpcast Upcast: rc/Base",
                "Fn widen [Arc]: ObjectUpcast Upcast: arc/Base",
                "Fn inspect [Obj]: ObjectUpcast Shared _1.PairLayer: objref/Base",
                "Fn inspect [Rc]: ObjectUpcast Shared _1.PairLayer: objref/Base",
                "Fn inspect [Arc]: ObjectUpcast Shared _1.PairLayer: objref/Base",
                "Fn mixed [Ref]: Borrow Shared _3: ref/ref/T",
                "Fn mixed [Obj]: Identity Move _3",
                "Closure [Owner]: Identity Copy _2",
                "Closure [Obj]: ObjectCreation Copy _2",
                "Closure [Rc]: ObjectCreation Copy _2",
                "Closure [Arc]: ObjectCreation Copy _2",
                "Fn give [Owner]: Identity Move _3",
                "Fn give [Obj]: ObjectCreation Move _3",
                "Fn give [Rc]: ObjectCreation Move _3",
                "Fn give [Arc]: ObjectCreation Move _3",
                "Fn peek [Obj]: ObjectUpcast Shared _1.PairLayer: objref/Base",
                "Fn peek [ObjRef]: ObjectUpcast Upcast: objref/Base",
                "Fn poke [Obj]: ObjectUpcast Uniq _2.PairLayer: objuniq/Base",
                "Fn poke [ObjUniq]: ObjectUpcast Uniq _2.PairLayer: objuniq/Base",
                "Fn cast [Raw]: Pointer Pointer: raw/u8",
                "Fn cast [Raw]: Pointer IntToPointer: raw/u8",
            ],
            lines);

        var rejected = Compile("func wrong<s/T>() -> s/i64\n    s is object\n    return 1@s/i64\n");
        var built = rejected.BuildMir();
        Assert.Empty(built.Findings);
        Assert.Empty(built.Records);
        var wrong = built.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "wrong");
        Assert.True(wrong.HasOpaque);
        Assert.DoesNotContain(wrong.Statements.ToArray(), static x => x.Rvalue.Kind == MirRvalueKind.Adapt);

        static string Form(MirBody body, in MirRvalue rvalue) => rvalue.Kind switch
        {
            MirRvalueKind.Use => $"{body.Operands[rvalue.Start].Kind} {Place(body, body.Operands[rvalue.Start].Place)}",
            MirRvalueKind.Ref => $"{(MirRefKind)rvalue.Op} {Place(body, rvalue.Place)}: {TyCtxt.Name(rvalue.Type!)}",
            _ => $"{(MirCastKind)rvalue.Op}: {TyCtxt.Name(rvalue.Type!)}",
        };

        static string Place(MirBody body, MirPlace place) => $"_{place.Root}{(place.Projection == 0 ? string.Empty : "." + body.Projections[place.Projection].Kind)}";
    }

    // SPEC 8.15, 23.1.1: Binding decides the case bound once per definition (Binding.CaseLimits), so no built body has more than 64 views.
    // Six binders of two cases give 64 distinct views of the body, its Closure and its default's template, in the old analysis's order (the
    // last binder fastest). A seventh puts the definition beyond the bound: Binding binds its parts but publishes none of them, so no body,
    // Closure or default template of it is built, also where another default omits its default, and a call that omits it takes an Opaque
    // value without a record or finding.
    [Fact]
    public void TheDefinitionCheckDecidesTheCaseBound()
    {
        foreach (var count in new[] { 6, 7 })
        {
            var names = "abcdegh"[..count].ToCharArray();
            var text = new StringBuilder("func f<").AppendJoin(", ", names.Select(static x => $"{x}/{char.ToUpperInvariant(x)}")).Append(">(");
            text.AppendJoin(", ", names.Select(static x => $"p{x}: {x}/{char.ToUpperInvariant(x)}")).Append(", k: i32 = 1 + 2) -> i32\n");
            text.AppendJoin(string.Empty, names.Select(static x => $"    {x} is owner or uniq\n"));
            text.Append("    let add = func (x: i32) -> i32 => x + k\n    _ = pa@move\n    return add(k)\npublic func main() -> ()\n    var n: i32 = 1\n");
            text.Append("    _ = f(n, n@uniq").Append(string.Concat(Enumerable.Repeat(", n", count - 2))).Append(")\n");
            text.Append("func g(x: i32 = f(1").Append(string.Concat(Enumerable.Repeat(", 1", count - 1))).Append(")) -> i32 => x\n");
            var c = Compile(text.ToString());
            Assert.Equal(count == 6, c.Binding.Result.IsComplete);
            Assert.Equal(count == 6 ? [] : new[] { DiagnosticCode.OwnershipCaseLimit_Kd }, c.Binding.Issues.Select(static x => x.Code));
            var program = c.BuildMir();
            Assert.Empty(program.Findings);
            Assert.Empty(program.Records);
            var parts = program.Bodies.ToArray().Where(static x => x.Source.Module == 0 && (x.Key.Decl?.Name == "f" || x.Key.Kind == MirBodyKind.Closure)).ToArray();
            var main = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "main");
            Assert.Equal(count == 7, program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "g" && x.Key.Kind == MirBodyKind.DefaultTemplate).HasOpaque);
            if (count == 7)
            {
                Assert.Empty(parts);
                Assert.Single(main.OpaqueSites.ToArray());
            }
            else
            {
                Assert.Equal([MirBodyKind.DefaultTemplate, MirBodyKind.Fn, MirBodyKind.Closure], parts.Select(static x => x.Key.Kind));
                Assert.All(parts, static x => Assert.Equal(64L, MirCases.Count(x)));
                Assert.False(main.HasOpaque);
                var (cases, views) = (new MirCases(c.Types), new List<string>());
                for (var view = 0L; view < MirCases.Count(parts[1]); view++)
                {
                    views.Add(string.Concat(cases.Select(parts[1], view).ToArray().Select(static x => x.Semantics == SemanticsKind.Owner ? 'o' : 'u')));
                }

                Assert.Equal(64, views.Distinct().Count());
                Assert.Equal(new[] { "oooooo", "ooooou", "uuuuuu" }, new[] { views[0], views[1], views[^1] });
            }

            c.Analyze();
            Assert.DoesNotContain(c.Problems, static x => x.Code is DiagnosticCode.OwnershipCaseLimit_Kd or DiagnosticCode.InternalInvariant_Kd);
        }
    }

    // SPEC 8.15, 23.1.1: a definition whose only part is a parameter default is bounded too, and beyond the bound the default has no
    // template. Seven unconstrained binders give 9^7 cases; a requirement's default is itself rejected (MisplacedSyntax_Kd).
    [Fact]
    public void ADefaultBeyondTheCaseBoundHasNoTemplate()
    {
        var c = CompilationTestHelper.Parse(
            "contract C\n    func need<a/A, b/B, c/C, d/D, e/E, g/G, h/H>(self: ref/Self, p: a/A, q: b/B, r: c/C, " +
            "u: d/D, v: e/E, w: g/G, y: h/H, k: i32 = 1 + 2) -> ()\n" +
            "public func main() -> ()\n    ()\n");
        Assert.Equal(nameof(DiagnosticCode.MisplacedSyntax_Kd), Assert.Single(TestDiagnostics.Of(c)).Code);
        c.Bind();
        _ = c.Binding.CheckStartup(OutputKind.Application);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.OwnershipCaseLimit_Kd);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Bodies.ToArray(), static x => x.Source.Module == 0 && x.Key.Kind == MirBodyKind.DefaultTemplate);
    }

    // SPEC 8.15, 23.1.1: an accessor's body is a part of its Property, so beyond the bound neither it nor a Field initializer, a method or
    // the synthesized constructor of a seven-binder struct is built, although Binding binds them.
    [Fact]
    public void NoMemberOfAContainerBeyondTheCaseBoundIsBuilt()
    {
        var c = Compile("struct S<a/A, b/B, c/C, d/D, e/E, g/G, h/H>\n" + string.Concat("abcdegh".Select(static x => $"    {x} is owner or uniq\n")) +
            "    var count: i32 = 1 + 2\n    computed size: i32\n        get(self: ref/Self) -> i32 => self.count + 1\n" +
            "    func touch(self: ref/Self) -> i32 => self.count + 2\npublic func main() -> ()\n    ()\n");
        Assert.Equal(4, c.Binding.Issues.Count(static x => x.Code == DiagnosticCode.OwnershipCaseLimit_Kd));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.All(program.Bodies.ToArray().Where(static x => x.Source.Module == 0), static x => Assert.True(x.Key.Decl is null || x.Key.Decl.Name == "main", x.Key.ToString()));
    }

    // SPEC 8.15 (DECISIONS R4-M139, R5's conditions): a call selects the view of its binder's case, the Semantics of the binder's whole
    // Type in the call's instance, read as every reader reads one (TyCtxt.Readable). Over storage Binding rejected (Bad's Field) the case
    // is the instance's, which no rejected part changes, so the view is the one its repair selects: one view, deterministic (before, every
    // admitted view). In a complete compilation no instance is Rejected (TyCtxtTest's ARejectedVerdictIsReadableOnlyBesideABindingFailure).
    [Theory]
    [InlineData("b@move", 1UL)]
    [InlineData("b@ref", 2UL)]
    public void ACallOverRejectedStorageSelectsItsRepairsView(string argument, ulong view)
    {
        foreach (var field in new[] { "Missing", "i32" })
        {
            var c = Compile($"struct Bad\n    public var m: {field} = 0\n    public init() => ()\ngroup H\n    public func keep<s/T>(item: s/T)\n" +
                $"        s is owner or ref\n        ()\nfunc g()\n    let b = Bad.init()\n    H.keep({argument})\n");
            var program = c.BuildMir();
            Assert.Empty(program.Findings);
            var call = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "g").Blocks.ToArray().Select(static x => x.Terminator)
                .Single(static x => x.Kind == MirTerminatorKind.Call && x.Callee.Symbol?.Name == "keep");
            Assert.Equal(view, new MirCases(c.Types).Views(program.Callee(call.Callee)!, call.Callee.Subst));
        }
    }

    // What a location's PairAcquire operands and PairLayer steps are under the selected view, each step with its selected Place's case Type.
    private readonly struct Resolver(MirProjection[] projections, MirCases cases, List<string> log) : IMirUses
    {
        public void Operand(in MirOperand operand)
        {
            if (operand.Kind == MirOperandKind.PairAcquire)
            {
                log.Add($"acquire _{operand.Place.Root} {(cases.Reborrows(operand) ? "reborrow" : "copy")}");
            }
        }

        public void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            for (var id = place.Projection; id != 0; id = projections[id].Prefix)
            {
                if (projections[id].Kind == MirProjectionKind.PairLayer)
                {
                    var layer = cases.Layer(place with { Projection = id });
                    log.Add($"@b{projections[id].Arg} {(layer is null ? "itself" : "*" + TyCtxt.Name(layer))}: {TyCtxt.Name(cases.Type(projections[id].Type!))}");
                }
            }
        }

        public void Effect(MirEffect effects, int site)
        {
        }
    }
}
