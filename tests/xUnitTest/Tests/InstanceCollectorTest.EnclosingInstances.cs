// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// NestedFunctionsNeedTheirEnclosingInstance's companion for definitions outside a named function's body (unit fi-closure, DECISIONS
// R5-M157; BU-60): a Closure in a generic struct's Field initializer or getter, and a function declared in one.
public sealed partial class InstanceCollectorTest
{
    // A Closure whose body depends on the struct's T, one naming no slot, one declaring a function that depends on T, and the uses of the
    // struct's two instances.
    private const string Capacity = "Array<T>.init(capacity: 4).capacity";
    private const string Sized = "func [] () -> isize => " + Capacity;
    private const string Four = "func [] () -> isize => 4";
    private const string Sizer = "func [] () -> isize\n        func size() -> isize => " + Capacity + "\n        return size()";
    private const string Built = "let a = Box<i32>.init()\nlet b = Box<i64>.init()\n";
    private const string Got = "let a = Box<i32>.init().get()\nlet b = Box<i64>.init().get()\n";
    private const string Read = "let a = Box<i32>.init().size\nlet b = Box<i64>.init().size\n";
    private const string Stored = "    public func get(self: ref/Self) -> isize => self.f()\n" + Got;
    private const string Inner = "let a = Outer<i32>.Inner.init()\nlet b = Outer<i64>.Inner.init()\n";

    // The struct's declaration up to a definition: a Field initializer's (also in a struct nested in a generic one), a constructor's (also a
    // method's) and a getter's; a constructor after a Field; a getter's body declaring a function; a closed struct's Field initializer with
    // a Closure.
    private const string FieldHead = "struct Box<T>\n    var v: isize = ";
    private const string NestedHead = "struct Outer<T>\n    public struct Inner\n        var v: isize = ";
    private const string Plain = "struct Box<T>\n    var v: isize\n    public init() => self.v = ";
    private const string GetterHead = Plain + "0\n    public computed size: isize\n        get() -> isize";
    private const string Init = "\n    public init() => ()\n";
    private const string Make = "\n            func make() -> isize => " + Capacity + "\n            return make()\n";
    private const string ClosedField = "struct Box\n    var v: isize = (func [] () -> isize => Array<i32>.init(capacity: 4).capacity)()";

    // The form, the program, its Closure items and the collector's records (code and source text, joined by " | ").
    public static TheoryData<string, string, int, string> EnclosedCases => new()
    {
        { "initializer", $"{FieldHead}({Sized})(){Init}{Built}", 2, string.Empty },
        { "two constructors", $"{FieldHead}({Sized})(){Init}    public init(n: isize) => ()\n{Built}", 2, string.Empty },
        { "two constructors of one instance", $"{FieldHead}({Sized})(){Init}    public init(n: isize) => ()\nlet a = Box<i32>.init()\nlet b = Box<i32>.init(n: 1)\n", 1, string.Empty },
        { "erased", $"struct Box<T>\n    var f: () -> isize = {Sized}{Init}{Built}", 2, string.Empty },
        { "stored, called in a method", $"struct Box<T>\n    var f = {Sized}{Init}{Stored}", 2, string.Empty },
        { "nested Type", $"{NestedHead}({Sized})()\n        public init() => ()\n{Inner}", 2, string.Empty },
        { "naming no slot", $"{FieldHead}({Four})(){Init}{Built}", 2, string.Empty },
        { "function in the initializer", $"{FieldHead}({Sizer}\n    )(){Init}{Built}", 2, "Unsupported_Kd size()" },
        { "function in a getter", $"{GetterHead}{Make}{Read}", 0, "Unsupported_Kd make()" },
        { "getter", $"{GetterHead} => ({Sized})()\n{Read}", 2, string.Empty },
        { "constructor", $"{Plain}({Sized})()\n{Built}", 2, string.Empty },
        { "method", $"{Plain}0\n    public func get(self: ref/Self) -> isize => ({Sized})()\n{Got}", 2, string.Empty },
        { "closed struct", $"{ClosedField}{Init}let a = Box.init()\n", 1, string.Empty },
        { "no definition", $"{FieldHead}{Capacity}{Init}{Built}", 0, string.Empty },
        { "stored, read through another instance", $"struct Box<T>\n    var f = {Sized}{Init}{Cross}", 2, string.Empty },
        { "owners with an Origin position, joined", Staged("let g = if c => bs.f else => bx.f"), 0, $"Unsupported_Kd {Pass}" },
        { "owners with an Origin position, assigned", Staged("var g = bx.f\n    g = bs.f"), 0, $"Unsupported_Kd {Pass}" },
        { "a function's Closure in a held context", Program(GenericClosureIdentityTest.HeldContext), 4, string.Empty },
        { "the same with an Origin parameter", Program(GenericClosureIdentityTest.HeldContextRef), 4, string.Empty },
        { "a held context in a held context", Program(GenericClosureIdentityTest.HeldContextNested), 6, string.Empty },
    };

