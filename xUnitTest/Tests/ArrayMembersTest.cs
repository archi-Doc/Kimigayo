// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.6.6 and 4.7.2: the Array access and mutation members and the Slice search operations, written in
/// Kimigayo over the compiler-supplied operations.</summary>
public class ArrayMembersTest
{
    private const string Access =
        "public func main()\n" +
        "    var values: Array<i32> = [30, 10, 20]\n" +
        "    require values.isEmpty == false else => $abort(\"isEmpty\")\n" +
        "    match values.tryGet(0)\n        .Some(let head) => require head == 30 else => $abort(\"first\")\n        .None => $abort(\"first none\")\n" +
        "    match values.tryGet(5)\n        .Some(_) => $abort(\"tryGet\")\n        .None => Console.writeLine(\"Out of range.\")\n" +
        "    match values.tryGet(^1)\n        .Some(let x) => require x == 20 else => $abort(\"tryGet index\")\n        .None => $abort(\"tryGet none\")\n" +
        "    match values.tryGetUniq(1)\n        .Some(let slot) => slot@follow = 11\n        .None => $abort(\"uniq\")\n" +
        "    require values[1] == 11 else => $abort(\"uniq write\")\n" +
        "    let empty: Array<i32> = []\n" +
        "    require empty.isEmpty else => $abort(\"empty\")\n" +
        "    match empty.tryGet(^1)\n        .Some(_) => $abort(\"empty last\")\n        .None => Console.writeLine(\"Empty has no first.\")\n" +
        "    Console.writeLine(\"Access ok.\")\n";

    private const string Mutation =
        "public func main()\n" +
        "    var values: Array<i32> = [1, 2, 3, 4, 5]\n" +
        "    values.swap(first: 0, second: 4)\n    values.swap(first: 1, second: 2)\n    values.swap(first: 4, second: 3)\n    values.swap(first: 2, second: 2)\n" +
        "    require values[0] == 5 and values[1] == 3 and values[2] == 2 and values[3] == 1 and values[4] == 4 else => $abort(\"swap\")\n" +
        "    let removed = values.swapRemove(1)\n" +
        "    require removed == 3 and values.length == 4 and values[1] == 4 else => $abort(\"swapRemove\")\n" +
        "    let tail = values.swapRemove(3)\n    require tail == 1 and values.length == 3 else => $abort(\"swapRemove last\")\n" +
        "    values.reverse()\n" +
        "    require values[0] == 2 and values[1] == 4 and values[2] == 5 else => $abort(\"reverse\")\n" +
        "    values.truncate(2)\n    values.truncate(9)\n" +
        "    require values.length == 2 else => $abort(\"truncate\")\n" +
        "    values.append(7)\n    values.append(8)\n    values.append(9)\n" +
        "    var more: Array<i32> = [10, 11]\n    values.appendAll(more@move)\n" +
        "    require values.length == 7 and values[2] == 7 and values[4] == 9 and values[6] == 11 else => $abort(\"append\")\n" +
        "    var single: Array<i32> = [4]\n    single.reverse()\n    var none: Array<i32> = []\n    none.reverse()\n" +
        "    Console.writeLine(\"Mutation ok.\")\n";

    private const string Owned =
        "struct Tracked\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit => Console.writeLine(\"Dropped.\")\n" +
        "public func main()\n" +
        "    var items: Array<Tracked> = []\n" +
        "    items.append(Tracked.init(1))\n    items.append(Tracked.init(2))\n    items.append(Tracked.init(3))\n    items.append(Tracked.init(4))\n" +
        "    items.swap(first: 0, second: 3)\n    items.reverse()\n" +
        "    require items[0].id == 1 and items[3].id == 4 and items[1].id == 3 else => $abort(\"moved\")\n" +
        "    let taken = items.swapRemove(0)\n" +
        "    require taken.id == 1 and items[0].id == 4 else => $abort(\"taken\")\n" +
        "    Console.writeLine(\"Truncating.\")\n    items.truncate(1)\n" +
        "    Console.writeLine(\"Truncated.\")\n";

