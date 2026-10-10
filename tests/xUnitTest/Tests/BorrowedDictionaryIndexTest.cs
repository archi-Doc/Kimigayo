// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class BorrowedDictionaryIndexTest(ITestOutputHelper testOutput)
{
    // SPEC 4.6.9, 10.2: K may itself be ref/i32, so the search argument is ref/(ref/i32).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferenceKeyFailuresKeepTheirActualCause(bool independent)
    {
        const string Prefix = "let key = 1\nlet entries = [key@ref: 42]\n";
        var source = Prefix + "let found = entries[key@ref]\n" + (independent ? "let missing = absent\n" : string.Empty);
        var path = Path.GetFullPath("DictionaryReferenceKey.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        c.Ownership.ControlFlow!.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var errors = result.Diagnostics.Where(static x => x.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Equal(independent ? 2 : 1, errors.Length);
        var error = Assert.Single(errors, static x => x.Code == "NoApplicableOverload_Kd");
        Assert.Equal("entries[key@ref]", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.DoesNotContain(errors, static x => x.Code.StartsWith("Unsupported", StringComparison.Ordinal));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("entries[key@ref]", console.Text, StringComparison.Ordinal);
        testOutput.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, true)[identity];
        Assert.Equal(error.Display!.Range, Assert.Single(sent, static x => x.Code == "NoApplicableOverload_Kd").Range);
        var valid = MinimalEmissionTest.Analyze(Prefix + "let search = key@ref\nrequire entries[search@ref] == 42 else => $abort(\"key\")");
        Assert.True(valid.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(valid, null));
    }

    [Theory]
    [InlineData("Read", "func read(values: ref/Dictionary<i32, i32>) -> i32 => values[1]\nlet values = [1: 42]\nrequire read(values) == 42 else => $abort(\"value\")")]
    [InlineData("SharedValue", "func read(values: ref/Dictionary<i32, string>) -> ref/string during values => values[1]@ref\nlet values = [1: \"ok\"]\nConsole.writeLine(read(values))", "ok\n")]
    [InlineData("ExclusiveRead", "func read(values: uniq/Dictionary<i32, i32>) -> i32 => values[1]\nvar values = [1: 42]\nrequire read(values@uniq) == 42 else => $abort(\"value\")")]
    [InlineData("Replace", "func change(values: uniq/Dictionary<i32, i32>) => values[1] = 42\nvar values = [1: 2]\nchange(values@uniq)\nrequire values[1] == 42 else => $abort(\"value\")")]
    [InlineData("Compound", "func change(values: uniq/Dictionary<i32, i32>) => values[1] += 40\nvar values = [1: 2]\nchange(values@uniq)\nrequire values[1] == 42 else => $abort(\"value\")")]
    [InlineData("Nested", "let values = [1: [2: 42]]\nrequire values[1][2] == 42 else => $abort(\"value\")")]
    public void ReferencedHandlesUseTheSameElementPlaceRules(string name, string source, string stdout = "")
        => ScalarEmissionTest.EmitFixture("BorrowedDictionaryIndex" + name, source, stdout);

    [Theory]
    [InlineData("TupleKey", "func read<K, V>(values: ref/Dictionary<K, V>, key: ref/K) -> ref/V during values\n    K is Equatable\n    return values[key]@ref\nlet values = [(1, 2): \"ok\"]\nConsole.writeLine(read(values, (1, 2)))", "ok\n")]
    [InlineData("GenericCopy", "func read<K, V>(values: ref/Dictionary<K, V>, key: ref/K) -> V\n    K is Equatable\n    V is Copy\n    return values[key]\nlet values = [1: (2, 3)]\nlet result = read(values, 1)\nrequire result.0 == 2 and result.1 == 3 else => $abort(\"value\")", "")]
    [InlineData("Unit", "func read(values: ref/Dictionary<i32, ()>) => values[1]\nlet values = [1: ()]\nread(values)", "")]
    [InlineData("ExclusiveResult", "func get(values: uniq/Dictionary<i32, i32>) -> uniq/i32 during values => values[1]@uniq\nvar values = [1: 2]\nlet value = get(values@uniq)\nvalue@follow = 42\nrequire values[1] == 42 else => $abort(\"value\")", "")]
    [InlineData("OwnedExclusive", "var values = [1: 2]\nlet value = values[1]@uniq\nvalue@follow = 42\nrequire values[1] == 42 else => $abort(\"value\")", "")]
    public void CompleteStoredTypesAndGenericWitnessesUseTheSharedPath(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("BorrowedDictionaryIndex" + name, source, stdout);

    [Fact]
    public void MissingKeysRetainTheIndexLocation()
        => ScalarEmissionTest.EmitFixture("BorrowedDictionaryIndexMissing", "func read(values: ref/Dictionary<i32, i32>) -> i32 => values[2]\nlet values = [1: 42]\nlet value = read(values)", string.Empty, 1, "Hello.kimi:1:55: abort KIMI_E_MISSING_KEY: Dictionary key was not found\n");

    [Fact]
    public void ReplacementSecuresTheValueBeforeLookupAndDestroysOnlyTheOldValue()
    {
        const string source = """
            struct Item
                public let name: string
                public init(name: string) => self.name = name@move
                drop => Console.writeLine(self.name)
            func key() -> i32
                Console.writeLine("key")
                return 1
            func value() -> Item
                Console.writeLine("value")
                return Item.init("new")
            func change(values: uniq/Dictionary<i32, Item>) => values[key()] = value()
            var values = [1: Item.init("old")]
            change(values@uniq)
            Console.writeLine("after")
            """;
        NativeAllocationAudit.WriteFixture("BorrowedDictionaryIndexOrder", source, 1, 1, 192, "value\nkey\nold\nafter\nnew\n");
    }

    [Theory]
    [InlineData("func change(values: ref/Dictionary<i32, i32>) => values[1] = 2", "SharedPathAccess_Kd")]
    [InlineData("func read(values: ref/Dictionary<i32, string>) -> string => values[1]", "TransferRequired_Kd")]
    public void SharedPermissionAndNonCopyReadsKeepExplicitDiagnostics(string source, string code)
    {
        var path = Path.GetFullPath("BorrowedDictionary.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var errors = result.Diagnostics.Where(static x => x.Severity == DiagnosticSeverity.Error).ToArray();
        var record = Assert.Single(errors);
        Assert.Equal(code, record.Code);
        Assert.DoesNotContain(errors, static x => x.Code.StartsWith("Unsupported", StringComparison.Ordinal));
        Assert.Contains("values[1]", record.Label, StringComparison.Ordinal);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(code, console.Text, StringComparison.Ordinal);
        testOutput.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        var output = new CheckOutput(CheckOutcome.Completed, false, TestPresence.No, result);
        var sent = Assert.Single(WorkspaceCheck.Place(output, [identity], identity, true)[identity], x => x.Code == code);
        Assert.Equal(record.Display!.Range, sent.Range);
        Assert.Contains(record.Label!, sent.Message, StringComparison.Ordinal);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void FailedValueReadKeepsIndependentInitializationErrors()
    {
        const string Source = "func read(values: ref/Dictionary<i32, string>) -> string\n    var number: i32\n    let copy = number\n    return values[1]";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        c.Ownership.ReportDiagnostics();
        var result = c.Diagnostics.Finalize();
        var errors = result.Diagnostics.Where(static x => x.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Equal(2, errors.Length);
        Assert.Single(errors, static x => x.Code == "TransferRequired_Kd");
        var uninitialized = Assert.Single(errors, static x => x.Code == "UninitializedPlace_Kd");
        Assert.Equal("number", Source.Substring(uninitialized.Span!.Value.Start, uninitialized.Span.Value.Length));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("var values = [1: 2]\nlet view = values@ref\nlet item = view[1]@ref\nvalues.clear()\nlet last = item + 1")]
    [InlineData("var values = [1: 2]\nlet view = values@uniq\nlet first = view[1]@uniq\nlet second = view[1]@uniq\nfirst@follow = 3\nsecond@follow = 4")]
    [InlineData("func change(values: uniq/Dictionary<i32, i32>) => values[values[1]] = 2")]
    public void ElementResultsAndSearchKeysRetainTheirLoans(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBorrowedIndexCompilationAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("func change(values: uniq/Dictionary<i32, i32>) => values[1] += 2\nvar values = [1: 40]\nchange(values@uniq)");
        void Compile()
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        for (var i = 0; i < 32; i++)
        {
            Compile();
        }

        Assert.Equal(0, AllocationMeasurement.Measure(Compile));
    }
}
