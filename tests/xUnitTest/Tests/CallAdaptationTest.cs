// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 10.2, 10.3: result filtering uses the same adaptation as the destination. Missing acquisition
// spelling is a useful repair only when the candidate's remaining types and constraints apply.
public class CallAdaptationTest
{
    private const string ReadResult = "func pick(x: ref/i32) -> ref/i32 => x\nlet a: i32 = 7\nlet v: i32 = pick(a)\nrequire v == 7 else => $abort(\"result\")";

    [Fact]
    public void AFixedExpectedResultReadsTheReturnedReference()
        => ScalarEmissionTest.EmitFixture("CallAdaptationReadResult", ReadResult, string.Empty);

    [Theory]
    [InlineData("Generic", "func pick<T>(x: ref/T) -> ref/T => x\nlet n: i32 = 7\nlet v: i32 = pick(n)\nrequire v == 7 else => $abort(\"generic\")")]
    [InlineData("Shared", "func pick(x: uniq/i32) -> uniq/i32 => x\nvar n: i32 = 7\nlet v: ref/i32 = pick(n@uniq)\nrequire v == 7 else => $abort(\"shared\")")]
    [InlineData("Object", "struct Item\n    public var n: i32 = 7\nfunc pick(x: objuniq/Item) -> objuniq/Item => x\nvar item = Kimi.Intrinsics.makeObj(Item.init())\nlet v: objref/Item = pick(item@objuniq)\nrequire v.n == 7 else => $abort(\"object\")")]
    public void ResultFilteringUsesTheDestinationAdaptation(string name, string source)
        => ScalarEmissionTest.EmitFixture("CallAdaptationResult" + name, source, string.Empty);

    [Fact]
    public void ATemporaryAtAFixedDestinationBindsButCannotEscapeItsStatement()
    {
        var c = MinimalEmissionTest.Analyze("let n: i32 = 1\nlet r: ref/(i32, i32) = (n, 2)\nlet value = r@follow");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("func f(x: i32) => ()\nlet s = \"s\"\nf(s)")]
    [InlineData("func f(x: uniq/i32) => ()\nvar x: i64 = 1\nf(x)")]
    [InlineData("func f(x: string, n: i32) => ()\nlet s = \"s\"\nf(s, true)")]
    [InlineData("func f(n: i32, x: string) => ()\nlet s = \"s\"\nf(true, s)")]
    [InlineData("func f(x: string) -> bool => true\nlet s = \"s\"\nlet n: i32 = f(s)")]
    [InlineData("func f<T>(x: T)\n    T is Copy\n    return\nlet s = \"s\"\nf(s)")]
    [InlineData("func f(x: uniq/i32, n: i32) => ()\nvar n: i32 = 1\nf(n, true)")]
    [InlineData("struct A\nstruct B\nfunc f(x: objuniq/A) => ()\nvar b = Kimi.Intrinsics.makeObj(B.init())\nf(b)")]
    public void InapplicableTypesAndConstraintsNeverOfferAcquisitionRepairs(string source)
    {
        var path = Path.GetFullPath("acquisition.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Equal(source[source.LastIndexOf("f(", StringComparison.Ordinal)..], Assert.Single(TestDiagnostics.Of(c)).Text);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.NoApplicableOverload_Kd), record.Code);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Null(record.Repairs);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.DoesNotContain("@move", console.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("@uniq", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, true)[identity]);
        Assert.Equal(record.Code, sent.Code);
        Assert.Equal(record.Display!.Range, sent.Range);
    }

    [Theory]
    [InlineData("func f(x: string) => ()\nlet s = \"s\"\nf(s)", DiagnosticCode.TransferRequired_Kd)]
    [InlineData("func f(x: uniq/i32) => ()\nvar n: i32 = 1\nf(n)", DiagnosticCode.ExclusiveBorrowRequired_Kd)]
    public void AcquisitionAloneStillExplainsTheFailure(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(code, Assert.Single(c.Binding.Issues).Code);
    }

    [Theory]
    [InlineData("func f(x: string) => ()\nlet s = \"s\"\nf(s@move)")]
    [InlineData("func f(x: uniq/i32) => ()\nvar n: i32 = 1\nf(n@uniq)")]
    public void CorrectAcquisitionPasses(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmResultAdaptationReusesStorage()
    {
        var c = CompilationTestHelper.ParseSuccess(ReadResult);
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
    }
}
