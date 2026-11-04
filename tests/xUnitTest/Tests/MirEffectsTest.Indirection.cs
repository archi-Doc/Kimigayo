// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>PLAN G349's shared acceptance rows for A4 (its problem statement, 2026-11-03): destroying a value of a non-regular Type
/// (SPEC 8.15.3) below an indirection, a Sealed handle's payload or an Array's element, uses that Type's destruction summary (16.3.3), a
/// fixed point per declaration that terminates for non-regular Types (15.3.5, 15.6.4). Where it is exact no check rests on a limit, so
/// A4 records what a regular twin records; the program's result stays the instance collector's located
/// <c>GenerationResourceLimit_Kd</c> (IMPL 21.3.2, A.4); and A4's work is polynomial in the growing Fields and the levels (V16).</summary>
public sealed partial class MirEffectsTest
{
    // The design note's reg3: two regular Types over two instances, three handle Fields each (exact on dev).
    internal const string Reg3 = "struct T<X>\n    public var a: obj/T<X>\n    public var b: obj/T<i32>\n    public var c: obj/U<X>\n" +
        "struct U<X>\n    public var a: obj/U<X>\n    public var b: obj/T<X>\n    public var c: Option<X>\n";

    private const string Metrics = "group Metrics\n    public var n: i32 = 0\n";

    // The growing Fields' arguments of br3 (PLAN G349) and their regular twins.
    private static readonly string[] GrowingArguments = ["(X, X)", "Option<X>", "(X, i32)"];

    private static readonly string[] RegularArguments = ["X", "i32", "Option<i32>"];

    // SPEC 16.3.3, 15.3.5, 15.6.4, 8.11.2 (problem statement §2): a confined witness destroying a value of the family. The summary is
    // exact, so A4 records what the regular twin does: nothing where no part has an effect (br1, br2, br3 with wrappers, arr3's Array
    // element, a pure drop, an atom bound to i32); the decided effect where a part has one (a drop writing a mutable static, an atom
    // bound to Loud, whose drop prints). reg3 and the twin are exact on dev and stay so.
    [Theory]
    [InlineData("br1", "")]
    [InlineData("br2", "")]
    [InlineData("br3w", "")]
    [InlineData("arr3w", "")]
    [InlineData("reg3", "")]
    [InlineData("twin3", "")]
    [InlineData("drop", "")]
    [InlineData("noisyDrop", Failed)]
    [InlineData("atom", Failed)]
    [InlineData("atomFlat", "")]
    public void ADestructionBelowAnIndirectionUsesTheTypesSummary(string form, string expected)
    {
        var (declarations, local) = IndirectionForm(form);
        Assert.Equal(Expected(expected), Problems(Sink + Loud + declarations + Witness + $"        let v: {local} = .None\n        return .Ok(())\n"));
    }