    // A stored Closure of Box<i64> read and called in Box<i32>'s method.
    private const string Cross = "    public func cross(self: ref/Self, other: Box<i64>) -> isize\n        let g = other.f\n        return g()\n" +
        "let a = Box<i32>.init().cross(Box<i64>.init())\n";

    // A Closure passing its T through, and two owners whose erased instance, Box<ref/i32>, has an Origin position: one static, one not.
    private const string Pass = "func [] (x: T) -> T => x";
    private const string PassBox = "struct Box<T>\n    T is Copy\n    public var v: T\n    public var f = " + Pass + "\n    public init(v: T) => self.v = v\n";

    // Box<T> with a Closure naming T, and GV<T>{o}'s member reading its own instance's and another owner's Closure.
    private const string SizedBox = "struct Box<T>\n    public var n = " + Sized + "\n    public init() => ()\n";
    private const string SelfOwner = "struct GV<T> {o}\n    T is Copy\n    let r: ref/i32 during o\n    public var n = " + Sized +
        "\n    public init(r: ref/i32 during o) => self.r = r\n    public func pick(self: ref/Self, c: bool, x: ref/i32) -> isize\n        let other = GV<T>.init(x)\n";

    private const string UseSelf = "\nfunc use(x: ref/i32) -> isize\n    let v = GV<i64>.init(x)\n    return v.pick(true, x)\nConsole.writeLine(\"ok\")\n";

    // The form, the program and its records.
    public static TheoryData<string, string, string> OneInstanceCases => new()
    {
        { "self joined", $"{SelfOwner}        let g = if c => self.n else => other.n\n        return g(){UseSelf}", $"Unsupported_Kd {Sized}; UninitializedPlace_Kd let g = if c => self.n else => other.n; Unsupported_Kd self.n; Unsupported_Kd g()" },
        { "self assigned", $"{SelfOwner}        var g = self.n\n        g = other.n\n        return g(){UseSelf}", $"Unsupported_Kd {Sized}; Unsupported_Kd self.n; Unsupported_Kd g()" },
        {
            "holder in the owner's frame",
            $"{SizedBox}    public func pick(self: ref/Self, c: bool) -> isize\n        let h = Holder<T>.init()\n        let g = if c => self.n else => h.g\n" +
                "        return g()\nstruct Holder<U>\n    public var g = Box<U>.init().n\n    public init() => ()\nlet k = Box<i64>.init().pick(true)\n",
            "UninitializedPlace_Kd let g = if c => self.n else => h.g; Unsupported_Kd self.n"
        },
        {
            "two instances",
            $"{PassBox}func two(x: ref/i32, w: ref/i64) -> ()\n    let a = Box<ref/i32>.init(v: x)\n    let b = Box<ref/i64>.init(v: w)\n    var g = a.f\n    g = b.f\n    return\nConsole.writeLine(\"ok\")\n",
            $"Unsupported_Kd {Pass}; TypeMismatch_Kd b.f"
        },
        {
            "two instances erased",
            $"{SizedBox}func use(y: ref/i32, z: ref/i64) -> isize\n    let a = Box<ref/i32>.init()\n    let b = Box<ref/i64>.init()\n" +
                "    let ea: () -> isize = a.n\n    let eb: () -> isize = b.n\n    return ea() + eb()\nConsole.writeLine(\"ok\")\n",
            $"Unsupported_Kd {Sized}; Unsupported_Kd a.n; Unsupported_Kd b.n"
        },
    };

