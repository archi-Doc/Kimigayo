// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class GenericStorageEmissionTest
{
    internal const string Box = "struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value\n    public func take(self: Self) -> T => self.value\n";
    private const string Choose = "func choose<T>(a: T, b: T, first: bool) -> T => if first => a else => b\n";
    private const string Token = "struct Token\n    let id: i32\n    public init(id: i32) => self.id = id\n    deinit\n        if self.id == 1 => Console.writeLine(\"one\")\n        if self.id == 2 => Console.writeLine(\"two\")\n        if self.id == 3 => Console.writeLine(\"three\")\n        if self.id == 4 => Console.writeLine(\"four\")\n";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "String", Box + "let b = Box<string>.init(\"text\")\nConsole.writeLine(b.take())", "text\n" },
        { "Array", Box + "let b = Box<[3 of i32]>.init([2, 4, 6])\nvar total = 0\nfor x in b.take() => total += x\nrequire total == 12 else => $abort(\"array\")\nConsole.writeLine(\"ok\")", "ok\n" },
        { "Scalars", Box + "let a = Box<i32>.init(42)\nlet b = Box<bool>.init(true)\nrequire a.take() == 42 and b.take() else => $abort(\"scalar\")", string.Empty },
        { "Unit", Box + "let b = Box<()>.init(())\nb.take()\nConsole.writeLine(\"ok\")", "ok\n" },
        { "EmptyArray", Box + "let b = Box<[0 of i32]>.init([])\nfor x in b.take() => $abort(\"empty\")\nConsole.writeLine(\"ok\")", "ok\n" },
        { "ChooseBoth", Choose + "Console.writeLine(choose(\"first\", \"other\", true))\nConsole.writeLine(choose(\"other\", \"second\", false))", "first\nsecond\n" },
        { "ChooseCopy", Choose + "let a: i32 = 12\nlet b: i32 = 30\nrequire choose(a, b, true) + choose(a, b, false) == a + b else => $abort(\"copy\")", string.Empty },
        { "ChoiceDestruction", Token + Choose + "do\n    let selected = choose(Token.init(1), Token.init(2), true)\n    Console.writeLine(\"returned\")\ndo\n    let selected = choose(Token.init(3), Token.init(4), false)\n    Console.writeLine(\"returned second\")", "two\nreturned\none\nthree\nreturned second\nfour\n" },
        { "BoxDestruction", Token + Box + "do\n    let box = Box<Token>.init(Token.init(1))\n    let value = box.take()\n    Console.writeLine(\"taken\")\nConsole.writeLine(\"done\")", "taken\none\ndone\n" },
        { "UnusedBox", Token + Box + Choose + "do\n    let box = choose(Box<Token>.init(Token.init(1)), Box<Token>.init(Token.init(2)), true)\n    let value = box.take()\n    Console.writeLine(\"taken\")", "two\ntaken\none\n" },
        { "CopyPremise", "func again<T>(value: T) -> T\n    T is Copy\n    let first = value\n    return value\nrequire again<i32>(42) == 42 else => $abort(\"copy\")", string.Empty },
        { "CopyBox", Box.Replace("    let value: T", "    Self is Copy when T is Copy\n    let value: T") + "let b = Box<i32>.init(42)\nrequire b.take() + b.take() == 84 else => $abort(\"copy box\")", string.Empty },
        { "MultiField", Token + "struct Pair<T>\n    let first: T\n    let second: T\n    let third: T\n    public init(a: T, b: T, c: T)\n        self.first = a\n        self.second = b\n        self.third = c\n    public func take(self: Self) -> T => self.first\ndo\n    let pair = Pair<Token>.init(Token.init(1), Token.init(2), Token.init(3))\n    let first = pair.take()\n    Console.writeLine(\"returned\")", "three\ntwo\nreturned\none\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("GenericStorage" + name, source, stdout);

    [Theory]
    [InlineData(Box + "let value = Box<string>.init(\"value\")\nConsole.writeLine(value.take())")]
    [InlineData("group Outer\n    public group Inner\n        public func choose<T>(a: T, b: T, first: bool) -> T => if first => a else => b\nConsole.writeLine(Outer.Inner.choose(\"a\", \"b\", true))")]
    public void GenericCallsBind(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData(Box + "let value = Box<string>.init(\"value\")\nConsole.writeLine(value.take())", true)]
    [InlineData(Box + "let value = Box<string>.init(\"value\")\nConsole.writeLine(value.take())\nConsole.writeLine(value.take())", false)]
    [InlineData("struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value\n    public func take(self: Self) -> T => self.value\n    deinit => ()\nConsole.writeLine(\"unused\")", false)]
    [InlineData("struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value\n    public func twice(self: Self) -> T\n        let first = self.value\n        return self.value\nConsole.writeLine(\"unused\")", false)]
    public void ChecksGenericOwnershipAtDefinition(string source, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(valid, c.Ownership.Result.IsVerified);
        if (valid)
        {
            Assert.Contains(c.Ownership.Bodies.SelectMany(x => x.Operations), x => x.Acquisition == AcquisitionKind.CopyOrMove);
        }
    }

    [Theory]
    [InlineData(Box + "let value = Box<i32>.init(\"wrong\")")]
    [InlineData(Box + "let value = Box<i32>.init()")]
    [InlineData(Box + "let value = Box<i32>.init(1)\nvalue.value")]
    [InlineData(Choose + "choose(\"text\", true, true)")]
    [InlineData("func twice<T>(value: T) -> T\n    let first = value\n    return value\nlet actual = twice<i32>(42)")]
    [InlineData("func twice<T>(value: T) -> T\n    let first = value\n    return value\nConsole.writeLine(\"unused\")")]
    [InlineData("func copied<T>(value: T) -> T\n    T is Copy\n    return value\nConsole.writeLine(copied<string>(\"text\"))")]
    public void RejectsInvalidInputsBeforeEmission(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }

    [Fact]
    public void MonomorphizesScalarAndOwnedInstancesOnce()
    {
        // SPEC 21.3.1: the i32 substitution is one concrete body for both calls; the string instance
        // joins its owned result through a result slot.
        var c = MinimalEmissionTest.Analyze(Choose + "Console.writeLine(choose(\"a\", \"b\", true))\nlet n = choose<i32>(1, 2, false)\nlet m = choose<i32>(3, 4, true)");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var body = Assert.Single(module.SharedBodies);
        Assert.Equal(2, body.PolicyCount); // T and bool, independent of parameter/temp/result occurrences.
        Assert.Equal(2, body.LiveFlags.Count(x => x)); // Only the conditionally consumed parameters need flags.
        Assert.Empty(module.SharedEntries);
        var instances = Instances(module);
        Assert.Equal(2, instances.Length);
        var instance = Assert.Single(instances, x => x.Abi.Result == "i32");
        Assert.Equal(["i32", "i32", "i1"], instance.Abi.Parameters.Select(x => x.Type).ToArray());
        Assert.Contains(instance.Instructions, x => x.Opcode == EmissionOpcode.Phi);
        var owned = Assert.Single(instances, x => x.Abi.ResultSlot);
        Assert.DoesNotContain(owned.Instructions, x => x.Opcode == EmissionOpcode.Phi);
    }

    [Theory]
    [InlineData("string", "\"text\"")]
    [InlineData("[3 of i32]", "[2, 4, 6]")]
    [InlineData("i32", "42")]
    public void MonomorphizesStructConstructorsAndFieldReads(string type, string value)
    {
        // The constructor's receiver is the call's instantiated Box<type>; take reads its substituted field.
        var c = MinimalEmissionTest.Analyze(Box + $"let b = Box<{type}>.init({value})\nlet v = b.take()");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.Empty(module.SharedEntries);
        var instances = Instances(module);
        Assert.Equal(2, instances.Length);
        Assert.Single(instances, x => x.Abi.ResultSlot && x.Subslots.Count == 1);
    }

    [Theory]
    [InlineData("i32", "4", "i32")]
    [InlineData("bool", "true", "i1")]
    public void MonomorphizesNestedGenericCalls(string type, string value, string physical)
    {
        // SPEC 21.3.1: the forwarded call inside outer<T> binds to inner's own instance under outer's substitution.
        const string Nested = "func inner<T>(value: T, n: i32) -> i32 => n + 1\nfunc outer<T>(value: T, n: i32) -> i32 => inner<T>(value, n)\n";
        var c = MinimalEmissionTest.Analyze(Nested + $"let v = outer<{type}>({value}, 1)");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.Empty(module.SharedEntries);
        var instances = Instances(module);
        Assert.Equal(2, instances.Length);
        Assert.All(instances, x => Assert.Equal(physical, x.Abi.Parameters[0].Type));
        var outer = Assert.Single(instances, x => x.Instructions.Any(i => i.Opcode == EmissionOpcode.Call));
        Assert.Contains(instances, x => !ReferenceEquals(x, outer) && x.Instructions.All(i => i.Opcode != EmissionOpcode.Call));
        ScalarEmissionTest.EmitFixture("GenericStorageNestedCall" + type, Nested + $"require outer<{type}>({value}, 1) == 2 else => $abort(\"nested\")", string.Empty);
    }

    [Theory]
    [InlineData("i32", "7")]
    [InlineData("string", "\"owned\"")]
    [InlineData("Token", "Token.init(1)")]
    public void MonomorphizesEnumPayloadConstruction(string type, string value)
    {
        // A committed CopyOrMove payload acquisition resolves to the instance's exact Copy or Move (SPEC 21.3.1).
        var c = MinimalEmissionTest.Analyze(Token + $"func wrap<T>(x: T) -> Option<T> => .Some(x)\nlet v = wrap<{type}>({value})");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.Empty(module.SharedEntries);
        var instance = Assert.Single(Instances(module));
        Assert.True(instance.Abi.ResultSlot);
        var generic = c.Ownership.Bodies.Single(x => x.Function.Name == "wrap");
        Assert.Contains(generic.Places, x => x.Kind == OwnershipPlaceKind.Payload && x.Acquisition == AcquisitionKind.CopyOrMove);
    }

    [Fact]
    public void MonomorphizesBorrowedArrayElementBorrows()
    {
        // SPEC 21.3.1: the element borrow of a length-generic borrowed array lowers per instance through
        // a bounds-checked element address with the substitution's element stride.
        var c = MinimalEmissionTest.Analyze("func weight<T>(value: ref/T) -> i32 => 1\nfunc get<length N, T>(values: ref/[N of T], index: isize) -> i32 => weight<T>(values[index]@ref/T)\nlet values: [2 of i32] = [7, 8]\nlet result = get<2, i32>(values@ref, 0)");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.Empty(module.SharedEntries);
        var instances = Instances(module);
        Assert.Equal(2, instances.Length);
        var get = Assert.Single(instances, x => x.Instructions.Any(i => i.Opcode == EmissionOpcode.Call));
        var address = Assert.Single(get.Instructions, i => i.Opcode == EmissionOpcode.Sequence);
        Assert.Equal("ArrayAddress", address.ScalarOperator);
        Assert.Equal(ArithmeticCheckKind.Bounds, address.Check);
        Assert.Equal(4, address.Representation!.Layout.Stride);
    }

    [Fact]
    public void MonomorphizesBorrowedStringArrayElements()
    {
        // SPEC 21.3.1: total<2, string> forms each element's string reference from the borrowed array's
        // element address (string stride) and forwards it; no instance keeps a shared entry.
        var c = MinimalEmissionTest.Analyze(GenericForwardingEmissionTest.Weight + "let values: [2 of string] = [\"left\", \"right\"]\nlet n = W.total<2, string>(values@ref)");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.Empty(module.SharedEntries);
        var total = Assert.Single(Instances(module), x => x.Instructions.Any(i => i.ScalarOperator == "ArrayAddress"));
        var address = Assert.Single(total.Instructions, i => i.ScalarOperator == "ArrayAddress");
        Assert.Equal(WindowsLowering.GetValue(BoundType.String)!.Layout.Stride, address.Representation!.Layout.Stride);
    }

    [Theory]
    [InlineData("func run<T>(value: ref/T, c: uniq/Counter) -> i32\n    c.add(2)\n    return c.read()\nlet v = true\nvar c = Counter.init(40)\nlet n = run(v, c)")]
    [InlineData("func run<T>(value: ref/T) -> i32\n    var c = Counter.init(1)\n    c.add(2)\n    return c.read()\nlet v = true\nlet n = run(v)")]
    [InlineData("func run<T>(value: ref/T, c: ref/Counter) -> i32 => c.read()\nlet v = true\nlet c = Counter.init(3)\nlet n = run(v, c)")]
    public void MonomorphizesConcreteMemberCallReceivers(string source)
    {
        // SPEC 21.3.1: a concrete callee's receiver Origin is instantiated at the call site and then seen
        // under the instance's substitution; the instance body is lowered instead of the shared entry.
        const string Counter = "struct Counter\n    var value: i32\n    public init(value: i32) => self.value = value\n    public func add(self: uniq/Self, amount: i32) => self.value += amount\n    public func read(self: ref/Self) -> i32 => self.value\n";
        var c = MinimalEmissionTest.Analyze(Counter + source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.Empty(module.SharedEntries);
        var instance = Assert.Single(Instances(module));
        Assert.Contains(instance.Instructions, x => x.Opcode == EmissionOpcode.Call);
    }

    [Fact]
    public void MonomorphizesSliceIteratorFieldReads()
    {
        // SPEC 21.3.1: Program 13's SliceIterator<Sample>.next copies its Slice-handle field into a temporary
        // and borrows the indexed element; no instance keeps a shared entry.
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../milestones/Milestone13.kimi")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.Empty(module.SharedEntries);
        Assert.Contains(Instances(module), x => x.Instructions.Any(i => i.Opcode == EmissionOpcode.TransferAggregate && i.Aggregate?.Value.Layout.Size == 16) &&
            x.Instructions.Any(i => i.Opcode == EmissionOpcode.Sequence && i.ScalarOperator == "SliceAddress"));
    }

    [Fact]
    public void MonomorphizesEnumPatternTests()
    {
        // SPEC 21.3.1: enum Pattern tests inside a generic body inspect the substituted subject Type per instance.
        var c = MinimalEmissionTest.Analyze("func present<T>(items: Slice<T>{source}) -> isize\n    var cursor = items.iterate()\n    var count: isize = 0\n    loop\n        match cursor.next()\n            .Some(let item) => count = count + 1\n            .None => exit\n    return count\nlet values: [2 of i32] = [4, 5]\nlet n = present(values[..])");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.Empty(module.SharedEntries);
        Assert.NotEmpty(Instances(module));
    }

    [Theory]
    [InlineData("func apply<F>(f: uniq/F) -> i32\n    F is Callable<uniq, () -> i32>\n    return f()\nlet n: i32 = 0\nvar f = func [var n] () -> i32\n    n += 1\n    return n\nlet r = apply(f@uniq)")]
    [InlineData("struct Item\n    public let value: i32 = 3\nfunc apply<T, F>(value: ref/T, visit: uniq/F) -> bool\n    F is Callable<uniq, (ref/T) -> bool>\n    return visit(value)\nlet item = Item.init()\nvar visit = func (value: ref/Item) => value.value == 3\nlet r = apply(item@ref, visit@uniq)")]
    public void MonomorphizesCallableConstraintCalls(string source)
    {
        // SPEC 21.3.1: the exclusive Callable receiver Loan of the instance body validates against the
        // substituted receiver Type, so the constrained call lowers per instance.
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.Empty(module.SharedEntries);
        Assert.Contains(Instances(module), x => x.Instructions.Any(i => i.Opcode == EmissionOpcode.Call));
    }

    [Fact]
    public void MonomorphizesCopyOrMovePatternBindings()
    {
        // SPEC 21.3.1: the try binding of a T payload copies for i32 and moves for string in each instance.
        var c = MinimalEmissionTest.Analyze("func unwrap<T>(x: T?) -> T? => .Some(try x)\nlet a = unwrap<i32>(.Some(8))\nlet b = unwrap<string>(.Some(\"owned\"))");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.Empty(module.SharedEntries);
        Assert.Equal(2, Instances(module).Length);
        var generic = c.Ownership.Bodies.Single(x => x.Function.Name == "unwrap");
        Assert.Contains(generic.Operations, x => x.Kind == OwnershipOperationKind.AcquirePattern && x.Acquisition == AcquisitionKind.CopyOrMove);
    }

    [Fact]
    public void MonomorphizesForwardedStringReferences()
    {
        // SPEC 21.3.1: forward<string> passes its ref/T parameter to weight<string> as the substituted
        // string reference; the concrete caller's string borrow also targets the instance entry.
        var c = MinimalEmissionTest.Analyze("func weight<T>(value: ref/T) -> i32 => 1\nfunc forward<T>(value: ref/T) -> i32 => weight<T>(value)\nlet s = \"text\"\nlet n = forward<string>(s)");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.Empty(module.SharedEntries);
        var instances = Instances(module);
        Assert.Equal(2, instances.Length);
        Assert.All(instances, x => Assert.Equal(["ptr"], x.Abi.Parameters.Select(p => p.Type).ToArray()));
    }

    [Fact]
    public void SelectedSpecializationIsNeverReplacedByTheGenericBody()
    {
        const string Source = "func count<T>(value: T) -> i32 => 77\nspecialize func count<i32>(value: i32) -> i32 => 5\nrequire count<i32>(1) == 5 and count<i64>(1) == 77 else => $abort(\"selection\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        // The selected body is called directly (SPEC 21.3.4); no transitional entry or shared body remains.
        Assert.Empty(module.SharedEntries);
        var instance = Assert.Single(Instances(module));
        Assert.Equal(["i64"], instance.Abi.Parameters.Select(x => x.Type).ToArray());
        var ir = ScalarEmissionTest.EmitFixture("GenericStorageSelectedSpecialization", Source, string.Empty);
        Assert.DoesNotContain("__kimi_shared", ir, StringComparison.Ordinal);
    }

    [Fact]
    public void InstanceAnalysisLeavesSourceBodiesAndIssuesUnchanged()
    {
        var c = MinimalEmissionTest.Analyze(Choose + "let n = choose<i32>(1, 2, false)");
        var bodies = c.Ownership.Bodies.Count;
        var generic = c.Ownership.Bodies.Single(x => x.Function.Name == "choose");
        var places = generic.Places.ToArray();
        Assert.True(c.Emission.Validate(out var error), error);
        Assert.Equal(bodies, c.Ownership.Bodies.Count);
        Assert.Empty(c.Ownership.Issues);
        Assert.Equal(places, generic.Places.ToArray());
        Assert.Contains(generic.Places, x => x.Acquisition == AcquisitionKind.CopyOrMove);
    }

    [Theory]
    [InlineData("acquisition")]
    [InlineData("projection")]
    [InlineData("cleanup")]
    [InlineData("parameter")]
    public void MalformedSharedPlansFailAndReanalysisRecovers(string defect)
    {
        var c = MinimalEmissionTest.Analyze(Box + "let b = Box<string>.init(\"text\")\nConsole.writeLine(b.take())");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "take");
        if (defect == "projection")
        {
            body.Projections[0] = body.Projections[0] with { Selector = 42 };
        }
        else if (defect == "cleanup")
        {
            body.CleanupStepStorage.Clear();
        }
        else if (defect == "parameter")
        {
            var place = body.Places.Single(x => x.Kind == OwnershipPlaceKind.Parameter);
            body.PlaceStorage[place.Id] = place with { Type = BoundType.String };
        }
        else
        {
            var id = body.OperationStorage.FindIndex(x => x.Projection >= 0 && x.Kind == OwnershipOperationKind.Produce);
            body.OperationStorage[id] = body.Operations[id] with { Acquisition = AcquisitionKind.Copy };
        }

        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void RebindingInvalidatesEntriesAndRestoresOnlyCurrentTypes()
    {
        var c = MinimalEmissionTest.Analyze(Box + "let b = Box<i32>.init(42)\nrequire b.take() == 42 else => $abort(\"value\")");
        for (var i = 0; i < 3; i++)
        {
            Assert.True(c.Emission.Validate(out var error), error);
            c.Bind();
            Assert.False(c.Emission.Validate(out _));
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
        }

        Assert.True(c.Emission.Validate(out var finalError), finalError);
    }

    // Monomorphized generic instances keep the caller-facing entry names.
    internal static EmissionFunction[] Instances(EmissionModule module)
        => Enumerable.Range(0, module.FunctionCount).Select(module.GetFunction).Where(x => x.Abi.Name.StartsWith("__kimi_generic_entry", StringComparison.Ordinal)).ToArray();
}
