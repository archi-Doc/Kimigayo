// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 8.4.10.4, 8.4.10.5: a conformance checks each witness against the bounds its Contract and the ancestors declare, from
// the declarations alone; user Contracts bound their requirements exactly as the standard ones do.
public class EffectBoundImplementationTest
{
    private const string Sink = "contract Sink\n    func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        effect confined\n" +
        "group Metrics\n    public var puts: isize = 0\n    public func record() => Metrics.puts += 1\n";

    private const string Source = "contract Source\n    associate Item\n    func take(self: uniq/Self) -> Option<Self.Item>\n        effect preserves results\n";

    private const string Main = "public func main() => ()\n";

    [Theory]
    [InlineData("struct CountingSink\n    Self is Sink\n    var count: isize = 0\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        self.count += 1\n        return .Ok(())\n", true)]
    [InlineData("struct Forward<W>\n    W is Sink\n    Self is Sink\n    var inner: W\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        return self.inner.put(value)\n", true)]
    [InlineData("struct LoggingSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        Metrics.record()\n        return .Ok(())\n", false)]
    [InlineData("struct ConsoleSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        Console.writeLine(\"put\")\n        return .Ok(())\n", false)]
    [InlineData("contract Plain\n    func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\nstruct Loose<W>\n    W is Plain\n    Self is Sink\n    var inner: W\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        return self.inner.put(value)\n", false)]
    public void ConfinedRejectsEnvironmentEffects(string declarations, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Sink + declarations + Main);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

    // SPEC 8.4.10.2: an environment effect obtains authority over mutable state from the environment; accesses made with
    // authority from the inputs are none, even through a raw pointer or a borrow of static storage.
    [Theory]
    [InlineData("struct RawSink\n    Self is Sink\n    var slot: raw/i32\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        unsafe\n            *self.slot = value\n        return .Ok(())\n", true)]
    [InlineData("struct DeviceSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        unsafe\n            let register = 0x40000000@raw/i32\n            *register = value\n        return .Ok(())\n", false)]
    [InlineData("group Native\n    #LibraryImport(\"kernel32\", \"QueryPerformanceCounter\")\n    public unsafe func query(value: raw/i64) -> i32\nstruct ForeignSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        let output: raw/i64 = null\n        unsafe => _ = Native.query(output)\n        return .Ok(())\n", false)]
    [InlineData("struct Counter\n    var count: isize = 0\n    public func increment(self: uniq/Self) => self.count += 1\ncontract Tally\n    func add(self: ref/Self, total: uniq/Counter)\n        effect confined\nstruct Adder\n    Self is Tally\n    public func add(self: ref/Self, total: uniq/Counter)\n        total.increment()\n", true)]
    [InlineData("contract Maker\n    func make(self: ref/Self) -> Array<i32>\n        effect confined\nstruct M\n    Self is Maker\n    var n: i32 = 4\n    public func make(self: ref/Self) -> Array<i32>\n        var result = Array<i32>.init(capacity: 2)\n        result.appendCopies([self.n, self.n][..])\n        return result@move\n", true)]
    [InlineData("contract Maker\n    func make(self: ref/Self) -> Array<i32>\n        effect confined\nstruct M\n    Self is Maker\n    var n: i32 = 4\n    public func make(self: ref/Self) -> Array<i32>\n        let values: [2 of i32] = [self.n, self.n]\n        return values.slice().toArray()\n", true)]
    [InlineData("contract Filler\n    func fill(self: ref/Self, values: uniq/Array<i32>)\n        effect confined\nstruct F\n    Self is Filler\n    public func fill(self: ref/Self, values: uniq/Array<i32>)\n        var view = values.sliceUniq()\n        view[0] = 7\n", true)]
    public void ConfinedClassifiesAuthorityByItsSource(string declarations, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Sink + declarations + Main);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

