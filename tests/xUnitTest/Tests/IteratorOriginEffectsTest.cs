// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class IteratorOriginEffectsTest
{
    // The generic conformances of AnIteratorConformanceIsAnOrdinaryBody, which AbstractCallReachTest also instantiates (L1).
    internal const string OrdinaryWrap = "struct Wrap<T> {a}\n    Self is Iterator\n    associate Iterator.Item is T\n    let counter: uniq/i32 during a\n    var pending: Option<T>\n    public func next(self: uniq/Self) -> Option<T>\n        self.counter@follow += 1\n        return Kimi.Intrinsics.exchange(self.pending@uniq, with: .None)";

    internal const string OrdinaryHold = "struct Hold<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        let first = self.inner.next()\n        return self.inner.next()";

    internal const string OrdinaryOuter = "struct Outer<I> {a}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during a\n    I is Iterator\n    let first: ref/i32 during a\n    var inner: I\n    public func next(self: uniq/Self) -> Option<ref/i32 during a>\n        _ = self.inner.next()\n        return .Some(self.first)";

    internal const string OrdinaryChain = "struct Chain<I> {source}\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var first: uniq/I during source\n    var second: uniq/I during source\n" +
        "    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        match self.first.next()\n            .Some(let item) => return .Some(item@move)\n            .None => return self.second.next()";

    internal const string OrdinaryPeek = "struct Peek<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    var pending: Option<I.(Iterator).Item>\n" +
        "    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        let following = self.inner.next()\n        return Kimi.Intrinsics.exchange(self.pending@uniq, with: following@move)";

    // SPEC 8.11.2, 16.3.3: under confined, destroying a value of an abstract Type has unknown effects (G28). Iterator declares no bound
    // (22.2.1), so the analyses of these forms run over the same bodies conforming to Feed, an Iterator-shaped Contract bounded by
    // confined; the native fixtures run them as Iterators.
    private const string Feed = "contract Feed\n    associate Item\n    func next(self: uniq/Self) -> Option<Self.Item>\n        effect confined\n";

    // SPEC 22.2.1, 22.2.3 (the adopted proposal Abstract Calls Reach by Paths): Iterator declares no effect bound, so an Iterator
    // conformance is an ordinary body: it may write a Place its items' Origin names, drop an item, call an unbounded requirement
    // or step another iterator (each a violation of the removed preserves results bound before the adoption); 15.6.3's containment
    // still applies (OriginRelationDiagnosticTest).
    [Theory]
    [InlineData("struct Cursor {a, b}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during a\n    let first: ref/i32 during a\n    let counter: uniq/i32 during a\n    public func next(self: uniq/Self) -> Option<ref/i32 during a>\n        self.counter@follow = 5\n        return .Some(self.first)")]
    [InlineData(OrdinaryWrap)]
    [InlineData(OrdinaryHold)]
    [InlineData(OrdinaryOuter)]
    [InlineData(OrdinaryChain)]
    [InlineData(OrdinaryPeek)]
    public void AnIteratorConformanceIsAnOrdinaryBody(string declarations)
    {
        var c = MinimalEmissionTest.Analyze(declarations);
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void ExactExpectedExclusiveReferencesAreReborrowed()
    {
        const string source = "struct Cursor {source}\n    Self is LendingIterator\n    associate LentItem(step) is ref/i32 during step\n    let value: uniq/i32 during source\n    public init(value: uniq/i32 during source) => self.value = value@move\n    public func next(self: uniq/Self during step) -> Option<ref/i32 during step>\n        do\n            let alias: uniq/i32 = self.value\n            alias@follow += 1\n        return .Some(self.value)\nvar value = 40\nvar cursor = Cursor.init(value@uniq)\nlet first = cursor.next()\nmatch first\n    .Some(let n) => require n == 41 else => $abort(\"first\")\n    .None => $abort(\"empty\")\nlet second = cursor.next()\nmatch second\n    .Some(let n) => require n == 42 else => $abort(\"second\")\n    .None => $abort(\"empty\")\nConsole.writeLine(\"exact reborrow\")";
        ScalarEmissionTest.EmitFixture("AssociatedLendingExactReborrow", source, "exact reborrow\n");
    }

    [Fact]
    public void IntersectionItemsSurviveIndependentSourceUpdates()
    {
        const string source = "struct Cursor {a, b, c}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during (a and b)\n    let first: ref/i32 during a\n    let second: ref/i32 during b\n    let other: uniq/i32 during c\n    var count: i32 = 0\n    public init(first: ref/i32 during a, second: ref/i32 during b, other: uniq/i32 during c)\n        self.first = first\n        self.second = second\n        self.other = other@move\n    public func next(self: uniq/Self) -> Option<ref/i32 during (a and b)>\n        self.other@follow += 1\n        self.count += 1\n        if self.count == 1 => return .Some(self.first)\n        return .Some(self.second)\nlet a = 41\nlet b = 42\nvar other = 0\nvar cursor = Cursor.init(a@ref, b@ref, other@uniq)\nlet first = cursor.next()\nlet second = cursor.next()\nmatch first\n    .Some(let n) => require n == 41 else => $abort(\"first\")\n    .None => $abort(\"empty\")\nmatch second\n    .Some(let n) => require n == 42 else => $abort(\"second\")\n    .None => $abort(\"empty\")\nrequire other == 2 else => $abort(\"other\")\nConsole.writeLine(\"intersection\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorIntersection", source, "intersection\n");
    }

    // Warm rebinding reruns the confined summaries of a Feed witness, which composes a generic helper, and of a formatting witness, and
    // the Origins of intersection items, without allocating.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmEffectBoundChecksDoNotAllocate()
    {
        const string Source = "group Helpers\n    public func bump<T>(value: uniq/i32, marker: ref/T) => value@follow += 1\n" +
            "struct Cursor {a, b, c}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during (a and b)\n    let first: ref/i32 during a\n    let second: ref/i32 during b\n    let other: uniq/i32 during c\n    var count: i32 = 0\n" +
            "    public func next(self: uniq/Self) -> Option<ref/i32 during (a and b)>\n        Helpers.bump(self.other, self.count@ref)\n        self.count += 1\n        if self.count == 1 => return .Some(self.first)\n        return .Some(self.second)\n" +
            "struct Writer\n    Self is BufferWriter\n    var local: i32 = 1\n    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>\n        _ = \"value: \\(self.local)\"\n        return .Err(BufferFull.init())";
        var c = Bounded(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var allocated = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Equal(0, allocated);
    }

    private const string Counter = "struct Counter\n    Self is Iterator\n    associate Iterator.Item is i32\n    var value: i32 = 0\n    public init() => ()\n" +
        "    public func next(self: uniq/Self) -> Option<i32>\n        if self.value == 3 => return .None\n        self.value += 1\n        return .Some(self.value)\n";

    // SPEC 22.2.3: a delegating wrapper forwards one inner iterator's items; that iterator's published bound covers them.
    [Fact]
    public void ADelegatingWrapperForwardsItsInnerItems()
    {
        const string Wrapper = "struct Wrapper<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    public var count: i32 = 0\n" +
            "    public init(inner: I) => self.inner = inner@move\n    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        self.count += 1\n        return self.inner.next()\n";
        var source = Counter + Wrapper + "var wrapper = Wrapper<Counter>.init(Counter.init())\nvar sum: i32 = 0\nloop\n    match wrapper.next()\n        .Some(let n) => sum += n\n        .None => exit\n" +
            "require sum == 6 and wrapper.count == 4 else => $abort(\"wrapper\")\nConsole.writeLine(\"wrapped\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorWrapper", source, "wrapped\n");
    }

    // SPEC 22.2.3: all items come from the one stored Iterator, so its bound covers every step of it, from any number of
    // call sites.
    [Fact]
    public void AWrapperMayStepItsStoredIteratorFromSeveralCallSites()
    {
        const string Alternate = "struct Alternate<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    public var even: bool = false\n" +
            "    public init(inner: I) => self.inner = inner@move\n    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        self.even = self.even == false\n" +
            "        if self.even => return self.inner.next()\n        return self.inner.next()\n";
        var source = Counter + Alternate + "var alternate = Alternate<Counter>.init(Counter.init())\nvar sum: i32 = 0\nloop\n    match alternate.next()\n        .Some(let n) => sum += n\n        .None => exit\n" +
            "require sum == 6 and alternate.even == false else => $abort(\"alternate\")\nConsole.writeLine(\"alternated\")"; // Four steps, the last one None.
        ScalarEmissionTest.EmitFixture("AssociatedIteratorAlternate", source, "alternated\n");
    }

    // SPEC 8.11.2, 16.3.3: an item discarded inside next, from the stored iterator or another one, runs an unknown destructor.
    [Theory]
    [InlineData("struct Wrapper<I> {a}\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is uniq/i32 during a\n    var inner: I\n    var slot: Option<uniq/i32 during a>\n    public func next(self: uniq/Self) -> Option<uniq/i32 during a>\n        _ = self.inner.next()\n        return Kimi.Intrinsics.exchange(self.slot@uniq, with: .None)")]
    [InlineData("struct Wrapper<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    var other: I\n    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        _ = self.other.next()\n        return self.inner.next()")]
    public void ADiscardedItemRunsAnUnknownDestructor(string wrapper)
    {
        var c = Bounded(wrapper);
        Assert.False(c.Binding.Result.IsComplete && c.Analysis.IsVerified);
        MinimalEmissionTest.AssertEffectBoundRejected(c);
    }

    // SPEC 22.2.3 (G28): matching an item of the stored Iterator by value destroys nothing when every arm either matches a
    // Case without payload or binds the whole payload and transfers it at once, so a wrapper that inspects an item before
    // returning it keeps the inner bound.
    [Fact]
    public void AWrapperMayInspectAnItemBeforeTransferringIt()
    {
        const string Retry = "struct Retry<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    public var retries: i32 = 0\n" +
            "    public init(inner: I) => self.inner = inner@move\n    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        match self.inner.next()\n" +
            "            .Some(let item) => return .Some(item@move)\n            .None\n                self.retries += 1\n                return self.inner.next()\n";
        var source = Counter + Retry + "var retry = Retry<Counter>.init(Counter.init())\nvar sum: i32 = 0\nloop\n    match retry.next()\n        .Some(let n) => sum += n\n        .None => exit\n" +
            "require sum == 6 and retry.retries == 1 else => $abort(\"retry\")\nConsole.writeLine(\"retried\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorRetry", source, "retried\n");
    }

    // An immutable local holding the item is not destroyed when the very next statement transfers it, directly or as a match
    // Subject whose arms transfer the payload.
    [Theory]
    [InlineData("let first = self.inner.next()\n        match first@move\n            .Some(let item) => return .Some(item@move)\n            .None => return self.inner.next()")]
    [InlineData("let first = self.inner.next()\n        return first@move")]
    [InlineData("let first = self.inner.next()\n        self.steps += 1\n        return first@move")] // G28: effects that cannot leave the block
    [InlineData("var first = self.inner.next()\n        self.steps += 1\n        return first@move")]
    [InlineData("let first = self.inner.next()\n        var i: i32 = 0\n        loop\n            i += 1\n            if i == 2 => exit\n        self.steps += i\n        match first@move\n" +
        "            .Some(let item) => return .Some(item@move)\n            .None => return self.inner.next()")]
    [InlineData("match self.inner.next()\n            .Some(let item)\n                self.steps += 1\n                return .Some(item@move)\n            .None => return self.inner.next()")]
    [InlineData("let first = self.inner.next()\n        label outer: loop\n            loop\n                if self.steps > 5 => exit to outer\n                exit\n            exit\n        return first@move")] // A Label inside the gap
    [InlineData("let first = self.inner.next()\n        let step: i32 = if self.steps > 0\n            self.steps -= 1\n            yield 1\n        else => 0\n        self.steps += step\n        return first@move")] // A yield inside the gap
    [InlineData("let first = self.inner.next()\n        defer => self.steps += 1\n        return first@move")] // The destructions are the planned cleanups, so a defer,
    [InlineData("let first = self.inner.next()\n        var i: i32 = 0\n        while i < 2 => i += 1\n        self.steps += i\n        return first@move")] // a while loop,
    [InlineData("var first = self.inner.next()\n        while self.steps < 0\n            let skipped = first@move\n            self.steps += 1\n            return skipped@move\n        return first@move")] // or a transfer inside one is not a syntactic boundary.
    public void AWrapperMayHoldAnItemInALocalBeforeTransferringIt(string body)
    {
        var wrapper = "struct Hold<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    public var steps: i32 = 0\n" +
            "    public init(inner: I) => self.inner = inner@move\n    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        " + body + "\n";
        var source = Counter + wrapper + "var hold = Hold<Counter>.init(Counter.init())\nvar sum: i32 = 0\nloop\n    match hold.next()\n        .Some(let n) => sum += n\n        .None => exit\n" +
            "require sum == 6 else => $abort(\"hold\")\nConsole.writeLine(\"held\")";
        var c = Bounded(source);
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    // G28: an item held across effects executes, and the effects run once per step.
    [Fact]
    public void AnItemHeldAcrossEffectsIsTransferredAtRunTime()
    {
        const string Hold = "struct Hold<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    public var steps: i32 = 0\n" +
            "    public init(inner: I) => self.inner = inner@move\n    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        var first = self.inner.next()\n" +
            "        self.steps += 1\n        return first@move\n";
        var source = Counter + Hold + "var hold = Hold<Counter>.init(Counter.init())\nvar sum: i32 = 0\nloop\n    match hold.next()\n        .Some(let n) => sum += n\n        .None => exit\n" +
            "require sum == 6 and hold.steps == 4 else => $abort(\"hold\")\nConsole.writeLine(\"held across\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorHoldAcross", source, "held across\n");
    }

    // G28: a returned Case construction or Tuple that stores the item, with parts that neither name it nor may leave the
    // block, transfers it at once, so an Iterator may number the values it hands out.
    private const string NumberedOnce = "struct NumberedOnce<T>\n    Self is Iterator\n    associate Iterator.Item is (isize, T)\n    var value: Option<T>\n    public var index: isize = 0\n" +
        "    public init(value: T) => self.value = .Some(value@move)\n    public func next(self: uniq/Self) -> Option<(isize, T)>\n" +
        "        match Kimi.Intrinsics.exchange(self.value@uniq, with: .None)\n            ";

    [Fact]
    public void AnItemStoredInAReturnedTupleIsTransferred()
    {
        var source = NumberedOnce + ".Some(let value)\n                self.index += 1\n                return .Some((self.index, value@move))\n            .None => return .None\n" +
            "var once = NumberedOnce<i32>.init(5)\nvar positions: isize = 0\nvar sum: i32 = 0\nloop\n    match once.next()\n" +
            "        .Some(let pair)\n            positions += pair.0\n            sum += pair.1\n        .None => exit\nrequire positions == 1 and sum == 5 and once.index == 1 else => $abort(\"numbered\")\nConsole.writeLine(\"numbered\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorNumberedOnce", source, "numbered\n");
    }

    // G28, SPEC 14.8.3: a guard Moves no payload and a false guard preserves the Subject, so guarded arms that transfer the
    // payload consume it; here the guard is false and the next arm transfers the value.
    [Fact]
    public void AGuardedArmThatTransfersThePayloadIsAccepted()
    {
        var source = NumberedOnce + ".Some(let value) if self.index > 0 => return .Some((1, value@move))\n            .Some(let value) => return .Some((2, value@move))\n" +
            "            .None => return .None\nvar once = NumberedOnce<i32>.init(5)\nvar positions: isize = 0\nvar sum: i32 = 0\nloop\n    match once.next()\n" +
            "        .Some(let pair)\n            positions += pair.0\n            sum += pair.1\n        .None => exit\nrequire positions == 2 and sum == 5 else => $abort(\"guarded\")\nConsole.writeLine(\"guarded\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorGuardedTransfer", source, "guarded\n");
    }

    // A guard that may leave the block, here by an ordinary return (SPEC 14.8.3), destroys the Subject on that path.
    [Fact]
    public void AGuardThatMayLeaveTheBlockIsRejected()
    {
        var source = NumberedOnce + ".Some(let value) if self.index > 0 or (return .None) => return .Some((1, value@move))\n            .Some(let value) => return .Some((2, value@move))\n" +
            "            .None => return .None\n";
        var c = Bounded(source);
        MinimalEmissionTest.AssertEffectBoundRejected(c);
        Assert.All(c.Binding.Issues.Select(x => x.Code).Concat(c.Problems.Select(x => x.Code)), code => Assert.Equal(DiagnosticCode.IncompatibleContractImplementation_Kd, code));
    }

    // G28: a nested pattern destroys only its untransferred parts: the isize position is Copy and the value is transferred,
    // so a stored pair is handed out whole; the value is a string here, whose ownership reaches the caller.
    private const string StoredPair = "struct StoredPair<T>\n    Self is Iterator\n    associate Iterator.Item is (isize, T)\n    var pending: Option<(isize, T)>\n" +
        "    public init(value: T) => self.pending = .Some((41, value@move))\n    public func next(self: uniq/Self) -> Option<(isize, T)>\n" +
        "        match Kimi.Intrinsics.exchange(self.pending@uniq, with: .None)\n            ";

    [Fact]
    public void ANestedPatternThatTransfersItsValueIsAccepted()
    {
        var source = StoredPair + ".Some((let position, let value)) => return .Some((position + 1, value@move))\n            .None => return .None\n" +
            "var stored = StoredPair<string>.init(\"Stored pair.\")\nvar positions: isize = 0\nloop\n    match stored.next()\n" +
            "        .Some(let pair)\n            positions += pair.0\n            Console.writeLine(pair.1)\n        .None => exit\nrequire positions == 42 else => $abort(\"pair\")\nConsole.writeLine(\"nested\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorNestedTransfer", source, "Stored pair.\nnested\n");
    }

    // A nested wildcard or a binding left to its scope destroys that part of the item inside next.
    [Theory]
    [InlineData(".Some((let position, _)) => return .None")]
    [InlineData(".Some((let position, let value)) => return .None")]
    [InlineData(".Some((let position, let value))\n                let kept = value@move\n                return .None")]
    public void ANestedPatternThatDropsAPartIsRejected(string arm)
    {
        var c = Bounded(StoredPair + arm + "\n            .None => return .None\n");
        MinimalEmissionTest.AssertEffectBoundRejected(c);
    }

    // An aggregate that stores the item but is not transferred on is destroyed inside next.
    [Theory]
    [InlineData(".Some(let value)\n                let pair = (self.index, value@move)\n                return .None")]
    [InlineData(".Some(let value)\n                let pair: Option<(isize, T)> = .Some((self.index, value@move))\n                return .None")]
    public void AnItemStoredInADroppedAggregateIsRejected(string arm)
    {
        var c = Bounded(NumberedOnce + arm + "\n            .None => return .None\n");
        MinimalEmissionTest.AssertEffectBoundRejected(c);
    }

    // G28: assigning a `var` item local after a statement moved it out initializes it, and the new value is transferred
    // later, so a retry may reuse the local; the intervening early return leaves nothing to destroy.
    private const string Reuse = "struct Reuse<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    public var retries: i32 = 0\n" +
        "    public init(inner: I) => self.inner = inner@move\n    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        var attempt = self.inner.next()\n" +
        "        match attempt@move\n            .Some(let item) => return .Some(item@move)\n            .None => self.retries += 1\n        if self.retries > 3 => return .None\n";

    [Fact]
    public void AMovedOutLocalMayBeReinitialized()
    {
        var source = Counter + Reuse + "        attempt = self.inner.next()\n        return attempt@move\n" +
            "var reuse = Reuse<Counter>.init(Counter.init())\nvar sum: i32 = 0\nloop\n    match reuse.next()\n        .Some(let n) => sum += n\n        .None => exit\n" +
            "require sum == 6 and reuse.retries == 1 else => $abort(\"reuse\")\nConsole.writeLine(\"reused\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorReinitialized", source, "reused\n");
    }

    // G28, SPEC 14.4: in a `loop`, the value declared before it is moved at the top of the first iteration, and a value
    // reinitialized at the end of the body is moved again at the top of the next one.
    private const string LoopRetry = "struct LoopRetry<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    public var retries: i32 = 0\n" +
        "    public init(inner: I) => self.inner = inner@move\n    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        var attempt = self.inner.next()\n        loop\n" +
        "            match attempt@move\n                .Some(let item) => return .Some(item@move)\n                .None => self.retries += 1\n            if self.retries > 3 => return .None\n";

    [Fact]
    public void ALoopMayReinitializeItsItemLocal()
    {
        var source = Counter + LoopRetry + "            attempt = self.inner.next()\n" +
            "var retry = LoopRetry<Counter>.init(Counter.init())\nvar sum: i32 = 0\nloop\n    match retry.next()\n        .Some(let n) => sum += n\n        .None => exit\n" +
            "require sum == 6 and retry.retries == 4 else => $abort(\"loop retry\")\nConsole.writeLine(\"loop retried\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorLoopRetry", source, "loop retried\n");
    }

    // A `continue` to the loop restarts its body, whose first use transfers the reinitialized value.
    [Fact]
    public void ALoopMayContinueWithItsReinitializedItem()
    {
        var c = Bounded(LoopRetry + "            attempt = self.inner.next()\n            if self.retries > 1 => continue\n            self.retries += 0\n");
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    // A `continue` that leaves a match arm drops the payload bound there.
    [Fact]
    public void AContinueThatDropsAPayloadIsRejected()
    {
        const string Source = "struct Skip<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    public var skipped: i32 = 0\n" +
            "    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        loop\n            match self.inner.next()\n                .Some(let item)\n" +
            "                    self.skipped += 1\n                    if self.skipped < 2 => continue\n                    return .Some(item@move)\n                .None => return .None\n";
        var c = Bounded(Source);
        MinimalEmissionTest.AssertEffectBoundRejected(c);
    }

    // An exit after the reinitialization leaves the loop with the value, which the function then destroys.
    [Fact]
    public void ALoopThatMayLeaveWithItsItemIsRejected()
    {
        var c = Bounded(LoopRetry + "            attempt = self.inner.next()\n            if self.retries > 2 => exit\n        return .None\n");
        MinimalEmissionTest.AssertEffectBoundRejected(c);
    }

    [Fact]
    public void AWhileLoopMayEndBeforeItsFirstMoveAndIsRejected()
    {
        const string Source = "struct WhileRetry<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    public var retries: i32 = 0\n" +
            "    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        var attempt = self.inner.next()\n        while self.retries < 3\n" +
            "            match attempt@move\n                .Some(let item) => return .Some(item@move)\n                .None => self.retries += 1\n            attempt = self.inner.next()\n        return .None\n";
        var c = Bounded(Source);
        MinimalEmissionTest.AssertEffectBoundRejected(c);
    }

    // G28: each branch of an `if` transfers the item or falls through holding it, and a later statement transfers what the
    // falling-through path holds.
    private const string Branch = "struct Branch<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    public var skipped: i32 = 0\n" +
        "    public init(inner: I) => self.inner = inner@move\n    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        let first = self.inner.next()\n        if self.skipped > 0\n";

    [Fact]
    public void ABranchMayTransferTheItemBeforeALaterTransfer()
    {
        var source = Counter + Branch + "            return first@move\n        self.skipped += 1\n        return first@move\n" +
            "var branch = Branch<Counter>.init(Counter.init())\nvar sum: i32 = 0\nloop\n    match branch.next()\n        .Some(let n) => sum += n\n        .None => exit\n" +
            "require sum == 6 and branch.skipped == 1 else => $abort(\"branch\")\nConsole.writeLine(\"branched\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorBranchTransfer", source, "branched\n");
    }

    [Fact]
    public void BothBranchesMayTransferTheItem()
    {
        var c = Bounded(Branch + "            return first@move\n        else\n            self.skipped += 1\n            return first@move\n");
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    // A branch that leaves while holding the item, or a fall-through path left without a later transfer, destroys it.
    [Theory]
    [InlineData("            return .None\n        return first@move\n")]
    [InlineData("            return first@move\n        self.skipped += 1\n        return .None\n")]
    [InlineData("            return first@move\n        else\n            return .None\n")]
    public void ABranchThatMayDestroyTheItemIsRejected(string tail)
    {
        var c = Bounded(Branch + tail);
        MinimalEmissionTest.AssertEffectBoundRejected(c);
    }

    // G28: an arm that yields or evaluates to a value storing the payload hands it to the match result, which the local
    // then owns and returns.
    private const string Pick = "struct Pick<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    public var steps: i32 = 0\n" +
        "    public init(inner: I) => self.inner = inner@move\n    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        let result: Option<I.(Iterator).Item> = match self.inner.next()\n";

    [Fact]
    public void AnArmYieldingThePayloadIsAccepted()
    {
        var source = Counter + Pick + "            .Some(let item)\n                self.steps += 1\n                yield .Some(item@move)\n            .None => .None\n        return result@move\n" +
            "var pick = Pick<Counter>.init(Counter.init())\nvar sum: i32 = 0\nloop\n    match pick.next()\n        .Some(let n) => sum += n\n        .None => exit\n" +
            "require sum == 6 and pick.steps == 3 else => $abort(\"pick\")\nConsole.writeLine(\"picked\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorYieldedPayload", source, "picked\n");
    }

    [Fact]
    public void AnArmValueStoringThePayloadIsAccepted()
    {
        var c = Bounded(Pick + "            .Some(let item) => .Some(item@move)\n            .None => .None\n        return result@move\n");
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    // An arm that yields another value, or a match result left to its scope, destroys the payload.
    [Theory]
    [InlineData("            .Some(let item)\n                yield .None\n            .None => .None\n        return result@move\n")]
    [InlineData("            .Some(let item) => .Some(item@move)\n            .None => .None\n        return .None\n")]
    public void AnArmThatDropsThePayloadIsRejected(string tail)
    {
        var c = Bounded(Pick + tail);
        MinimalEmissionTest.AssertEffectBoundRejected(c);
    }

    // G28: a value-producing `if` or `match` whose every branch delivers the item stores it in its result.
    private const string Choose = "struct Choose<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    public var steps: i32 = 2\n" +
        "    public init(inner: I) => self.inner = inner@move\n    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        let first = self.inner.next()\n" +
        "        let chosen: Option<I.(Iterator).Item> = ";

    [Fact]
    public void ASelectionDeliveringTheItemInEveryBranchIsAccepted()
    {
        var source = Counter + Choose + "if self.steps > 0\n            self.steps -= 1\n            yield first@move\n        else => first@move\n        return chosen@move\n" +
            "var choose = Choose<Counter>.init(Counter.init())\nvar sum: i32 = 0\nloop\n    match choose.next()\n        .Some(let n) => sum += n\n        .None => exit\n" +
            "require sum == 6 and choose.steps == 0 else => $abort(\"choose\")\nConsole.writeLine(\"chosen\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorChosenValue", source, "chosen\n");
    }

    [Theory]
    [InlineData("match self.steps\n            0 => first@move\n            _ => first@move\n        return chosen@move\n")]
    [InlineData("match first@move\n            .Some(let item) => .Some(item@move)\n            .None => .None\n        return chosen@move\n")]
    public void AMatchDeliveringTheItemIsAccepted(string tail)
    {
        var c = Bounded(Choose + tail);
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    // A branch that delivers another value leaves the item to its scope.
    [Theory]
    [InlineData("if self.steps > 0 => first@move\n        else => .None\n        return chosen@move\n")]
    [InlineData("match self.steps\n            0 => first@move\n            _ => .None\n        return chosen@move\n")]
    public void ASelectionThatMayDropTheItemIsRejected(string tail)
    {
        var c = Bounded(Choose + tail);
        MinimalEmissionTest.AssertEffectBoundRejected(c);
    }

    // G28: a match on another value is judged as an `if`: each arm transfers the item or falls through holding it.
    private const string Select = "struct Select<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    public var skipped: i32 = 0\n" +
        "    public init(inner: I) => self.inner = inner@move\n    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        let first = self.inner.next()\n        match self.skipped\n";

    [Fact]
    public void AMatchArmMayTransferTheItemBeforeALaterTransfer()
    {
        var source = Counter + Select + "            0 => self.skipped += 1\n            _ => return first@move\n        return first@move\n" +
            "var select = Select<Counter>.init(Counter.init())\nvar sum: i32 = 0\nloop\n    match select.next()\n        .Some(let n) => sum += n\n        .None => exit\n" +
            "require sum == 6 and select.skipped == 1 else => $abort(\"select\")\nConsole.writeLine(\"selected\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorSelectTransfer", source, "selected\n");
    }

    // An arm that leaves while holding the item, or a later path left without a transfer, destroys it.
    [Theory]
    [InlineData("            0 => return .None\n            _ => ()\n        return first@move\n")]
    [InlineData("            0 => return first@move\n            _ => ()\n        return .None\n")]
    public void AMatchArmThatMayDestroyTheItemIsRejected(string tail)
    {
        var c = Bounded(Select + tail);
        MinimalEmissionTest.AssertEffectBoundRejected(c);
    }

    // G28: a constructor's first assignment of a field without a default initializes it, so an Iterator may build a
    // struct item around a value it hands out.
    private const string Entries = "struct Entries<T>\n    Self is Iterator\n    associate Iterator.Item is Entry<T>\n    var value: Option<T>\n" +
        "    public init(value: T) => self.value = .Some(value@move)\n    public func next(self: uniq/Self) -> Option<Entry<T>>\n" +
        "        match Kimi.Intrinsics.exchange(self.value@uniq, with: .None)\n            .Some(let value) => return .Some(Entry<T>.init(7, value@move))\n            .None => return .None\n";

    [Fact]
    public void AnItemBuiltByAConstructorIsAccepted()
    {
        const string Entry = "struct Entry<T>\n    public let position: isize\n    public let value: T\n    public init(position: isize, value: T)\n        self.position = position\n        self.value = value@move\n";
        var source = Entry + Entries + "var entries = Entries<string>.init(\"Entry value.\")\nvar positions: isize = 0\nloop\n    match entries.next()\n" +
            "        .Some(let entry)\n            positions += entry.position\n            Console.writeLine(entry.value)\n        .None => exit\nrequire positions == 7 else => $abort(\"entry\")\nConsole.writeLine(\"constructed\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorConstructedItem", source, "Entry value.\nconstructed\n");
    }

    // A field with a default is initialized before the body, so assigning it replaces and destroys that value.
    [Fact]
    public void AConstructorReplacingADefaultFieldIsRejected()
    {
        const string Entry = "struct Entry<T>\n    public let position: isize\n    public var value: Option<T> = .None\n    public init(position: isize, value: T)\n        self.position = position\n        self.value = .Some(value@move)\n";
        var c = Bounded(Entry + Entries);
        MinimalEmissionTest.AssertEffectBoundRejected(c);
    }

    // G28: an item passed by value to a helper is owned by the helper, whose parameter is not destroyed when its body
    // transfers it at once.
    private const string Helper = "struct Helper<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n" +
        "    public init(inner: I) => self.inner = inner@move\n    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        match self.inner.next()\n" +
        "            .Some(let item) => return self.help(item@move)\n            .None => return .None\n";

    [Fact]
    public void AnItemPassedToAHelperThatTransfersItIsAccepted()
    {
        var source = Counter + Helper + "    func help(self, value: I.(Iterator).Item) -> Option<I.(Iterator).Item>\n        let wrapped: Option<I.(Iterator).Item> = .Some(value@move)\n        return wrapped@move\n" +
            "var helper = Helper<Counter>.init(Counter.init())\nvar sum: i32 = 0\nloop\n    match helper.next()\n        .Some(let n) => sum += n\n        .None => exit\n" +
            "require sum == 6 else => $abort(\"helper\")\nConsole.writeLine(\"helped\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorHelper", source, "helped\n");
    }

    // A helper that drops its parameter, directly or through a local, destroys the item inside next.
    [Theory]
    [InlineData("    func help(self, value: I.(Iterator).Item) -> Option<I.(Iterator).Item> => .None\n")]
    [InlineData("    func help(self, value: I.(Iterator).Item) -> Option<I.(Iterator).Item>\n        let kept = value@move\n        return .None\n")]
    public void AnItemPassedToAHelperThatDropsItIsRejected(string help)
    {
        var c = Bounded(Helper + help);
        MinimalEmissionTest.AssertEffectBoundRejected(c);
    }

    // A reinitialized value left to its scope, or an assignment that replaces a value no statement moved out, destroys it.
    [Theory]
    [InlineData("        attempt = self.inner.next()\n        return .None\n")]
    [InlineData("        attempt = self.inner.next()\n        attempt = self.inner.next()\n        return attempt@move\n")]
    public void AReinitializedLocalThatMayBeDestroyedIsRejected(string tail)
    {
        var c = Bounded(Reuse + tail);
        MinimalEmissionTest.AssertEffectBoundRejected(c);
    }

    // A statement between the item's initialization and its transfer that names the item, or that may leave the block
    // normally (a return, or an exit to an iteration outside it), may destroy it inside next.
    [Theory]
    [InlineData("let first = self.inner.next()\n        return self.inner.next()")]
    [InlineData("let first = self.inner.next()\n        if self.steps == 3 => return .None\n        return first@move")]
    [InlineData("let first = self.inner.next()\n        let copy = first@move\n        self.steps += 1\n        return .None")]
    [InlineData("var first = self.inner.next()\n        first = self.inner.next()\n        return first@move")]
    [InlineData("label outer: loop\n            let first = self.inner.next()\n            loop\n                if self.steps == 3 => exit to outer\n                exit\n            return first@move\n        return .None")]
    [InlineData("loop\n            let first = self.inner.next()\n            if self.steps == 3 => exit\n            return first@move\n        return .None")]
    [InlineData("match self.inner.next()\n            .Some(let item)\n                if self.steps == 3 => return .None\n                return .Some(item@move)\n            .None => return .None")]
    public void ALocalItemThatMayBeDestroyedIsRejected(string body)
    {
        var source = "struct Hold<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    public var steps: i32 = 0\n" +
            "    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        " + body + "\n";
        var c = Bounded(source);
        MinimalEmissionTest.AssertEffectBoundRejected(c);
    }

    // A payload that is discarded, or bound and left to its scope, runs an unknown destructor inside next.
    [Theory]
    [InlineData(".Some(_) => return self.inner.next()")]
    [InlineData(".Some(let item) => return self.inner.next()")]
    [InlineData(".Some(let item)\n                let again = self.inner.next()\n                return .Some(item@move)")]
    public void AnInspectedItemThatIsNotTransferredAtOnceIsRejected(string arm)
    {
        var source = "struct Retry<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n" +
            "    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        match self.inner.next()\n            " + arm + "\n            .None => return .None\n";
        var c = Bounded(source);
        MinimalEmissionTest.AssertEffectBoundRejected(c);
    }

    private static Compilation Bounded(string source)
        => MinimalEmissionTest.Analyze(Feed + source.Replace("is Iterator", "is Feed", StringComparison.Ordinal)
            .Replace("Iterator.Item", "Feed.Item", StringComparison.Ordinal).Replace("(Iterator)", "(Feed)", StringComparison.Ordinal));
}
