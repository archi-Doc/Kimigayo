// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>PLAN G349's shared acceptance rows for A5 (its problem statement, 2026-11-03): a body that checks its Loans across a
/// reassignment of a value of a non-regular Type below an indirection (SPEC 8.15.3) judges what destroying it observes and what it holds
/// by the Type's summaries (16.3.3, 15.3.5, 15.6.6): exact, no record where its regular twin has none, and A5's work polynomial in the
/// growing Fields and the levels (V16).</summary>
public sealed partial class MirBorrowsTest
{
    // SPEC 15.6.6, 16.3.3, 15.3.5 (problem statement §2): a live ref across the reassignment, which destroys the old value: the value
    // holds no Loan and its destruction observes none, so the summary gives no record, as the regular twins (reg3, twin3) give on dev.
    [Theory]
    [InlineData("br1", false)]
    [InlineData("br3w", false)]
    [InlineData("br3w", true)]
    [InlineData("arr3w", false)]
    [InlineData("drop", false)]
    [InlineData("reg3", false)]
    [InlineData("twin3", false)]
    public void AReassignmentBelowAnIndirectionUsesTheTypesSummary(string form, bool parameter)
    {
        var (declarations, local) = form switch
        {
            "br1" => (MirEffectsTest.Growing(1, 0), "Option<obj/T<i32>>"),
            "br3w" => (MirEffectsTest.Growing(3, 6), "Option<obj/T<i32>>"),
            "arr3w" => (MirEffectsTest.Growing(3, 6, array: true), "Option<T<i32>>"),
            "drop" => (MirEffectsTest.Growing(3, 6, "    drop => ()\n"), "Option<obj/T<i32>>"),
            "reg3" => (MirEffectsTest.Reg3, "Option<obj/T<(i32, i64)>>"),
            "twin3" => (MirEffectsTest.Growing(3, 0, regular: true), "Option<obj/T<i32>>"),
            _ => throw new ArgumentOutOfRangeException(nameof(form), form, null),
        };
        var c = CompileDepthForm(declarations + LoanBody(local, parameter));
        var program = Analyze(c);
        Assert.Empty(DepthRecords(c, program, null).Select(static x => $"{x.Line} {x.Code}"));
    }

    // Problem statement §4: the G272 pins whose growing Nest (Prelude's Field Option<rc/Nest<(T, T)>>) passes the depth bound below its rc
    // handle give their regular twin's records (Option<rc/Nest<T>>, exact on dev): TheBoundsLocatedRecordsBecomeTheLimitRecord's
    // deepAtReturn, made, deep, ownLimitLocal, ownDeep and init, and the pins they name (AnUnclassifiedDestructionOrCallIsUnsupported,
    // AnOwnStepViewIsAProducerOnlyWhereAnAccessMayDepart), whose OwnershipDepthLimit_Kd the change removes; those pins take this verdict.
    [Theory]
    [InlineData("deepAtReturn")]
    [InlineData("made")]
    [InlineData("deep")]
    [InlineData("ownLimitLocal")]
    [InlineData("ownDeep")]
    [InlineData("init")]
    public void AGrowingNestGivesItsRegularTwinsRecords(string form)
    {
        var (source, function) = DepthForm(form);
        var twin = source.Replace("Option<rc/Nest<(T, T)>>", "Option<rc/Nest<T>>", StringComparison.Ordinal);
        Assert.NotEqual(source, twin);
        Assert.Equal(Records(twin), Records(source));

        string[] Records(string text)
        {
            var c = CompileDepthForm(text);
            return [.. DepthRecords(c, Analyze(c), function).Select(static x => $"{x.Line} {x.Code}")];
        }
    }

