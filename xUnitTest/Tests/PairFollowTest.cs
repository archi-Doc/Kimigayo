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
        "func view<s/T>(c: ref/Collection<s/T>, i: isize) -> ref/T during c\n    s is value or valueborrow\n    return c[i]@follow@ref\n" +
        "func viewUniq<s/T>(c: uniq/Collection<s/T>, i: isize) -> uniq/T during c\n    s is owner or uniq\n    return c[i]@follow@uniq\n" +
        "func total<s/T>(c: ref/Collection<s/T>) -> i32\n    s is value or valueborrow\n    T is Loaded\n    var sum: i32 = 0\n    for i in c.indices\n        sum += c[i].load()\n    return sum\n" +
        "public func main()\n" +
        "    var owned = Collection<Node>.init([Node.init(1), Node.init(2)])\n" +
        "    require view(owned, 0).weight == 1 else => $abort(\"owned view\")\n" +
        "    let slot = viewUniq(owned@uniq, 1)\n    slot.weight = 5\n" +
        "    require owned[1].weight == 5 and total(owned) == 6 else => $abort(\"owned\")\n" +
        "    Console.writeLine(\"Owned elements total 6.\")\n" +
        "    let a = Node.init(10)\n    let b = Node.init(20)\n" +
        "    let refs = Collection<ref/Node>.init([a@ref, b@ref])\n" +
        "    require view(refs, 1).weight == 20 and total(refs) == 30 else => $abort(\"shared\")\n" +
        "    Console.writeLine(\"Borrowed elements total 30.\")\n" +
        "    var c = Node.init(30)\n    var d = Node.init(40)\n" +
        "    var uniqs = Collection<uniq/Node>.init([c@uniq, d@uniq])\n" +
        "    let target = viewUniq(uniqs@uniq, 0)\n    target.weight = 33\n" +
        "    require view(uniqs, 1).weight == 40 and total(uniqs) == 73 else => $abort(\"exclusive\")\n" +
        "    Console.writeLine(\"Exclusive elements total 73.\")\n";

    // SPEC 3.5.3: a Scalar read and a receiver through pair layers whose outer Origins are independent.
    private const string Scaled = Collection +
        "func scaled<s/T>(item: s/T, factor: s/i32) -> i32\n    s is value or valueborrow\n    T is Loaded\n    return item.load() * factor\n" +
        "public func main()\n" +
        "    require scaled(Node.init(2), 5) == 10 else => $abort(\"owner\")\n" +
        "    let node = Node.init(4)\n    let three: i32 = 3\n" +
        "    require scaled(node@ref, three@ref) == 12 else => $abort(\"ref\")\n" +
        "    var other = Node.init(6)\n    var four: i32 = 4\n" +
        "    require scaled(other@uniq, four@uniq) == 24 else => $abort(\"uniq\")\n" +
        "    Console.writeLine(\"Scaled.\")\n";

    // SPEC 13.4, 10.2, 7.3: comparisons, fixed ref/U parameters and a returned index access through pair layers.
    private const string Positions = Collection +
        "struct Point\n    Self is Equatable\n    public var x: i32\n    public init(x: i32) => self.x = x\n" +
        "    public func equals(self: ref/Self, other: ref/Self) -> bool => self.x == other.x\n" +
        "func equal<s/T>(a: s/T, b: s/T) -> bool\n    s is value or valueborrow\n    T is Equatable\n    return a == b\n" +
        "func peek<s/T>(c: ref/Collection<s/T>, i: isize) -> ref/T during c\n    s is value or valueborrow\n    return c[i]\n" +
        "func twice(n: ref/i32) -> i32 => n * 2\n" +
        "func show<s/T>(item: s/T, factor: s/i32) -> i32\n    s is value or valueborrow\n    return twice(factor)\n" +
        "public func main()\n" +
        "    require equal(Point.init(1), Point.init(1)) else => $abort(\"owner\")\n" +
        "    let p = Point.init(2)\n    let q = Point.init(3)\n" +
        "    require not equal(p@ref, q@ref) else => $abort(\"ref\")\n" +
        "    var owned = Collection<Node>.init([Node.init(1), Node.init(2)])\n" +
        "    require peek(owned, 1).weight == 2 else => $abort(\"peek\")\n" +
        "    let a = Node.init(10)\n    let refs = Collection<ref/Node>.init([a@ref])\n" +
        "    require peek(refs, 0).weight == 10 else => $abort(\"peek ref\")\n" +
        "    let three: i32 = 3\n" +
        "    require show(Node.init(0), 4) == 8 and show(a@ref, three@ref) == 6 else => $abort(\"show\")\n" +
        "    Console.writeLine(\"Positions.\")\n";

    // SPEC 15.1.6, 14.6.2: a pair Subject iterates in the weakest mode over the admitted cases.
    private const string Iterated =
        "func sum<s/T>(items: s/Array<i32>, marker: s/T) -> i32\n    s is value or valueborrow\n    var total: i32 = 0\n    for x in items\n        total += x\n    return total\n" +
        "func bump<s/T>(items: s/Array<i32>, marker: s/T)\n    s is uniq\n    for x in items\n        x@follow += 1\n" +
        "public func main()\n" +
        "    var owned: Array<i32> = [1, 2, 3]\n    let z: i32 = 0\n    require sum(owned@move, z) == 6 else => $abort(\"owner\")\n" +
        "    let values: Array<i32> = [4, 5]\n    let m: i32 = 0\n    require sum(values@ref, m@ref) == 9 else => $abort(\"ref\")\n" +
        "    var more: Array<i32> = [7, 8]\n    var n: i32 = 0\n    bump(more@uniq, n@uniq)\n" +
        "    require sum(more@uniq, n@uniq) == 17 and more[1] == 9 else => $abort(\"uniq\")\n" +
        "    Console.writeLine(\"Iterated.\")\n";

    // SPEC 14.8.1: a structural Pattern selects through a pair layer; its bindings are references fixed at definition.
    private const string Matched =
        "func first<s/T>(o: s/Option<i32>, marker: s/T) -> i32\n    s is value or valueborrow\n    match o\n        .Some(let v) => return v\n        .None => return 0\n" +
        "func second<s/T>(pair: s/(i32, i32), marker: s/T) -> i32\n    s is value or valueborrow\n    match pair\n        (_, let b) => return b\n" +
        "func raise<s/T>(o: s/Option<i32>, marker: s/T)\n    s is uniq\n    match o\n        .Some(let v) => v@follow += 10\n        .None => ()\n" +
        "public func main()\n" +
        "    let z: i32 = 0\n    require first(Option<i32>.Some(3), z) == 3 and second((1, 2), z) == 2 else => $abort(\"owner\")\n" +
        "    let some = Option<i32>.Some(4)\n    let none = Option<i32>.None\n    let tuple = (5, 6)\n" +
        "    require first(some@ref, z@ref) == 4 and first(none@ref, z@ref) == 0 and second(tuple@ref, z@ref) == 6 else => $abort(\"ref\")\n" +
        "    var other = Option<i32>.Some(7)\n    var n: i32 = 0\n" +
        "    raise(other@uniq, n@uniq)\n" +
        "    require first(other@uniq, n@uniq) == 17 else => $abort(\"uniq\")\n" +
        "    Console.writeLine(\"Matched.\")\n";

    // SPEC 7.3: an owning receiver through a pair layer is written p@follow.m(), which Copies the selected Place.
    private const string Spent =
        "struct Coin\n    Self is Copy\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func spend(self: Self) -> i32 => self.n\n" +
        "func spend<s/T>(item: s/Coin, marker: s/T) -> i32\n    s is value or valueborrow\n    return item@follow.spend()\n" +
        "public func main()\n" +
        "    let z: i32 = 0\n    require spend(Coin.init(3), z) == 3 else => $abort(\"owner\")\n" +
        "    let c = Coin.init(4)\n    require spend(c@ref, z@ref) == 4 else => $abort(\"ref\")\n" +
        "    var d = Coin.init(5)\n    var n: i32 = 0\n    require spend(d@uniq, n@uniq) == 5 and d.n == 5 else => $abort(\"uniq\")\n" +
        "    Console.writeLine(\"Spent.\")\n";

    // SPEC 13.5.5.1, 10.2: adapting s/(ref/V during a) to ref/V Copies the inner ref, so the result depends only on a and the
    // slot holding the reference may change while the result is live.
    private const string Inner =
        "struct Node\n    public var weight: i32\n    public init(weight: i32) => self.weight = weight\n" +
        "func inner<s/T>(x: s/(ref/Node during a), marker: s/T) -> ref/Node during a\n    s is value or valueborrow\n    return x\n" +
        "public func main()\n" +
        "    let node = Node.init(9)\n    let z: i32 = 0\n    var holder = node@ref\n" +
        "    require inner(holder, z).weight == 9 else => $abort(\"owner\")\n" +
        "    let kept = inner(holder@ref, z@ref)\n    holder = node@ref\n" +
        "    var n: i32 = 0\n    require kept.weight == 9 and inner(holder@uniq, n@uniq).weight == 9 else => $abort(\"borrow\")\n" +
        "    Console.writeLine(\"Inner.\")\n";

    // SPEC 13.5.5.1, 3.5.3: a Scalar read through two pair layers; an s/U argument is matched once s is inferred, whatever
    // the argument order, so each instance has exactly the layers its bindings form.
    private const string Nested =
        "func twice<s/T, t/U>(x: s/(t/i32 during b), m: s/T, n: t/U) -> i32\n    s is value or valueborrow\n    t is valueborrow\n    return x * 2\n" +
        "public func main()\n" +
        "    var v: i32 = 4\n    let z: i32 = 0\n    var w: i32 = 0\n" +
        "    require twice(v@ref, z, z@ref) == 8 and twice(v@uniq, z, w@uniq) == 8 else => $abort(\"owner\")\n" +
        "    let r = v@ref\n    require twice(r@ref, z@ref, z@ref) == 8 else => $abort(\"ref\")\n" +
        "    var q = v@uniq\n    require twice(q@ref, z@ref, w@uniq) == 8 else => $abort(\"uniq\")\n" +
        "    Console.writeLine(\"Nested.\")\n";

    // SPEC 13.5.5.1: s/(t/U) needs two follows; an owner binding of the outer layer is no layer, even above a reference.
    private const string Followed =
        "func twice<s/T, t/U>(x: s/(t/i32 during b), m: s/T, n: t/U) -> i32\n    s is value or valueborrow\n    t is valueborrow\n    return x@follow@follow * 2\n" +
        "func once<s/T>(x: s/i32, m: s/T) -> i32\n    s is value or valueborrow\n    return x@follow * 3\n" +
        "public func main()\n" +
        "    var v: i32 = 4\n    let z: i32 = 0\n    var w: i32 = 0\n" +
        "    require twice(v@ref, z, z@ref) == 8 and twice(v@uniq, z, w@uniq) == 8 else => $abort(\"owner\")\n" +
        "    let r = v@ref\n    require twice(r@ref, z@ref, z@ref) == 8 else => $abort(\"ref\")\n" +
        "    require once(v, z) == 12 and once(v@ref, z@ref) == 12 and once(v@uniq, w@uniq) == 12 else => $abort(\"once\")\n" +
        "    Console.writeLine(\"Followed.\")\n";

    // SPEC 3.4.1, 7.3: a shared receiver below two pair layers; each instance reads through the layers it has.
    private const string NestedReceiver =
        "struct Node\n    public var weight: i32\n    public init(weight: i32) => self.weight = weight\n    public func load(self: ref/Self) -> i32 => self.weight\n" +
        "func weigh<s/T, t/U>(x: s/(t/Node during b), m: s/T, n: t/U) -> i32\n    s is value or valueborrow\n    t is valueborrow\n    return x.load()\n" +
        "public func main()\n" +
        "    let node = Node.init(6)\n    let z: i32 = 0\n    var w: i32 = 0\n    let r = node@ref\n" +
        "    require weigh(node@ref, z, z@ref) == 6 and weigh(r@ref, z@ref, z@ref) == 6 else => $abort(\"ref\")\n" +
        "    var v = Node.init(7)\n    var q = v@uniq\n" +
        "    require weigh(q@ref, z@ref, w@uniq) == 7 else => $abort(\"uniq\")\n" +
        "    Console.writeLine(\"Nested receiver.\")\n";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "NestedReceiver", NestedReceiver, "Nested receiver.\n" },
        { "Followed", Followed, "Followed.\n" },
        { "Nested", Nested, "Nested.\n" },
        { "Inner", Inner, "Inner.\n" },
        { "Spent", Spent, "Spent.\n" },
        { "Matched", Matched, "Matched.\n" },
        { "Iterated", Iterated, "Iterated.\n" },
        { "Positions", Positions, "Positions.\n" },
        { "Explicit", Explicit, "Owned followed.\nShared followed.\nExclusive followed.\n" },
        { "Scaled", Scaled, "Scaled.\n" },
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

    [Theory]
    [InlineData("public func main()\n    let a = Node.init(10)\n    let b = Node.init(20)\n    let refs = Collection<ref/Node>.init([a@ref, b@ref])\n    let n = refs.indices.length")]
    [InlineData("public func main()\n    var c = Node.init(30)\n    var d = Node.init(40)\n    var uniqs = Collection<uniq/Node>.init([c@uniq, d@uniq])\n    let n = uniqs.indices.length")]
    [InlineData("public func main()\n    var owned = Collection<Node>.init([Node.init(1), Node.init(2)])\n    require owned[1].weight == 2 else => $abort(\"x\")")]
    public void ConstructionQualifierOriginsAreInferred(string source)
    {
        var c = MinimalEmissionTest.Analyze(Collection + source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 15.4.4: an omitted Origin that nothing constrains is not invented; the local is rejected.
    [Fact]
    public void UnconstrainedQualifierOriginIsRejected()
    {
        var c = MinimalEmissionTest.Analyze(Collection + "public func main()\n    let refs = Collection<ref/Node>.init([])\n    let n = refs.indices.length");
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("func same<s/T>(item: s/T, left: s/i32, right: s/i32) -> bool\n    s is value or valueborrow\n    return left == right")]
    [InlineData("func equal<s/T>(a: s/T, b: s/T) -> bool\n    s is value or valueborrow\n    T is Equatable\n    return a == b")]
    [InlineData("func peek<s/T>(c: ref/Collection<s/T>, i: isize) -> ref/T during c\n    s is value or valueborrow\n    return c[i]")]
    [InlineData("func twice(n: ref/i32) -> i32 => n * 2\nfunc show<s/T>(item: s/T, factor: s/i32) -> i32\n    s is value or valueborrow\n    return twice(factor)")]
    [InlineData("func sum<s/T>(items: s/Array<i32>, marker: s/T) -> i32\n    s is value or valueborrow\n    var total: i32 = 0\n    for x in items\n        total += x\n    return total")]
    [InlineData("contract Bump\n    func bump(self: uniq/Self)\nstruct Cell\n    Self is Bump\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func bump(self: uniq/Self) => self.n += 1\nfunc bumpAll<s/T>(c: uniq/Collection<s/T>)\n    s is owner or uniq\n    T is Bump\n    for i in c.indices\n        c[i].bump()")]
    [InlineData("contract Bump\n    func bump(self: uniq/Self)\nfunc bumpOne<s/T>(item: s/T)\n    s is uniq\n    T is Bump\n    item.bump()")]
    [InlineData("func sumAll<s/T>(c: ref/Collection<s/T>) -> i32\n    s is value or valueborrow\n    T is Loaded\n    var total: i32 = 0\n    for i in c.indices\n        match c[i].load()\n            let w => total += w\n    return total")]
    [InlineData("func bump<s/T>(items: s/Array<i32>, marker: s/T)\n    s is uniq\n    for x in items\n        x@follow += 1")]
    [InlineData("func keep<s/T, t/U>(x: s/(t/i32 during b), m: s/T, n: t/U)\n    s is value or valueborrow\n    t is valueborrow\n    let r = x@follow@ref\n    let q = x@ref")]
    [InlineData("func keep<s/T>(x: s/i32, m: s/T)\n    s is value or valueborrow\n    let r = x@ref")]
    [InlineData("func same<s/T>(c: ref/Collection<s/T>) -> bool\n    s is value or valueborrow\n    T is Equatable\n    return c[0] == c[1]")]
    public void PairPositionsVerify(string source)
    {
        var c = MinimalEmissionTest.Analyze(Collection + source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 7.3: an owning receiver is acquired through a pair layer only by the Scalar read; p@follow.m() Copies the Place.
    [Theory]
    [InlineData("struct Token\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func consume(self: Self) -> i32 => self.n\nfunc f<s/T>(item: s/Token, marker: s/T) -> i32\n    s is value or valueborrow\n    return item.consume()", false)]
    [InlineData("struct Token\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func consume(self: Self) -> i32 => self.n\nfunc f<s/T>(item: s/Token, marker: s/T) -> i32\n    s is value or valueborrow\n    return item@follow.consume()", false)]
    [InlineData("struct Coin\n    Self is Copy\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func spend(self: Self) -> i32 => self.n\nfunc f<s/T>(item: s/Coin, marker: s/T) -> i32\n    s is value or valueborrow\n    return item.spend()", false)]
    [InlineData("struct Coin\n    Self is Copy\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func spend(self: Self) -> i32 => self.n\nfunc f<s/T>(item: s/Coin, marker: s/T) -> i32\n    s is value or valueborrow\n    return item@follow.spend()", true)]
    public void OwningReceivers(string source, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == (c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified), MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 8.1.2: an s/U nested in a Type argument gains no omitted Origin; SPEC 13.5.5.1: a followed element Place is never
    // transferred by a bare initializer.
    [Theory]
    [InlineData("func peekFirst<s/T>(c: ref/Collection<s/Option<i32>>, marker: s/T) -> i32\n    s is value or valueborrow\n    return 0")]
    [InlineData("func pick<s/T>(c: ref/Collection<s/T>, i: isize) -> i32\n    s is value or valueborrow\n    T is Loaded\n    let n = c[i]\n    return n.load()")]
    public void PairPositionsReject(string source)
    {
        var c = MinimalEmissionTest.Analyze(Collection + source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 10.2: s/U parameters are solved together with the other arguments, whatever their order, and must agree on s.
    [Fact]
    public void PairApplicationsAgreeOnSemantics()
    {
        var c = MinimalEmissionTest.Analyze("func sum<s/T>(items: s/Array<i32>, marker: s/T) -> i32\n    s is value or valueborrow\n    return 0\n" +
            "public func main()\n    let values: Array<i32> = [4, 5]\n    var m: i32 = 0\n    let r = sum(values@ref, m@uniq)");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NoApplicableOverload_Kd);
    }

    // SPEC 3.5.3, 13.5.5.1: without s is value or valueborrow the pair layer is not followed, so no Scalar is read.
    [Fact]
    public void ScalarReadNeedsAFollowablePair()
    {
        var c = MinimalEmissionTest.Analyze(Box + "func f<s/T>(item: s/T, factor: s/i32) -> i32\n    return factor * 2");
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void ConcreteOwnedPlaceIsNotFollowed()
    {
        var c = MinimalEmissionTest.Analyze("let x: i32 = 1\nlet y = x@follow");
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}
