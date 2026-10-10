// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.6.6 and 4.7.2: the Array access and mutation members and the Slice search operations, written in
/// Kimigayo over the compiler-supplied operations.</summary>
public class ArrayMembersTest
{
    private const string Repeating =
        "struct Point\n    Self is Copy\n    public var x: i32 = 1\n    public var y: i32 = 2\n" +
        "func make<U>(value: U, count: isize) -> Array<U>\n    U is Copy\n    return Array<U>.init(repeating: value, count: count)\n" +
        "func twice<U>(value: U) -> isize\n    U is Copy\n    let a = Array<U>.init(repeating: value, count: 2)\n    let b = Array<U>.init(repeating: value, count: 3)\n    return a.length + b.length\n" +
        "func touch<V>(value: V) -> isize\n    V is Copy\n    return 1\n" +
        "func thenGeneric<U>(value: U) -> isize\n    U is Copy\n    let a = Array<U>.init(repeating: value, count: 2)\n    return a.length + touch(value)\n" +
        "public func main()\n" +
        "    let explicit = Array<i64>.init(repeating: 7, count: 3)\n" +
        "    let inferred = Array.init(repeating: 8, count: 2)\n" +
        "    let expected: Array<i64> = Array.init(repeating: 9, count: 1)\n" +
        "    let empty = Array<i32>.init(repeating: 1, count: 0)\n" +
        "    let points = Array<Point>.init(repeating: Point.init(), count: 2)\n" +
        "    let pairs = Array<[2 of i32]>.init(repeating: [3, 4], count: 2)\n" +
        "    let units = Array<()>.init(repeating: (), count: 4)\n" +
        "    var grown = make(5, 2)\n" +
        "    grown.append(6)\n" +
        "    var n = 10\n" +
        "    let refs = Array.init(repeating: n@ref, count: 2)\n" +
        "    require explicit.length == 3 and explicit[2] == 7 and inferred[1] == 8 and expected[0] == 9 else => $abort(\"values\")\n" +
        "    require empty.length == 0 and empty.capacity == 0 and points[1].y == 2 and pairs[1][1] == 4 and units.length == 4 else => $abort(\"shapes\")\n" +
        "    require grown.length == 3 and grown[2] == 6 and refs[1]@follow == 10 and twice(n@ref) == 5 and thenGeneric(1) == 3 else => $abort(\"generic\")\n" +
        "    Console.writeLine(\"Repeating ok.\")\n";

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
        "struct Tracked\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop => Console.writeLine(\"Dropped.\")\n" +
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
        "struct Tracked\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop => Console.writeLine(\"Dropped.\")\n" +
        "struct Bag\n    var items: Array<Tracked>\n    public init(items: Array<Tracked>) => self.items = items@move\n    public func count(self) -> isize => self.items.length\n" +
        "func make() -> Bag\n    var items: Array<Tracked> = []\n    items.append(Tracked.init(1))\n    items.append(Tracked.init(2))\n    return Bag.init(items@move)\n" +
        "public func main()\n" +
        "    let bag = make()\n" +
        "    require bag.count() == 2 else => $abort(\"count\")\n" +
        "    Console.writeLine(\"Bag ready.\")\n";

    // SPEC 4.7.2, 4.7.4: init(! capacity:) reserves once, also for a generic or Non-Copy element; zero allocates nothing.
    private const string Capacity =
        "struct Tag\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop => Console.writeLine(\"Tag dropped.\")\n" +
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

