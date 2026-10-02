// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
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
    [InlineData("struct RawSink\n    Self is Sink\n    var slot: unsafe/i32\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        unsafe\n            *self.slot = value\n        return .Ok(())\n", true)]
    [InlineData("struct DeviceSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        unsafe\n            let register = 0x40000000@unsafe/i32\n            *register = value\n        return .Ok(())\n", false)]
    [InlineData("group Native\n    #LibraryImport(\"kernel32\", \"QueryPerformanceCounter\")\n    public unsafe func query(value: unsafe/i64) -> i32\nstruct ForeignSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        let output: unsafe/i64 = null\n        unsafe => _ = Native.query(output)\n        return .Ok(())\n", false)]
    [InlineData("struct Counter\n    var count: isize = 0\n    public func increment(self: uniq/Self) => self.count += 1\ncontract Tally\n    func add(self: ref/Self, total: uniq/Counter)\n        effect confined\nstruct Adder\n    Self is Tally\n    public func add(self: ref/Self, total: uniq/Counter)\n        total.increment()\n", true)]
    public void ConfinedClassifiesAuthorityByItsSource(string declarations, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Sink + declarations + Main);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

    // SPEC 8.4.10.2, 22.3.1: a foreign call accesses only what its arguments permit, so preserves results admits it.
    [Fact]
    public void PreservesResultsAdmitsForeignCalls()
    {
        const string Declarations = "group Native\n    #LibraryImport(\"kernel32\", \"QueryPerformanceCounter\")\n    public unsafe func query(value: unsafe/i64) -> i32\n" +
            "struct Polled<J>\n    J is Iterator\n    Self is Source\n    associate Source.Item is J.Item\n    var inner: J\n    public func take(self: uniq/Self) -> Option<J.Item>\n" +
            "        let output: unsafe/i64 = null\n        unsafe => _ = Native.query(output)\n        return self.inner.next()\n";
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
    // caller can borrow again only after the earlier results end, so the caller's own Loans reject the replacement.
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
        Assert.Contains(TestDiagnostics.Of(c), static x => x.Code == nameof(DiagnosticCode.ComparisonLoanConflict_Kd) && x.Text == "values");
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
