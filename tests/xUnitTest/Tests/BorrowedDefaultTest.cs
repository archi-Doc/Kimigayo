// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 7.2.3: existing external shared dependencies may be copied, but no new Loan of a pending argument may escape.
public class BorrowedDefaultTest
{
    private const string Nested = "func same(x: ref/i32 during a, y: ref/i32 during a = x) -> ref/i32 during a => y\nfunc f(a: ref/i32 during o, b: ref/i32 during p, y: ref/i32 during o = same(a), z: ref/i32 during p = same(b)) -> i32 => y@follow + z@follow\nlet n = 3\nlet m = 7\nrequire f(n@ref, m@ref) == 10 and f(m@ref, n@ref) == 10 else => $abort(\"origins\")";

    [Theory]
    [InlineData("Shared", "func f(x: ref/i32 during a, y: ref/i32 during a = x) -> i32 => y@follow\nlet n = 7\nrequire f(n@ref) == 7 else => $abort(\"shared\")")]
    [InlineData("SharedString", "func f(x: ref/string during a, y: ref/string during a = x) => Console.writeLine(y)\nlet text = \"external\"\nf(text@ref)", "external\n")]
    [InlineData("Returned", "func f(x: ref/i32 during a, y: ref/i32 during a = x) -> ref/i32 during a => y\nlet n = 7\nlet r = f(n@ref)\nrequire r@follow == 7 else => $abort(\"returned\")")]
    [InlineData("Call", "func same(x: ref/i32 during a) -> ref/i32 during a => x\nfunc f(x: ref/i32 during a, y: ref/i32 during a = same(x)) -> i32 => y@follow\nlet n = 7\nrequire f(n@ref) == 7 else => $abort(\"call\")")]
    [InlineData("Tuple", "func f(x: ref/i32 during a, y: (ref/i32 during a, i32) = (x, 2)) -> i32 => y.0@follow + y.1\nlet n = 7\nrequire f(n@ref) == 9 else => $abort(\"tuple\")")]
    [InlineData("EarlierDefault", "func f(x: ref/i32 during a, y: ref/i32 during a = x, z: ref/i32 during a = y) -> i32 => z@follow\nlet n = 7\nrequire f(n@ref) == 7 else => $abort(\"earlier\")")]
    [InlineData("Nested", Nested)]
    [InlineData("TupleInput", "func f(x: (ref/i32 during a, i32), y: ref/i32 during a = x.0) -> i32 => y@follow\nlet n = 7\nrequire f((n@ref, 2)) == 7 else => $abort(\"tuple input\")")]
    [InlineData("Array", "func f(x: ref/i32 during a, y: [2 of ref/i32 during a] = [x, x]) -> i32 => y[1]@follow\nlet n = 7\nrequire f(n@ref) == 7 else => $abort(\"array\")")]
    [InlineData("Struct", "struct Box {a}\n    public let value: ref/i32 during a\n    public init(value: ref/i32 during a) => self.value = value\nfunc f(x: ref/i32 during a, y: Box{v} = Box.init(x)) -> i32\n    origin v.a == a\n    return y.value@follow\nlet n = 7\nrequire f(n@ref) == 7 else => $abort(\"struct\")")]
    [InlineData("LastUse", "func f(x: ref/i32 during a, y: ref/i32 during a = x) -> ref/i32 during a => y\nvar n = 7\nlet r = f(n@ref)\nlet copied = r@follow\nn = 9\nrequire copied == 7 and n == 9 else => $abort(\"last use\")")]
    public void ExistingExternalReferencesCanBeCopied(string name, string source, string output = "")
        => ScalarEmissionTest.EmitFixture("BorrowedDefault" + name, source, output);

    [Theory]
    [InlineData("func f(x: uniq/i32 during a, y: ref/i32 during a = x) => ()\npublic func main() => ()")]
    public void NewPreparedLoansAreRejectedAtTheirDeclaration(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.DoesNotContain(c.Ownership.Issues, issue => issue.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void APreparedSlotsBorrowCannotFitAnExternalOrigin()
    {
        const string Source = "func f(x: i32, y: ref/i32 during a = x@ref) => ()\npublic func main() => ()";
        var error = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), error.Code);
        Assert.Equal("x@ref", Source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
    }

    [Fact]
    public void CopiedExternalLoansRemainLiveAfterTheCall()
    {
        const string Source = "func f(x: ref/i32 during a, y: ref/i32 during a = x) -> ref/i32 during a => y\nvar n = 7\nlet r = f(n@ref)\nn = 9\n_ = r@follow";
        var path = Path.GetFullPath("borrowed-default.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ComparisonLoanConflict_Kd), error.Code);
        Assert.Equal("n = 9", Source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Contains(error.Related!, related => related.Role == "loan" && related.Span is { } span && Source.Substring(span.Start, span.Length) == "let r = f(n@ref)");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(error.Message, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal((error.Display!.Range, error.Code), (sent.Range, sent.Code));
            Assert.Contains(error.Message, sent.Message, StringComparison.Ordinal);
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ReferenceDefaultsAllocateNoRuntimeStorage()
        => NativeAllocationAudit.WriteFixture("BorrowedDefaultNoAllocation", Nested, 0, 0, 0);

    [TestClass(DisableParallelization = true)]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Fact]
        public void WarmReferenceDefaultsAllocateNothing()
        {
            var c = MinimalEmissionTest.Analyze(Nested);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
            var valid = true;
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
            Assert.True(valid);
        }
    }
}