    // SPEC 4.7.2, 7.4: appendCopies exists for Copy elements and leaves the source unchanged.
    private const string Copies =
        "public func main()\n    var values: Array<i32> = [1, 2]\n    let more: Array<i32> = [3, 4]\n    values.appendCopies(more[..])\n" +
        "    values.appendCopies(more[1..])\n" +
        "    require values.length == 5 and values[3] == 4 and values[4] == 4 and more.length == 2 else => $abort(\"copies\")\n" +
        "    Console.writeLine(\"Copies ok.\")\n";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "Copies", Copies, "Copies ok.\n" },
        { "Capacity", Capacity, "Capacity ok.\nTag dropped.\n" },
        { "Field", Field, "Bag ready.\nDropped.\nDropped.\n" },
        { "Access", Access, "Out of range.\nEmpty has no first.\nAccess ok.\n" },
        { "Mutation", Mutation, "Mutation ok.\n" },
        { "Owned", Owned, "Truncating.\nDropped.\nDropped.\nTruncated.\nDropped.\nDropped.\n" },
        { "Repeating", Repeating, "Repeating ok.\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("ArrayMembers" + name, source, stdout);

    // SPEC 4.7.2: a negative capacity Aborts.
    [Fact]
    public void NegativeCapacityAborts()
        => ScalarEmissionTest.EmitFixture("ArrayMembersNegativeCapacity", "let bad = Array<i32>.init(capacity: -1)\n", string.Empty, 1, "Hello.kimi:1:11: abort KIMI_E_ARG_RANGE: Argument out of range\n");

    // SPEC 4.7.2, 7.6.4: named Function Items satisfy the generic Callable parameters of the source library members.
    [Fact]
    public void CallbackArgumentsExecuteThroughFunctionItems()
        => ScalarEmissionTest.EmitFixture("ArrayMembersCallables", Callables, "Callables ok.\n");

    // SPEC 4.7.2, 7.6.1, 10.5: direct anonymous callbacks take their parameter Types from the Callable signature, also with captures and
    // a Non-Copy element Type.
    [Fact]
    public void DirectAnonymousCallbacksExecute()
    {
        const string Source = "var values: Array<i32> = [3, 1, 2, 5, 4]\nvalues.sort(by: func (a, b) => a@follow - b@follow)\nvalues.removeAll(matching: func (v) => v@follow % 2 == 0)\n" +
            "let limit = 3\nvalues.removeAll(matching: func [limit] (v) => v@follow > limit)\n" +
            "var names: Array<string> = [\"b\", \"a\", \"c\"]\nnames.sort(by: func (a: ref/string, b: ref/string) -> i32 => if a == b => 0 else if a == \"a\" => -1 else => 1)\n" +
            "require values.length == 2 and values[0] == 1 and values[1] == 3 and names[0] == \"a\" else => $abort(\"callbacks\")\nConsole.writeLine(\"Anonymous ok.\")";
        ScalarEmissionTest.EmitFixture("ArrayMembersAnonymousCallables", Source, "Anonymous ok.\n");
    }

    // SPEC 4.7.4: a negative count Aborts at the construction, before any element is placed.
    [Fact]
    public void NegativeRepeatingCountAborts()
        => ScalarEmissionTest.EmitFixture("ArrayMembersNegativeRepeating", "let bad = Array<i32>.init(repeating: 1, count: -1)\n", string.Empty, 1, "Hello.kimi:1:11: abort KIMI_E_ARG_RANGE: Argument out of range\n");

    // SPEC 22.1: the call keeps the public constructor it selected and executes the linked implementation; a user constructor with the
    // same spellings is an ordinary constructor.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatingConstructionExecutesItsLinkedImplementation(bool user)
    {
        var source = user ? "struct Box<T>\n    public var size: isize = 0\n    public init(! repeating: T, count: isize)\n        T is Copy\n        self.size = count\nlet b = Box<i32>.init(repeating: 1, count: 2)" : "let a = Array<i32>.init(repeating: 1, count: 2)";
        var c = CompilationTestHelper.BindSuccess(source);
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single(static x => x.SymbolOf() is { Declaration: FunctionKoto { IsConstructor: true } });
        var constructor = call.SymbolOf()!;
        Assert.Equal(user ? constructor : c.Library.GetSymbol(KimiDeclarationId.ArrayRepeatingImplementation), call.CallOf()!.Target);
        Assert.Equal(user ? null : KimiDeclarationId.ArrayRepeating, constructor.LibraryDeclaration);
    }

    // The public constructor's premise and labels decide applicability and every report names it, never its implementation.
    [Theory]
    [InlineData("let a = Array<string>.init(repeating: \"x\", count: 2)", "NoApplicableOverload_Kd")]
    [InlineData("func make<U>(x: U) -> Array<U>\n    return Array<U>.init(repeating: x, count: 2)\nlet a = make(1)", "UnprovenConstraint_Kd")]
    [InlineData("let a = Array<i32>.init(1, 2)", "NoApplicableOverload_Kd")]
    public void RepeatingConstructionReportsThePublicDeclaration(string source, string code)
    {
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(code, record.Code);
        if (source.Contains("init(1, 2)", StringComparison.Ordinal))
        {
            Assert.Equal(2, record.Related!.Length);
        }
    }

    [Theory]
    [InlineData("var values: Array<i32> = [1]\nlet view = values[..]\nvalues.swap(first: 0, second: 0)\nlet n = view.length")]
    [InlineData("let values: Array<i32> = [1]\nvalues.truncate(0)")]
    [InlineData("var values: Array<i32> = [1]\nlet view = values[..]\nvalues.reverse()\nlet n = view.length")]
    [InlineData("var values: Array<string> = []\nlet more: Array<string> = [\"a\"]\nvalues.appendCopies(more[..])")]
    [InlineData("var n = 10\nlet refs = Array.init(repeating: n@ref, count: 2)\nn = 11\nlet first = refs[0]@follow")]
    public void Rejects(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("raw/Array<i32>", "length")]
    [InlineData("raw/Array<i32>", "capacity")]
    [InlineData("raw/Array<i32>", "isEmpty")]
    [InlineData("raw/Array<i32>", "indices")]
    [InlineData("obj/Array<i32>", "length")]
    [InlineData("raw/[3 of i32]", "length")]
    public void MetadataIsNotSelectedThroughARawPointerOrObjectHandle(string type, string member)
    {
        // SPEC 3.4.1, 12.4.1: selection continues only through safe value references; a raw pointer is never dereferenced
        // implicitly, so its sequence metadata is not reachable from safe code.
        var c = MinimalEmissionTest.Analyze($"func f(p: {type}) => p.{member}");
        var issue = Assert.Single(c.Binding.Issues);
        Assert.Equal(Kimi.DiagnosticCode.UnresolvedBinding_Kd, issue.Code);
        Assert.Equal($"p.{member}", issue.Node.ToString());
    }

    [Theory]
    [InlineData("ref/Array<i32>", "length")]
    [InlineData("uniq/Array<i32>", "capacity")]
    [InlineData("ref/Array<i32>", "isEmpty")]
    [InlineData("uniq/Array<i32>", "indices")]
    public void MetadataIsSelectedThroughASafeReference(string type, string member)
    {
        var c = MinimalEmissionTest.Analyze($"func f(p: {type}) => p.{member}");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}