    // PLAN G349 (design note §7): a declaration with an Origin slot, growing below an indirection, composes its node through the
    // instance's Type and Origin arguments, the substitution Binding.StoredType makes of its Fields (TyCtxt.Arguments): the atom of its
    // Field X reads Loud, whose drop prints, or i32, and no slot reads as a limit. A Type declaration has no length slot (SPEC 4.4.1).
    [Theory]
    [InlineData("Loud", Failed)]
    [InlineData("i32", "")]
    public void ADeclarationsOriginSlotComposesThroughTheInstance(string argument, string expected)
    {
        const string Slots = "struct T<X> {s}\n    public var a: Option<X>\n    public var r: Option<ref/i32 during s>\n" +
            "    public var f0: obj/(T<(X, X)> during s)\n";
        var c = MirBuildTest.Compile(Sink + Loud + Slots + Witness + $"        let v: Option<obj/(T<{argument}> during static)> = .None\n" +
            "        return .Ok(())\npublic func main() => ()\n");
        Assert.DoesNotContain(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(Expected(expected), Problems(c, c.BuildMir()));
    }

    // IMPL 21.3.2, A.4 (problem statement §2): the program's result is the instance collector's located record, as on dev: its
    // GenerationResourceLimit_Kd at the local (br1, br2, br3 with wrappers, arr3's) or at the parameter's Type (br3p); for the drop form
    // the located Unsupported_Kd at the local (dev: the destructor's instance meets Binding's storage bound first, PLAN G128, not G349's);
    // none for reg3 and the twin.
    [Theory]
    [InlineData("br1", "local", "GenerationResourceLimit_Kd let v: Option<obj/T<i32>> = .None")]
    [InlineData("br2", "local", "GenerationResourceLimit_Kd let v: Option<obj/T<i32>> = .None")]
    [InlineData("br3w", "local", "GenerationResourceLimit_Kd let v: Option<obj/T<i32>> = .None")]
    [InlineData("br3w", "parameter", "GenerationResourceLimit_Kd Option<obj/T<i32>>")]
    [InlineData("arr3w", "local", "GenerationResourceLimit_Kd let v: Option<T<i32>> = .None")]
    [InlineData("drop", "local", "Unsupported_Kd let v: Option<obj/T<i32>> = .None")]
    [InlineData("reg3", "local", "")]
    [InlineData("twin3", "local", "")]
    public void TheProgramsResultIsTheCollectorsRecord(string form, string site, string expected)
        => Assert.Equal(expected, CollectorRecords(IndirectionForm(form), site));

    // Problem statement §4: the pins whose growing Nest (a Field Option<rc/Nest<(T, T)>>) passes the depth bound below its rc handle give
    // their regular twin's records (Option<rc/Nest<T>>, exact on dev): AGrowingRecursiveDestructionUsesItsSummary (was EndsAtTheBound) and the G272 rows' "nest"
    // form (MirEffectsTest.DepthLimit, removed: tuple40 represents its rules), whose OwnershipDepthLimit_Kd the change removed.
    [Fact]
    public void AGrowingNestGivesItsRegularTwinsRecords()
    {
        var body = "        _ = Nest<i32>.init(value: 1)\n        return .Ok(())\n";
        var twin = GrowingNest.Replace("Option<rc/Nest<(T, T)>>", "Option<rc/Nest<T>>", StringComparison.Ordinal);
        Assert.NotEqual(GrowingNest, twin);
        Assert.Equal(Problems(Sink + Loud + twin + Witness + body), Problems(Sink + Loud + GrowingNest + Witness + body));
    }

    // Kept apart (problem statement §5; WORKFLOW Full step 2): br3, br3p and arr3 at full size, the PLAN's programs, which run 77 s, 97 s
    // and over 100 s on dev: no A4 record and the collector's record at the site. Explicit: they join the counted rows when the choice lands.
    [Theory(Explicit = true)]
    [InlineData("br3", "local", "GenerationResourceLimit_Kd let v: Option<obj/T<i32>> = .None")]
    [InlineData("br3", "parameter", "GenerationResourceLimit_Kd Option<obj/T<i32>>")]
    [InlineData("arr3", "local", "GenerationResourceLimit_Kd let v: Option<T<i32>> = .None")]
    public void TheFullSizeFormsGiveTheCollectorsRecord(string form, string site, string expected)
        => Assert.Equal(expected, CollectorRecords(form == "br3" ? (Growing(3, 0), "Option<obj/T<i32>>") : (Growing(3, 0, array: true), "Option<T<i32>>"), site));

    // V16 (problem statement §3): A4's evaluations and its cold allocation over the program grow at most linearly in the growing Fields
    // (b = 1, 2, 3 of br3's family behind two wrappers per level, the levels a depth of 32 holds fixed) and in the levels (a chain of 5, 7
    // and 9 declarations with three Fields each), and a warm re-analysis allocates nothing and takes the same evaluations; the regular twins
    // are rows that must not change. Each size runs a fresh A4 over a built program, as SummariesScaleLinearly does.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("fields", false)]
    [InlineData("fields", true)]
    [InlineData("levels", false)]
    [InlineData("levels", true)]
    public void ADestructionBelowAnIndirectionScalesPolynomially(string axis, bool regular)
    {
        var sizes = axis == "fields" ? new[] { 1, 2, 3 } : [5, 7, 9];
        var (evaluations, analyses) = (new long[3], new (MirEffects Effects, MirDataflow Flow, MirCases Cases, MirProgram Program, MirReport Report)[3]);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var allocated = AllocationMeasurement.MeasureSeries(sizes.Length, i =>
        {
            var declarations = axis == "fields" ? Growing(sizes[i], 2, regular: regular) : LevelChain(sizes[i], regular);
            var local = axis == "fields" ? "Option<obj/T<i32>>" : "Option<obj/T1<i32>>";
            var c = MirBuildTest.Compile(declarations + $"public func main() -> ()\n    let v: {local} = .None\n    return\n");
            var program = c.BuildMir();
            Assert.Empty(Problems(c, program));
            var (cases, report) = (new MirCases(c.Types), new MirReport());
            var flow = new MirDataflow(new MirReach());
            var effects = new MirEffects(c.Types, cases, flow);
            analyses[i] = (effects, flow, cases, program, report);
            return () => Analyze(effects, flow, cases, program, report);
        });
        for (var i = 0; i < sizes.Length; i++)
        {
            var (effects, flow, cases, program, report) = analyses[i];
            evaluations[i] = effects.Evaluations;
            Assert.Equal(0, AllocationMeasurement.Measure(() =>
            {
                program.Version++;
                report.Clear();
                Analyze(effects, flow, cases, program, report);
            }));
            Assert.Equal(evaluations[i], effects.Evaluations);
        }

        output.WriteLine($"{axis} {string.Join(", ", sizes)} (regular {regular}): evaluations {string.Join(", ", evaluations)}, cold A4 allocation " +
            $"{string.Join(", ", allocated)}, {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms");
        Assert.True(evaluations[2] - evaluations[1] <= (2 * (evaluations[1] - evaluations[0])) + 64, $"evaluations {string.Join(", ", evaluations)}");
        Assert.True(allocated[2] - allocated[1] <= (2 * (allocated[1] - allocated[0])) + 4096, $"cold A4 allocation {string.Join(", ", allocated)}");
    }

    // br3's family: T<X> with the first `fields` of its three Fields, each a handle (or an Array) over `wrappers` inline W layers over T at
    // a growing (or, for the twin, a regular) argument, and a member; the wrappers set how many levels a depth bound holds (wr probes).
    internal static string Growing(int fields, int wrappers, string member = "", bool array = false, bool regular = false)
    {
        var text = new StringBuilder("struct W<Y>\n    public var y: Y\nstruct T<X>\n");
        for (var i = 0; i < fields; i++)
        {
            var target = string.Concat(Enumerable.Repeat("W<", wrappers)) + $"T<{(regular ? RegularArguments : GrowingArguments)[i]}>" + new string('>', wrappers);
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"    public var f{i}: {(array ? $"Array<{target}>" : $"obj/{target}")}\n");
        }

        return text.Append(member).ToString();
    }

