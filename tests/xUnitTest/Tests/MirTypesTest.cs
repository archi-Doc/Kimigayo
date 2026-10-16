// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>The Type facts A5 reads (docs/dev/DECISIONS.md R4-M36): which values may hold a Loan (<see cref="MirTypes.HoldsLoans"/>) and
/// which destructions may observe what they hold (<see cref="MirTypes.Observes"/>), over the parameter Types of one function.</summary>
public sealed class MirTypesTest(ITestOutputHelper output)
{
    private const string Types = "struct Plain\n    var n: i32\nstruct Holder {a}\n    let r: ref/i32 during a\n" +
        "struct Logger {sink}\n    let out: uniq/i32 during sink\n    drop\n        _ = self.out@follow\n" +
        "open struct Base\n    var n: i32\nstruct Chain\n    var next: Option<obj/Chain>\nstruct Tree\n    var children: Array<Tree>\n" +
        "struct Nest<T>\n    var value: T\n    var next: Option<rc/Nest<(T, T)>>\n";

    // SPEC 15.6, 3.2.2, 15.3.5, 5.2.2: a value may hold a Loan where a part of its own storage is a borrow or pair layer, a Slice, a
    // Function value or an abstract Type, or a Type argument its storage may hold (a library collection behind a raw pointer, a Loan);
    // a raw pointer, scalars, strings and Origin-free aggregates hold none. A repeating recursive Type ends exactly, a growing one at the
    // walk's bound, conservatively.
    [Fact]
    public void LoansAreHeldByBorrowsAndWhatMayStoreThem()
    {
        var body = Probe(
            "func probe<s/T>(scalar: i32, text: string, numbers: Array<i32>, plain: Plain, raw: raw/(ref/i32 during a), chain: Chain, " +
            "tree: Tree, borrow: ref/i32, borrows: Array<ref/i32 during a>, tuple: (i32, ref/i32 during a), " +
            "fixed: [2 of ref/i32 during a], table: Dictionary<i32, ref/i32 during a>, option: Option<ref/i32 during a>, holder: Holder, " +
            "handle: obj/Holder, slice: Slice<i32>, view: UniqSlice<i32>, loan: Loan<ref/i32 during a>, " +
            "iterator: ArrayOwningIterator<ref/i32 during a>, action: () -> (), layer: s/T, nest: Nest<i32>)\n    s is owner or ref\n" +
            "    return\n").Body;
        var seen = new HashSet<BoundType>(ReferenceEqualityComparer.Instance);
        Assert.Equal(
            [("scalar", false), ("text", false), ("numbers", false), ("plain", false), ("raw", false), ("chain", false), ("tree", false),
                ("borrow", true), ("borrows", true), ("tuple", true), ("fixed", true), ("table", true), ("option", true),
                ("holder", true), ("handle", true), ("slice", true), ("view", true), ("loan", true), ("iterator", true), ("action", true),
                ("layer", true), ("nest", true)],
            Parameters(body).Select(x => (x.Name, MirTypes.HoldsLoans(x.Type, seen))).ToArray());
    }

    // SPEC 15.6.6, 16.3.3: destroying a value observes what it holds where its destruction runs a user drop (Logger, also as an element
    // or a Sealed handle's payload), destroys a Function value or an abstract part not proven Copy, or releases a handle whose payload is
    // not Sealed; a Sealed recursive handle ends. A part past the walk's depth bound, also below a growing recursive handle, is deep: the
    // caller's located Unsupported.
    [Fact]
    public void DestructionObservesUserDropsAndUnknownParts()
    {
        var (body, c) = Probe(
            "func probe<T, C>(holder: Holder, logger: Logger, loggers: Array<Logger>, owned: obj/Logger, shared: rc/Plain, atomic: arc/Plain, " +
            "based: obj/Base, action: () -> (), value: T, copied: C, chain: Chain, tree: Tree, nest: Nest<i32>)\n    C is Copy\n    return\n");
        var seen = new HashSet<BoundType>(ReferenceEqualityComparer.Instance);
        Assert.Equal(
            [("holder", false, false), ("logger", true, false), ("loggers", true, false), ("owned", true, false), ("shared", false, false),
                ("atomic", false, false), ("based", true, false), ("action", true, false), ("value", true, false), ("copied", false, false),
                ("chain", false, false), ("tree", true, true), ("nest", true, true)],
            Parameters(body).Select(x => (x.Name, MirTypes.Observes(x.Type, c.Types, body.Key.Decl, seen, out var deep), deep)).ToArray());
    }

    // R4-M36: every local Type of every library body is classified and none is deep; a borrow layer always holds a Loan and a raw pointer
    // never. The output gives the counts.
    [Fact]
    public void LibraryTypesClassify()
    {
        var c = MirBuildTest.Compile("public func main() => ()\n");
        var seen = new HashSet<BoundType>(ReferenceEqualityComparer.Instance);
        var (locals, holding, observing) = (0, 0, 0);
        foreach (var body in c.BuildMir().Bodies)
        {
            foreach (var local in body.Locals.ToArray())
            {
                var holds = MirTypes.HoldsLoans(local.Type, seen);
                var raw = local.Type.Semantics == Kimi.Compiler.Parsing.SemanticsKind.Raw;
                Assert.True(raw ? !holds : holds || !MirTypes.IsReference(local.Type), $"{body.Key.Decl?.Name}: {local.Name}");
                var observes = MirTypes.Observes(local.Type, c.Types, body.Key.Decl, seen, out var deep);
                Assert.False(deep, $"{body.Key.Decl?.Name}: {local.Name}");
                (locals, holding, observing) = (locals + 1, holding + (holds ? 1 : 0), observing + (observes ? 1 : 0));
            }
        }

        output.WriteLine($"library locals {locals}: holding Loans {holding}, observing {observing}");
        Assert.True(holding > 0 && observing > 0);
    }

    private static (MirBody Body, Compilation Compilation) Probe(string function)
    {
        var c = MirBuildTest.Compile(Types + function);
        Assert.True(c.Binding.Issues.Count == 0, string.Join(", ", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        return (program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "probe"), c);
    }

    // The Arg locals after the return local, in parameter order.
    private static IEnumerable<(string Name, BoundType Type)> Parameters(MirBody body)
        => body.Locals.ToArray().Skip(1).TakeWhile(static x => x.Kind == MirLocalKind.Arg).Select(static x => (x.Name!, x.Type));
}
