// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.7.1, 8.4: a Contract declares Type parameters, and receiver[key] on a user Type resolves through its
/// Indexable&lt;Key&gt; conformance: index for reads and shared borrows, indexUniq for updates and exclusive borrows.</summary>
public class IndexableContractTest
{
    private const string Pair =
        "struct Pair<T>\n    Self is UniqIndexable<isize>\n    associate Element is T\n    var first: T\n    var second: T\n" +
        "    public init(first: T, second: T)\n        self.first = first@move\n        self.second = second@move\n" +
        "    public func index(self, key: ref/isize) -> ref/T during self\n        if key == 0 => return self.first@ref\n        return self.second@ref\n" +
        "    public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/T during self\n        if key == 0 => return self.first@uniq\n        return self.second@uniq\n";

    private const string Shared =
        "struct View\n    Self is Indexable<isize>\n    associate Element is i32\n    var value: i32\n    public init(value: i32) => self.value = value\n" +
        "    public func index(self, key: ref/isize) -> ref/i32 during self => self.value@ref\n";

    // SPEC 8.6.1: a generic Type's conformance names its reference in the Type's own parameters; an instance dispatches
    // through the reference the generic call recorded, instantiated (`Indexable<isize>` for `Keyed<isize>`).
    private const string Keyed =
        "struct Keyed<K>\n    Self is Indexable<K>\n    associate Element is i32\n    var value: i32\n    public init(value: i32) => self.value = value\n" +
        "    public func index(self, key: ref/K) -> ref/i32 during self => self.value@ref\n";

    // SPEC 8.4.3: a Constraint on a bound Contract reference indexes a Type parameter through the requirement.
    private const string FirstPlace =
        "func firstPlace<S>(items: ref/S) -> ref/S.Element during items\n    S is Indexable<isize>\n    return items[0]@ref\n";

    private const string FirstUniq =
        "func firstUniq<S>(items: uniq/S) -> uniq/S.Element during items\n    S is UniqIndexable<isize>\n    return items[0]@uniq\n";

