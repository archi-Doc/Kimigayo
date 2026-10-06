// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class DefaultAccessTest
{
    private const string Increment = "func increment(x: uniq/i32) -> i32\n    x@follow += 1\n    return x@follow\n";

    [Theory]
    [InlineData("ReadReserved", "func f(x: uniq/i32, y: i32 = x) -> i32\n    x@follow = y + 1\n    return y\nvar n = 3\nrequire f(n@uniq) == 3 and n == 4 else => $abort(\"read\")")]
    [InlineData("CopyStoredReference", "func f(x: uniq/(ref/i32 during a) during b, y: ref/i32 during a = x) -> i32 => y@follow\nvar n = 3\nvar r = n@ref\nrequire f(r@uniq) == 3 else => $abort(\"stored reference\")")]
    [InlineData("ShareReserved", "func f(x: uniq/i32 during a, y: i32 = label read: do\n    let r: ref/i32 during a = x\n    exit to read r@follow\n) -> i32\n    x@follow = y + 1\n    return y\nvar n = 3\nrequire f(n@uniq) == 3 and n == 4 else => $abort(\"share\")")]
    [InlineData("IndependentExclusive", Increment + "func f(value: i32 = label work: do\n    var local = 3\n    exit to work increment(local@uniq)\n) -> i32 => value\nrequire f() == 4 else => $abort(\"exclusive local\")")]
    [InlineData("IndependentReceiver", "struct Meter\n    public var n: i32 = 3\n    public func increment(self: uniq/Self) -> i32\n        self.n += 1\n        return self.n\nfunc f(value: i32 = label work: do\n    var local = Meter.init()\n    exit to work local.increment()\n) -> i32 => value\nrequire f() == 4 else => $abort(\"receiver\")")]
    public void PreparedInputsAreSharedAndIndependentLocalsAreWritable(string name, string source)
        => ScalarEmissionTest.EmitFixture("DefaultAccess" + name, source, string.Empty);

    [Theory]
    [InlineData(Increment + "func f(x: uniq/i32, y: i32 = increment(x)) => ()\npublic func main() => ()", "DefaultArgumentAccess_Kd")]
    [InlineData("func f(x: uniq/i32, y: i32 = label work: do\n    x@follow = 9\n    exit to work 1\n) => ()\npublic func main() => ()", "DefaultArgumentAccess_Kd")]
    [InlineData("func f(x: uniq/i32 during a, y: ref/i32 during a = x) => ()\npublic func main() => ()", "DefaultArgumentBorrow_Kd")]
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during a = x@follow@ref) => ()\npublic func main() => ()", "DefaultArgumentBorrow_Kd")]
    [InlineData("func f(x: uniq/i32 during a, y: ref/i32 during a = label work: do\n    let shared: ref/i32 during a = x\n    exit to work shared\n) => ()\npublic func main() => ()", "DefaultArgumentBorrow_Kd")]
    [InlineData("func f(x: uniq/i32 during a, y: (ref/i32 during a, i32) = (x, 1)) => ()\npublic func main() => ()", "DefaultArgumentBorrow_Kd")]
    [InlineData("func shared(x: ref/i32 during a) -> ref/i32 during a => x\nfunc f(x: uniq/i32 during a, y: ref/i32 during a = shared(x)) => ()\npublic func main() => ()", "DefaultArgumentBorrow_Kd")]
    [InlineData("func choose(c: bool, x: ref/i32 during a, z: ref/i32 during a) -> ref/i32 during a => if c => x else => z\nfunc f(x: uniq/i32 during a, z: ref/i32 during a, y: ref/i32 during a = choose(true, x, z)) => ()\npublic func main() => ()", "DefaultArgumentBorrow_Kd")]
    public void PreparedAccessFailuresExplainTheForbiddenOperation(string source, string code)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(code, error.Code);
        Assert.Contains("prepared argument", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(error.Related!);
    }

    [Fact]
    public void EqualOriginNamesDoNotTurnAStoredReferenceCopyIntoAReborrow()
    {
        const string source = "func f(x: uniq/(ref/i32 during a) during a, y: ref/i32 during a = x) -> i32 => y@follow\npublic func main() => ()";
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    [Theory]
    [InlineData("func f(x: uniq/i32, y: i32 = increment(x)) => ()\nvar n = 1\nf(n@uniq)")]
    [InlineData("func f(x: uniq/i32, y: i32 = increment(x)) => ()\nvar n = 1\nf(n@uniq, 2)")]
    public void InvalidDefaultIsCheckedOnceWhetherOmittedOrSupplied(string source)
    {
        var result = DiagnosticCorpus.Check(Increment + source);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("DefaultArgumentAccess_Kd", error.Code);
    }

    [Theory]
    [InlineData(Increment + "func f(x: uniq/i32, y: i32 = increment(x)) => ()\npublic func main() => ()", "DefaultArgumentAccess_Kd")]
    [InlineData("func f(x: uniq/i32 during a, y: ref/i32 during a = x) => ()\npublic func main() => ()", "DefaultArgumentBorrow_Kd")]
    public void DiagnosticsPreserveTheCauseAndParameterInEveryAdapter(string source, string code)
    {
        var path = Path.GetFullPath("default-access.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(code, record.Code);
        Assert.Equal("preceding prepared parameter", Assert.Single(record.Related!).Label);
        Assert.NotNull(record.Advice);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(record.Message, console.Text, StringComparison.Ordinal);
        Assert.Contains(record.Advice, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal((record.Display!.Range, record.Code), (sent.Range, sent.Code));
            Assert.Contains(record.Advice, sent.Message, StringComparison.Ordinal);
        }
    }

    [TestClass(DisableParallelization = true)]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Fact]
        public void PreparedAccessPlansReuseStorage()
        {
            var c = MinimalEmissionTest.Analyze("func f(x: uniq/i32, y: i32 = x) -> i32 => y\nvar n = 3\nf(n@uniq)");
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
            var valid = true;
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
            Assert.True(valid);
        }
    }
}
