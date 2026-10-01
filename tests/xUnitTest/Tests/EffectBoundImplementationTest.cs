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

    // SPEC 8.4.10.5: one stored J and one delegated d with an equal normalized result are covered; two Fields naming J are not.
    [Theory]
    [InlineData("struct Drain<J>\n    J is Iterator\n    Self is Source\n    associate Source.Item is J.Item\n    var inner: J\n    public var taken: isize = 0\n    public func take(self: uniq/Self) -> Option<J.Item>\n        self.taken += 1\n        return self.inner.next()\n", true)]
    [InlineData("struct Merge<J>\n    J is Iterator\n    Self is Source\n    associate Source.Item is J.Item\n    var left: J\n    var right: J\n    public func take(self: uniq/Self) -> Option<J.Item>\n        match self.left.next()\n            .Some(let item) => return .Some(item@move)\n            .None => return self.right.next()\n", false)]
    [InlineData("struct Lending<J> {a}\n    J is LendingIterator\n    Self is Source\n    associate Source.Item is ref/i32 during a\n    var inner: J\n    let value: ref/i32 during a\n    public func take(self: uniq/Self) -> Option<ref/i32 during a>\n        _ = self.inner.next()\n        return .Some(self.value)\n", false)]
    [InlineData("struct Plain<J>\n    J is LendingIterator\n    Self is Source\n    associate Source.Item is i32\n    var inner: J\n    public func take(self: uniq/Self) -> Option<i32>\n        _ = self.inner.next()\n        return .None\n", true)]
    [InlineData("struct Counting\n    Self is Source\n    associate Source.Item is i32\n    var value: i32 = 0\n    public func take(self: uniq/Self) -> Option<i32>\n        self.value += 1\n        return .Some(self.value)\n", true)]
    public void PreservesResultsUsesTheDelegationRule(string declarations, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Source + declarations + Main);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
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
