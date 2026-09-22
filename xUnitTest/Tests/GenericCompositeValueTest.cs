// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class GenericCompositeValueTest
{
    private const string Resource = "struct Resource\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit\n        if self.id == 1 => Console.writeLine(\"one\")\n        if self.id == 2 => Console.writeLine(\"two\")\n        if self.id == 3 => Console.writeLine(\"three\")\n        if self.id == 4 => Console.writeLine(\"four\")\n";
    private const string Delivery = "enum Delivery<T>\n    Ready(T)\n    Missing\n";
    private const string Relay = "func relay<T>(value: T) -> T\n    let pending: T = value\n    defer => Console.writeLine(\"secured\")\n    return pending\n";
    private const string Choose = "func choose<T>(first: T, second: T, useFirst: bool) -> T => if useFirst => first else => second\n";
    private const string Package = "func package<T>(value: T) -> Delivery<T> => .Ready(value)\n";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "ArrayPayload", Resource + Delivery + "let value: Delivery<[2 of Resource]> = .Ready([Resource.init(1), Resource.init(2)])\nmatch value\n    .Ready(let items)\n        require items[0].id == 1 and items[1].id == 2 else => $abort(\"payload\")\n        Console.writeLine(\"received\")\n    .Missing => $abort(\"missing\")", "received\ntwo\none\n" },
        { "ResourcePayload", Resource + Delivery + "let value: Delivery<Resource> = .Ready(Resource.init(1))\nmatch value\n    .Ready(let item)\n        require item.id == 1 else => $abort(\"payload\")\n        Console.writeLine(\"received\")\n    .Missing => $abort(\"missing\")", "received\none\n" },
        { "RelayArray", Resource + Relay + "do\n    let items = relay<[2 of Resource]>([Resource.init(1), Resource.init(2)])\n    Console.writeLine(\"received\")", "secured\nreceived\ntwo\none\n" },
        { "RelayTuple", Relay + "let value: (string, (i32, bool)) = relay((\"tuple\", (7, true)))\nmatch value\n    (let text, (7, true)) => Console.writeLine(text)\n    _ => $abort(\"tuple\")", "secured\ntuple\n" },
        { "CopyArray", Relay + "let source: [2 of i32] = [5, 8]\nlet result = relay(source)\nrequire source[0] == 5 and result[1] == 8 else => $abort(\"copy\")", "secured\n" },
        { "ChooseSecond", Resource + Delivery + Package + Choose + GenericStorageEmissionTest.Box + "do\n    let selected = choose(Box<[2 of Resource]>.init([Resource.init(1), Resource.init(2)]), Box<[2 of Resource]>.init([Resource.init(3), Resource.init(4)]), false)\n    let delivered = package(selected.take())\n    match delivered\n        .Ready(let items)\n            require items[0].id == 3 and items[1].id == 4 else => $abort(\"selection\")\n            Console.writeLine(\"received\")\n        .Missing => $abort(\"missing\")", "two\none\nreceived\nfour\nthree\n" },
        { "UnusedDelivery", Resource + Delivery + Package + "do\n    let items: [2 of Resource] = [Resource.init(1), Resource.init(2)]\n    let delivered = package(items)\n    Console.writeLine(\"unused\")", "unused\ntwo\none\n" },
        { "InactivePayload", Resource + Delivery + "let value: Delivery<[2 of Resource]> = .Missing\nmatch value\n    .Ready(let items) => $abort(\"wrong case\")\n    .Missing => Console.writeLine(\"missing\")", "missing\n" },
        { "DiscardPayload", Resource + Delivery + "let value: Delivery<[2 of Resource]> = .Ready([Resource.init(1), Resource.init(2)])\nmatch value\n    .Ready(_) => Console.writeLine(\"discarded\")\n    .Missing => $abort(\"missing\")", "discarded\ntwo\none\n" },
        { "NestedPayload", Resource + Delivery + "let value: Delivery<(Resource, [2 of Resource])> = .Ready((Resource.init(1), [Resource.init(2), Resource.init(3)]))\nmatch value\n    .Ready((let first, let rest))\n        require first.id == 1 and rest[1].id == 3 else => $abort(\"nested\")\n        Console.writeLine(\"received\")\n    .Missing => $abort(\"missing\")", "received\nthree\ntwo\none\n" },
        { "RemainingPayload", Resource + Delivery + "let value: Delivery<(Resource, Resource)> = .Ready((Resource.init(1), Resource.init(2)))\nmatch value\n    .Ready((_, let last)) => Console.writeLine(\"received\")\n    .Missing => $abort(\"missing\")", "received\ntwo\none\n" },
        { "ZeroSizePayload", "struct Zero\n    deinit => Console.writeLine(\"zero\")\n" + Delivery + "let value: Delivery<[3 of Zero]> = .Ready([Zero.init(), Zero.init(), Zero.init()])\nmatch value\n    .Ready(let items) => Console.writeLine(\"received\")\n    .Missing => $abort(\"missing\")", "received\nzero\nzero\nzero\n" },
        { "ZeroSizeLocal", "struct Zero\n    deinit => Console.writeLine(\"zero\")\nlet items: [2 of Zero] = [Zero.init(), Zero.init()]\nConsole.writeLine(\"local\")", "local\nzero\nzero\n" },
        { "ZeroSizeTuple", "struct Zero\n    deinit => Console.writeLine(\"zero\")\nlet items = (Zero.init(), Zero.init())\nConsole.writeLine(\"tuple\")", "tuple\nzero\nzero\n" },
        { "ZeroSizeRelay", "struct Zero\n    deinit => Console.writeLine(\"zero\")\n" + Relay + "let items = relay<[3 of Zero]>([Zero.init(), Zero.init(), Zero.init()])\nConsole.writeLine(\"received\")", "secured\nreceived\nzero\nzero\nzero\n" },
        { "EmptyPayload", Resource + Delivery + "let value: Delivery<[0 of Resource]> = .Ready([])\nmatch value\n    .Ready(let items) => Console.writeLine(\"empty\")\n    .Missing => $abort(\"missing\")", "empty\n" },
        { "StructDestructor", Resource + Delivery + "struct Envelope\n    let items: [2 of Resource]\n    public init(items: [2 of Resource]) => self.items = items\n    deinit => Console.writeLine(\"envelope\")\nlet value: Delivery<Envelope> = .Ready(Envelope.init([Resource.init(1), Resource.init(2)]))\nmatch value\n    .Ready(let envelope) => Console.writeLine(\"received\")\n    .Missing => $abort(\"missing\")", "received\nenvelope\ntwo\none\n" },
        { "EarlyReturn", Resource + Delivery + "func unwrap(value: Delivery<[2 of Resource]>) -> [2 of Resource]\n    match value\n        .Ready(let items) => return items\n        .Missing => $abort(\"missing\")\nlet input: Delivery<[2 of Resource]> = .Ready([Resource.init(1), Resource.init(2)])\nlet result = unwrap(input)\nConsole.writeLine(\"returned\")", "returned\ntwo\none\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("GenericComposite" + name, source, stdout);

    [Fact]
    public void ZeroSizeDestructionRetainsAnAddressWithoutChangingLayout()
    {
        var source = Fixtures.Single(x => x.Data.Item1 == "ZeroSizePayload").Data.Item2;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies.Single(x => x.Matches.Count != 0);
        var place = Assert.Single(body.SymbolPlaces, x => x.Key.Name == "items").Value;
        var function = Assert.Single(Enumerable.Range(0, module.FunctionCount).Select(module.GetFunction), x => ReferenceEquals(x.Abi, WindowsLowering.Entry));
        var slot = Assert.Single(function.Slots, x => x.Place == place);
        Assert.Equal(0, slot.Value.Layout.Size);
    }

    [Fact]
    public void AbortingPayloadDestructorStopsRemainingCleanup()
        => ScalarEmissionTest.EmitFixture(
            "GenericCompositeAbortPayload",
            "struct Stop\n    deinit => $abort(\"stop\")\n" + Resource + Delivery + "let value: Delivery<(Resource, Stop)> = .Ready((Resource.init(1), Stop.init()))\nmatch value\n    .Ready((let first, let last)) => Console.writeLine(\"received\")\n    .Missing => $abort(\"missing\")",
            "received\n",
            1,
            "Hello.kimi:2:15: abort KIMI_E_ABORT: stop\n");

    [Fact]
    public void AbortingDeferDoesNotDeliverOrDestroySecuredResult()
        => ScalarEmissionTest.EmitFixture(
            "GenericCompositeAbortResult",
            "func relay<T>(value: T) -> T\n    defer => $abort(\"stop\")\n    return value\n" + Resource + "let result = relay<[2 of Resource]>([Resource.init(1), Resource.init(2)])\nConsole.writeLine(\"not delivered\")",
            string.Empty,
            1,
            "Hello.kimi:2:14: abort KIMI_E_ABORT: stop\n");

    [Theory]
    [InlineData(Resource + GenericStorageEmissionTest.Box + "let value = Box<Resource>.init(Resource.init(1))\nlet item = value.take()\nlet invalid = value")]
    [InlineData(Resource + Delivery + "let value: Delivery<Resource> = .Ready(Resource.init(1))\nmatch value\n    .Ready(let item) => ()\n    .Missing => ()\nlet invalid = value")]
    [InlineData("func duplicate<T>(value: T) -> (T, T) => (value, value)\nlet value = duplicate<i32>(1)")]
    [InlineData("func duplicate<T>(value: T) -> (T, T) => (value, value)\nConsole.writeLine(\"unused definition\")")]
    [InlineData("struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value\n    public func take(self: Self) -> T => self.value\n    deinit => ()\nConsole.writeLine(\"unused definition\")")]
    [InlineData("func observe<T>(value: ref/T) => ()\nfunc relay<T>(value: T) -> T\n    let pending = value\n    defer => observe<T>(pending@ref/T)\n    return pending\nConsole.writeLine(\"unused definition\")")]
    public void RejectsInvalidOwnershipUniversally(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("acquisition")]
    [InlineData("payload")]
    [InlineData("cleanup")]
    public void RejectsCorruptOwnershipPlansAndRecovers(string defect)
    {
        var c = MinimalEmissionTest.Analyze(Fixtures.First().Data.Item2);
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies.Single(x => x.Matches.Count != 0);
        if (defect == "acquisition")
        {
            var id = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.AcquirePattern);
            body.OperationStorage[id] = body.Operations[id] with { Acquisition = AcquisitionKind.Copy };
        }
        else if (defect == "payload")
        {
            body.DecompositionStorage[0] = body.Decompositions[0] with { PayloadStart = 0 };
        }
        else
        {
            body.CleanupStepStorage.Clear();
        }

        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.Validate(out error), error);
    }

    [Fact]
    public void InstancesKeepCopyAndDifferentDestructorsForEqualSizes()
    {
        var other = Resource.Replace("Resource", "Other").Replace("one", "other one").Replace("two", "other two");
        var source = Resource + other + Relay + "do\n    let result = relay<[2 of Resource]>([Resource.init(1), Resource.init(2)])\ndo\n    let result = relay<[2 of Other]>([Other.init(1), Other.init(2)])\nlet source: [2 of i32] = [5, 8]\nlet copied = relay(source)\nrequire source[0] == 5 and copied[1] == 8 else => $abort(\"copy\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        // Equal-size substitutions still get separate bodies; the native run checks Copy and each destructor.
        Assert.Empty(module.SharedEntries);
        Assert.Equal(3, GenericStorageEmissionTest.Instances(module).Length);
        ScalarEmissionTest.EmitFixture("GenericCompositePolicies", source, "secured\ntwo\none\nsecured\nother two\nother one\nsecured\n");
    }

    [Fact]
    public void SerializedReloadPreservesCompositeAcquisition()
    {
        var c = MinimalEmissionTest.Analyze(Fixtures.First().Data.Item2);
        using var before = new StringWriter();
        Assert.True(c.Emission.WriteIr(before, out var error), error);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.True(restored.Bind().IsComplete);
        restored.Binding.CheckStartup(OutputKind.Application);
        Assert.True(restored.Ownership.Analyze().IsVerified);
        using var after = new StringWriter();
        Assert.True(restored.Emission.WriteIr(after, out error), error);
        Assert.Equal(before.ToString(), after.ToString());
        ScalarEmissionTest.WriteFixture("GenericCompositeReload", after.ToString(), "received\ntwo\none\n");
    }
}
