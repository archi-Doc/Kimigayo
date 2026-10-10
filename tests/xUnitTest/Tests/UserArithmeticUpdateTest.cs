// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class UserArithmeticUpdateTest
{
    private const string Counter = """
        struct Counter
            Self is Addable<Counter>
            associate Output is Counter
            public let value: i32
            public init(value: i32) => self.value = value
            public func added(self: ref/Self, right: ref/Counter) -> Counter => Counter.init(self.value + right.value)
            drop => ()
        """;

    [Fact]
    public void ALocalUpdateEndsInspectionBeforeReplacement()
        => ScalarEmissionTest.EmitFixture("UserArithmeticUpdateLocal", Counter + "\nvar total = Counter.init(21)\ntotal += total\nrequire total.value == 42 else => $abort(\"self update\")", string.Empty);

    [Fact]
    public void GenericUpdateRetainsThePublishedContract()
    {
        const string Program = "\nfunc doubled<T>(input: T) -> T\n    T is Addable<T>\n    T.(Addable<T>).Output is T\n    var value = input@move\n    value += value\n    return value@move\nlet result = doubled(Counter.init(21))\nrequire result.value == 42 else => $abort(\"generic update\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticUpdateGeneric", Counter + Program, string.Empty);
    }

    [Fact]
    public void RightHandSideCompletesBeforeTheOldValueIsBorrowed()
    {
        const string Program = "\nfunc right(target: uniq/Counter) -> Counter\n    target@follow = Counter.init(41)\n    return Counter.init(1)\nvar total = Counter.init(0)\ntotal += right(total@uniq)\nrequire total.value == 42 else => $abort(\"RHS first\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticUpdateOrder", Counter + Program, string.Empty);
    }

    [Fact]
    public void NumericTargetUsesTheRightProvidersSelectedOutput()
    {
        const string Program = "struct Offset\n    Self is LeftAddable<i32>\n    associate Output is i32\n    public func addedFrom(left: ref/i32, self: ref/Self) -> i32 => left + 2\nvar total = 40\ntotal += Offset.init()\nrequire total == 42 else => $abort(\"numeric target\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticUpdateNumeric", Program, string.Empty);
    }

    [Theory]
    [InlineData("Follow", "func add(target: uniq/Counter, right: ref/Counter) => target@follow += right\nvar total = Counter.init(21)\nlet right = Counter.init(21)\nadd(total@uniq, right)\nrequire total.value == 42 else => $abort(\"follow\")")]
    [InlineData("Place", "func slot(target: uniq/Counter) -> place uniq/Counter during target\n    Console.writeLine(\"locate\")\n    return target@follow\nvar total = Counter.init(21)\nlet right = Counter.init(21)\nslot(total@uniq) += right\nrequire total.value == 42 else => $abort(\"place\")")]
    [InlineData("Field", "struct Box\n    public var item: Counter\n    public init() => self.item = Counter.init(21)\nvar box = Box.init()\nbox.item += box.item\nrequire box.item.value == 42 else => $abort(\"field\")")]
    [InlineData("Tuple", "var pair = (Counter.init(21), 7)\npair.0 += pair.0\nrequire pair.0.value == 42 and pair.1 == 7 else => $abort(\"tuple\")")]
    [InlineData("Raw", "var total = Counter.init(21)\nlet right = Counter.init(21)\nlet pointer = total@raw\nunsafe => *pointer += right\nrequire total.value == 42 else => $abort(\"raw\")")]
    public void AddressTargetsAreLocatedOnce(string name, string program)
        => ScalarEmissionTest.EmitFixture("UserArithmeticUpdate" + name, Counter + "\n" + program, name == "Place" ? "locate\n" : string.Empty);

    [Theory]
    [InlineData("Fixed", "[2 of Counter]")]
    [InlineData("Array", "Array<Counter>")]
    public void DynamicIndexReusesOneLocationAfterSelfInspection(string name, string type)
    {
        var program = "\nfunc select() -> isize\n    Console.writeLine(\"index\")\n    return 0\nvar values: " + type + " = [Counter.init(21), Counter.init(7)]\nvalues[select()] += values[0]\nrequire values[0].value == 42 and values[1].value == 7 else => $abort(\"dynamic index\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticUpdateIndex" + name, Counter + program, "index\n");
    }

    [Theory]
    [InlineData("Field", "struct Box\n    public var item: Counter\n    public init() => self.item = Counter.init(21)\nfunc locate(box: uniq/Box) -> uniq/Box during box\n    Console.writeLine(\"locate\")\n    return box\nfunc add(box: uniq/Box, right: ref/Counter) => locate(box).item += right\nvar box = Box.init()\nlet right = Counter.init(21)\nadd(box@uniq, right)\nrequire box.item.value == 42 else => $abort(\"borrowed field\")", "locate\n")]
    [InlineData("Fixed", "func select() -> isize\n    Console.writeLine(\"index\")\n    return 0\nfunc add(values: uniq/[2 of Counter]) => values[select()] += values[0]\nvar values: [2 of Counter] = [Counter.init(21), Counter.init(7)]\nadd(values@uniq)\nrequire values[0].value == 42 and values[1].value == 7 else => $abort(\"borrowed fixed\")", "index\n")]
    [InlineData("Array", "func select() -> isize\n    Console.writeLine(\"index\")\n    return 0\nfunc add(values: uniq/Array<Counter>) => values[select()] += values[0]\nvar values: Array<Counter> = [Counter.init(21), Counter.init(7)]\nadd(values@uniq)\nrequire values[0].value == 42 and values[1].value == 7 else => $abort(\"borrowed array\")", "index\n")]
    [InlineData("Indexer", "struct Box\n    Self is UniqIndexable<isize>\n    associate Element is Counter\n    var item: Counter\n    public init() => self.item = Counter.init(21)\n    public func index(self, key: ref/isize) -> place ref/Counter during self => self.item\n    public func indexUniq(self: uniq/Self, key: ref/isize) -> place uniq/Counter during self\n        Console.writeLine(\"indexUniq\")\n        return self.item\nvar box = Box.init()\nlet right = Counter.init(21)\nbox[0] += right\nrequire box[0].value == 42 else => $abort(\"user index\")", "indexUniq\n")]
    public void BorrowedTargetsRetainTheirEvaluatedLocation(string name, string program, string output)
        => ScalarEmissionTest.EmitFixture("UserArithmeticUpdateBorrowed" + name, Counter + "\n" + program, output);

    [Theory]
    [InlineData("Array<Counter>")]
    [InlineData("[1 of Counter]")]
    public void BorrowedElementReplacementStillChecksExistingChildren(string type)
    {
        var source = Counter + "\nfunc add(values: uniq/" + type + ")\n    let saved = values[0]@ref\n    values[0] += values[0]\n    require saved.value == 21 else => $abort(\"live child\")\nvar values: " + type + " = [Counter.init(21)]\nadd(values@uniq)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Code == DiagnosticCode.ComparisonLoanConflict_Kd);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure is OwnershipFailure.Internal or OwnershipFailure.Unsupported);
    }

    [Fact]
    public void RetainedElementLoanStillPreventsReplacement()
    {
        var c = MinimalEmissionTest.Analyze(Counter + "\nvar values: Array<Counter> = [Counter.init(21)]\nlet saved = values[0]@ref\nvalues[0] += values[0]\nrequire saved.value == 21 else => $abort(\"saved element\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Code == DiagnosticCode.ComparisonLoanConflict_Kd);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure is OwnershipFailure.Internal or OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void InstalledElementContentsRetainTheirExternalLoans(bool overwrite, bool compound)
    {
        const string View = "struct View {a}\n    Self is Addable<Self>\n    associate Output is Self\n    public let value: ref/i32 during a\n    public init(value: ref/i32 during a) => self.value = value\n    public func added(self: ref/Self, right: ref/Self) -> Self => Self.init(right.value)\n";
        var source = View + "func run(a: uniq/i32 during s, b: uniq/i32 during s) -> i32\n    var values: Array<View during s> = [View.init(a@follow@ref)]\n    values[0] " + (compound ? "+=" : "=") + " View.init(b@follow@ref)\n" +
            (overwrite ? "    b@follow = 9\n" : string.Empty) + "    return values[0].value\nvar a = 1\nvar b = 2\nrequire run(a@uniq, b@uniq) == 2 else => $abort(\"content loan\")";
        var path = Path.GetFullPath("arithmetic-element-loan.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (overwrite)
        {
            Assert.Contains(c.Ownership.Issues, static x => x.Code == DiagnosticCode.ComparisonLoanConflict_Kd);
            Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure is OwnershipFailure.Internal or OwnershipFailure.Unsupported);
            c.Ownership.ReportDiagnostics();
            c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
            var result = c.Diagnostics.Finalize(rejected: true);
            var error = Assert.Single(result.Diagnostics);
            Assert.Equal(nameof(DiagnosticCode.ComparisonLoanConflict_Kd), error.Code);
            Assert.Equal(source.IndexOf("b@follow = 9", StringComparison.Ordinal), error.Span!.Value.Start);
            Assert.Contains(error.Related!, static x => x.Role == "loan");
            var console = new DiagnosticContractTest.DiagnosticConsole();
            new Kimigayo(console).Render(result, string.Empty);
            Assert.Contains("value retaining the conflicting loan", console.Text, StringComparison.Ordinal);
            var identity = SourceIdentity.FromPath(path);
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, true)[identity]);
            Assert.Equal(error.Code, sent.Code);
            Assert.Equal(error.Display!.Range, sent.Range);
        }
        else
        {
            ScalarEmissionTest.WriteFixture("UserArithmeticUpdateContent" + compound, CompilationTestHelper.WriteIr(c), string.Empty);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandardGetAndCustomSetBorrowNonCopyStorage(bool throughReference)
    {
        const string Box = "\nstruct Box\n    public var item: Counter\n        set(value: Counter) -> ()\n            Console.writeLine(\"set\")\n            storage = value@move\n    public init() => self.item = Counter.init(21)\n";
        var program = throughReference ? "func add(box: uniq/Box, right: ref/Counter) => box.item += right\nvar box = Box.init()\nlet right = Counter.init(21)\nadd(box@uniq, right)" : "var box = Box.init()\nbox.item += box.item";
        ScalarEmissionTest.EmitFixture("UserArithmeticUpdateSetter" + throughReference, Counter + Box + program + "\nrequire box.item.value == 42 else => $abort(\"setter\")", "set\n");
    }

    [Fact]
    public void OutputFitsTheSetterAfterOneGetAndOneOperation()
    {
        var counter = Counter.Replace("associate Output is Counter", "associate Output is i32", StringComparison.Ordinal)
            .Replace("-> Counter => Counter.init(self.value + right.value)", "-> i32 => self.value + right.value", StringComparison.Ordinal);
        const string Program = "\nstruct Box\n    public var raw: i32 = 21\n    public computed item: Counter\n        get() -> Counter\n            Console.writeLine(\"get\")\n            return Counter.init(self.raw)\n        set(value: i32) -> ()\n            Console.writeLine(\"set\")\n            self.raw = value\nfunc locate(box: uniq/Box) -> uniq/Box during box\n    Console.writeLine(\"locate\")\n    return box\nfunc right() -> Counter\n    Console.writeLine(\"right\")\n    return Counter.init(21)\nvar box = Box.init()\nlocate(box@uniq).item += right()\nrequire box.raw == 42 else => $abort(\"output\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticUpdateOutput", counter + Program, "right\nlocate\nget\nset\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetterTemporaryKeepsItsDestructionDependencies(bool destructor)
    {
        const string View = "struct View {a}\n    Self is Addable<()>\n    associate Output is i32\n    let value: ref/i32 during a\n    public init(value: ref/i32 during a) => self.value = value\n    public func added(self: ref/Self, right: ref/()) -> i32 => self.value + 1\n";
        const string Program = "struct Box\n    public var raw: i32 = 41\n    public computed item: View\n        get() -> View during self => View.init(self.raw@ref)\n        set(value: i32) -> () => self.raw = value\nvar box = Box.init()\nbox.item += ()\nrequire box.raw == 42 else => $abort(\"getter dependency\")";
        var source = View + (destructor ? "    drop => _ = self.value\n" : string.Empty) + Program;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (destructor)
        {
            Assert.Contains(c.Ownership.Issues, static x => x.Code == DiagnosticCode.ComparisonLoanConflict_Kd);
            Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure is OwnershipFailure.Internal or OwnershipFailure.Unsupported);
        }
        else
        {
            ScalarEmissionTest.WriteFixture("UserArithmeticUpdateGetterLifetime", CompilationTestHelper.WriteIr(c), string.Empty);
        }
    }

    [Fact]
    public void ReplacementDestroysEachOwnedValueOnce()
    {
        var counter = Counter.Replace("drop => ()", "drop => Console.writeLine(\"\\(self.value)\")", StringComparison.Ordinal);
        const string Program = "\nstruct Box\n    public var item: Counter\n    public init() => self.item = Counter.init(21)\nvar box = Box.init()\nbox.item += box.item\nrequire box.item.value == 42 else => $abort(\"replacement\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticUpdateDestruction", counter + Program, "21\n42\n");
    }

    [Fact]
    public void AnIndependentLoanStillPreventsReplacement()
    {
        var c = MinimalEmissionTest.Analyze(Counter + "\nvar total = Counter.init(21)\nlet saved = total@ref\ntotal += total\nrequire saved.value == 21 else => $abort(\"saved\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Contains(c.Ownership.Issues, static x => x.Code == DiagnosticCode.ComparisonLoanConflict_Kd);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("local")]
    [InlineData("property")]
    [InlineData("index")]
    [InlineData("borrowed")]
    public void WarmUpdatesReuseCallAndReplacementStorage(string kind)
    {
        var source = kind == "property" ? "\nstruct Box\n    public var item: Counter\n        set(value: Counter) -> () => storage = value@move\n    public init() => self.item = Counter.init(21)\nvar box = Box.init()\nbox.item += box.item"
            : kind == "index" ? "\nfunc select() -> isize => 0\nvar values: Array<Counter> = [Counter.init(21)]\nvalues[select()] += values[0]"
            : kind == "borrowed" ? "\nfunc add(values: uniq/Array<Counter>) => values[0] += values[0]\nvar values: Array<Counter> = [Counter.init(21)]\nadd(values@uniq)"
            : "\nvar total = Counter.init(21)\ntotal += total";
        var c = MinimalEmissionTest.Analyze(Counter + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }
}
