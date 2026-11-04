// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>A5's observation (<see cref="MirBorrows.Observation"/>; SPEC 15.6.6, 16.3.3, DECISIONS R4-M147, R4-M153): what destroying a
/// value of a Type may observe of what it holds, over the parameter Types of one function (MirTypesTest's probe) and the library's
/// locals, read after A4's summaries of the program version (PLAN G349 U2: the Observer composes A4's declaration nodes).</summary>
public sealed partial class MirBorrowsTest
{
    // SPEC 23.1.1, 23.1.2 (DECISIONS R4-M153's revision 3): a walk's answer is the Join of its parts' outcomes, the same in every order of
    // a Tuple's components, an Array element's components and a struct's Fields. Beside a part past the bound (deep, a Tuple of i32
    // nested 40 deep: Deep alone) a decided part is the answer before and after it (Observation: a Logger, whose drop observes; the guard:
    // an exclusive reference to a borrow), and a Limit part outranks it (an abstract part not proven Copy; a stored raw layer whose members
    // Binding does not publish). At depth 10 nothing is past the bound (deep: none) and each answer is the same.
    [Theory]
    [InlineData(40)]
    [InlineData(10)]
    public void AWalksAnswerIsTheSameInEveryOrderOfItsParts(int depth)
    {
        var deep = new string('(', depth) + "i32" + string.Concat(Enumerable.Repeat(", i32)", depth));
        var past = depth > 32 ? "Deep" : "None";
        var observed = Rows("o", "Logger", "T", "Decided");
        var guarded = Rows("g", "uniq/(ref/i32 during a) during a", "Wrap<raw/u8 during x>", "Through");
        var (body, c) = MirTypesTest.Probe(
            "struct Wrap<E>\n    var value: E\nstruct Two<A, B>\n    var a: A\n    var b: B\n" +
            $"func probe<T>(x: uniq/(ref/i32 during a) during a, {string.Join(", ", observed.Concat(guarded).Select(static x => $"{x.Name}: {x.Type}"))})\n" +
            "    return\n");
        var observation = Observations(c);
        var walked = new Dictionary<MirHoldingKey, MirModeDepths>();
        Assert.Equal(
            observed.Concat(guarded).Select(static x => (x.Name, x.Answer)).ToArray(),
            MirTypesTest.Parameters(body).Skip(1).Select(x => (x.Name, x.Name[0] == 'o' ? Observed(x.Type) : Guard(x.Type))).ToArray());

        string Observed(BoundType type) => observation(type, body.Key.Decl).ToString();

        string Guard(BoundType type)
        {
            _ = MirTypes.HoldsLoans(type, c.Types, walked, [], out var writes);
            return writes.ToString();
        }

        // Each container of each order of a decided part (D) or a Limit part (L) and the deep part (P), and the deep part alone.
        (string Name, string Type, string Answer)[] Rows(string walk, string decided, string limit, string decidedAnswer)
        {
            (string Name, string Format)[] containers = [("Tuple", "({0}, {1})"), ("Array", "Array<({0}, {1})>"), ("Field", "Two<{0}, {1}>")];
            (string Name, string First, string Second, string Answer)[] orders =
                [("DP", decided, deep, decidedAnswer), ("PD", deep, decided, decidedAnswer), ("LP", limit, deep, "Limit"), ("PL", deep, limit, "Limit")];
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            return
            [
                .. containers.SelectMany(x => orders.Select(o => ($"{walk}{x.Name}{o.Name}", string.Format(culture, x.Format, o.First, o.Second), o.Answer))),
                ($"{walk}Deep", deep, past),
            ];
        }
    }

    // SPEC 15.6.6, 16.3.3: destroying a value observes what it holds where its destruction runs a user drop (Logger, also as an element
    // or a Sealed handle's payload), destroys a Function value, or releases a handle whose payload is not Sealed; an abstract part not
    // proven Copy, whose instance decides, is the Limit (DECISIONS R4-M147); a Sealed recursive handle ends. A part past the walk's depth
    // bound, also below a growing recursive handle, is Deep (DECISIONS R4-M153). An observing part is the decided answer (MirOutcome).
    [Fact]
    public void DestructionObservesUserDropsAndUnknownParts()
    {
        var (body, c) = MirTypesTest.Probe(
            "func probe<T, C>(holder: Holder, logger: Logger, loggers: Array<Logger>, owned: obj/Logger, shared: rc/Plain, atomic: arc/Plain, " +
            "based: obj/Base, action: () -> (), value: T, copied: C, chain: Chain, tree: Tree, forest: Forest, table: Table, logged: Logged, " +
            "nest: Nest<i32>)\n    C is Copy\n    return\n");
        var observation = Observations(c);
        Assert.Equal(
            [("holder", "None"), ("logger", "Decided"), ("loggers", "Decided"),
                ("owned", "Decided"), ("shared", "None"), ("atomic", "None"),
                ("based", "Decided"), ("action", "Decided"), ("value", "Limit"),
                ("copied", "None"), ("chain", "None"), ("tree", "None"), ("forest", "None"),
                ("table", "None"), ("logged", "Decided"), ("nest", "Deep")],
            MirTypesTest.Parameters(body).Select(x => (x.Name, observation(x.Type, body.Key.Decl).ToString())).ToArray());
    }

    // R4-M36, R4-M116: every local Type of every library body is classified and none is deep; a borrow or raw layer always holds a Loan.
    // The output gives the counts.
    [Fact]
    public void LibraryTypesClassify()
    {
        var c = MirBuildTest.Compile("public func main() => ()\n");
        var walked = new Dictionary<MirHoldingKey, MirModeDepths>();
        var observation = Observations(c);
        var (locals, holding, observing) = (0, 0, 0);
        foreach (var body in c.BuildMir().Bodies)
        {
            foreach (var local in body.Locals.ToArray())
            {
                var holds = MirTypes.HoldsLoans(local.Type, c.Types, walked, [], out _);
                var raw = local.Type.Semantics == Kimi.Compiler.Parsing.SemanticsKind.Raw;
                Assert.True(holds || !(raw || MirTypes.IsReference(local.Type)), $"{body.Key.Decl?.Name}: {local.Name}");
                var observed = observation(local.Type, body.Key.Decl);
                Assert.False(observed.IsDeep);
                var observes = !observed.IsNone;
                (locals, holding, observing) = (locals + 1, holding + (holds ? 1 : 0), observing + (observes ? 1 : 0));
            }
        }

        output.WriteLine($"library locals {locals}: holding Loans {holding}, observing {observing}");
        Assert.True(holding > 0 && observing > 0);
    }

    /// <summary>Gets A5's observation of a compilation's program version, after A4's summaries of it (a Stratum's run of one body).</summary>
    /// <param name="c">The compilation.</param>
    /// <returns>The observation of a Type under a definition's premises.</returns>
    internal static Func<BoundType?, BindingSymbol?, MirOutcome> Observations(Compilation c)
    {
        var (stratum, program) = (new Stratum(c.Types), c.BuildMir());
        stratum.Run(program, program.Bodies.ToArray()[^1]);
        return stratum.Borrows.Observation;
    }
}
