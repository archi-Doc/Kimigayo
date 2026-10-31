// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class GenericStorageEmissionTest
{
    internal const string Box = "struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value@move\n    public func take(self: Self) -> T => self.value@move\n";
    private const string Choose = "func choose<T>(a: T, b: T, first: bool) -> T => if first => a@move else => b@move\n";
    private const string Holder = "struct Holder<s/T>\n    public let value: s/T\n    public init(value: s/T) => self.value = value@move\n";
    private const string Item = "struct Item\n    public let id: i32 = 7\n";

    private const string Anchored = "struct Pair<s/T, U> {a}\n    s is value or valueborrow\n    public let anchor: ref/i32 during a\n    public let value: s/T\n    public let other: s/U during a\n" +
        "    public init(anchor: ref/i32 during a, value: s/T, other: s/U during a)\n        self.anchor = anchor\n        self.value = value@move\n        self.other = other@move\n";

    private const string Token = "struct Token\n    let id: i32\n    public init(id: i32) => self.id = id\n    drop\n        if self.id == 1 => Console.writeLine(\"one\")\n        if self.id == 2 => Console.writeLine(\"two\")\n        if self.id == 3 => Console.writeLine(\"three\")\n        if self.id == 4 => Console.writeLine(\"four\")\n";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "String", Box + "let b = Box<string>.init(\"text\")\nConsole.writeLine(b@move.take())", "text\n" },
        { "Array", Box + "let b = Box<[3 of i32]>.init([2, 4, 6])\nvar total = 0\nfor x in b@move.take() => total += x\nrequire total == 12 else => $abort(\"array\")\nConsole.writeLine(\"ok\")", "ok\n" },
        { "Scalars", Box + "let a = Box<i32>.init(42)\nlet b = Box<bool>.init(true)\nrequire a@move.take() == 42 and b@move.take() else => $abort(\"scalar\")", string.Empty },
        { "Unit", Box + "let b = Box<()>.init(())\nb@move.take()\nConsole.writeLine(\"ok\")", "ok\n" },
        { "EmptyArray", Box + "let b = Box<[0 of i32]>.init([])\nfor x in b@move.take() => $abort(\"empty\")\nConsole.writeLine(\"ok\")", "ok\n" },
        { "ChooseBoth", Choose + "Console.writeLine(choose(\"first\", \"other\", true))\nConsole.writeLine(choose(\"other\", \"second\", false))", "first\nsecond\n" },
        { "ChooseCopy", Choose + "let a: i32 = 12\nlet b: i32 = 30\nrequire choose(a, b, true) + choose(a, b, false) == a + b else => $abort(\"copy\")", string.Empty },
        { "ChoiceDestruction", Token + Choose + "do\n    let selected = choose(Token.init(1), Token.init(2), true)\n    Console.writeLine(\"returned\")\ndo\n    let selected = choose(Token.init(3), Token.init(4), false)\n    Console.writeLine(\"returned second\")", "two\nreturned\none\nthree\nreturned second\nfour\n" },
        { "BoxDestruction", Token + Box + "do\n    let box = Box<Token>.init(Token.init(1))\n    let value = box@move.take()\n    Console.writeLine(\"taken\")\nConsole.writeLine(\"done\")", "taken\none\ndone\n" },
        { "UnusedBox", Token + Box + Choose + "do\n    let box = choose(Box<Token>.init(Token.init(1)), Box<Token>.init(Token.init(2)), true)\n    let value = box@move.take()\n    Console.writeLine(\"taken\")", "two\ntaken\none\n" },
        { "CopyPremise", "func again<T>(value: T) -> T\n    T is Copy\n    let first = value\n    return value\nrequire again<i32>(42) == 42 else => $abort(\"copy\")", string.Empty },
        { "CopyBox", Box.Replace("    let value: T", "    Self is Copy when T is Copy\n    let value: T") + "let b = Box<i32>.init(42)\nrequire b.take() + b.take() == 84 else => $abort(\"copy box\")", string.Empty },
        { "MultiField", Token + "struct Pair<T>\n    let first: T\n    let second: T\n    let third: T\n    public init(a: T, b: T, c: T)\n        self.first = a@move\n        self.second = b@move\n        self.third = c@move\n    public func take(self: Self) -> T => self.first@move\ndo\n    let pair = Pair<Token>.init(Token.init(1), Token.init(2), Token.init(3))\n    let first = pair@move.take()\n    Console.writeLine(\"returned\")", "three\ntwo\nreturned\none\n" },
    };

    // SPEC 8.1.1, 8.1.2, 8.15: a struct Field of pair Type stores the instance's Type in every admitted Semantics, the original
    // `s/T`, an application `s/U` and its conditional slot `s/U during a` alike; each header slot is anchored by a stored Origin.
    public static TheoryData<string, string, string> PairFixtures => new()
    {
        { "Ref", Holder + "let n: i32 = 3\nlet h = Holder<ref/i32>.init(n@ref)\nConsole.writeLine(\"\\(h.value)\")", "3\n" },
        { "Uniq", Holder + "var n: i32 = 3\nlet h = Holder<uniq/i32>.init(n@uniq)\nConsole.writeLine(\"\\(h.value)\")", "3\n" },
        { "Owner", Holder + "let h = Holder<i32>.init(4)\nConsole.writeLine(\"\\(h.value)\")", "4\n" },
        { "Inferred", Holder + "let n: i32 = 3\nlet h = Holder.init(n@ref)\nConsole.writeLine(\"\\(h.value)\")", "3\n" },
        { "Obj", Item + Holder + "let owner = Kimi.Intrinsics.makeObj(Item.init())\nlet h = Holder<obj/Item>.init(owner@move)\nConsole.writeLine(\"\\(h.value.id)\")", "7\n" },
        { "ObjRef", Item + Holder + "let owner = Kimi.Intrinsics.makeObj(Item.init())\nlet h = Holder<objref/Item>.init(owner@objref)\nConsole.writeLine(\"\\(h.value.id)\")", "7\n" },
        { "Raw", Holder + "var n: i32 = 3\nlet h = Holder<raw/i32>.init(n@raw)\nConsole.writeLine(\"ok\")", "ok\n" },
        { "Destructor", Holder + "    drop\n        Console.writeLine(\"drop\")\ndo\n    let n: i32 = 3\n    let h = Holder<ref/i32>.init(n@ref)\n    Console.writeLine(\"\\(h.value)\")", "3\ndrop\n" },
        { "UniqWrite", Holder.Replace("    public let value", "    public var value") + "var n: i32 = 3\nvar h = Holder<uniq/i32>.init(n@uniq)\nh.value@follow = 9\nConsole.writeLine(\"\\(h.value)\")\nConsole.writeLine(\"\\(n)\")", "9\n9\n" },
        { "NestedCapture", Holder.Replace("    public let value", "    s is value or valueborrow\n    public let value") + "let n: i32 = 3\nlet inner = Holder<ref/i32>.init(n@ref)\nlet outer = Holder.init(inner@ref)\nConsole.writeLine(\"\\(outer.value@follow.value)\")\nlet f = func [outer@move] () -> i32 => outer.value@follow.value@follow\nConsole.writeLine(\"\\(f())\")", "3\n3\n" },
        { "ArrayLiteral", Holder + "let n: i32 = 3\nlet m: i32 = 4\nlet a = Holder<ref/i32>.init(n@ref)\nlet b = Holder<ref/i32>.init(m@ref)\nvar xs = [a@move, b@move]\nConsole.writeLine(\"\\(xs[1].value)\")\nlet c = Holder<ref/i32>.init(n@ref)\nxs.append(c@move)\nConsole.writeLine(\"\\(xs.length)\")", "4\n3\n" },
        { "Application", Anchored + "let k: i32 = 9\nlet y: i64 = 7\nlet h = Pair<ref/i32, i64>.init(k@ref, k@ref, y@ref)\nlet g = Pair<i32, i64>.init(k@ref, 5, 6)\nvar w: i32 = 1\nvar z: i64 = 8\nlet u = Pair<uniq/i32, i64>.init(k@ref, w@uniq, z@uniq)\nConsole.writeLine(\"\\(h.other) \\(g.other) \\(u.value)\")", "7 6 1\n" },
        { "OptionField", "struct Holder<s/T>\n    s is value or valueborrow\n    public let value: Option<s/T>\n    public init(value: Option<s/T>) => self.value = value@move\nlet n: i32 = 3\nlet h = Holder<ref/i32>.init(.Some(n@ref))\nlet g = Holder<i32>.init(.None)\nmatch h.value\n    .Some(let v) => Console.writeLine(\"\\(v@follow)\")\n    .None => Console.writeLine(\"none\")\nmatch g.value\n    .Some(let v) => Console.writeLine(\"\\(v)\")\n    .None => Console.writeLine(\"none\")", "3\nnone\n" },
        { "PairOptionField", "struct Pair<s/T, U> {a}\n    s is value or valueborrow\n    public let anchor: ref/i32 during a\n    public let value: s/T\n    public let other: s/Option<U> during a\n    public init(anchor: ref/i32 during a, value: s/T, other: s/Option<U> during a)\n        self.anchor = anchor\n        self.value = value@move\n        self.other = other@move\nlet k: i32 = 9\nlet y: Option<i64> = .Some(7)\nlet h = Pair<ref/i32, i64>.init(k@ref, k@ref, y@ref)\nlet g = Pair<i32, i64>.init(k@ref, 5, .Some(6))\nConsole.writeLine(\"ok\")", "ok\n" },
        { "ArrayField", "struct Pair<s/T, U> {a}\n    s is value or valueborrow\n    public let anchor: ref/i32 during a\n    public let value: s/T\n    public let other: Array<s/U during a>\n    public init(anchor: ref/i32 during a, value: s/T, other: Array<s/U during a>)\n        self.anchor = anchor\n        self.value = value@move\n        self.other = other@move\nlet k: i32 = 9\nlet g = Pair<i32, i64>.init(k@ref, 5, [6, 7])\nConsole.writeLine(\"\\(g.other.length)\")", "2\n" },
        { "NestedField", "struct Pair<s/T, t/U> {a, b}\n    s is value or valueborrow\n    t is value or valueborrow\n    origin b outlives a\n    public let anchor: ref/i32 during a\n    public let anchor2: ref/i32 during b\n    public let value: s/T\n    public let other: t/U\n    public let both: s/(t/i64 during b) during a\n    public init(anchor: ref/i32 during a, anchor2: ref/i32 during b, value: s/T, other: t/U, both: s/(t/i64 during b) during a)\n        self.anchor = anchor\n        self.anchor2 = anchor2\n        self.value = value@move\n        self.other = other@move\n        self.both = both@move\nlet k: i32 = 9\nlet y: i64 = 7\nlet r = y@ref\nlet h = Pair<ref/i32, ref/i32>.init(k@ref, k@ref, k@ref, k@ref, r@ref)\nlet g = Pair<i32, i32>.init(k@ref, k@ref, 5, 6, 8)\nConsole.writeLine(\"ok\")", "ok\n" },
        { "EnumPayload", "enum Choice<s/T, U> {a}\n    s is owner or ref\n    First(s/T)\n    Second(s/U during a)\nlet y: i64 = 7\nlet c: Choice<ref/i32, i64> = .Second(y@ref)\nlet d: Choice<i32, i64> = .Second(8)\nConsole.writeLine(\"ok\")", "ok\n" },
        { "ObjectFamily", "struct Node\n    public let n: i32\n    public init(n: i32) => self.n = n\nstruct Pair<s/T, U>\n    s is object\n    U is ObjectPayload\n    public let value: s/T\n    public let other: s/U\n    public init(value: s/T, other: s/U)\n        self.value = value@move\n        self.other = other@move\nlet a = Node.init(1)@obj\nlet b = Node.init(2)@obj\nlet h = Pair<obj/Node, Node>.init(a@move, b@move)\nConsole.writeLine(\"ok\")", "ok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("GenericStorage" + name, source, stdout);

    [Theory]
    [MemberData(nameof(PairFixtures))]
    public void PairFieldsExecute(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("GenericStoragePair" + name, source, stdout);

    // The same instances keep the ordinary checks: a Loan the stored reference holds, an escape beyond its Borrow, a bare transfer
    // and a pair application without an Object Target (SPEC 8.1.2: the slot is present, so its formation needs the target).
    [Theory]
    [InlineData(Holder + "var n: i32 = 3\nlet h = Holder<ref/i32>.init(n@ref)\nn = 4\nConsole.writeLine(\"\\(h.value)\")", nameof(DiagnosticCode.ComparisonLoanConflict_Kd))]
    [InlineData(Holder + "func leak() -> Holder<ref/i32 during static>\n    let n: i32 = 3\n    let h = Holder<ref/i32>.init(n@ref)\n    return h@move\nlet h = leak()\nConsole.writeLine(\"\\(h.value)\")", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd))]
    [InlineData("struct Holder<s/T>\n    public let value: s/T\n    public init(value: s/T) => self.value = value\nlet n: i32 = 3\nlet h = Holder<ref/i32>.init(n@ref)\nConsole.writeLine(\"\\(h.value)\")", nameof(DiagnosticCode.TransferRequired_Kd))]
    [InlineData("struct Holder<s/T, U> {a}\n    let value: s/T\n    let other: s/U during a\nConsole.writeLine(\"unused\")", nameof(DiagnosticCode.UnprovenConstraint_Kd))]
    public void PairFieldsKeepTheirChecks(string source, string code)
        => Assert.Equal([code], DiagnosticCorpus.Check(source).Diagnostics.Select(static x => x.Code));

    [Theory]
    [InlineData(Box + "let value = Box<string>.init(\"value\")\nConsole.writeLine(value@move.take())")]
    [InlineData("group Outer\n    public group Inner\n        public func choose<T>(a: T, b: T, first: bool) -> T => if first => a@move else => b@move\nConsole.writeLine(Outer.Inner.choose(\"a\", \"b\", true))")]
    public void GenericCallsBind(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData(Box + "let value = Box<string>.init(\"value\")\nConsole.writeLine(value@move.take())", true)]
    [InlineData(Box + "let value = Box<string>.init(\"value\")\nConsole.writeLine(value@move.take())\nConsole.writeLine(value@move.take())", false)]
    [InlineData("struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value@move\n    public func take(self: Self) -> T => self.value@move\n    drop => ()\nConsole.writeLine(\"unused\")", false)]
    [InlineData("struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value@move\n    public func twice(self: Self) -> T\n        let first = self.value@move\n        return self.value@move\nConsole.writeLine(\"unused\")", false)]
    public void ChecksGenericOwnershipAtDefinition(string source, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(valid, c.Analysis.IsVerified);
    }

    [Theory]
    [InlineData(Box + "let value = Box<i32>.init(\"wrong\")")]
    [InlineData(Box + "let value = Box<i32>.init()")]
    [InlineData(Box + "let value = Box<i32>.init(1)\nvalue.value")]
    [InlineData(Choose + "choose(\"text\", true, true)")]
    [InlineData("func twice<T>(value: T) -> T\n    let first = value@move\n    return value@move\nlet actual = twice<i32>(42)")]
    [InlineData("func twice<T>(value: T) -> T\n    let first = value@move\n    return value@move\nConsole.writeLine(\"unused\")")]
    [InlineData("func copied<T>(value: T) -> T\n    T is Copy\n    return value\nConsole.writeLine(copied<string>(\"text\"))")]
    public void RejectsInvalidInputsBeforeEmission(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(c.Generate(output));
        Assert.Empty(output.ToString());
        Assert.False(c.Binding.Result.IsComplete && c.Analysis.IsVerified);
    }

    // SPEC 21.3.1, IMPL 21.3: a function Item and a direct call of the same instance call its one generated function.
    [Fact]
    public void AFunctionItemSharesTheInstanceOfADirectCall()
    {
        var c = MinimalEmissionTest.Analyze(Choose + "let pick = choose<i32>\nlet n = pick(1, 2, false)\nlet m = choose<i32>(3, 4, true)");
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        var startup = GeneratedIr.Parse(CompilationTestHelper.WriteIr(c)).Startup;
        var calls = startup.Instructions.Where(static x => x.Callee?.Signature == "internal i32 (i32, i32, i1)").ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Same(calls[0].Callee, calls[1].Callee);
    }

    [Theory]
    [InlineData("i32", "4")]
    [InlineData("bool", "true")]
    public void MonomorphizesNestedGenericCalls(string type, string value)
    {
        // SPEC 21.3.1: the forwarded call inside outer<T> binds to inner's own instance under outer's substitution.
        const string Nested = "func inner<T>(value: T, n: i32) -> i32 => n + 1\nfunc outer<T>(value: T, n: i32) -> i32 => inner<T>(value@move, n)\n";
        ScalarEmissionTest.EmitFixture("GenericStorageNestedCall" + type, Nested + $"require outer<{type}>({value}, 1) == 2 else => $abort(\"nested\")", string.Empty);
    }

    // SPEC 8.15.3: T -> Box<T> re-enters grow with ever new keys; generation stops with a resource failure, distinguished from semantic and
    // representation errors.
    [Fact]
    public void GrowingSubstitutionKeysAreResourceDiagnosed()
        => MinimalEmissionTest.AssertGenerationResourceLimit(Box + "func grow<T>(value: T, n: i32) -> i32 => if n == 0 => 0 else => grow<Box<T>>(Box<T>.init(value@move), n - 1)\nlet r = grow<i32>(1, 3)", "grow<Box<T>>(Box<T>.init(value@move), n - 1)");

    [Fact]
    public void SelectedSpecializationIsNeverReplacedByTheGenericBody()
    {
        const string Source = "func count<T>(value: T) -> i32 => 77\nspecialize func count<i32>(value: i32) -> i32 => 5\nrequire count<i32>(1) == 5 and count<i64>(1) == 77 else => $abort(\"selection\")";
        // The selected body is called directly (SPEC 21.3); no transitional entry or shared body remains.
        ScalarEmissionTest.EmitFixture("GenericStorageSelectedSpecialization", Source, string.Empty);
    }

    [Fact]
    public void RebindingInvalidatesEntriesAndRestoresOnlyCurrentTypes()
    {
        var c = MinimalEmissionTest.Analyze(Box + "let b = Box<i32>.init(42)\nrequire b@move.take() == 42 else => $abort(\"value\")");
        for (var i = 0; i < 3; i++)
        {
            Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
            c.Bind();
            Assert.False(c.Generate(TextWriter.Null));
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Analyze().IsVerified);
        }

        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }
}
