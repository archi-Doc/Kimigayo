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

    private const string Lookup = "func lookup<T>(entries: ref/Dictionary<i32, T>, key: ref/i32) -> Option<ref/T during entries> => entries.tryGet(key)\n";

    private const string Compound = "func run(value: uniq/i32 during a)\n    var entries = [1: (value@move, 7)]\n    let found = ";

    private const string FieldHolders = "struct P\n    public var x: i32 = 0\n    public init() => ()\nstruct H2 {a}\n    public var o: objuniq/P during a\n" +
        "struct H3 {a}\n    public var l: uniq/i32 during a\n";

    // The `OldRule` tests below expect the rule before Loan identity by base (A5 S1). Now a Borrow carries the Loans of the
    // references it follows, from the last shared one on (SPEC 15.6.3), among them the Loan that the `index`, `indexUniq`
    // or `tryGet` result keeps on `entries`; clearing or removing from `entries` while `found` is live is then an error.
    // The default pipeline keeps the old rule for these forms until R6-check; the forms whose names Binding gives through the
    // index result's layer (DECISIONS BU-27) have SPEC's verdict on it now (ABorrowPastAResultKeepsTheContainersLoan).
    [Theory]
    [Trait("OldRule", "S1")]
    [InlineData("Tuple", "entries[1].0@follow@ref")]
    public void CompoundStoredReferencesOutliveTheContainer(string name, string selection)
    {
        var source = Lookup + Compound + selection + "\n    entries.clear()\n    require found == 42 else => $abort(\"value\")\nvar value = 42\nrun(value@uniq)";
        ScalarEmissionTest.EmitFixture("DictionaryStoredReferenceCompound" + name, source, string.Empty);
    }

    // SPEC 15.6.3, 4.7.1 (DECISIONS BU-27; formerly OldRule S1 rows, R4-M50 superseded in part): `found` derives from the `index`,
    // `indexUniq` or `tryGet` result and carries that call's Loan on `entries`, which Binding names in `found`'s Type, so clearing or
    // removing from `entries` while `found` is live is a Loan conflict on the default pipeline too.
    [Theory]
    [InlineData("OptionalTuple", Lookup + Compound + "match entries.tryGet(1)\n        .Some(let item) => item.0@follow@ref\n        .None => $abort(\"missing\")\n    entries.clear()\n    require found == 42 else => $abort(\"value\")", "entries.clear()")]
    [InlineData("GenericOptionalTuple", Lookup + Compound + "match lookup(entries@ref, 1)\n        .Some(let item) => item.0@follow@ref\n        .None => $abort(\"missing\")\n    entries.clear()\n    require found == 42 else => $abort(\"value\")", "entries.clear()")]
    [InlineData("Shared", Header + "    let found = entries.index(1)@follow@follow@ref\n    entries.clear()\n    require found == 42 else => $abort(\"value\")", "entries.clear()")]
    [InlineData("Exclusive", Header + "    let found = entries.indexUniq(1)@follow@follow@uniq\n    entries.clear()\n    found@follow = 99", "entries.clear()")]
    [InlineData("Conditional", "func run(value: uniq/i32 during a, enabled: bool)\n    var entries: Dictionary<i32, uniq/i32 during a> = [:]\n    if enabled => _ = entries.tryInsert(1, value@move)\n    let found = entries[1]@follow@ref\n    entries.clear()\n    require found == 42 else => $abort(\"value\")", "entries.clear()")]
    [InlineData("Cleared", Header + "    let view = entries@ref\n    let found = view[1]@follow@ref\n    entries.clear()\n    require found == 42 else => $abort(\"value\")", "entries.clear()")]
    [InlineData("Removed", Header + "    let view = entries@ref\n    let found = view[1]@follow@ref\n    _ = entries.remove(1)\n    require found == 42 else => $abort(\"value\")", "entries.remove(1)")]
    public void ABorrowPastAResultKeepsTheContainersLoan(string name, string source, string mutation)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, name + ": " + MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict() && c.SyntaxOf(x.Subject).ToString() == mutation);
        Assert.DoesNotContain(c.Problems, static x => x.Code == DiagnosticCode.Unsupported_Kd);
        Assert.False(c.Validate(out _));
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
        Assert.DoesNotContain(c.Problems, static x => x.Code == DiagnosticCode.Unsupported_Kd);
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict() && c.SyntaxOf(x.Subject).ToString() == "entries.clear()");
        Assert.DoesNotContain(c.Problems, x => c.SyntaxOf(x.Subject).ToString() == "item.0" || c.SyntaxOf(x.Subject).ToString() == "item.0@follow");
        Assert.False(c.Validate(out _));
        c.ReportAnalysis();
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
    [InlineData("entries.index(1)@follow@follow@ref", "entries.indexUniq(1)@follow@follow = 99")]
    [InlineData("entries.indexUniq(1)@follow@follow@uniq", "let read: i32 = entries.index(1)@follow@follow")]
    public void DirectPlaceEntriesCannotBypassStoredChildren(string child, string access)
    {
        var source = Header + "    let found = " + child + "\n    " + access + "\n    require found == 42 else => $abort(\"value\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Problems, static x => x.Code.IsLoanConflict());
        Assert.DoesNotContain(c.Problems, static x => x.Code == DiagnosticCode.Unsupported_Kd);
        Assert.False(c.Validate(out _));
    }

    [Theory]
    [InlineData("ref", "entries[1]@follow = 99")]
    [InlineData("uniq", "let snapshot: i32 = entries[1]@follow")]
    public void LocalStoredParentsCannotBypassTheirChildren(string mode, string access)
    {
        var source = "var value = 42\nvar entries = [1: value@uniq]\nlet child = entries[1]@follow@" + mode + "\n" + access + "\nrequire child == 42 else => $abort(\"value\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Problems, static x => x.Code.IsLoanConflict());
        Assert.DoesNotContain(c.Problems, static x => x.Code == DiagnosticCode.Unsupported_Kd);
        Assert.False(c.Validate(out _));
    }

    // The Cleared and Removed forms keep the index result's Loan (ABorrowPastAResultKeepsTheContainersLoan).
    [Theory]
    [Trait("Purpose", "Allocation")]
    [InlineData("Owned", "entries", "")]
    [InlineData("Shared", "view", "let view = entries@ref\n    ")]
    [InlineData("Exclusive", "view", "let view = entries@uniq\n    ")]
    public void ExternalReborrowsDoNotDependOnTheReferenceSlot(string name, string receiver, string prefix)
    {
        var source = Header + "    " + prefix + "let found = " + receiver + "[1]@follow@ref\n    require found == 42 else => $abort(\"value\")\nvar value = 42\nrun(value@uniq)";
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
        Assert.False(c.Analysis.IsVerified);
        Assert.DoesNotContain(c.Problems, static x => x.Code == DiagnosticCode.Unsupported_Kd);
        Assert.Contains(c.Problems, x => c.SyntaxOf(x.Subject).ToString()!.Contains("entries[1]", StringComparison.Ordinal));
        Assert.False(c.Validate(out _));
    }

    // SPEC 4.7.1, 15.6.2 (DECISIONS BU-8): on the MIR a write, an exclusive borrow or an exclusive Reborrow through the stored reference
    // selects indexUniq, whose exclusive receiver Loan conflicts with a live shared borrow of the Dictionary; a read through it does not.
    [Theory]
    [InlineData("change(entries[1])", true)]
    [InlineData("entries[1]@follow = 99", true)]
    [InlineData("let child = entries[1]@follow@uniq\n    child@follow = 3", true)]
    [InlineData("let read: i32 = entries[1]@follow", false)]
    public void AnExclusiveUseConflictsWithASharedBorrowOnTheMir(string use, bool conflicts)
    {
        var c = MirBuildTest.Compile("func inspect<T>(value: ref/T) => ()\n" + Header + "    let view = entries@ref\n    " + use + "\n    inspect(view)\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Equal(conflicts, program.Problems.All.ToArray().Any(static x => x.Code.IsLoanConflict() && x.Subject.Module == 0));
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

    // Expects the old rule (PLAN G231, until R6-check): SPEC 3.5.1 forbids a second Reborrow of a stored reference reached through an
    // exclusive parameter's Field, and a write through that Field, while a Reborrow of it in a Dictionary literal is live. The old analysis
    // accepts these forms, as it accepts their Array and `let` twins (STATUS Loans); A5 rejects them with a ComparisonLoanConflict_Kd at the
    // second Reborrow or at the write (DECISIONS R4-M136: each row's ledger key and MIR target side). G231's fix flips the old side.
    [Theory]
    [Trait("OldRule", "ledger:1b737830afa3:9")]
    [Trait("OldRule", "ledger:bf266bcef6ad:10")]
    [InlineData("    let d = [1: h.l, 2: h.l]\n    _ = d@move\n", "2: h.l]", "h.l")]
    [InlineData("    let d = [1: h.l]\n    h.l@follow = 3\n    _ = d@move\n", "h.l@follow = 3", null)]
    public void AFieldReborrowConflictInADictionaryLiteralIsG231sKnownAcceptance(string body, string conflict, string? subject)
    {
        var c = AnalyzedFieldReborrow("func f(h: uniq/(H3 during a) during s) -> i32\n" + body);
        MirTargetSide.Check(c, (DiagnosticCode.ComparisonLoanConflict_Kd, MirTargetSide.At(conflict, subject)));
    }

    // The `objuniq/P` Field's form of AFieldReborrowConflictInADictionaryLiteralIsG231sKnownAcceptance. Each value, adapted at the
    // literal's value Type, is Reborrowed as the `uniq` Field's is (PLAN G232), so A5 reports only the conflict, no Origin relation.
    [Fact]
    [Trait("OldRule", "ledger:e877352d188e:9")]
    public void AnObjectViewFieldReborrowConflictInADictionaryLiteralIsG231sKnownAcceptance()
    {
        var c = AnalyzedFieldReborrow("func f(h: uniq/(H2 during a) during s) -> i32\n    let d = [1: h.o, 2: h.o]\n    _ = d@move\n");
        MirTargetSide.Check(c, (DiagnosticCode.ComparisonLoanConflict_Kd, MirTargetSide.At("2: h.o]", "h.o")));
    }

    // The Conditional form is ABorrowPastAResultKeepsTheContainersLoan's.
    [Theory]
    [Trait("OldRule", "S1")]
    [InlineData("Literal", "var entries = [1: value@move]")]
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

    // Every row but SharedCopy follows the old rule, as above.
    [Theory]
    [Trait("OldRule", "S1")]
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
        Assert.Contains(c.Problems, static x => x.Code.IsLoanConflict());
        Assert.DoesNotContain(c.Problems, static x => x.Code == DiagnosticCode.Unsupported_Kd);
        var valid = MinimalEmissionTest.Analyze(source.Replace("    entries[1]@follow = 99\n", string.Empty, StringComparison.Ordinal));
        Assert.True(valid.Analysis.IsVerified, MinimalEmissionTest.Describe(valid, null));
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
        c.ReportAnalysis();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        // SPEC 15.6.2, 15.6.3 (DECISIONS BU-27): each call conflicts with `found` at its argument's reservation and at its index's indexUniq
        // receiver, an exclusive Borrow of `entries` while `found` keeps the index result's shared Loan on it.
        Assert.Equal(independent ? 4 : 2, result.Diagnostics.Length);
        for (var i = 0; i < result.Diagnostics.Length; i++)
        {
            var error = result.Diagnostics[i];
            Assert.Equal("CallActivationConflict_Kd", error.Code);
            Assert.Equal(DiagnosticSeverity.Error, error.Severity);
            Assert.Equal(i % 2 == 0 ? "change(entries[1])" : "entries[1]", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
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

    // The valid twins of ABorrowPastAResultKeepsTheContainersLoan's forms: `entries` is cleared after `found`'s last use, or not at all
    // (DECISIONS BU-27).
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoredInputAnalysisReusesItsStorage(bool optionalTuple)
    {
        var c = MinimalEmissionTest.Analyze(optionalTuple
            ? "func run(value: uniq/i32 during a)\n    var entries = [1: (value@move, 7)]\n    let found = match entries.tryGet(1)\n        .Some(let item) => item.0@follow@ref\n        .None => $abort(\"missing\")\n    require found == 42 else => $abort(\"value\")"
            : Header + "    let found = entries[1]@follow@ref\n    require found == 42 else => $abort(\"value\")\n    entries.clear()");
        Assert.True(c.Analyze().IsVerified);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Analyze()));
    }

    // A G231 row's analyzed Program.kimi compilation, the function returning 0 after the given lines, and its old side: accepted.
    private static Compilation AnalyzedFieldReborrow(string function)
    {
        var c = MinimalEmissionTest.Analyze(FieldHolders + function + "    return 0\npublic func main() => ()\n", "Program.kimi");
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
        return c;
    }
}
