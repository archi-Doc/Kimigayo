// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 13.5.5.1 pair layers: a pair s/T whose admitted set lies in value or valueborrow is a safe value-reference
/// layer that may or may not exist. @follow selects its direct target; each operation applies the rule of every admitted
/// Semantics, takes the weakest capability and never offers Take.</summary>
public class PairFollowTest
{
    private const string Box =
        "struct Box<E>\n    public var item: E\n    public init(item: E) => self.item = item@move\n";

    private const string Explicit = Box +
        "func view<s/T>(box: ref/Box<s/T>) -> ref/T during box\n    s is value or valueborrow\n    return box.item@follow@ref\n" +
        "func viewUniq<s/T>(box: uniq/Box<s/T>) -> uniq/T during box\n    s is owner or uniq\n    return box.item@follow@uniq\n" +
        "func read<s/T>(box: ref/Box<s/T>) -> T\n    s is value or valueborrow\n    T is Copy\n    return box.item@follow\n" +
        "func shared(number: ref/i32)\n" +
        "    let box = Box<ref/i32 during number>.init(item: number)\n" +
        "    let b = view(box)\n    require b == 11 and read(box) == 11 else => $abort(\"shared\")\n" +
        "    Console.writeLine(\"Shared followed.\")\n" +
        "func exclusive(target: uniq/i32)\n" +
        "    var box = Box<uniq/i32 during target>.init(item: target@move)\n" +
        "    let inner = viewUniq(box@uniq)\n    inner@follow = 9\n" +
        "    let c = view(box)\n    require c == 9 and read(box) == 9 else => $abort(\"exclusive\")\n" +
        "public func main()\n" +
        "    var owned = Box<i32>.init(item: 7)\n" +
        "    let a = view(owned)\n    require a == 7 else => $abort(\"view\")\n" +
        "    let slot = viewUniq(owned@uniq)\n    slot@follow = 8\n" +
        "    require owned.item == 8 and read(owned) == 8 else => $abort(\"owned\")\n" +
        "    Console.writeLine(\"Owned followed.\")\n" +
        "    let number: i32 = 11\n    shared(number@ref)\n" +
        "    var target: i32 = 2\n    exclusive(target@uniq)\n" +
        "    require target == 9 else => $abort(\"target\")\n" +
        "    Console.writeLine(\"Exclusive followed.\")\n";