    // SPEC 8.4.10.2, 8.4.10.5: an effect the summary cannot classify yet is one Unsupported_Kd at the conformance: the destructions of
    // a static initializer other than a constant (reading an immutable static is no environment effect), and a conflict that the
    // untried exclusion of a virtual slot call producing the item might remove. A virtual call producing no item keeps its violation.
    [Theory]
    [InlineData("contract Read\n    func read(self: ref/Self) -> i32\n        effect confined\ngroup G\n    public let value: i32 = 2 + 3\nstruct S\n    Self is Read\n    public func read(self: ref/Self) -> i32 => G.value\n", "Self is Read", nameof(DiagnosticCode.Unsupported_Kd))]
    [InlineData(Source + "open struct Feed<T>\n    public init() => ()\n    public virtual func next(self: objref/Self) -> Option<T>\n        effect preserves results\n        return .None\nstruct Drain<T>\n    Self is Source\n    associate Source.Item is T\n    var inner: obj/Feed<T>\n    public init() => self.inner = Kimi.Intrinsics.makeObj(Feed<T>.init())\n    public func take(self: uniq/Self) -> Option<T> => self.inner.next()\n", "Self is Source", nameof(DiagnosticCode.Unsupported_Kd))]
    [InlineData(Source + "contract Step\n    associate Item\n    func step(self: uniq/Self) -> Option<Self.Item>\n        effect preserves results\nopen struct Feed\n    public init() => ()\n    public virtual func next(self: objref/Self) -> Option<i32>\n        effect preserves results\n        return .None\nstruct Drain<J>\n    J is Step\n    Self is Source\n    associate Source.Item is J.Item\n    var inner: J\n    var feed: obj/Feed\n    public func take(self: uniq/Self) -> Option<J.Item>\n        _ = self.feed.next()\n        return self.inner.step()\n", "self.feed.next()", nameof(DiagnosticCode.IncompatibleContractImplementation_Kd))]
    public void AnUnclassifiedEffectIsAnImplementationLimit(string declarations, string at, string code)
    {
        var source = declarations + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((code, source.IndexOf(at, StringComparison.Ordinal), at.Length), (error.Code, error.Span!.Value.Start, error.Span.Value.Length));
    }

    // SPEC 8.4.10.2: every compiler function has one effect class. The Storage and Raw families act only on their inputs and the
    // allocator; Console output and the test temporary directory are environment effects; formatting dispatch is followed to its
    // witness before classification. A new kind is unclassified until it is placed here.
    [Fact]
    public void EveryCompilerFunctionHasOneEffectClass()
    {
        foreach (var kind in Enum.GetValues<CompilerFunctionKind>())
        {
            var expected = kind switch
            {
                CompilerFunctionKind.None => Binding.CompilerFunctionEffect.Unclassified,
                CompilerFunctionKind.WriteLine or CompilerFunctionKind.WriteLineUtf8 or CompilerFunctionKind.TestTempDirectory => Binding.CompilerFunctionEffect.Environment,
                CompilerFunctionKind.WriterWrite or CompilerFunctionKind.TextToString or CompilerFunctionKind.TextTryFormat => Binding.CompilerFunctionEffect.Formatting,
                _ => Binding.CompilerFunctionEffect.Inputs,
            };
            Assert.True(expected == Binding.ClassifyCompilerFunction(kind), kind.ToString());
        }
    }