    // SPEC 6.1.3, 6.3.3, 8.13.3 (DECISIONS R5-M43, R5-M157, BU-60): an instance of a definition generic in its context holds the instance it
    // instantiates in. A Closure in a generic struct's Field initializer (also a nested struct's), wherever it is made, called, erased or
    // stored and called from a method's instance, and whether or not its body names a slot, holds its Type's instance: one item per
    // instance, shared by every constructor and member, also when read through another instance (before: each request of its body was
    // the EnclosingInstance gap). Its held instance holding a generic function's Closure (Holder<C>), also through a function with an Origin
    // parameter or inside a second held context, is closed in each of the function's instances: one key per instance, none open (before:
    // InternalInvariant_Kd, R5's B1). An owner instance with an Origin position stays that gap, as before, until a use opens it (PLAN G300). A
    // function nested in such an initializer or in a generic getter is the gap at its call, as in a method (PLAN G149). A Closure in a
    // getter, a constructor or a method has one item per instance, a closed struct's initializer Closure is a root and an initializer
    // without a definition has none, each without a record. Every admitted representative closes its body.
    [Theory]
    [MemberData(nameof(EnclosedCases))]
    public void DefinitionsOutsideAFunctionBodyNeedTheirEnclosingInstance(string form, string source, int closures, string records)
    {
        var c = MirBuildTest.Compile(source);
        Assert.True(c.Binding.Result.IsComplete, form + ": " + MinimalEmissionTest.Describe(c));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal(records, string.Join(" | ", set.Records.Select(x => $"{x.Code} {Text(c.SyntaxOf(x.Subject))}")));
        Assert.Equal(closures, set.Items.ToArray().Count(static x => x.Key.Body.Def.Kind == MirBodyKind.Closure));
        Assert.All(set.Items.ToArray(), x => Assert.True(Closed(c, program, x), form));

        string Text(Koto node) => source.Substring(node.Span.Start, node.Span.Length);
    }

    // DECISIONS BU-60 (PLAN G300's gate, the readers' view): whether an owner instance's erased form has an Origin position is one fact for every owner
    // of that instance, a static-only one included, so Box<ref/i32 during static> and Box<ref/i32> both stay the EnclosingInstance gap. The
    // check reports the records dev reports: the gap at the literal, also for a joined or assigned read (before: TypeMismatch_Kd, the
    // static-only owner closed apart), and beside it Binding's own record at a call whose signature names T.
    [Theory]
    [InlineData("let g = if c => bs.f else => bx.f", "Unsupported_Kd " + Pass)]
    [InlineData("var g = bx.f\n    g = bs.f", "Unsupported_Kd " + Pass)]
    [InlineData("let g = bs.f\n    _ = g(s)", "Unsupported_Kd " + Pass + "; NoApplicableOverload_Kd g(s)")]
    public void AnOwnerWithAnOriginPositionStaysTheGap(string body, string records) => Assert.Equal(records, CheckRecords(Staged(body)));

    // SPEC 3, 6.1.3 (DECISIONS BU-60, the owner's (a)): one producer forms a held closure's context from the instance modulo Origins, none
    // in the owner's own frame (the identity test), so every read of one instance has one Closure Type and two instances never share one.
    // In GV<T>{o}'s member, self.n and GV<T>.init(x).n, and in Box<T>'s, self.n and Holder<T>.g (Box<U>.init().n), join or assign as one
    // Type: dev's records (before: TypeMismatch_Kd hiding them). Box<ref/i32> and Box<ref/i64> are two instances (SPEC 3's Concrete Closure
    // Type): TypeMismatch_Kd at the assignment beside the gap (before: none, one shared Type), and each erased read is the gap at its read.
    [Theory]
    [MemberData(nameof(OneInstanceCases))]
    public void OneInstanceModuloOriginsHasOneClosureType(string form, string source, string records)
        => Assert.True(CheckRecords(source) is var actual && actual == records, $"{form}: {actual}");

    private static string Staged(string body)
        => $"{PassBox}func pick(c: bool, s: ref/i32 during static, x: ref/i32) -> i32\n    let bs = Box<ref/i32 during static>.init(v: s)\n" +
            $"    let bx = Box<ref/i32>.init(v: x)\n    {body}\n    return 1\nConsole.writeLine(\"ok\")\n";

    private static string Program(string[] lines) => string.Join('\n', lines) + "\n";

    // The check's records: code and source text, joined by "; ".
    private static string CheckRecords(string source)
        => string.Join("; ", DiagnosticCorpus.Check(source).Diagnostics.Select(x => $"{x.Code} {(x.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty)}"));
}
