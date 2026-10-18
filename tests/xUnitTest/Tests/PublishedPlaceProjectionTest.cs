// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class PublishedPlaceProjectionTest
{
    private const string Header = "func slot<D, K>(values: uniq/D, key: ref/K) -> uniq/D.Element during values\n    D is UniqIndexable<K>\n    return values[key]@uniq\nfunc change(value: uniq/i32) => value@follow += 2\n";

    [Theory]
    [InlineData("Index", "values[1]")]
    [InlineData("Direct", "values.indexUniq(1)")]
    [InlineData("Generic", "slot(values@uniq, 1)")]
    public void FixedArrayElementsKeepTheirSelectedStorage(string name, string selection)
    {
        var source = Header + "var values: Dictionary<i32, [2 of i32]> = [1: [0, 7]]\n" + selection + "[0] = 40\n" + selection + "[0] += 2\nrequire values[1][0] == 42 else => $abort(\"value\")";
        ScalarEmissionTest.EmitFixture("PublishedPlaceArray" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("Index", "values[1]")]
    [InlineData("Direct", "values.indexUniq(1)")]
    [InlineData("Generic", "slot(values@uniq, 1)")]
    public void StoredExclusiveReferencesKeepTheSelectedPlaceCapability(string name, string selection)
    {
        var source = Header + "var value = 0\nvar values = [1: (value@uniq, 7)]\n" + selection + ".0@follow = 39\n" +
            selection + ".0@follow++\nchange(" + selection + ".0)\nrequire value == 42 else => $abort(\"value\")";
        ScalarEmissionTest.EmitFixture("PublishedPlaceReference" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("Index", "values[1]")]
    [InlineData("Direct", "values.indexUniq(1)")]
    [InlineData("Generic", "slot(values@uniq, 1)")]
    public void CopyFieldsUpdateTheSelectedStorage(string name, string selection)
    {
        var source = Header + "var values = [1: (0, 7)]\n" + selection + ".0 = 39\n" + selection + ".0++\n" + selection + ".0 += 2\nrequire values[1].0 == 42 else => $abort(\"value\")";
        ScalarEmissionTest.EmitFixture("PublishedPlaceCopy" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("values[1].0@follow += 2")]
    [InlineData("change(values[1].0)")]
    public void SharedLayersStillRejectExclusiveAcquisition(string use)
    {
        var c = MinimalEmissionTest.Analyze(Header + "func run(values: ref/Dictionary<i32, (uniq/i32 during a, i32)>)\n    " + use);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Code is DiagnosticCode.SharedPathAccess_Kd or DiagnosticCode.NoApplicableOverload_Kd);
    }

    [Theory]
    [InlineData("Index", "values[key()]")]
    [InlineData("Direct", "values.indexUniq(key())")]
    [InlineData("Generic", "slot(values@uniq, key())")]
    public void UpdatesEvaluateTheRightHandSideFirstAndTheSelectorOnce(string name, string selection)
    {
        var source = Header + "func key() -> i32\n    Console.writeLine(\"key\")\n    return 1\nfunc rhs() -> i32\n    Console.writeLine(\"rhs\")\n    return 2\n" +
            "var values = [1: (40, 7)]\n" + selection + ".0 += rhs()\nrequire values[1].0 == 42 else => $abort(\"value\")";
        ScalarEmissionTest.EmitFixture("PublishedPlaceOrder" + name, source, "rhs\nkey\n");
    }

    [Theory]
    [InlineData("values[1]")]
    [InlineData("values.indexUniq(1)")]
    [InlineData("slot(values@uniq, 1)")]
    public void OptionalChildrenStillSuspendStoredParentMutation(string selection)
    {
        var source = Header + "func run(value: uniq/i32 during a)\n    var values = [1: (value@move, 7)]\n" +
            "    let child = match values.tryGet(1)\n        .Some(let item) => item.0@follow@ref\n        .None => $abort(\"missing\")\n" +
            "    " + selection + ".0@follow = 99\n    require child == 42 else => $abort(\"value\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict() && c.SyntaxOf(x.Subject).ToString() != "item.0");
        Assert.DoesNotContain(c.Problems, static x => x.Code == DiagnosticCode.Unsupported_Kd);
        Assert.False(c.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ProjectionAcquisitionReusesAnalysisStorage()
    {
        var c = MinimalEmissionTest.Analyze(Header + "var value = 40\nvar values = [1: (value@uniq, 7)]\nchange(values[1].0)\nrequire value == 42 else => $abort(\"value\")");
        var valid = true;
        var bytes = AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified);
        Assert.True(valid);
        Assert.Equal(0, bytes);
    }

    [Theory]
    [InlineData("Index", "values[1]")]
    [InlineData("Direct", "values.indexUniq(1)")]
    [InlineData("Generic", "slot(values@uniq, 1)")]
    public void NamedStoredFieldsUseTheSameAcquisition(string name, string selection)
    {
        var source = Header + "struct Pair\n    public var item: i32\n    public init(item: i32) => self.item = item\n" +
            "var values = [1: Pair.init(0)]\n" + selection + ".item = 39\n" + selection + ".item++\n" + selection + ".item += 2\nrequire values[1].item == 42 else => $abort(\"value\")";
        ScalarEmissionTest.EmitFixture("PublishedPlaceField" + name, source, string.Empty);
    }

    // SPEC 3.4.3, 4.7.1, 7.3, 13.4 (PLAN G147 (2b)): a part below an element storing one reference layer is a Place reached through that
    // reference, so a receiver, a write, an update and a Tuple element change the referent, a compared string is inspected in place,
    // and a shared read of a built-in element reads it through its stored reference.
    [Fact]
    public void PartsBelowAStoredReferenceReachItsReferent()
    {
        const string Source = """
            struct Inner
                public var x: i32 = 0
                public var name: string = "i"
                public init() => ()
                public func mutate(self: uniq/Self) => self.x += 7
                public func look(self) -> i32 => self.x
            struct Point
                public var inner: Inner = Inner.init()
                public var n: i32 = 1
                public var name: string = "p"
                public init() => ()
            func named(value: uniq/Point during a) -> bool
                var entries: Dictionary<i32, uniq/Point during a> = [1: value@move]
                return entries[1].name == "p" and entries[1].inner.name == "i"
            func change(value: uniq/Point during a)
                var entries: Dictionary<i32, uniq/Point during a> = [1: value@move]
                entries[1].inner.mutate()
                entries[1].n = 5
                entries[1].n += entries[1].inner.look()
            func pair(value: uniq/(i32, i32) during a)
                var entries: Dictionary<i32, uniq/(i32, i32) during a> = [1: value@move]
                entries[1].0 += entries[1].1
            func read(value: uniq/Point during a) -> i32
                var vals: Array<uniq/Point during a> = [value@move]
                return vals[0].n
            var p = Point.init()
            change(p@uniq)
            var t: (i32, i32) = (1, 2)
            pair(t@uniq)
            require p.inner.x == 7 and p.n == 12 and t.0 == 3 and read(p@uniq) == 12 and named(p@uniq) else => $abort("stored")
            """;
        ScalarEmissionTest.EmitFixture("PublishedPlaceStoredLayer", Source, string.Empty);
    }

    // DECISIONS BU-10 (until R6-check): the old paths reach the referent of the reference an element stores only one layer deep, and
    // exclusively only where no built-in element is on the path, so the old analysis reports any other read through an element's stored
    // layers, a comparison's included, as the located Unsupported_Kd at the element, although Binding accepts it and the MIR builds it
    // in place.
    [Theory]
    [InlineData("func f(value: uniq/(uniq/Point during a) during b)\n    var entries: Dictionary<i32, uniq/(uniq/Point during a) during b> = [1: value@move]\n    entries[1].inner.mutate()", "entries[1]")]
    [InlineData("func f(value: uniq/(uniq/Point during a) during b) -> i32\n    var entries: Dictionary<i32, uniq/(uniq/Point during a) during b> = [1: value@move]\n    return entries[1].n", "entries[1]")]
    [InlineData("func f(value: uniq/(uniq/Point during a) during b)\n    var entries: Dictionary<i32, uniq/(uniq/Point during a) during b> = [1: value@move]\n    entries[1].n = 5", "entries[1]")]
    [InlineData("func f(value: uniq/Point during a)\n    var vals: Array<uniq/Point during a> = [value@move]\n    vals[0].inner.mutate()", "vals[0]")]
    [InlineData("func f(value: uniq/Point during a)\n    var vals: Array<uniq/Point during a> = [value@move]\n    vals[0].n = 5", "vals[0]")]
    [InlineData("func f(value: uniq/Point during a)\n    var vals: Array<uniq/Point during a> = [value@move]\n    vals[0].n += 1", "vals[0]")]
    [InlineData("func f(value: uniq/Point during a)\n    let vals: Array<uniq/Point during a> = [value@move]\n    vals[0].inner.mutate()", "vals[0]")]
    [InlineData("func f(a: uniq/Array<uniq/Point during x>) => a[0].n = 5", "a[0]")]
    [InlineData("func f(value: uniq/(i32, i32) during a)\n    var vals: Array<uniq/(i32, i32) during a> = [value@move]\n    vals[0].0 = 5", "vals[0]")]
    [InlineData("func f(value: uniq/(uniq/Point during a) during b) -> i32\n    var vals: Array<uniq/(uniq/Point during a) during b> = [value@move]\n    return vals[0].n", "vals[0]")]
    [InlineData("func f(value: uniq/Point during a)\n    var vals: Array<Dictionary<i32, uniq/Point during a>> = [[1: value@move]]\n    vals[0][1].n = 5", "vals[0][1]")]
    [InlineData("func f(value: uniq/Point during a)\n    var vals: Array<Dictionary<i32, uniq/Point during a>> = [[1: value@move]]\n    vals[0][1].inner.mutate()", "vals[0][1]")]
    [InlineData("func f(value: uniq/(uniq/Point during a) during b) -> bool\n    var entries: Dictionary<i32, uniq/(uniq/Point during a) during b> = [1: value@move]\n    return entries[1].name == \"a\"", "entries[1]")]
    public void TheOldEmissionReborrowLimitIsUnsupported(string function, string element)
    {
        var c = MinimalEmissionTest.Analyze("struct Inner\n    public var x: i32 = 0\n    public init() => ()\n    public func mutate(self: uniq/Self) => self.x = 7\n" +
            "struct Point\n    public var inner: Inner = Inner.init()\n    public var n: i32 = 0\n    public var name: string = \"\"\n    public init() => ()\n" + function + "\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var unsupported = Assert.Single(c.Problems, static x => x.Code == DiagnosticCode.Unsupported_Kd);
        Assert.Equal(element, c.SyntaxOf(unsupported.Subject).ToString());
    }
}
