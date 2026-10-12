// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>The MIR analyses' driver and problem record (docs/dev/DECISIONS.md R4-M2a, R4-M3a): <see cref="MirAnalysis"/> runs once per
/// program version, and <see cref="MirReport"/> keeps one problem per code and subject with the views that have it.</summary>
public sealed class MirAnalysisTest
{
    // R4-M3a: a problem of a code and subject added again ORs its views into the first; another code or subject is its own problem.
    [Fact]
    public void ARepeatedProblemMergesItsViews()
    {
        var report = new MirReport();
        report.Add(new(DiagnosticCode.MovedPlace_Kd, new(0, 5), 0b01));
        report.Add(new(DiagnosticCode.UninitializedPlace_Kd, new(0, 5), 0b01));
        report.Add(new(DiagnosticCode.MovedPlace_Kd, new(0, 6), 0b01));
        report.Add(new(DiagnosticCode.MovedPlace_Kd, new(0, 5), 0b10));
        Assert.Equal(
            [new(DiagnosticCode.MovedPlace_Kd, new(0, 5), 0b11), new(DiagnosticCode.UninitializedPlace_Kd, new(0, 5), 0b01), new MirProblem(DiagnosticCode.MovedPlace_Kd, new(0, 6), 0b01)],
            report.All.ToArray());
        report.Clear();
        Assert.Equal(0, report.All.Length);
    }

    // R4-M1: the analyses run once per program version: building an unchanged version again keeps its problems, and a new Binding pass
    // starts without any.
    [Fact]
    public void ProblemsBelongToTheirVersion()
    {
        var (c, _) = SemanticSnapshotTest.Bind("milestone/Milestone1");
        var program = c.BuildMir();
        program.Problems.Add(new(DiagnosticCode.MovedPlace_Kd, program.Bodies[0].Source, 1));
        Assert.Equal(1, c.BuildMir().Problems.All.Length);
        c.Binding.Bind(BindingMode.Final);
        Assert.Equal(0, c.BuildMir().Problems.All.Length);
    }

    // R4-M12, V17: the library bodies have no problems.
    [Fact]
    public void TheLibraryHasNoProblems()
    {
        var (c, _) = SemanticSnapshotTest.Bind("library");
        Assert.Equal(0, c.BuildMir().Problems.All.Length);
    }

    // V16: a warm report allocates nothing: clearing keeps the storage the problems of a version need.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmReportAllocatesNothing()
    {
        var report = new MirReport();
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            report.Clear();
            for (var i = 0; i < 64; i++)
            {
                report.Add(new(i % 2 == 0 ? DiagnosticCode.MovedPlace_Kd : DiagnosticCode.UninitializedPlace_Kd, new(0, (i % 48) + 1), 1UL << (i % 3)));
            }
        }));
        Assert.Equal(64 - 16, report.All.Length);
    }
}