    // A Point whose part an element storing a reference to it reaches (SPEC 3.4.3, 10.2; PLAN G147 (2b)).
    private const string Stored = "struct Inner\n    public var x: i32 = 0\n    public init() => ()\nstruct Point\n    public var inner: Inner = Inner.init()\n    public init() => ()\n";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "CopyRead", Pair + "var pair = Pair<i32>.init(1, 2)\nlet second = pair[1]\nlet key: isize = 0\nrequire second == 2 and pair[key] == 1 else => $abort(\"read\")\npair[key] = 5\nrequire pair[0] == 5 else => $abort(\"write\")\nConsole.writeLine(\"ok\")", "ok\n" },
        { "SharedBorrow", Pair + "var pair = Pair<string>.init(\"Pair first.\", \"Pair second.\")\nConsole.writeLine(pair[1])\nlet view = pair[0]@ref\nConsole.writeLine(view)\nlet annotated: ref/string = pair[1]\nConsole.writeLine(annotated)", "Pair second.\nPair first.\nPair second.\n" },
        { "ExclusiveWrite", Pair + "var counts = Pair<i32>.init(1, 2)\ncounts[1] += 40\nlet exclusive = counts[0]@uniq\nexclusive@follow = 7\nrequire counts[1] == 42 and counts[0] == 7 else => $abort(\"write\")\ncounts[0] = counts[1] + 1\nrequire counts[0] == 43 else => $abort(\"replace\")\nConsole.writeLine(\"Pair replaced.\")\nvar names = Pair<string>.init(\"Pair first.\", \"Pair second.\")\nlet lent = names[1]@uniq\nConsole.writeLine(lent)", "Pair replaced.\nPair second.\n" },
        { "ThroughReferences", Pair + "func show(pair: ref/Pair<string>) => Console.writeLine(pair[1])\nfunc bump(pair: uniq/Pair<i32>) => pair[0] += 1\nvar names = Pair<string>.init(\"a\", \"b\")\nshow(names)\nvar counts = Pair<i32>.init(41, 0)\nbump(counts@uniq)\nrequire counts[0] == 42 else => $abort(\"bump\")\nConsole.writeLine(\"ok\")", "b\nok\n" },
        { "SharedOnly", Shared + "let view = View.init(42)\nrequire view[0] == 42 else => $abort(\"shared\")\nConsole.writeLine(\"ok\")", "ok\n" },
        { "GenericShared", Pair + Shared + FirstPlace + "var pair = Pair<string>.init(\"Pair first.\", \"Pair second.\")\nConsole.writeLine(firstPlace(pair))\nlet counts = Pair<i32>.init(7, 8)\nlet view = View.init(9)\nrequire firstPlace(counts) == 7 and firstPlace(view) == 9 else => $abort(\"generic\")\nlet lent = firstPlace(pair)@follow@ref\nConsole.writeLine(lent)", "Pair first.\nPair first.\n" },
        { "GenericExclusive", Pair + FirstUniq + "var counts = Pair<i32>.init(1, 2)\nfirstUniq(counts@uniq)@follow = 40\nfirstUniq(counts@uniq)@follow += 2\nrequire counts[0] == 42 and counts[1] == 2 else => $abort(\"uniq\")\nConsole.writeLine(\"ok\")", "ok\n" },
        { "GenericConformanceReference", Keyed + FirstPlace + "let keyed = Keyed<isize>.init(7)\nrequire firstPlace(keyed) == 7 and keyed[1] == 7 else => $abort(\"keyed\")\nConsole.writeLine(\"ok\")", "ok\n" },
        { "PlaceComparison", Pair + FirstPlace + "var names = Pair<string>.init(\"a\", \"b\")\nlet held = names[1]@ref\nrequire firstPlace(names) == \"a\" and names[1] == held and firstPlace(names) != held and held == names[1] else => $abort(\"compare\")\nConsole.writeLine(\"ok\")", "ok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("Indexable" + name, source, stdout);

    [Theory]
    [InlineData(Pair + "let pair = Pair<i32>.init(1, 2)\npair[0] = 3", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData(Pair + "func f(pair: ref/Pair<i32>) => pair[0] = 3", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Pair + "var pair = Pair<string>.init(\"a\", \"b\")\nlet taken = pair[0]@move", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData(Shared + "var view = View.init(1)\nview[0] = 2", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Shared + "var view = View.init(1)\nlet exclusive = view[0]@uniq", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("struct Box\n    var value: i32\n    public init(value: i32) => self.value = value\n    public func index(self, key: ref/isize) -> ref/i32 during self => self.value@ref\nlet box = Box.init(1)\nlet read = box[0]", DiagnosticCode.NotIndexable_Kd)]
    [InlineData("struct Wrong\n    Self is Indexable<isize>\n    associate Element is string\n    var value: i32\n    public init(value: i32) => self.value = value\n    public func index(self, key: ref/isize) -> ref/i32 during self => self.value@ref", DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("struct Missing\n    Self is UniqIndexable<isize>\n    associate Element is i32\n    var value: i32\n    public init(value: i32) => self.value = value\n    public func index(self, key: ref/isize) -> ref/i32 during self => self.value@ref", DiagnosticCode.MissingContractImplementation_Kd)]
    [InlineData("struct Value\n    Self is Indexable<isize>\n    associate Element is i32\n    var value: i32\n    public init(value: i32) => self.value = value\n    public func index(self, key: ref/isize) -> i32 => self.value", DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("func f<S>(items: uniq/S) -> uniq/S.Element during items\n    S is Indexable<isize>\n    return items[0]@uniq", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Shared + FirstUniq + "var view = View.init(1)\nfirstUniq(view@uniq)@follow = 2", DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData("struct Bare\n    Self is UniqIndexable<isize>\n    associate Element is i32\n    var value: i32 = 0\n    public init() => ()\n    public func index(self, key: ref/isize) -> ref/i32 during self => self.value@ref\n    public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/i32 during self => self.value", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("struct Two\n    Self is UniqIndexable<isize>\n    var value: i32 = 0\n    public init() => ()\n    public func index(self, key: ref/isize) -> ref/i32 during self => self.value@ref\n    public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/i64 during self => $abort(\"two\")", DiagnosticCode.AssociatedTypeInferenceFailed_Kd)]
    [InlineData(Pair + "var pair = Pair<i32>.init(1, 2)\nlet n: i32 = 7\npair.indexUniq(0) = n", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData(Stored + "func f(entries: ref/Dictionary<i32, uniq/Point during a>) -> ref/Inner during a\n    return entries[1].inner@ref", DiagnosticCode.UnprovenOriginRelation_Kd)]
    [InlineData(Stored + "func f(entries: ref/Dictionary<i32, uniq/Point during a>) -> ref/Inner during a\n    return entries[1]@follow.inner@ref", DiagnosticCode.UnprovenOriginRelation_Kd)]
    [InlineData(Stored + "func f(entries: uniq/Dictionary<i32, uniq/Point during a>) -> uniq/Inner during a\n    return entries[1].inner@uniq", DiagnosticCode.UnprovenOriginRelation_Kd)]
    [InlineData(Stored + "func f(entries: uniq/Dictionary<i32, uniq/Point during a>) -> uniq/Inner during a\n    return entries[1]@follow.inner@uniq", DiagnosticCode.UnprovenOriginRelation_Kd)]
    public void RejectsAtBinding(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }

    // SPEC 3.4.3, 10.2 (PLAN G147 (2b)): a part below an element storing a shared reference is read through one shared reference, which
    // restarts the dependency at that stored reference, so it depends on the stored reference's Origin, not on the selection.
    [Theory]
    [InlineData("ref")]
    [InlineData("uniq")]
    public void APartBelowAStoredSharedReferenceKeepsItsOrigin(string receiver)
    {
        var c = MinimalEmissionTest.Analyze(Stored + $"func f(entries: {receiver}/Dictionary<i32, ref/Point during a>) -> ref/Inner during a\n    return entries[1].inner@ref");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData(Pair + "var pair = Pair<string>.init(\"a\", \"b\")\nlet bare = pair[0]")]
    [InlineData(Pair + "var pair = Pair<i32>.init(1, 2)\nlet view = pair[0]@ref\npair[0] = 3\nrequire view == 1 else => $abort(\"x\")")]
    public void RejectsAtOwnership(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(c.Generate(output));
        Assert.Empty(output.ToString());
        Assert.False(c.Binding.Result.IsComplete && c.Analysis.IsVerified);
    }

    // SPEC 8.6.1, 4.7.1: Indexable<isize> and Indexable<Name> are distinct conformances with their own Element, specified
    // through the bound reference; `receiver[key]` selects the conformance whose Key is the key's own Type, for reads and,
    // through UniqIndexable, for updates.
    private const string TwoKeys = """
        struct Name
            Self is Copy
            public let id: isize
            public init(id: isize) => self.id = id

        struct Table
            Self is UniqIndexable<isize>
            Self is UniqIndexable<Name>
            associate Indexable<isize>.Element is i32
            associate Indexable<Name>.Element is i64
            var small: i32
            var large: i64
            public init(small: i32, large: i64)
                self.small = small
                self.large = large
            public func index(self, key: ref/isize) -> ref/i32 during self => self.small@ref
            public func index(self, key: ref/Name) -> ref/i64 during self => self.large@ref
            public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/i32 during self => self.small@uniq
            public func indexUniq(self: uniq/Self, key: ref/Name) -> uniq/i64 during self => self.large@uniq

        """;

    [Fact]
    public void TheKeyTypeSelectsAmongSeveralConformances()
    {
        const string Use = "var table = Table.init(3, 4)\nlet k: isize = 0\nlet n = Name.init(1)\nlet a: i32 = table[k]\nlet b: i64 = table[n]\n" +
            "table[k] = 30\ntable[n] = 40\nlet c: i32 = table[k]\nlet d: i64 = table[n]\nrequire a == 3 and b == 4 and c == 30 and d == 40 else => $abort(\"index\")\nConsole.writeLine(\"two keys\")";
        ScalarEmissionTest.EmitFixture("IndexableContractTwoKeys", TwoKeys + Use, "two keys\n");
    }

    // SPEC 8.6.1, 4.7.1: inside a generic body, a parameter with two Indexable conformances selects the one whose Key is the
    // key's own Type, and `S.(Indexable<Name>).Element` names that conformance's Element, distinct from the other one.
    private const string GenericTwoKeys =
        "func pick<S>(table: ref/S, k: isize, n: Name) -> S.(Indexable<Name>).Element\n    S is Indexable<isize>\n    S is Indexable<Name>\n" +
        "    S.(Indexable<isize>).Element is Copy\n    S.(Indexable<Name>).Element is Copy\n    let small: S.(Indexable<isize>).Element = table[k]\n    return table[n]\n";

    [Fact]
    public void TheKeyTypeSelectsAmongSeveralGenericConformances()
    {
        const string Use = "let table = Table.init(3, 4)\nlet b: i64 = pick(table@ref, 0, Name.init(1))\nrequire b == 4 else => $abort(\"pick\")\nConsole.writeLine(\"generic keys\")";
        ScalarEmissionTest.EmitFixture("IndexableContractGenericTwoKeys", TwoKeys + GenericTwoKeys + Use, "generic keys\n");
    }

    // Qualified projections, per-reference requirement candidates and reference dispatch add no warm allocation: warm
    // Binding, ownership analysis and IR writing of the generic two-key program and its concrete counterpart allocate
    // nothing. Checking whether two conformances to one Contract declaration may collide reuses its scratch map.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmGenericTwoKeyCompilationAddsNoAllocation()
    {
        const string Generic = "let table = Table.init(3, 4)\nlet n = Name.init(1)\nlet b: i64 = pick(table@ref, 0, n)\nrequire b == 4 else => $abort(\"pick\")";
        const string Concrete = "let table = Table.init(3, 4)\nlet n = Name.init(1)\nlet b: i64 = table[n]\nrequire b == 4 else => $abort(\"pick\")";
        var generic = Measure(TwoKeys + GenericTwoKeys + Generic);
        var concrete = Measure(TwoKeys + Concrete);
        Assert.Equal((0L, 0L, 0L), generic);
        Assert.Equal((0L, 0L, 0L), concrete);

        static (long Binding, long Ownership, long Generation) Measure(string source)
        {
            var c = MinimalEmissionTest.Analyze(source);
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Analyze().IsVerified);
            Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
            var success = true;
            var binding = AllocationMeasurement.Measure(() => success &= c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            var ownership = AllocationMeasurement.Measure(() => success &= c.Analyze().IsVerified);
            var generation = AllocationMeasurement.Measure(() => success &= c.Generate(TextWriter.Null));
            Assert.True(success);
            return (binding, ownership, generation);
        }
    }

    // Through UniqIndexable, updates select each conformance's indexUniq by the key Type in generic code.
    [Fact]
    public void GenericUpdatesSelectAmongSeveralUniqConformances()
    {
        const string Update = "func keySlot<S>(table: uniq/S, k: isize) -> uniq/S.(Indexable<isize>).Element during table\n    S is UniqIndexable<isize>\n    S is UniqIndexable<Name>\n    return table[k]@uniq\n" +
            "func nameSlot<S>(table: uniq/S, n: Name) -> uniq/S.(Indexable<Name>).Element during table\n    S is UniqIndexable<isize>\n    S is UniqIndexable<Name>\n    return table[n]@uniq\n";
        const string Use = "var table = Table.init(3, 4)\nkeySlot(table@uniq, 0)@follow = 30\nnameSlot(table@uniq, Name.init(1))@follow = 40\nnameSlot(table@uniq, Name.init(1))@follow += 2\nlet k: isize = 0\nlet n = Name.init(1)\n" +
            "let a: i32 = table[k]\nlet b: i64 = table[n]\nrequire a == 30 and b == 42 else => $abort(\"update\")\nConsole.writeLine(\"generic updates\")";
        ScalarEmissionTest.EmitFixture("IndexableContractGenericUpdates", TwoKeys + Update + Use, "generic updates\n");
    }

    // The two Element projections are distinct Types in the generic body.
    [Fact]
    public void GenericElementsOfDistinctReferencesDoNotMix()
    {
        const string Mixed = "func mix<S>(table: ref/S, n: Name) -> S.(Indexable<isize>).Element\n    S is Indexable<isize>\n    S is Indexable<Name>\n" +
            "    S.(Indexable<Name>).Element is Copy\n    return table[n]\n";
        var c = MinimalEmissionTest.Analyze(TwoKeys + Mixed);
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("let table = Table.init(3, 4)\nlet a: i32 = table[0]")]
    [InlineData("let table = Table.init(3, 4)\nlet s: string = \"x\"\nlet a: i32 = table[s]")]
    public void AKeyThatSelectsNoSingleConformanceIsRejected(string use)
    {
        var c = MinimalEmissionTest.Analyze(TwoKeys + use);
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void ARequirementCallKeepsItsReferenceAcrossAnArgumentBoundAfterSelection()
    {
        // The range literal argument is bound after selection through its own synthesized call; that call once cleared the
        // enclosing requirement reference, so the generic call had no implementation mapping and generation failed.
        const string Source = "struct Keyed<K>\n    Self is Indexable<K>\n    associate Element is i32\n    var value: i32\n    public init(value: i32) => self.value = value\n" +
            "    public func index(self, key: ref/K) -> ref/i32 during self => self.value@ref\n" +
            "func pick<S>(items: ref/S) -> ref/S.Element during items\n    S is Indexable<Range<i32, i32>>\n    return items.index(0..2)\n" +
            "let keyed = Keyed<Range<i32, i32>>.init(7)\nrequire pick(keyed) == 7 else => $abort(\"pick\")\nConsole.writeLine(\"ok\")";
        var c = MinimalEmissionTest.Analyze(Source);
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single(x => x.ToString() == "items.index(0..2)");
        Assert.NotNull(call.CallOf()!.RequirementContract);
        ScalarEmissionTest.EmitFixture("IndexableRequirementRangeArgument", Source, "ok\n");
    }

    private const string ConditionalUniq =
        "struct Box<T>\n    Self is Indexable<isize>\n    associate Element is T\n    var value: T\n    public init(value: T) => self.value = value@move\n" +
        "    public func index(self, key: ref/isize) -> ref/T during self => self.value@ref\n\n" +
        "    Self is UniqIndexable<isize> when T is Copy\n        public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/T during self => self.value@uniq\n\n";

    [Fact]
    public void AReadDoesNotNeedAConditionalUniqConformance()
    {
        // SPEC 4.7.1, 8.3.3: a read selects index; the refuted UniqIndexable condition of Box<string> supplies nothing and
        // is not judged at the read, while Box<i32> keeps indexUniq for its update.
        const string Use = "var names = Box<string>.init(\"a\")\nConsole.writeLine(names[0])\nvar counts = Box<i32>.init(1)\ncounts[0] = 2\nrequire counts[0] == 2 else => $abort(\"update\")\nConsole.writeLine(\"done\")";
        ScalarEmissionTest.EmitFixture("IndexableConditionalUniqRead", ConditionalUniq + Use, "a\ndone\n");
    }

    [Theory]
    [InlineData(ConditionalUniq, "Box<string> conforms to UniqIndexable only under a condition that does not hold here")]
    [InlineData("struct Box<T>\n    Self is Indexable<isize>\n    associate Element is T\n    var value: T\n    public init(value: T) => self.value = value@move\n    public func index(self, key: ref/isize) -> ref/T during self => self.value@ref\n", "Box<string> has no UniqIndexable conformance")]
    public void AnUpdateWithoutIndexUniqExplainsTheMissingConformance(string declarations, string note)
    {
        var result = DiagnosticCorpus.Check(declarations + "public func main()\n    var names = Box<string>.init(\"a\")\n    names[0] = \"b\"\n");
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.SharedPathAccess_Kd), record.Code);
        Assert.StartsWith(note, record.Note);
    }

    // SPEC 4.7.1, 8.6.1 (PLAN G290): among several Key conformances, the Note names the UniqIndexable conformance the check selected by
    // the key, also one whose argument is a projection over the parameter or the parameter, as the closed twin does (before: on dev
    // AmbiguousBinding_Kd for both, then a false "has no UniqIndexable conformance for this key").
    [Theory]
    [InlineData("    S is HasItem\n", "Option<S.(HasItem).Item>", "M<SrcI>", "Option<i32>")]
    [InlineData("", "Option<S>", "M<string>", "Option<string>")]
    [InlineData("", "Option<i32>", "M<string>", "Option<i32>")]
    public void AKeyedUpdateWithoutIndexUniqNamesTheSelectedConformance(string premise, string key, string instance, string local)
    {
        var result = DiagnosticCorpus.Check(
            "contract HasItem\n    associate Item\nstruct SrcI\n    Self is HasItem\n    associate HasItem.Item is i32\nstruct Name\n    Self is Copy\n" +
            $"    public let id: isize\n    public init(id: isize) => self.id = id\nstruct M<S>\n{premise}    Self is Indexable<{key}>\n" +
            $"    Self is UniqIndexable<Name>\n    associate Indexable<{key}>.Element is i32\n    associate Indexable<Name>.Element is i64\n" +
            "    var small: i32\n    var large: i64\n    public init(small: i32, large: i64)\n        self.small = small\n        self.large = large\n" +
            $"    public func index(self, key: ref/{key}) -> ref/i32 during self => self.small@ref\n" +
            "    public func index(self, key: ref/Name) -> ref/i64 during self => self.large@ref\n" +
            "    public func indexUniq(self: uniq/Self, key: ref/Name) -> uniq/i64 during self => self.large@uniq\n\n" +
            $"    Self is UniqIndexable<{key}> when S is Copy\n" +
            $"        public func indexUniq(self: uniq/Self, key: ref/{key}) -> uniq/i32 during self => self.small@uniq\n" +
            $"public func main()\n    var m = {instance}.init(3, 4)\n    let k: {local} = .None\n    m[k] = 5\n");
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.SharedPathAccess_Kd), record.Code);
        Assert.StartsWith($"{instance} conforms to UniqIndexable only under a condition that does not hold here", record.Note);
    }

    // SPEC 8.5.5, 4.7.1: the reference results of index and indexUniq are evidence for Element like any function result; a written call
    // returns the reference itself, and only the index expression follows it.
    [Fact]
    public void IndexResultsInferTheElementAndWrittenCallsReturnTheReference()
    {
        const string Source = """
            struct Cells
                Self is UniqIndexable<isize>
                var first: i32
                var second: i32
                public init(first: i32, second: i32)
                    self.first = first
                    self.second = second
                public func index(self, key: ref/isize) -> ref/i32 during self
                    if key == 0 => return self.first@ref
                    return self.second@ref
                public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/i32 during self
                    if key == 0 => return self.first@uniq
                    return self.second@uniq
            func head<S>(items: ref/S) -> S.Element
                S is Indexable<isize>
                S.Element is Copy
                return items[0]
            var cells = Cells.init(1, 2)
            cells[1] += 40
            let lent: ref/i32 = cells.index(1)
            require lent == 42 else => $abort("lent")
            cells.indexUniq(0)@follow = 7
            require head(cells) == 7 else => $abort("head")
            Console.writeLine("ok")
            """;
        ScalarEmissionTest.EmitFixture("IndexableInferredElement", Source, "ok\n");
    }

    [Fact]
    public void BoundContractReferencesKeepDistinctIdentities()
    {
        var c = MinimalEmissionTest.Analyze(Pair + "var pair = Pair<i32>.init(1, 2)\nrequire pair[0] == 1 else => $abort(\"x\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var uniqIndexable = c.Library.GetSymbol(KimiDeclarationId.UniqIndexable)!;
        Assert.Contains(uniqIndexable.Contract!.Ancestors, x => ReferenceEquals(x.Declaration, c.Library.GetSymbol(KimiDeclarationId.Indexable)!.Declaration) && !ReferenceEquals(x, c.Library.GetSymbol(KimiDeclarationId.Indexable)));
    }
}
