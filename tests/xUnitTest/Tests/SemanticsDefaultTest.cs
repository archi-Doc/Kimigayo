// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class SemanticsDefaultTest
{
    private const string Read = "func f<s/T>(x: s/T, y: T = x@follow) -> T\n    s is owner or ref or uniq\n    T is Copy\n    return y\n";
    private const string Run = "var n = 3\nrequire f(7) == 7 and f(n@ref) == 3 and f(n@uniq) == 3 else => $abort(\"cases\")\nrequire f(true) else => $abort(\"type\")";

    [Fact]
    public void DefaultsInspectEveryAdmittedSemantics()
        => ScalarEmissionTest.EmitFixture("SemanticsDefaultRead", Read + Run, string.Empty);

    [Fact]
    public void TemporaryInspectionEndsInsideEachDefaultCase()
        => ScalarEmissionTest.EmitFixture("SemanticsDefaultInspection", "func f<s/T>(x: s/T, y: T = label work: do\n    let r = x@follow@ref\n    exit to work r@follow\n) -> T\n    s is owner or ref or uniq\n    T is Copy\n    return y\n" + Run, string.Empty);

    [Fact]
    public void ExclusiveCaseCannotReturnItsNewReborrow()
    {
        const string source = "func f<s/T>(x: s/T, y: s/T = x) -> ()\n    s is owner or ref or uniq\n    T is Copy\n    return\nf(3, 4)";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal("DefaultArgumentBorrow_Kd", error.Code);
        var fact = Assert.Single(error.Reason!);
        Assert.Equal(("case", "s = uniq"), (fact.Name, fact.Value));
        Assert.Contains("s = uniq", error.Note, StringComparison.Ordinal);
        Assert.Contains(error.Related!, x => x.Label == "the Semantics binding s");
        var path = Path.GetFullPath("semantics-default.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("s = uniq", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Contains("s = uniq", sent.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("f(3, 4)")]
    public void DeclarationChecksDefaultLocalLoansWithoutOmission(string call)
    {
        const string source = "func f<s/T>(x: s/T, y: T = label work: do\n    var local = x@follow\n    let r = local@ref\n    local = x@follow\n    exit to work r@follow\n) -> T\n    s is owner or ref or uniq\n    T is Copy\n    return y\n";
        var errors = DiagnosticCorpus.Check(source + (call.Length == 0 ? "public func main() => ()" : call)).Diagnostics;
        var error = Assert.Single(errors);
        Assert.Equal("ComparisonLoanConflict_Kd", error.Code);
        Assert.Equal(source.IndexOf("local = x@follow\n    exit", StringComparison.Ordinal), error.Span!.Value.Start);
    }

    [TestClass(DisableParallelization = true)]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Fact]
        public void DefaultCasePlansReuseStorage()
        {
            var c = MinimalEmissionTest.Analyze(Read + Run);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
            var valid = true;
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
            Assert.True(valid);
            var restored = CompilationTestHelper.Reload(c);
            Assert.True(restored.Bind().IsComplete);
            restored.Binding.CheckStartup(OutputKind.Application);
            Assert.True(restored.Ownership.Analyze().IsVerified);
            Assert.True(restored.Emission.WriteIr(TextWriter.Null, out error), MinimalEmissionTest.Describe(restored, error));
        }
    }
}