    // SPEC 4.7.5: the Kimi body of each operation that publishes its summary stays within its row with abstract Type parameters,
    // under confined and under preserves results; `K` equality is the only parameter-dependent requirement a row publishes, and the
    // capacity and clear rows publish none at Binding (destructions are checked after ownership analysis). indexUniq lends `uniq/V`
    // from its exclusive receiver, so its own input access affects the Loans earlier results keep and preserves results never holds.
    [Fact]
    public void PublishedOperationBodiesStayWithinTheirRows()
    {
        var c = MinimalEmissionTest.Analyze(Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var equals = ((ContractKoto)c.Library.GetSymbol(KimiDeclarationId.Equatable)!.Declaration).Members.OfType<FunctionKoto>().Single(static x => x.Name == "equals").SymbolOf();
        var operations = 0;
        foreach (var entry in KimiLibraryCatalog.Entries)
        {
            if (!entry.PublishedSummary)
            {
                continue;
            }

            var operation = c.Library.GetSymbol(entry.Id)!;
            Assert.True(KimiLibraryCatalog.PublishesSummary(operation));
            if (entry.Implementation is { } implementation)
            {
                // A bodiless constructor's summary is its linked implementation's, which is checked as its own entry.
                Assert.Same(c.Library.GetSymbol(implementation), c.Library.ConstructorImplementation(operation));
                continue;
            }

            var published = entry.Id is KimiDeclarationId.DictionaryReserve or KimiDeclarationId.DictionaryShrinkToFit or KimiDeclarationId.DictionaryClear or KimiDeclarationId.ArrayRepeatingImplementation or KimiDeclarationId.ArrayCapacityImplementation ? null : equals;
            foreach (var (confined, preserves) in new[] { (true, false), (false, true) })
            {
                var (valid, violation, node) = c.Binding.SummarizePublishedOperation(operation, confined, preserves, published);
                var lends = preserves && entry.Id == KimiDeclarationId.DictionaryIndexUniq;
                Assert.True(valid != lends && (!lends || violation == Binding.EffectViolation.ResultLoan), $"{entry.Name} (confined {confined}, preserves {preserves}): {violation} at {node}");
            }

            operations++;
        }

        Assert.Equal(10, operations);
    }

    // SPEC 8.4.10.2, 22.3.1: a foreign call accesses only what its arguments permit, so preserves results admits it.
    [Fact]
    public void PreservesResultsAdmitsForeignCalls()
    {
        const string Declarations = "group Native\n    #LibraryImport(\"kernel32\", \"QueryPerformanceCounter\")\n    public unsafe func query(value: raw/i64) -> i32\n" +
            "struct Polled<J>\n    J is Iterator\n    Self is Source\n    associate Source.Item is J.Item\n    var inner: J\n    public func take(self: uniq/Self) -> Option<J.Item>\n" +
            "        let output: raw/i64 = null\n        unsafe => _ = Native.query(output)\n        return self.inner.next()\n";
        var c = MinimalEmissionTest.Analyze(Source + Declarations + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 8.4.10.2, 5.2.2: an access through a raw Place borrow is compared with held Loans under its anchor, which no earlier
    // result can keep, so preserves results admits it even when the item is abstract.
    [Fact]
    public void PreservesResultsComparesRawAccessesUnderTheAnchor()
    {
        const string Declarations = "struct Counted<J>\n    J is Iterator\n    Self is Source\n    associate Source.Item is J.Item\n    var inner: J\n    var counter: raw/isize\n" +
            "    public func take(self: uniq/Self) -> Option<J.Item>\n        unsafe\n            let count = (*self.counter)@uniq\n            count@follow += 1\n        return self.inner.next()\n";
        var c = MinimalEmissionTest.Analyze(Source + Declarations + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 8.4.10.5: a call of d on a value reached through a Field path of self is compared with no Loan of an earlier result
    // when every result Loan comes from d on that value and no value on the path is replaced; other sources are compared.
    [Theory]
    [InlineData("struct Drain<J>\n    J is Iterator\n    Self is Source\n    associate Source.Item is J.Item\n    var inner: J\n    public var taken: isize = 0\n    public func take(self: uniq/Self) -> Option<J.Item>\n        self.taken += 1\n        return self.inner.next()\n", true)]
    [InlineData("struct Numbered<J>\n    J is Iterator\n    Self is Source\n    associate Source.Item is (isize, J.Item)\n    var inner: J\n    var index: isize = 0\n    public func take(self: uniq/Self) -> Option<(isize, J.Item)>\n        match self.inner.next()\n            .Some(let item)\n                self.index += 1\n                return .Some((self.index, item@move))\n            .None => return .None\n", true)]
    [InlineData("struct Kept<J>\n    J is Iterator\n    Self is Source\n    associate Source.Item is J.Item\n    var inner: J\n    public func take(self: uniq/Self) -> Option<J.Item>\n        let item = self.inner.next()\n        return item@move\n", true)]
    [InlineData("struct Lent<J> {source}\n    J is Iterator\n    Self is Source\n    associate Source.Item is J.Item\n    var inner: uniq/J during source\n    public func take(self: uniq/Self) -> Option<J.Item>\n        return self.inner.next()\n", true)]
    [InlineData("struct Wrapper<J>\n    public var iterator: J\nstruct Nested<J>\n    J is Iterator\n    Self is Source\n    associate Source.Item is J.Item\n    var wrapper: Wrapper<J>\n    public func take(self: uniq/Self) -> Option<J.Item>\n        return self.wrapper.iterator.next()\n", true)]
    [InlineData("struct Merge<J>\n    J is Iterator\n    Self is Source\n    associate Source.Item is J.Item\n    var left: J\n    var right: J\n    public func take(self: uniq/Self) -> Option<J.Item>\n        match self.left.next()\n            .Some(let item) => return .Some(item@move)\n            .None => return self.right.next()\n", false)]
    [InlineData("struct Swapped<J>\n    J is Iterator\n    Self is Source\n    associate Source.Item is J.Item\n    var inner: J\n    var spare: J\n    public func take(self: uniq/Self) -> Option<J.Item>\n        Kimi.Intrinsics.swap(self.inner@uniq, self.spare@uniq)\n        return self.inner.next()\n", false)]
    [InlineData("struct Lending<J> {a}\n    J is LendingIterator\n    Self is Source\n    associate Source.Item is ref/i32 during a\n    var inner: J\n    let value: ref/i32 during a\n    public func take(self: uniq/Self) -> Option<ref/i32 during a>\n        _ = self.inner.next()\n        return .Some(self.value)\n", false)]
    [InlineData("struct Plain<J>\n    J is LendingIterator\n    Self is Source\n    associate Source.Item is i32\n    var inner: J\n    public func take(self: uniq/Self) -> Option<i32>\n        _ = self.inner.next()\n        return .None\n", true)]
    [InlineData("struct Counting\n    Self is Source\n    associate Source.Item is i32\n    var value: i32 = 0\n    public func take(self: uniq/Self) -> Option<i32>\n        self.value += 1\n        return .Some(self.value)\n", true)]
    public void PreservesResultsTracesResultLoans(string declarations, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Source + declarations + Main);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 8.4.10.5: an earlier result that may come from an argument keeps Loans that the call of d is compared with.
    [Fact]
    public void PreservesResultsComparesArgumentLoans()
    {
        const string Declarations = "contract Refill\n    associate Item\n    func take(self: uniq/Self, spare: Option<Self.Item>) -> Option<Self.Item>\n        effect preserves results\n" +
            "struct Topped<J>\n    J is Iterator\n    Self is Refill\n    associate Refill.Item is J.Item\n    var inner: J\n    public func take(self: uniq/Self, spare: Option<J.Item>) -> Option<J.Item>\n" +
            "        match self.inner.next()\n            .Some(let item) => return .Some(item@move)\n            .None => return spare@move\n";
        var c = MinimalEmissionTest.Analyze(Declarations + Main);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    // SPEC 8.4.10.5: replacing the stepped value between calls through another member needs a new value, whose source the
    // caller can borrow again only after the earlier results end, so the caller's own Loans reject the replacement. The Loan
    // `drain` retains on `values` is one conflict, stated once at the activation of the new value's acquisition (SPEC 15.6.7).
    [Fact]
    public void AReplacementBetweenCallsMeetsTheCallerLoans()
    {
        const string Declarations = "struct Drain<J>\n    J is Iterator\n    Self is Source\n    associate Source.Item is J.Item\n    var inner: J\n" +
            "    public init(inner: J) => self.inner = inner@move\n    public func take(self: uniq/Self) -> Option<J.Item> => self.inner.next()\n" +
            "    public func reset(self: uniq/Self, fresh: J) => self.inner = fresh@move\n" +
            "public func main()\n    var values = [1, 2, 3]\n    var drain = Drain<ArrayUniqIterator<i32>>.init(values.iterateUniq())\n    let first = drain.take()\n" +
            "    drain.reset(values.iterateUniq())\n    let second = drain.take()\n    match first@move\n        .Some(let item) => item@follow += 1\n        .None => ()\n";
        var c = MinimalEmissionTest.Analyze(Source + Declarations);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        c.Ownership.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal((nameof(DiagnosticCode.CallActivationConflict_Kd), "values.iterateUniq()"), (error.Code, error.Text));
    }

    // SPEC 8.4.10.1: the bound is the Contract's guarantee, so only Self is StableSource is checked against it.
    [Theory]
    [InlineData("Source", true)]
    [InlineData("StableSource", false)]
    public void OnlyTheDeclaringContractObligates(string contract, bool valid)
    {
        const string Declarations = "contract Source\n    associate Item\n    func take(self: uniq/Self) -> Option<Self.Item>\ncontract StableSource: Source\n    effect Source.take confined\n";
        var c = MinimalEmissionTest.Analyze(Declarations + $"struct Noisy\n    Self is {contract}\n    associate Source.Item is i32\n    public func take(self: uniq/Self) -> Option<i32>\n        Console.writeLine(\"take\")\n        return .None\n" + Main);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    // Warm rebinding of user-declared bounds and their witness summaries allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmUserEffectBoundChecksDoNotAllocate()
    {
        const string Declarations = "struct Forward<W>\n    W is Sink\n    Self is Sink\n    var inner: W\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        return self.inner.put(value)\n" +
            "struct Drain<J>\n    J is Iterator\n    Self is Source\n    associate Source.Item is J.Item\n    var inner: J\n    public func take(self: uniq/Self) -> Option<J.Item>\n        return self.inner.next()\n";
        var c = MinimalEmissionTest.Analyze(Sink + Source + Declarations + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var allocated = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Equal(0, allocated);
    }
}