    private const string Callables =
        "func isLarge(value: ref/i32) -> bool => value > 25\n" +
        "func ascending(left: ref/i32, right: ref/i32) -> i32 => left - right\n" +
        "func descending(left: ref/i32, right: ref/i32) -> i32 => right - left\n" +
        "public func main()\n" +
        "    var values: Array<i32> = [30, 10, 20, 40]\n" +
        "    values.removeAll(matching: isLarge)\n" +
        "    require values.length == 2 and values[0] == 10 and values[1] == 20 else => $abort(\"removeAll\")\n" +
        "    var numbers: Array<i32> = [5, 3, 9, 1, 7, 2, 8, 3]\n    numbers.sort(by: ascending)\n" +
        "    var index: isize = 1\n    while index < numbers.length\n        require numbers[index - 1] <= numbers[index] else => $abort(\"sort\")\n        index += 1\n" +
        "    require numbers[0] == 1 and numbers[7] == 9 and numbers.length == 8 else => $abort(\"sort ends\")\n" +
        "    numbers.sort(by: descending)\n" +
        "    require numbers[0] == 9 and numbers[1] == 8 and numbers[7] == 1 else => $abort(\"sort by\")\n" +
        "    Console.writeLine(\"Callables ok.\")\n";

    // SPEC 4.5, 16.3.2: an Array field is destroyed with its struct, elements before the buffer, once.
    private const string Field =
        "struct Tracked\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit => Console.writeLine(\"Dropped.\")\n" +
        "struct Bag\n    var items: Array<Tracked>\n    public init(items: Array<Tracked>) => self.items = items@move\n    public func count(self) -> isize => self.items.length\n" +
        "func make() -> Bag\n    var items: Array<Tracked> = []\n    items.append(Tracked.init(1))\n    items.append(Tracked.init(2))\n    return Bag.init(items@move)\n" +
        "public func main()\n" +
        "    let bag = make()\n" +
        "    require bag.count() == 2 else => $abort(\"count\")\n" +
        "    Console.writeLine(\"Bag ready.\")\n";

    // SPEC 4.7.2, 4.7.4: init(! capacity:) reserves once, also for a generic or Non-Copy element; zero allocates nothing.
    private const string Capacity =
        "struct Tag\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit => Console.writeLine(\"Tag dropped.\")\n" +
        "func make<T>(count: isize) -> Array<T>\n    return Array<T>.init(capacity: count)\n" +
        "public func main()\n" +
        "    var values = Array<i32>.init(capacity: 4)\n" +
        "    require values.length == 0 and values.capacity >= 4 else => $abort(\"capacity\")\n" +
        "    values.append(1)\n    values.append(2)\n" +
        "    let empty = Array<i32>.init(capacity: 0)\n" +
        "    require values[1] == 2 and empty.capacity == 0 else => $abort(\"append\")\n" +
        "    var tags = make<Tag>(3)\n    tags.append(Tag.init(1))\n" +
        "    var names = Array<string>.init(capacity: 2)\n    names.append(\"a\")\n" +
        "    require tags.capacity >= 3 and names[0] == \"a\" else => $abort(\"generic\")\n" +
        "    Console.writeLine(\"Capacity ok.\")\n";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "Capacity", Capacity, "Capacity ok.\nTag dropped.\n" },
        { "Field", Field, "Bag ready.\nDropped.\nDropped.\n" },
        { "Access", Access, "Out of range.\nEmpty has no first.\nAccess ok.\n" },
        { "Mutation", Mutation, "Mutation ok.\n" },
        { "Owned", Owned, "Truncating.\nDropped.\nDropped.\nTruncated.\nDropped.\nDropped.\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("ArrayMembers" + name, source, stdout);

    // SPEC 4.7.2: a negative capacity Aborts.
    [Fact]
    public void NegativeCapacityAborts()
        => ScalarEmissionTest.EmitFixture("ArrayMembersNegativeCapacity", "let bad = Array<i32>.init(capacity: -1)\n", string.Empty, 1, "Hello.kimi:1:11: abort KIMI_E_ARGUMENT: Invalid argument value\n");

    // P26 boundary (PLAN G10): the callback members verify in the library, but a function passed to their Callable
    // parameter is not yet bound; the call is diagnosed instead of generating code. P26 turns this into an execution.
    [Fact]
    public void CallbackArgumentsAwaitCallableValues()
    {
        var c = MinimalEmissionTest.Analyze(Callables);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Node.ToString()!.StartsWith("values.removeAll", StringComparison.Ordinal));
        Assert.DoesNotContain(c.Binding.Issues, x => x.Node.CodeContext.Kotonoha == c.Library.Kotonoha);
    }

    [Theory]
    [InlineData("var values: Array<i32> = [1]\nlet view = values[..]\nvalues.swap(first: 0, second: 0)\nlet n = view.length")]
    [InlineData("let values: Array<i32> = [1]\nvalues.truncate(0)")]
    [InlineData("var values: Array<i32> = [1]\nlet view = values[..]\nvalues.reverse()\nlet n = view.length")]
    public void Rejects(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }
}
