// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class DictionaryStoredReferenceTest(ITestOutputHelper output)
{
    private const string Header = "func change(value: uniq/i32) => value@follow = 99\nfunc run(value: uniq/i32 during a)\n    var entries: Dictionary<i32, uniq/i32 during a> = [:]\n    _ = entries.tryInsert(1, value@move)\n";

    [Theory]
    [InlineData("ref", "entries[1]@follow = 99")]
    [InlineData("uniq", "let snapshot: i32 = entries[1]@follow")]
    public void LocalStoredParentsCannotBypassTheirChildren(string mode, string access)
    {
        var source = "var value = 42\nvar entries = [1: value@uniq]\nlet child = entries[1]@follow@" + mode + "\n" + access + "\nrequire child == 42 else => $abort(\"value\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [Trait("Purpose", "Allocation")]
    [InlineData("Owned", "entries", "")]
    [InlineData("Shared", "view", "let view = entries@ref\n    ")]
    [InlineData("Exclusive", "view", "let view = entries@uniq\n    ")]
    [InlineData("Cleared", "view", "let view = entries@ref\n    ", "entries.clear()")]
    [InlineData("Removed", "view", "let view = entries@ref\n    ", "_ = entries.remove(1)")]
    public void ExternalReborrowsDoNotDependOnTheReferenceSlot(string name, string receiver, string prefix, string after = "")
    {
        var source = Header + "    " + prefix + "let found = " + receiver + "[1]@follow@ref\n    " + after + "\n    require found == 42 else => $abort(\"value\")\nvar value = 42\nrun(value@uniq)";
        NativeAllocationAudit.WriteFixture("DictionaryStoredReference" + name, source, 1, 1, 128);
    }

    [Theory]
    [InlineData("change(entries[1])")]
    [InlineData("entries[1]@follow = 99")]
    public void StoredParentAccessIsSuspendedWhileTheChildIsLive(string mutation)
    {
        var source = Header + "    let view = entries@ref\n    let found = view[1]@follow@ref\n    " + mutation + "\n    require found == 42 else => $abort(\"value\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.Contains(c.Ownership.Issues, x => x.Source.ToString()!.Contains("entries[1]", StringComparison.Ordinal));
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("Call", "change(entries[1])")]
    [InlineData("Store", "entries[1]@follow = 99")]
    public void ParentAccessResumesAfterTheChildsLastUse(string name, string mutation)
    {
        var source = Header + "    let found = entries[1]@follow@ref\n    require found == 42 else => $abort(\"value\")\n    " + mutation + "\nvar value = 42\nrun(value@uniq)\nrequire value == 99 else => $abort(\"changed\")";
        ScalarEmissionTest.EmitFixture("DictionaryStoredReferenceResume" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("Literal", "var entries = [1: value@move]")]
    [InlineData("Conditional", "var entries: Dictionary<i32, uniq/i32 during a> = [:]\n    if enabled => _ = entries.tryInsert(1, value@move)")]
    public void ConstructionAndConditionalStorageRetainTheInput(string name, string construction)
    {
        var source = "func run(value: uniq/i32 during a, enabled: bool)\n    " + construction + "\n    let found = entries[1]@follow@ref\n    entries.clear()\n    require found == 42 else => $abort(\"value\")\nvar value = 42\nrun(value@uniq, true)";
        ScalarEmissionTest.EmitFixture("DictionaryStoredReference" + name, source, string.Empty);
    }

    [Fact]
    public void EqualOriginsKeepIndependentStoredInputsSeparate()
    {
        const string source = "func run(left: uniq/i32 during a, right: uniq/i32 during a)\n    var first: Dictionary<i32, uniq/i32 during a> = [:]\n    var second: Dictionary<i32, uniq/i32 during a> = [:]\n    _ = first.tryInsert(1, left@move)\n    _ = second.tryInsert(1, right@move)\n    let found = first[1]@follow@ref\n    second[1]@follow = 99\n    require found == 42 else => $abort(\"value\")\nvar left = 42\nvar right = 0\nrun(left@uniq, right@uniq)\nrequire right == 99 else => $abort(\"changed\")";
        ScalarEmissionTest.EmitFixture("DictionaryStoredReferenceIndependent", source, string.Empty);
    }

    [Theory]
    [InlineData("Local", "var value = 42\nvar entries = [1: value@uniq]\nlet found = entries[1]@follow@ref\nentries.clear()\nrequire found == 42 else => $abort(\"value\")")]
    [InlineData("SharedCopy", "let value = 42\nvar entries = [1: value@ref]\nlet found = entries[1]\nentries.clear()\nrequire found == 42 else => $abort(\"value\")")]
    [InlineData("String", "var value = \"value\"\nvar entries = [1: value@uniq]\nlet found = entries[1]@follow@ref\nentries.clear()\nrequire found == \"value\" else => $abort(\"value\")")]
    [InlineData("ExclusiveChild", "var value = 0\nvar entries = [1: value@uniq]\nlet found = entries[1]@follow@uniq\nentries.clear()\nfound@follow = 42\nrequire value == 42 else => $abort(\"value\")")]
    public void LocalInputsAndSharedCopiesOutliveTheSlot(string name, string source)
        => ScalarEmissionTest.EmitFixture("DictionaryStoredReference" + name, source, string.Empty);

    [Fact]
    public void OrdinaryWritableArgumentsUseTheSameStorageContract()
    {
        const string source = "func store(target: uniq/Dictionary<i32, uniq/i32 during a>, value: uniq/i32 during a)\n    _ = target.tryInsert(1, value@move)\nfunc run(value: uniq/i32 during a)\n    var entries: Dictionary<i32, uniq/i32 during a> = [:]\n    store(entries@uniq, value@move)\n    let found = entries[1]@follow@ref\n    entries[1]@follow = 99\n    require found == 42 else => $abort(\"value\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        var valid = MinimalEmissionTest.Analyze(source.Replace("    entries[1]@follow = 99\n", string.Empty, StringComparison.Ordinal));
        Assert.True(valid.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(valid, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicConflictPointsToTheMutationAndRetainedChild(bool independent)
    {
        var source = Header + "    let found = entries[1]@follow@ref\n    change(entries[1])\n" +
            (independent ? "    change(entries[1])\n" : string.Empty) + "    require found == 42 else => $abort(\"value\")";
        var path = Path.GetFullPath("stored-dictionary-loan.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        Assert.Equal(independent ? 2 : 1, result.Diagnostics.Length);
        foreach (var error in result.Diagnostics)
        {
            Assert.Equal("CallActivationConflict_Kd", error.Code);
            Assert.Equal(DiagnosticSeverity.Error, error.Severity);
            Assert.Equal("change(entries[1])", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
            var retained = Assert.Single(error.Related!);
            Assert.Equal("loan", retained.Role);
            Assert.Contains("found", source.Substring(retained.Span!.Value.Start, retained.Span.Value.Length), StringComparison.Ordinal);
        }

        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("value retaining the conflicting loan", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sentRecords = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            Assert.Equal(result.Diagnostics.Length, sentRecords.Length);
            for (var i = 0; i < sentRecords.Length; i++)
            {
                var sent = sentRecords[i];
                var error = result.Diagnostics[i];
                Assert.Equal(error.Display!.Range, sent.Range);
                if (related)
                {
                    Assert.Equal(error.Related![0].Range, Assert.Single(sent.RelatedInformation!).Location.Range);
                }
                else
                {
                    Assert.Contains("value retaining the conflicting loan", sent.Message, StringComparison.Ordinal);
                }
            }
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void StoredInputAnalysisReusesItsStorage()
    {
        var c = MinimalEmissionTest.Analyze(Header + "    let found = entries[1]@follow@ref\n    entries.clear()\n    require found == 42 else => $abort(\"value\")");
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Ownership.Analyze()));
    }
}