    // A chain of `levels` declarations: each Ti<X> holds three handles to T(i+1) at br3's growing arguments and the last holds X, so the
    // chain is regular with 3^(levels - 1) distinct instances of its last declaration (one per level for the twin).
    internal static string LevelChain(int levels, bool regular = false)
    {
        var text = new StringBuilder();
        for (var k = 1; k < levels; k++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"struct T{k}<X>\n");
            for (var i = 0; i < 3; i++)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"    public var f{i}: obj/T{k + 1}<{(regular ? "X" : GrowingArguments[i])}>\n");
            }
        }

        return text.Append(System.Globalization.CultureInfo.InvariantCulture, $"struct T{levels}<X>\n    public var v: Option<X>\n").ToString();
    }

    // The forms of the problem statement, each destroyed in the confined witness: its declarations and the Type of the destroyed local.
    private static (string Declarations, string Local) IndirectionForm(string form) => form switch
    {
        "br1" => (Growing(1, 0), "Option<obj/T<i32>>"),
        "br2" => (Growing(2, 0), "Option<obj/T<i32>>"),
        "br3w" => (Growing(3, 6), "Option<obj/T<i32>>"),
        "arr3w" => (Growing(3, 6, array: true), "Option<T<i32>>"),
        "reg3" => (Reg3, "Option<obj/T<(i32, i64)>>"),
        "twin3" => (Growing(3, 0, regular: true), "Option<obj/T<i32>>"),
        "drop" => (Growing(3, 6, "    drop => ()\n"), "Option<obj/T<i32>>"),
        "noisyDrop" => (Metrics + Growing(3, 6, "    drop => Metrics.n += 1\n"), "Option<obj/T<i32>>"),
        "atom" => (Growing(3, 6, "    public var v: Option<X>\n"), "Option<obj/T<Loud>>"),
        "atomFlat" => (Growing(3, 6, "    public var v: Option<X>\n"), "Option<obj/T<i32>>"),
        _ => throw new ArgumentOutOfRangeException(nameof(form), form, null),
    };

    // A form's program with its value at a main local or a parameter: no A4 problem, then the collector's records as code and first line.
    private static string CollectorRecords((string Declarations, string Local) form, string site)
    {
        var c = MirBuildTest.Compile(form.Declarations + (site == "local"
            ? $"public func main() -> ()\n    let v: {form.Local} = .None\n    return\n"
            : $"public func f(v: {form.Local}) -> ()\n    return\n"));
        var program = c.BuildMir();
        Assert.Empty(Problems(c, program));
        _ = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        return string.Join(" | ", program.Records(MirRecordClass.Collection).Select(x => $"{x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0].Trim()}"));
    }
}
