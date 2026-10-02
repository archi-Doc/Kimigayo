// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class DependentDictionaryLiteralTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Value", "let value = 42\nlet entries = [1: value@ref]\nrequire entries[1] == 42 else => $abort(\"value\")")]
    [InlineData("Key", "let key = 1\nlet entries = [key@ref: 42]\nlet search = key@ref\nrequire entries[search@ref] == 42 else => $abort(\"key\")")]
    [InlineData("Both", "let key = 1\nlet value = 42\nlet entries = [key@ref: value@ref]\nlet search = key@ref\nrequire entries[search@ref] == 42 else => $abort(\"both\")")]
    [InlineData("MultipleOrigins", "let first = 1\nlet second = 2\nlet a = 41\nlet b = 42\nlet entries = [first@ref: a@ref, second@ref: b@ref]\nlet searchFirst = first@ref\nlet searchSecond = second@ref\nrequire entries[searchFirst@ref] == 41 and entries[searchSecond@ref] == 42 else => $abort(\"origins\")")]
    [InlineData("BeforePlacement", "func key(value: uniq/i32) -> i32\n    value@follow = 42\n    return 1\nvar value = 0\nlet entries = [key(value@uniq): value@ref]\nrequire entries[1] == 42 else => $abort(\"placed\")")]
    [InlineData("NamedInput", "func run(value: uniq/i32 during a)\n    let entries = [1: value@follow@ref]\n    require entries[1] == 42 else => $abort(\"value\")\n    value@follow = 99\nvar value = 42\nrun(value@uniq)\nrequire value == 99 else => $abort(\"changed\")")]
    [InlineData("IndependentInputs", "func run(left: uniq/i32 during a, right: uniq/i32 during a)\n    let entries = [1: left@follow@ref]\n    right@follow = 99\n    require entries[1] == 42 else => $abort(\"value\")\nvar left = 42\nvar right = 0\nrun(left@uniq, right@uniq)\nrequire right == 99 else => $abort(\"changed\")")]
    [InlineData("Generic", "func make<K, V>(key: K, value: V) -> Dictionary<K, V>\n    K is Equatable\n    return [key@move: value@move]\nlet key = 1\nlet value = 42\nlet entries = make(key@ref, value@ref)\nlet search = key@ref\nrequire entries[search@ref] == 42 else => $abort(\"generic\")")]
    [InlineData("Exclusive", "var value = 0\nvar entries = [1: value@uniq]\nmatch entries.remove(1)\n    .Some((let key, let item)) => item@follow = 42\n    .None => $abort(\"missing\")\nrequire value == 42 else => $abort(\"exclusive\")")]
    [InlineData("MovedExclusive", "func run(value: uniq/i32 during a)\n    var entries = [1: value@move]\n    match entries.remove(1)\n        .Some((let key, let item)) => item@follow = 42\n        .None => $abort(\"missing\")\nvar value = 0\nrun(value@uniq)\nrequire value == 42 else => $abort(\"moved\")")]
    [InlineData("Tuple", "let value = 42\nlet entries = [1: (value@ref, \"ok\")]\nrequire entries[1].0 == 42 else => $abort(\"tuple\")")]
    public void BorrowedEntriesUseTheOrdinaryStoragePath(string name, string source)
        => ScalarEmissionTest.EmitFixture("DependentDictionaryLiteral" + name, source, string.Empty);

    [Theory]
    [InlineData("var value = 42\nlet entries = [1: value@ref]\nvalue = 99\nrequire entries[1] == 42 else => $abort(\"value\")")]
    [InlineData("func run(value: uniq/i32 during a)\n    let entries = [1: value@follow@ref]\n    value@follow = 99\n    require entries[1] == 42 else => $abort(\"value\")")]
    [InlineData("func change(key: uniq/i32) -> i32\n    key@follow = 2\n    return 42\nvar key = 1\nlet entries = [key@ref: change(key@uniq)]")]
    [InlineData("func change(value: uniq/i32) -> i32\n    value@follow = 99\n    return 2\nvar value = 42\nlet entries = [1: value@ref, change(value@uniq): value@ref]\nrequire entries[1] == 42 else => $abort(\"value\")")]
    public void StoredSharedReferencesPreventMutationUntilTheirLastUse(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void PartialConstructionDestroysOwnedValuesBeforeTheReferencedKey()
    {
        const string source = """
            struct Item
                public let name: string
                public init(name: string) => self.name = name@move
                drop => Console.writeLine(self.name)
            func run()
                let first = 1
                let second = 2
                let entries = [first@ref: Item.init("first"), second@ref: (label value: do
                    return
                    exit to value Item.init("bad")
                )]
            run()
            Console.writeLine("after")
            """;
        ScalarEmissionTest.EmitFixture("DependentDictionaryLiteralPartial", source, "first\nafter\n");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StoredDestructorsKeepTheirReferentsLive(bool mutate, bool dictionary)
    {
        var source = "struct Observer {a}\n    let value: ref/i32 during a\n    public init(value: ref/i32 during a) => self.value = value\n    drop => require self.value == 42 else => $abort(\"drop\")\nvar value = 42\nlet entries = [" + (dictionary ? "1: " : string.Empty) + "Observer.init(value@ref)]\n" + (mutate ? "value = 99" : "require entries.length == 1 else => $abort(\"length\")");
        if (!mutate)
        {
            ScalarEmissionTest.EmitFixture("DependentDictionaryLiteralObserver" + dictionary, source, string.Empty);
            return;
        }

        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void AStoredExclusiveInputCannotBeMovedAgain()
    {
        var c = MinimalEmissionTest.Analyze("func run(value: uniq/i32 during a)\n    let entries = [1: value@move]\n    let again = value@move");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.PossiblyMovedUse);
    }

    [Fact]
    public void ALocalReferentCannotEscapeThroughTheDictionaryResult()
    {
        var c = MinimalEmissionTest.Analyze("func make() -> Dictionary<i32, ref/i32 during static>\n    let value = 42\n    return [1: value@ref]");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void PublicConflictExplainsTheCallAndTheRetainingCollection()
    {
        const string source = "func change(value: uniq/i32) => value@follow = 99\nvar value = 42\nlet entries = [1: value@ref]\nchange(value@uniq)\nrequire entries[1] == 42 else => $abort(\"value\")";
        var path = Path.GetFullPath("literal-loan.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete);
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("CallActivationConflict_Kd", error.Code);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal("change(value@uniq)", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        var retained = Assert.Single(error.Related!);
        Assert.Equal("loan", retained.Role);
        Assert.Contains("entries", source.Substring(retained.Span!.Value.Start, retained.Span.Value.Length), StringComparison.Ordinal);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("value retaining the conflicting loan", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, true)[identity]);
        Assert.Equal(error.Display!.Range, sent.Range);
        Assert.Equal(retained.Range, Assert.Single(sent.RelatedInformation!).Location.Range);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void SharedEntriesAllocateOnlyTheEntryBuffer()
        => NativeAllocationAudit.WriteFixture("DependentDictionaryLiteralCost", "let key = 1\nlet value = 42\nlet entries = [key@ref: value@ref]\nlet search = key@ref\nrequire entries[search@ref] == 42 else => $abort(\"value\")", 1, 1, 128);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void DependentLiteralCompilationReusesItsStorage()
    {
        var c = MinimalEmissionTest.Analyze("func run(value: uniq/i32 during a)\n    let entries = [1: value@follow@ref]\n    require entries[1] == 42 else => $abort(\"value\")\nvar value = 42\nrun(value@uniq)");
        void Compile()
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        for (var i = 0; i < 8; i++)
        {
            Compile();
        }

        Assert.Equal(0, AllocationMeasurement.Measure(Compile));
        var original = CompilationTestHelper.WriteIr(c);
        var loaded = CompilationTestHelper.Reload(c);
        Assert.True(loaded.Bind().IsComplete);
        loaded.Binding.CheckStartup(OutputKind.Application);
        Assert.True(loaded.Ownership.Analyze().IsVerified);
        Assert.Equal(original, CompilationTestHelper.WriteIr(loaded));
    }
}
