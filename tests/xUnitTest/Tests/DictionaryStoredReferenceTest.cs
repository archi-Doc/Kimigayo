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
    [InlineData("Tuple", "entries[1].0@follow@ref")]
    [InlineData("OptionalTuple", "match entries.tryGet(1)\n        .Some(let item) => item.0@follow@ref\n        .None => $abort(\"missing\")")]
    [InlineData("GenericOptionalTuple", "match lookup(entries@ref, 1)\n        .Some(let item) => item.0@follow@ref\n        .None => $abort(\"missing\")")]
    public void CompoundStoredReferencesOutliveTheContainer(string name, string selection)
    {
        var source = "func lookup<T>(entries: ref/Dictionary<i32, T>, key: ref/i32) -> Option<ref/T during entries> => entries.tryGet(key)\n" +
            "func run(value: uniq/i32 during a)\n    var entries = [1: (value@move, 7)]\n    let found = " + selection +
            "\n    entries.clear()\n    require found == 42 else => $abort(\"value\")\nvar value = 42\nrun(value@uniq)";
        ScalarEmissionTest.EmitFixture("DictionaryStoredReferenceCompound" + name, source, string.Empty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalTupleSlotsRetainTheContainerUntilLastUse(bool independent)
    {
        var source = "func run(value: uniq/i32 during a)\n    var entries = [1: (value@move, 7)]\n" +
            "    match entries.tryGet(1)\n        .Some(let item)\n            entries.clear()\n" +
            (independent ? "            entries.clear()\n" : string.Empty) +
            "            require item.0 == 42 else => $abort(\"value\")\n        .None => ()";
        var path = Path.GetFullPath("optional-tuple-slot.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict && x.Source.ToString() == "entries.clear()");
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Source.ToString() == "item.0" || x.Source.ToString() == "item.0@follow");
        Assert.False(c.Emission.Validate(out _));
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        Assert.Equal(independent ? 2 : 1, result.Diagnostics.Length);
        foreach (var error in result.Diagnostics)
        {
            Assert.Equal("CallActivationConflict_Kd", error.Code);
            Assert.Equal("entries.clear()", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
            Assert.NotEmpty(error.Related!);
        }

        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            Assert.Equal(result.Diagnostics.Length, sent.Length);
            for (var i = 0; i < sent.Length; i++)
            {
                Assert.Equal(result.Diagnostics[i].Display!.Range, sent[i].Range);
                if (related)
                {
                    Assert.NotEmpty(sent[i].RelatedInformation!);
                }
                else
                {
                    Assert.Contains("value retaining the conflicting loan", sent[i].Message, StringComparison.Ordinal);
                }
            }
        }
    }

    [Theory]
    [InlineData("Shared", "index", "ref", "require found == 42 else => $abort(\"value\")")]
    [InlineData("Exclusive", "indexUniq", "uniq", "found@follow = 99")]
    public void DirectPlaceEntriesPreserveTheStoredCapability(string name, string entry, string mode, string use)
    {
        var source = Header + "    let found = entries." + entry + "(1)@follow@" + mode + "\n    entries.clear()\n    " + use +
            "\nvar value = 42\nrun(value@uniq)\nrequire value == " + (mode == "uniq" ? "99" : "42") + " else => $abort(\"result\")";
        ScalarEmissionTest.EmitFixture("DictionaryStoredReferenceDirect" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("entries.index(1)@follow@ref", "entries.indexUniq(1)@follow = 99")]
    [InlineData("entries.indexUniq(1)@follow@uniq", "let read: i32 = entries.index(1)@follow")]
    public void DirectPlaceEntriesCannotBypassStoredChildren(string child, string access)
    {
        var source = Header + "    let found = " + child + "\n    " + access + "\n    require found == 42 else => $abort(\"value\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

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

    // SPEC 15.6.4: a referent stored in a Dictionary stays borrowed from the store until the Dictionary's last use, also on the
    // next iteration of a loop, whose earlier operations follow the store at run time. A write through the reference before the
    // store and the second exclusive store of the same reference are each a conflict.
    [Fact]
    public void AStoreInALoopRetainsTheReferentOnLaterIterations()
    {
        const string Source = "func run(value: uniq/i32 during a)\n    var entries: Dictionary<i32, uniq/i32 during a> = [:]\n    var i: i32 = 0\n    while i < 2\n" +
            "        value@follow = 7\n        _ = entries.tryInsert(i, value)\n        i += 1\n    let found = entries[0]@follow@ref\npublic func main() => ()\n";
        var records = DiagnosticCorpus.Check(Source).Diagnostics.OrderBy(static x => x.Span!.Value.Start).ToArray();
        Assert.Equal(["ComparisonLoanConflict_Kd", "CallActivationConflict_Kd"], records.Select(static x => x.Code));
        Assert.Equal(Source.IndexOf("value@follow = 7", StringComparison.Ordinal), records[0].Span!.Value.Start);
        Assert.Equal("value", Source.Substring(records[0].Span!.Value.Start, records[0].Span!.Value.Length));
        Assert.Equal("entries.tryInsert(i, value)", Source.Substring(records[1].Span!.Value.Start, records[1].Span!.Value.Length));
    }

    // A Dictionary declared in the loop drops the stored reference at the end of each iteration, and a write after the
    // Dictionary's last use is unconstrained.
    [Fact]
    public void AStoredReferenceEndsWithItsHolder()
    {
        const string Source = """
            func run(value: uniq/i32 during a) -> i32
                var total: i32 = 0
                var i: i32 = 0
                while i < 2
                    var entries: Dictionary<i32, uniq/i32 during a> = [:]
                    value@follow = 7 + i
                    _ = entries.tryInsert(0, value)
                    let found = entries[0]@follow@ref
                    total += found
                    i += 1
                value@follow = 1
                return total
            var number: i32 = 0
            let total = run(number@uniq)
            require total == 15 and number == 1 else => $abort("value")
            Console.writeLine("stored")
            """;
        ScalarEmissionTest.EmitFixture("DictionaryStoredReferenceLoopHolder", Source, "stored\n");
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoredInputAnalysisReusesItsStorage(bool optionalTuple)
    {
        var c = MinimalEmissionTest.Analyze(optionalTuple
            ? "func run(value: uniq/i32 during a)\n    var entries = [1: (value@move, 7)]\n    let found = match entries.tryGet(1)\n        .Some(let item) => item.0@follow@ref\n        .None => $abort(\"missing\")\n    entries.clear()\n    require found == 42 else => $abort(\"value\")"
            : Header + "    let found = entries[1]@follow@ref\n    entries.clear()\n    require found == 42 else => $abort(\"value\")");
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Ownership.Analyze()));
    }
}
