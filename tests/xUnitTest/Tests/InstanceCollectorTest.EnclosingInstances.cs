// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// NestedFunctionsNeedTheirEnclosingInstance's companion for definitions outside a named function's body (unit fi-closure, DECISIONS
// R5-M157): a Closure in a generic struct's Field initializer or getter, and a function declared in one.
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

    // The records of a Closure's literal and its call.
    private const string MadeAndCalled = "Unsupported_Kd " + Sized + " | Unsupported_Kd (" + Sized + ")()";

    // The form, the program, its Closure items and the collector's records (code and source text, joined by " | ").
    public static TheoryData<string, string, int, string> EnclosedCases => new()
    {
        { "initializer", $"{FieldHead}({Sized})(){Init}{Built}", 0, MadeAndCalled },
        { "two constructors", $"{FieldHead}({Sized})(){Init}    public init(n: isize) => ()\n{Built}", 0, MadeAndCalled },
        { "erased", $"struct Box<T>\n    var f: () -> isize = {Sized}{Init}{Built}", 0, $"Unsupported_Kd {Sized}" },
        { "stored, called in a method", $"struct Box<T>\n    var f = {Sized}{Init}{Stored}", 0, $"Unsupported_Kd {Sized} | Unsupported_Kd self.f()" },
        { "nested Type", $"{NestedHead}({Sized})()\n        public init() => ()\n{Inner}", 0, MadeAndCalled },
        { "naming no slot", $"{FieldHead}({Four})(){Init}{Built}", 0, $"Unsupported_Kd {Four} | Unsupported_Kd ({Four})()" },
        { "function in the initializer", $"{FieldHead}({Sizer}\n    )(){Init}{Built}", 0, $"Unsupported_Kd {Sizer} | Unsupported_Kd ({Sizer}\n    )()" },
        { "function in a getter", $"{GetterHead}{Make}{Read}", 0, "Unsupported_Kd make()" },
        { "getter", $"{GetterHead} => ({Sized})()\n{Read}", 2, string.Empty },
        { "constructor", $"{Plain}({Sized})()\n{Built}", 2, string.Empty },
        { "method", $"{Plain}0\n    public func get(self: ref/Self) -> isize => ({Sized})()\n{Got}", 2, string.Empty },
        { "closed struct", $"{ClosedField}{Init}let a = Box.init()\n", 1, string.Empty },
        { "no definition", $"{FieldHead}{Capacity}{Init}{Built}", 0, string.Empty },
    };

    // SPEC 6.1.3, 6.3.3, 8.13.3 (DECISIONS R5-M43, R5-M157): an instance of a definition generic in its context holds the instance it
    // instantiates in. A Closure in a generic struct's Field initializer (also a nested struct's), wherever it is made, called, erased or
    // stored and called from a method's instance, and whether or not its body names a slot, gets no enclosing instance from Binding yet
    // (PLAN G300), so its substitution binds nothing and each request of its body is the
    // EnclosingInstance gap (before: InternalInvariant_Kd from an open representative, or one identity item every instance shared); a
    // function nested in a generic getter is the gap at its call, as in a method (PLAN G149). A Closure in a getter, a constructor or a method
    // has one item per instance, a closed struct's initializer Closure is a root and an initializer without a definition has none, each
    // without a record. Every admitted representative closes its body.
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
}
