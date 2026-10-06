// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class LocalRegionCompositionTest
{
    private const string Values = "let a = 1\nlet b = 2\n";
    private const string Cell = "struct Cell {source}\n    public var item: ref/i32 during source\n    public init(item: ref/i32 during source) => self.item = item\n";
    private const string Put = "func put<T>(slot: uniq/T, value: T) => slot@follow = value@move\n";
    private const string None = "func none() -> Option<ref/i32 during source> => .None\n";

    [Theory]
    [InlineData("Tuple", Values + "var value = (a@ref, 7)\nvalue = (b@ref, 9)\nrequire value.0@follow == 2 and value.1 == 9 else => $abort(\"tuple\")")]
    [InlineData("FixedArray", Values + "var value: [1 of ref/i32] = [a@ref]\nvalue = [b@ref]\nrequire value[0]@follow == 2 else => $abort(\"fixed\")")]
    [InlineData("Struct", Cell + Values + "var value = Cell.init(a@ref)\nvalue.item = b@ref\nrequire value.item@follow == 2 else => $abort(\"field\")")]
    [InlineData("Array", Values + "var value: Array<ref/i32> = [a@ref]\nvalue@uniq.append(b@ref)\nrequire value[0]@follow == 1 and value[1]@follow == 2 else => $abort(\"array\")")]
    [InlineData("Dictionary", Values + "var value: Dictionary<i32, ref/i32> = [1: a@ref]\nvalue[1] = b@ref\nrequire value[1]@follow == 2 else => $abort(\"dictionary\")")]
    [InlineData("Slice", "let a: [1 of i32] = [1]\nlet b: [1 of i32] = [2]\nvar value = a[..]\nvalue = b[..]\nrequire value[0] == 2 else => $abort(\"slice\")")]
    [InlineData("Enum", None + Values + "var value = none()\nvalue = .Some(b@ref)\nmatch value\n    .Some(let item) => require item@follow == 2 else => $abort(\"some\")\n    .None => $abort(\"none\")")]
    [InlineData("AnnotatedEnum", Values + "var value: Option<ref/i32> = .None\nvalue = .Some(b@ref)\nmatch value\n    .Some(let item) => require item@follow == 2 else => $abort(\"some\")\n    .None => $abort(\"none\")")]
    [InlineData("IndirectEnum", Put + None + Values + "var value = none()\nput(value@uniq, .Some(b@ref))\nmatch value\n    .Some(let item) => require item@follow == 2 else => $abort(\"some\")\n    .None => $abort(\"none\")")]
    [InlineData("Capture", "var a = 1\nvar b = 2\nvar view = a@ref\nlet f = func [view] () => view@follow\nview = b@ref\nrequire view@follow == 2 else => $abort(\"view\")\nb = 3\nrequire f() == 1 else => $abort(\"capture\")")]
    [InlineData("EmptyFixedReturn", None + "func empty() -> Option<ref/i32 during static>\n    let value = none()\n    return value\nmatch empty()\n    .Some(_) => $abort(\"some\")\n    .None => ()")]
    [InlineData("AnonymousResult", Values + "let f = func [a@ref, b@ref] ()\n    var view = a\n    view = b\n    return view\nrequire f()@follow == 2 else => $abort(\"result\")")]
    public void CompositeSlotsKeepOneTypeAndTheActualValues(string name, string source)
        => ScalarEmissionTest.EmitFixture("LocalRegionComposition" + name, source, string.Empty);

    private const string MutableValues = "var a = 1\nvar b = 2\n";

    [Theory]
    [InlineData("TupleEnded", MutableValues + "var value = (a@ref, 7)\nvalue = (b@ref, 9)\na = 3\nrequire value.0@follow == 2 else => $abort(\"tuple\")")]
    [InlineData("FixedEnded", MutableValues + "var value: [1 of ref/i32] = [a@ref]\nvalue = [b@ref]\na = 3\nrequire value[0]@follow == 2 else => $abort(\"fixed\")")]
    [InlineData("StructEnded", Cell + MutableValues + "var value = Cell.init(a@ref)\nvalue = Cell.init(b@ref)\na = 3\nrequire value.item@follow == 2 else => $abort(\"struct\")")]
    [InlineData("FieldEnded", Cell + MutableValues + "var value = Cell.init(a@ref)\nvalue.item = b@ref\na = 3\nrequire value.item@follow == 2 else => $abort(\"field\")")]
    [InlineData("ArrayEnded", MutableValues + "var value: Array<ref/i32> = [a@ref]\nvalue = [b@ref]\na = 3\nrequire value[0]@follow == 2 else => $abort(\"array\")")]
    [InlineData("DictionaryEnded", MutableValues + "var value: Dictionary<i32, ref/i32> = [1: a@ref]\nvalue = [1: b@ref]\na = 3\nrequire value[1]@follow == 2 else => $abort(\"dictionary\")")]
    [InlineData("EnumEnded", MutableValues + "var value: Option<ref/i32> = .Some(a@ref)\nvalue = .None\na = 3\nmatch value\n    .Some(_) => $abort(\"some\")\n    .None => ()")]
    [InlineData("TupleCopyEnded", MutableValues + "var value = (a@ref, 7)\nlet saved = value\nvalue = (b@ref, 9)\nrequire saved.0@follow == 1 else => $abort(\"saved\")\na = 3\nrequire value.0@follow == 2 else => $abort(\"current\")")]
    [InlineData("TuplePartEnded", MutableValues + "var value = (a@ref, 7)\nvalue.0 = b@ref\na = 3\nrequire value.0@follow == 2 else => $abort(\"part\")")]
    [InlineData("TuplePartsEnded", MutableValues + "var value = (a@ref, a@ref)\nvalue.0 = b@ref\nvalue.1 = b@ref\na = 3\nrequire value.0@follow == 2 and value.1@follow == 2 else => $abort(\"parts\")")]
    [InlineData("NestedPartsEnded", MutableValues + "var value = ((a@ref, 7), b@ref)\nvalue.0.0 = b@ref\na = 3\nrequire value.0.0@follow == 2 and value.1@follow == 2 else => $abort(\"nested\")")]
    [InlineData("FixedPartsEnded", MutableValues + "var value: [2 of ref/i32] = [a@ref, a@ref]\nvalue[0] = b@ref\nvalue[1] = b@ref\na = 3\nrequire value[0]@follow == 2 and value[1]@follow == 2 else => $abort(\"parts\")")]
    [InlineData("TupleBranchEnded", MutableValues + "var value = (a@ref, a@ref)\nif a == 1\n    value.0 = b@ref\nelse\n    value.0 = b@ref\nvalue.1 = b@ref\na = 3\nrequire value.0@follow == 2 and value.1@follow == 2 else => $abort(\"branch\")")]
    [InlineData("TupleCopyAfterParts", MutableValues + "var value = (a@ref, a@ref)\nvalue.0 = b@ref\nvalue.1 = b@ref\nlet saved = value\na = 3\nrequire saved.0@follow == 2 and saved.1@follow == 2 else => $abort(\"saved\")")]
    public void CompleteAndPartialReplacementEndOnlyTheOldLoans(string name, string source)
        => ScalarEmissionTest.EmitFixture("LocalRegionComposition" + name, source, string.Empty);

    [Theory]
    [InlineData(MutableValues + "var value = (a@ref, 7)\nvalue = (b@ref, 9)\nb = 3\nrequire value.0@follow == 2 else => $abort(\"tuple\")", "b = 3")]
    [InlineData(MutableValues + "var value: [1 of ref/i32] = [a@ref]\nvalue = [b@ref]\nb = 3\nrequire value[0]@follow == 2 else => $abort(\"fixed\")", "b = 3")]
    [InlineData(Cell + MutableValues + "var value = Cell.init(a@ref)\nvalue.item = b@ref\nb = 3\nrequire value.item@follow == 2 else => $abort(\"field\")", "b = 3")]
    [InlineData(MutableValues + "var value: Array<ref/i32> = [a@ref]\nvalue@uniq.append(b@ref)\nb = 3\nrequire value[1]@follow == 2 else => $abort(\"array\")", "b = 3")]
    [InlineData(MutableValues + "var value: Dictionary<i32, ref/i32> = [1: a@ref]\nvalue[1] = b@ref\nb = 3\nrequire value[1]@follow == 2 else => $abort(\"dictionary\")", "b = 3")]
    [InlineData(MutableValues + "var value: Option<ref/i32> = .None\nvalue = .Some(b@ref)\nb = 3\nmatch value\n    .Some(let item) => require item@follow == 2 else => $abort(\"some\")\n    .None => ()", "b = 3")]
    [InlineData(Put + None + MutableValues + "var value = none()\nput(value@uniq, .Some(b@ref))\nb = 3\nmatch value\n    .Some(let item) => require item@follow == 2 else => $abort(\"some\")\n    .None => ()", "b = 3")]
    [InlineData(MutableValues + "var value = (a@ref, 7)\nlet saved = value\nvalue = (b@ref, 9)\na = 3\nrequire saved.0@follow == 1 else => $abort(\"saved\")", "a = 3")]
    [InlineData(MutableValues + "var view = a@ref\nlet f = func [view] () => view@follow\nview = b@ref\na = 3\nrequire f() == 1 else => $abort(\"capture\")", "a = 3")]
    [InlineData(MutableValues + "var value = (a@ref, a@ref)\nvalue.0 = b@ref\na = 3\nrequire value.1@follow == 1 else => $abort(\"sibling\")", "a = 3")]
    [InlineData(MutableValues + "var value: [2 of ref/i32] = [a@ref, a@ref]\nvalue[0] = b@ref\na = 3\nrequire value[1]@follow == 1 else => $abort(\"sibling\")", "a = 3")]
    [InlineData(MutableValues + "var value = (a@ref, a@ref)\nlet saved = value\nvalue.0 = b@ref\nvalue.1 = b@ref\na = 3\nrequire saved.0@follow == 1 else => $abort(\"saved\")", "a = 3")]
    public void EveryStoredValueRetainsItsLiveLoan(string source, string location)
    {
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Contains(errors, x => x.Code == nameof(DiagnosticCode.ComparisonLoanConflict_Kd) && source.Substring(x.Span!.Value.Start, x.Span.Value.Length) == location);
        Assert.DoesNotContain(errors, x => x.Category == DiagnosticCategory.Unsupported);
    }

    [Theory]
    [Trait("Purpose", "Allocation")]
    [InlineData(MutableValues + "var value = (a@ref, a@ref)\nvalue.0 = b@ref\nvalue.1 = b@ref\na = 3\nrequire value.0@follow == 2 else => $abort(\"parts\")")]
    [InlineData(Values + "var value: Option<ref/i32> = .None\nvalue = .Some(b@ref)\nmatch value\n    .Some(let item) => require item@follow == 2 else => $abort(\"some\")\n    .None => ()")]
    [InlineData(Values + "let f = func [a@ref, b@ref] ()\n    var view = a\n    view = b\n    return view\nrequire f()@follow == 2 else => $abort(\"result\")")]
    public void CompositeRegionsReuseTheirGraphsAndStorage(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var expected = CompilationTestHelper.WriteIr(c);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(expected, CompilationTestHelper.WriteIr(c));
    }
}