    private const string Collected = Collection +
        "func wrap<E>(items: Array<E>) -> Collection<E>\n    return Collection<E>.init(items@move)\n" +
        "func view<s/T>(c: ref/Collection<s/T>, i: isize) -> ref/T during c\n    s is value or valueborrow\n    return c[i]@follow@ref\n" +
        "func viewUniq<s/T>(c: uniq/Collection<s/T>, i: isize) -> uniq/T during c\n    s is owner or uniq\n    return c[i]@follow@uniq\n" +
        "func total<s/T>(c: ref/Collection<s/T>) -> i32\n    s is value or valueborrow\n    T is Loaded\n    var sum: i32 = 0\n    for i in c.indices\n        sum += c[i].load()\n    return sum\n" +
        "public func main()\n" +
        "    var owned = wrap([Node.init(1), Node.init(2)])\n" +
        "    require view(owned, 0).weight == 1 else => $abort(\"owned view\")\n" +
        "    let slot = viewUniq(owned@uniq, 1)\n    slot.weight = 5\n" +
        "    require owned[1].weight == 5 and total(owned) == 6 else => $abort(\"owned\")\n" +
        "    Console.writeLine(\"Owned elements total 6.\")\n" +
        "    let a = Node.init(10)\n    let b = Node.init(20)\n" +
        "    let shared: Array<ref/Node> = [a@ref, b@ref]\n    let refs = wrap(shared@move)\n" +
        "    require view(refs, 1).weight == 20 and total(refs) == 30 else => $abort(\"shared\")\n" +
        "    Console.writeLine(\"Borrowed elements total 30.\")\n" +
        "    var c = Node.init(30)\n    var d = Node.init(40)\n" +
        "    let exclusive: Array<uniq/Node> = [c@uniq, d@uniq]\n    var uniqs = wrap(exclusive@move)\n" +
        "    let target = viewUniq(uniqs@uniq, 0)\n    target.weight = 33\n" +
        "    require view(uniqs, 1).weight == 40 and total(uniqs) == 73 else => $abort(\"exclusive\")\n" +
        "    Console.writeLine(\"Exclusive elements total 73.\")\n";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "Explicit", Explicit, "Owned followed.\nShared followed.\nExclusive followed.\n" },
        { "Collected", Collected, "Owned elements total 6.\nBorrowed elements total 30.\nExclusive elements total 73.\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("PairFollow" + name, source, stdout);

    [Theory]
    [InlineData("func f<s/T>(box: ref/Box<s/T>) -> ref/T during box\n    return box.item@follow@ref", DiagnosticCode.UnprovenConstraint_Kd)]
    [InlineData("func f<s/T>(box: ref/Box<s/T>) -> ref/T during box\n    s is owner or obj\n    return box.item@follow@ref", DiagnosticCode.UnprovenConstraint_Kd)]
    [InlineData("func f<s/T>(box: uniq/Box<s/T>) -> uniq/T during box\n    s is value or valueborrow\n    return box.item@follow@uniq", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("func f<s/T>(box: Box<s/T>) -> T\n    s is value\n    return box.item@follow@move", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData("func f<s/T>(box: ref/Box<s/T>) -> ref/T during box\n    s is owner or uniq\n    return box.item@follow@uniq", DiagnosticCode.SharedPathAccess_Kd)]
    public void Rejects(string function, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(Box + function);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }

    [Theory]
    [InlineData("func g<s/T>(box: ref/Box<s/T>) -> ref/(s/T) during box\n    s is value or valueborrow\n    return box.item@ref")]
    [InlineData("func g<s/T>(box: ref/Box<s/T>) -> ref/T during box\n    s is value or valueborrow\n    return box.item@follow@ref")]
    [InlineData("func g<s/T>(box: ref/Box<s/T>)\n    s is value or valueborrow\n    let r = box.item@ref")]
    public void SlotBorrowsAndFollowsVerify(string source)
    {
        var c = MinimalEmissionTest.Analyze(Box + source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    private const string Collection =
        "contract Loaded\n    func load(self: ref/Self) -> i32\n" +
        "struct Node\n    Self is Loaded\n    public var weight: i32\n    public init(weight: i32) => self.weight = weight\n    public func load(self: ref/Self) -> i32 => self.weight\n" +
        "struct Collection<E>\n    Self is UniqIndexable<isize>\n    associate Element is E\n    var items: Array<E>\n" +
        "    public computed indices: ResolvedRange\n        get() -> ResolvedRange => self.items.indices\n" +
        "    public init(items: Array<E>) => self.items = items@move\n" +
        "    public func index(self, key: ref/isize) -> place ref/E during self => self.items[key]\n" +
        "    public func indexUniq(self: uniq/Self, key: ref/isize) -> place uniq/E during self => self.items[key]\n";

    [Theory]
    [InlineData("func n(c: ref/Collection<Node>) -> isize\n    return c.indices.length")]
    [InlineData("func n<T>(c: ref/Collection<T>) -> isize\n    return c.indices.length")]
    [InlineData("func n<s/T>(c: ref/Collection<s/T>, i: isize) -> ref/T during c\n    s is value or valueborrow\n    return c[i]@follow@ref")]
    [InlineData("func n<s/T>(c: ref/Collection<s/T>, i: isize) -> i32\n    s is value or valueborrow\n    T is Loaded\n    return c[i]@follow.load()")]
    [InlineData("func n<s/T>(c: ref/Collection<s/T>, i: isize) -> i32\n    s is value or valueborrow\n    T is Loaded\n    return c[i].load()")]
    public void CollectionAccessVerifies(string source)
    {
        var c = MinimalEmissionTest.Analyze(Collection + source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void ConcreteOwnedPlaceIsNotFollowed()
    {
        var c = MinimalEmissionTest.Analyze("let x: i32 = 1\nlet y = x@follow");
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}