    // PLAN G349 U2 (F5, N2): written nesting below an indirection, 17 handle layers of obj/S<…> over a borrow (each one level: a composed
    // atom is walked at its instance's depth), stays within the walk's bound, as in A4 (MirEffectsTest's s17): no record for the borrowed
    // parameter's destruction, as its 2-layer twin; and A4 then A5 over a second program version allocate nothing (the observation's
    // composition and A4's nodes A5 meets).
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(17)]
    [InlineData(2)]
    public void AWrittenNestingBelowAnIndirectionIsExact(int layers)
    {
        var type = string.Concat(Enumerable.Repeat("obj/S<", layers)) + "ref/i32 during a" + new string('>', layers);
        var c = CompileDepthForm($"struct S<X>\n    public var x: X\nfunc deep(n: {type})\n    let r = n@ref\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var (stratum, deep) = (new Stratum(c.Types), Body(program, "deep"));
        stratum.Run(program);
        Assert.Empty(DepthRecords(c, program, "deep").Select(static x => $"{x.Line} {x.Code}"));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            program.Version++;
            stratum.Run(program, deep);
        }));
    }

    // Kept apart (problem statement §5; WORKFLOW Full step 2): br3loan and br3pl at full size, the design note's programs, which run over
    // 100 s on dev: no A5 record. Explicit: they join the counted rows when the choice lands.
    [Theory(Explicit = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void TheFullSizeFormsGiveNoRecord(bool parameter)
    {
        var c = CompileDepthForm(MirEffectsTest.Growing(3, 0) + LoanBody("Option<obj/T<i32>>", parameter));
        Assert.Empty(DepthRecords(c, Analyze(c), null).Select(static x => $"{x.Line} {x.Code}"));
    }

    // V16 (problem statement §3): A5's cold allocation, Work and the parts its Type walks enter (WalkedParts, DECISIONS R4-M174) on
    // br3loan's body (main, after every other body and A4's summaries) grow at most linearly in the growing Fields (b = 1, 2, 3 behind two
    // wrappers per level) and in the levels (a chain of 5, 7 and 9 declarations with three Fields each); the regular twins are rows that
    // must not change; and A4 then A5 on main over a second program version allocate nothing (PLAN G349 U2, N2: the observation's
    // composition on a new version).
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("fields", false)]
    [InlineData("fields", true)]
    [InlineData("levels", false)]
    [InlineData("levels", true)]
    public void AReassignmentBelowAnIndirectionScalesPolynomially(string axis, bool regular)
    {
        var sizes = axis == "fields" ? new[] { 1, 2, 3 } : [5, 7, 9];
        var (work, parts, strata, programs) = (new long[3], new long[3], new Stratum[3], new (MirProgram Program, MirBody Main)[3]);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var allocated = AllocationMeasurement.MeasureSeries(sizes.Length, i =>
        {
            var c = CompileDepthForm((axis == "fields" ? MirEffectsTest.Growing(sizes[i], 2, regular: regular) : MirEffectsTest.LevelChain(sizes[i], regular)) +
                LoanBody(axis == "fields" ? "Option<obj/T<i32>>" : "Option<obj/T1<i32>>", parameter: false));
            var program = c.BuildMir();
            Assert.Empty(program.Findings);
            Assert.Empty(DepthRecords(c, program, null).Select(static x => $"{x.Line} {x.Code}"));
            var (stratum, main) = (new Stratum(c.Types), Body(program, "main"));
            foreach (var body in program.Bodies.ToArray().Where(x => x != main))
            {
                stratum.Run(program, body);
            }

            (strata[i], programs[i]) = (stratum, (program, main));
            return () => stratum.Run(program, main);
        });
        for (var i = 0; i < sizes.Length; i++)
        {
            (work[i], parts[i]) = (strata[i].Borrows.Work, strata[i].Borrows.WalkedParts);
            var (stratum, (program, main)) = (strata[i], programs[i]);
            Assert.Equal(0, AllocationMeasurement.Measure(() =>
            {
                program.Version++;
                stratum.Run(program, main);
            }));
        }

        output.WriteLine($"{axis} {string.Join(", ", sizes)} (regular {regular}): A5 Work {string.Join(", ", work)}, walked parts {string.Join(", ", parts)}, " +
            $"cold allocation of main's run {string.Join(", ", allocated)}, {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms");
        Assert.True(Grows(work, 2), string.Join(", ", work));
        Assert.True(Grows(parts, 2), $"walked parts {string.Join(", ", parts)}");
        Assert.True(allocated[2] - allocated[1] <= (2 * (allocated[1] - allocated[0])) + 4096, $"cold allocation {string.Join(", ", allocated)}");
    }

    // br3loan's body over a local of a form's Type, and br3pl's over a parameter Moved into a local.
    private static string LoanBody(string local, bool parameter) => "public func use(r: ref/i32) -> ()\n    return\n" + (parameter
        ? $"public func f(v: {local}, n: i32) -> ()\n    var m = n\n    let r = m@ref\n    var w = v@move\n    w = .None\n    use(r)\n    return\n"
        : $"public func main() -> ()\n    var n: i32 = 1\n    var v: {local} = .None\n    let r = n@ref\n    v = .None\n    use(r)\n    return\n");
}
