// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Verification;
using Xunit;

namespace XunitTest;

// SPEC 15.3.1, 15.6.3: fitting an acquired reference to another Origin preserves its actual parent.
public class FixedOriginRetentionTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, "ref")]
    [InlineData(true, "ref")]
    [InlineData(false, "uniq")]
    [InlineData(true, "uniq")]
    public void AnAnnotatedChildStillSuspendsItsParent(bool annotation, string mode)
    {
        var source = LocalChild(annotation, true, mode);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData(false, "ref")]
    [InlineData(true, "ref")]
    [InlineData(false, "uniq")]
    [InlineData(true, "uniq")]
    public void AnEndedChildRestoresItsParent(bool annotation, string mode)
    {
        var source = LocalChild(annotation, false, mode) + "\nvar value: i32 = 41\nlet independent: i32 = 7\n" +
            "require use(value@uniq, independent@ref) == 41 else => $abort(\"parent\")";
        ScalarEmissionTest.EmitFixture("FixedOriginRetentionEnded" + mode + annotation, source, string.Empty);
    }

    [Theory]
    [InlineData("ref")]
    [InlineData("uniq")]
    public void AnAnnotationDoesNotBorrowTheIndependentInput(string mode)
    {
        var source = "func use(z: uniq/i32, x: uniq/i32) -> i32\n    origin z outlives x\n    let y: " + mode +
            "/i32 during x = " + (mode == "ref" ? "z@follow@ref" : "z") +
            "\n    x@follow = 9\n    return y@follow\nvar z: i32 = 41\nvar x: i32 = 7\n" +
            "require use(z@uniq, x@uniq) == 41 else => $abort(\"source\")\nrequire x == 9 else => $abort(\"independent\")";
        ScalarEmissionTest.EmitFixture("FixedOriginRetentionIndependent" + mode, source, string.Empty);
    }

    [Fact]
    public void TheConflictIdentifiesTheActualParentAndRetainedChild()
    {
        var source = LocalChild(true, true, "uniq");
        var path = Path.GetFullPath("fixed-origin-retention.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete);
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("ComparisonLoanConflict_Kd", error.Code);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal("z", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal("This operation conflicts with an active loan", error.Message);
        var retained = Assert.Single(error.Related!);
        Assert.Equal("loan", retained.Role);
        Assert.Contains("y", source.Substring(retained.Span!.Value.Start, retained.Span.Value.Length), StringComparison.Ordinal);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("value retaining the conflicting loan", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            if (related)
            {
                Assert.Equal(retained.Range, Assert.Single(sent.RelatedInformation!).Location.Range);
            }
            else
            {
                Assert.Contains("value retaining the conflicting loan", sent.Message, StringComparison.Ordinal);
            }
        }
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmLocalTransfersAllocateNothing(bool live)
    {
        var c = MinimalEmissionTest.Analyze(LocalChild(true, live, "uniq") + "\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified == !live, iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    [Fact]
    public void EditingTheAcquisitionRevokesItsPreviousLoan()
    {
        var source = LocalChild(true, true, "ref") + "\npublic func main() => ()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.False(c.Ownership.Result.IsVerified);
        var local = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().Single(static x => x.NameKoto.IdentifierName == "y");
        foreach (var parent in new[] { false, true })
        {
            var donor = MinimalEmissionTest.Analyze(parent ? source : source.Replace("= z@follow@ref", "= x", StringComparison.Ordinal));
            var initializer = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<VariableKoto>().Single(static x => x.NameKoto.IdentifierName == "y").InitializerKoto!;
            Assert.True(KotoHelper.Replace(local, local.InitializerKoto!, initializer));
            Assert.True(c.Bind().IsComplete);
            Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
            Assert.Equal(!parent, c.Ownership.Analyze().IsVerified);
            Assert.Equal(!parent, c.Emission.WriteIr(TextWriter.Null, out _));
        }
    }

    private static string LocalChild(bool annotation, bool live, string mode)
        => "func use(z: uniq/i32, x: ref/i32) -> i32\n    origin z outlives x\n    let y" +
            (annotation ? ": " + mode + "/i32 during x" : string.Empty) + " = " + (mode == "ref" ? "z@follow@ref" : "z") + "\n" +
            (live ? "    z@follow = 41\n    return y@follow" : "    require y@follow == 41 else => $abort(\"child\")\n    z@follow = 41\n    return z@follow");
}
