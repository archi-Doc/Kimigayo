// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class IteratorOriginEffectsTest
{
    [Fact]
    public void ExactExpectedExclusiveReferencesAreReborrowed()
    {
        const string source = "struct Cursor {source}\n    Self is LendingIterator\n    associate LentItem(step) is ref/i32 during step\n    let value: uniq/i32 during source\n    public init(value: uniq/i32 during source) => self.value = value@move\n    public func next(self: uniq/Self during step) -> Option<ref/i32 during step>\n        do\n            let alias: uniq/i32 during source = self.value\n            alias@follow += 1\n        return .Some(self.value)\nvar value = 40\nvar cursor = Cursor.init(value@uniq)\nlet first = cursor.next()\nmatch first\n    .Some(let n) => require n == 41 else => $abort(\"first\")\n    .None => $abort(\"empty\")\nlet second = cursor.next()\nmatch second\n    .Some(let n) => require n == 42 else => $abort(\"second\")\n    .None => $abort(\"empty\")\nConsole.writeLine(\"exact reborrow\")";
        ScalarEmissionTest.EmitFixture("AssociatedLendingExactReborrow", source, "exact reborrow\n");
    }

    [Theory]
    [InlineData("ref", "a", true)]
    [InlineData("uniq", "a", false)]
    [InlineData("ref", "b", true)]
    [InlineData("uniq", "b", false)]
    public void ImplicitLocalReborrowsKeepTheirOriginalAccessEffect(string semantics, string origin, bool valid)
    {
        var source = "struct Cursor {a, b}\n    origin a outlives b\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during a\n    let value: uniq/i32 during a\n    public func next(self: uniq/Self) -> Option<ref/i32 during a>\n        do\n            let alias: " + semantics + "/i32 during " + origin + " = self.value\n            _ = alias@move\n        return .Some(self.value)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

    [Fact]
    public void IntersectionItemsSurviveIndependentSourceUpdates()
    {
        const string source = "struct Cursor {a, b, c}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during (a and b)\n    let first: ref/i32 during a\n    let second: ref/i32 during b\n    let other: uniq/i32 during c\n    var count: i32 = 0\n    public init(first: ref/i32 during a, second: ref/i32 during b, other: uniq/i32 during c)\n        self.first = first\n        self.second = second\n        self.other = other@move\n    public func next(self: uniq/Self) -> Option<ref/i32 during (a and b)>\n        self.other@follow += 1\n        self.count += 1\n        if self.count == 1 => return .Some(self.first)\n        return .Some(self.second)\nlet a = 41\nlet b = 42\nvar other = 0\nvar cursor = Cursor.init(a@ref, b@ref, other@uniq)\nlet first = cursor.next()\nlet second = cursor.next()\nmatch first\n    .Some(let n) => require n == 41 else => $abort(\"first\")\n    .None => $abort(\"empty\")\nmatch second\n    .Some(let n) => require n == 42 else => $abort(\"second\")\n    .None => $abort(\"empty\")\nrequire other == 2 else => $abort(\"other\")\nConsole.writeLine(\"intersection\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorIntersection", source, "intersection\n");
    }

    [Fact]
    public void SharedImplicitReborrowsAllowRetainedItems()
    {
        const string source = "struct Cursor {a}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during a\n    let value: uniq/i32 during a\n    public init(value: uniq/i32 during a) => self.value = value@move\n    public func next(self: uniq/Self) -> Option<ref/i32 during a>\n        let alias: ref/i32 during a = self.value\n        require alias == 42 else => $abort(\"alias\")\n        return .Some(alias)\nvar value = 42\nvar cursor = Cursor.init(value@uniq)\nlet first = cursor.next()\nlet second = cursor.next()\nmatch first\n    .Some(let n) => require n == 42 else => $abort(\"first\")\n    .None => $abort(\"empty\")\nmatch second\n    .Some(let n) => require n == 42 else => $abort(\"second\")\n    .None => $abort(\"empty\")\nConsole.writeLine(\"implicit shared\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorImplicitShared", source, "implicit shared\n");
    }

    [Theory]
    [InlineData("self.first@follow += 1", false)]
    [InlineData("self.second@follow += 1", false)]
    [InlineData("self.other@follow += 1", true)]
    [InlineData("self.count += 1", true)]
    public void IntersectionResultsKeepEveryPossibleSource(string operation, bool valid)
    {
        var source = "struct Cursor {a, b, c}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during (a and b)\n    let first: uniq/i32 during a\n    let second: uniq/i32 during b\n    let other: uniq/i32 during c\n    var count: i32 = 0\n    public func next(self: uniq/Self) -> Option<ref/i32 during (a and b)>\n        " + operation + "\n        if self.count == 0 => return .Some(self.first)\n        return .Some(self.second)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

    private const string Point = "struct Point\n    public var x: i32 = 0\n";
    private const string Box = "struct Box\n    Self is UniqIndexable<isize>\n    associate Element is i32\n    var value: i32 = 0\n" +
        "    public func index(self, key: ref/isize) -> place ref/i32 during self => self.value\n" +
        "    public func indexUniq(self: uniq/Self, key: ref/isize) -> place uniq/i32 during self => self.value\n";

    private const string Bump = "group Helpers\n    public func bump(value: uniq/i32) => value@follow += 1\n";

    // SPEC 22.1.2.4: every write that reaches a Loan of an earlier item conflicts, however the Place is written; the same
    // write through an unrelated Origin does not.
    [Theory]
    [InlineData(Point, "let point: uniq/Point during {0}", "self.point.x += 1")]
    [InlineData("", "let values: uniq/Array<i32> during {0}", "self.values.append(1)")]
    [InlineData(Box, "let box: uniq/Box during {0}", "self.box[0] += 1")]
    [InlineData(Box, "let box: uniq/Box during {0}", "_ = self.box[0]@uniq/i32")]
    [InlineData(Bump, "let counter: uniq/i32 during {0}", "Helpers.bump(self.counter)")]
    [InlineData("", "let counter: uniq/i32 during {0}", "self.counter@follow = 5")]
    public void WritesReachingItemLoansAreRejected(string prefix, string field, string operation)
    {
        for (var i = 0; i < 2; i++)
        {
            var origin = i == 0 ? "a" : "b";
            var source = prefix + "struct Cursor {a, b}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during a\n    let first: ref/i32 during a\n    " +
                string.Format(System.Globalization.CultureInfo.InvariantCulture, field, origin) + "\n    public func next(self: uniq/Self) -> Option<ref/i32 during a>\n        " + operation + "\n        return .Some(self.first)";
            var c = MinimalEmissionTest.Analyze(source);
            Assert.True((origin == "b") == c.Binding.Result.IsComplete, origin + "\n" + MinimalEmissionTest.Describe(c, null));
            if (origin == "a")
            {
                Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
            }
        }
    }

    // Shared reads through the items' Origin keep shared items valid; the use selects index, not indexUniq.
    [Theory]
    [InlineData(Point, "let point: uniq/Point during a", "_ = self.point.x")]
    [InlineData(Box, "let box: uniq/Box during a", "_ = self.box[0]")]
    [InlineData("", "let values: uniq/Array<i32> during a", "_ = self.values.length")]
    public void SharedReadsOfItemSourcesAreAllowed(string prefix, string field, string operation)
    {
        var source = prefix + "struct Cursor {a}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during a\n    let first: ref/i32 during a\n    " + field +
            "\n    public func next(self: uniq/Self) -> Option<ref/i32 during a>\n        " + operation + "\n        return .Some(self.first)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 8.4.3, 22.1.2.4: an abstract item may be instantiated with any Loan of the conforming Type's Origins.
    [Theory]
    [InlineData("self.counter@follow += 1", false)]
    [InlineData("self.count += 1", true)]
    public void AbstractItemsKeepEveryOriginOfTheirType(string operation, bool valid)
    {
        var source = "struct Wrap<T> {a}\n    Self is Iterator\n    associate Iterator.Item is T\n    let counter: uniq/i32 during a\n    var count: i32 = 0\n    var pending: Option<T>\n" +
            "    public func next(self: uniq/Self) -> Option<T>\n        " + operation + "\n        return Kimi.Intrinsics.exchange(self.pending@uniq, with: .None)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

    // Warm rebinding reruns both effect bounds, through intersection items, generic callees and formatting, without allocating.
    [Fact]
    public void WarmEffectBoundChecksDoNotAllocate()
    {
        const string Source = "group Helpers\n    public func bump<T>(value: uniq/i32, marker: ref/T) => value@follow += 1\n" +
            "struct Cursor {a, b, c}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during (a and b)\n    let first: ref/i32 during a\n    let second: ref/i32 during b\n    let other: uniq/i32 during c\n    var count: i32 = 0\n" +
            "    public func next(self: uniq/Self) -> Option<ref/i32 during (a and b)>\n        Helpers.bump(self.other, self.count@ref)\n        self.count += 1\n        if self.count == 1 => return .Some(self.first)\n        return .Some(self.second)\n" +
            "struct Writer\n    Self is BufferWriter\n    var local: i32 = 1\n    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>\n        _ = \"value: \\(self.local)\"\n        return .Err(BufferFull.init())";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var allocated = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Equal(0, allocated);
    }

    private const string Counter = "struct Counter\n    Self is Iterator\n    associate Iterator.Item is i32\n    var value: i32 = 0\n    public init() => ()\n" +
        "    public func next(self: uniq/Self) -> Option<i32>\n        if self.value == 3 => return .None\n        self.value += 1\n        return .Some(self.value)\n";

    // SPEC 22.1.2.4: a delegating wrapper forwards one inner iterator's items; that iterator's published bound covers them.
    [Fact]
    public void ADelegatingWrapperForwardsItsInnerItems()
    {
        const string Wrapper = "struct Wrapper<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    public var count: i32 = 0\n" +
            "    public init(inner: I) => self.inner = inner@move\n    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        self.count += 1\n        return self.inner.next()\n";
        var source = Counter + Wrapper + "var wrapper = Wrapper<Counter>.init(Counter.init())\nvar sum: i32 = 0\nloop\n    match wrapper.next()\n        .Some(let n) => sum += n\n        .None => exit\n" +
            "require sum == 6 and wrapper.count == 4 else => $abort(\"wrapper\")\nConsole.writeLine(\"wrapped\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorWrapper", source, "wrapped\n");
    }

    // SPEC 22.1.2.4: all items come from the one stored Iterator, so its bound covers every step of it, from any number of
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

    // The inner bound covers only the stored iterator's own items: a wrapper whose items are not that iterator's, that
    // steps a second iterator, or that stores another value naming it (an item slot), cannot certify its own bound; an
    // item discarded inside next runs an unknown destructor.
    [Theory]
    [InlineData("struct Wrapper<I> {a}\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is uniq/i32 during a\n    var inner: I\n    var slot: Option<uniq/i32 during a>\n    public func next(self: uniq/Self) -> Option<uniq/i32 during a>\n        _ = self.inner.next()\n        return Kimi.Intrinsics.exchange(self.slot@uniq, with: .None)")]
    [InlineData("struct Wrapper<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    var other: I\n    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        _ = self.other.next()\n        return self.inner.next()")]
    [InlineData("struct Chain<I> {source}\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var first: uniq/I during source\n    var second: uniq/I during source\n" +
        "    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        match self.first.next()\n            .Some(let item) => return .Some(item@move)\n            .None => return self.second.next()")]
    [InlineData("struct Peek<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    var pending: Option<I.(Iterator).Item>\n" +
        "    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        let following = self.inner.next()\n        return Kimi.Intrinsics.exchange(self.pending@uniq, with: following@move)")]
    [InlineData("struct Wrapper<I> {source}\n    I is LendingIterator\n    Self is LendingIterator\n    associate LendingIterator.LentItem(step) is I.(LendingIterator).LentItem(step)\n    var inner: uniq/I during source\n\n" +
        "    Self is Iterator when I is Iterator\n        associate Iterator.Item is I.(Iterator).Item\n    public func next(self: uniq/Self during step) -> Option<I.(LendingIterator).LentItem(step)>\n        _ = self.inner.next()\n        return self.inner.next()")]
    public void AWrapperWithUnboundedInnerEffectsIsRejected(string wrapper)
    {
        var c = MinimalEmissionTest.Analyze(wrapper);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    // SPEC 22.1.2.4 (G28): matching an item of the stored Iterator by value destroys nothing when every arm either matches a
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
    public void AWrapperMayHoldAnItemInALocalBeforeTransferringIt(string body)
    {
        var wrapper = "struct Hold<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    public var steps: i32 = 0\n" +
            "    public init(inner: I) => self.inner = inner@move\n    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        " + body + "\n";
        var source = Counter + wrapper + "var hold = Hold<Counter>.init(Counter.init())\nvar sum: i32 = 0\nloop\n    match hold.next()\n        .Some(let n) => sum += n\n        .None => exit\n" +
            "require sum == 6 else => $abort(\"hold\")\nConsole.writeLine(\"held\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
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
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        Assert.All(c.Binding.Issues, issue => Assert.Equal(DiagnosticCode.IncompatibleContractImplementation_Kd, issue.Code));
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
        var c = MinimalEmissionTest.Analyze(StoredPair + arm + "\n            .None => return .None\n");
        Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    // An aggregate that stores the item but is not transferred on is destroyed inside next.
    [Theory]
    [InlineData(".Some(let value)\n                let pair = (self.index, value@move)\n                return .None")]
    [InlineData(".Some(let value)\n                let pair: Option<(isize, T)> = .Some((self.index, value@move))\n                return .None")]
    public void AnItemStoredInADroppedAggregateIsRejected(string arm)
    {
        var c = MinimalEmissionTest.Analyze(NumberedOnce + arm + "\n            .None => return .None\n");
        Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
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

    // An exit after the reinitialization leaves the loop with the value, which the function then destroys.
    [Fact]
    public void ALoopThatMayLeaveWithItsItemIsRejected()
    {
        var c = MinimalEmissionTest.Analyze(LoopRetry + "            attempt = self.inner.next()\n            if self.retries > 2 => exit\n        return .None\n");
        Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    [Fact]
    public void AWhileLoopMayEndBeforeItsFirstMoveAndIsRejected()
    {
        const string Source = "struct WhileRetry<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    public var retries: i32 = 0\n" +
            "    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        var attempt = self.inner.next()\n        while self.retries < 3\n" +
            "            match attempt@move\n                .Some(let item) => return .Some(item@move)\n                .None => self.retries += 1\n            attempt = self.inner.next()\n        return .None\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
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
        var c = MinimalEmissionTest.Analyze(Helper + help);
        Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    // A reinitialized value left to its scope, or an assignment that replaces a value no statement moved out, destroys it.
    [Theory]
    [InlineData("        attempt = self.inner.next()\n        return .None\n")]
    [InlineData("        attempt = self.inner.next()\n        attempt = self.inner.next()\n        return attempt@move\n")]
    public void AReinitializedLocalThatMayBeDestroyedIsRejected(string tail)
    {
        var c = MinimalEmissionTest.Analyze(Reuse + tail);
        Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    // A statement between the item's initialization and its transfer that names the item, or that may leave the block
    // normally (a return, or an exit to an iteration outside it), may destroy it inside next.
    [Theory]
    [InlineData("let first = self.inner.next()\n        return self.inner.next()")]
    [InlineData("let first = self.inner.next()\n        if self.steps == 3 => return .None\n        return first@move")]
    [InlineData("let first = self.inner.next()\n        let copy = first@move\n        self.steps += 1\n        return .None")]
    [InlineData("var first = self.inner.next()\n        first = self.inner.next()\n        return first@move")]
    [InlineData("outer: loop\n            let first = self.inner.next()\n            loop\n                if self.steps == 3 => exit to outer\n                exit\n            return first@move\n        return .None")]
    [InlineData("loop\n            let first = self.inner.next()\n            if self.steps == 3 => exit\n            return first@move\n        return .None")]
    [InlineData("match self.inner.next()\n            .Some(let item)\n                if self.steps == 3 => return .None\n                return .Some(item@move)\n            .None => return .None")]
    public void ALocalItemThatMayBeDestroyedIsRejected(string body)
    {
        var source = "struct Hold<I>\n    I is Iterator\n    Self is Iterator\n    associate Iterator.Item is I.(Iterator).Item\n    var inner: I\n    public var steps: i32 = 0\n" +
            "    public func next(self: uniq/Self) -> Option<I.(Iterator).Item>\n        " + body + "\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
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
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    // A call without a published effect bound, such as a requirement of a Type parameter, cannot certify next.
    [Fact]
    public void UnboundedRequirementCallsAreRejected()
    {
        const string Source = "struct Outer<I> {a}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during a\n    I is Iterator\n    let first: ref/i32 during a\n    var inner: I\n" +
            "    public func next(self: uniq/Self) -> Option<ref/i32 during a>\n        _ = self.inner.next()\n        return .Some(self.first)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }
}
