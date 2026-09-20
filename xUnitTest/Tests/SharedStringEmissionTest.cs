// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// G10c: concrete string leaves in definition-side shared bodies.
public class SharedStringEmissionTest
{
    public static TheoryData<string, string, string, string> Fixtures => new()
    {
        { "Print", "func inspect<T>(value?: ref/T)\n    Console.writeLine(\"seen\")\nlet v: i64 = 3\ninspect(v)\nlet flag = true\ninspect(flag)", "seen\nseen\n", "seen=2" },
        { "UnicodeEmpty", "func print<T>(value?: ref/T)\n    Console.writeLine(\"日本語\\0\")\n    Console.writeLine(\"\")\nlet v = true\nprint(v)", "日本語\0\n\n", "日本語\0=1;=1" },
        { "Return", "func make<T>(value?: ref/T) -> string => \"made\"\nlet v = true\nConsole.writeLine(make(v))", "made\n", "made=1" },
        { "Parameter", "func print<T>(value?: ref/T, text?: string)\n    Console.writeLine(text)\nlet v = true\nprint(v, \"parameter\")", "parameter\n", "parameter=1" },
        { "Forwarded", "func make<T>(value?: ref/T) -> string => \"forwarded\"\nfunc relay<T>(value?: ref/T) -> string => make(value)\nlet v = true\nConsole.writeLine(relay(v))", "forwarded\n", "forwarded=1" },
        { "Replacement", "func replace<T>(value?: ref/T)\n    var text = \"old\"\n    text = \"new\"\n    text = text\n    Console.writeLine(text)\nlet v = true\nreplace(v)", "new\n", "old=1;new=1" },
        { "ConditionalMove", "func print<T>(value?: ref/T, flag?: bool)\n    let text = \"text\"\n    if flag => Console.writeLine(text)\nlet v = true\nprint(v, true)\nprint(v, false)", "text\n", "text=2" },
        { "ConcreteCall", "func echo(text?: string) -> string => text\nfunc relay<T>(value?: ref/T) -> string => echo(\"relay\")\nlet v = true\nConsole.writeLine(relay(v))", "relay\n", "relay=1" },
        { "Selection", "func choose<T>(value?: ref/T, flag?: bool) -> string => if flag => \"yes\" else => \"no\"\nlet v = true\nConsole.writeLine(choose(v, true))\nConsole.writeLine(choose(v, false))", "yes\nno\n", "yes=1;no=1" },
        { "Loop", "func repeat<T>(value?: ref/T)\n    var i = 0\n    while i < 3\n        let text = \"tick\"\n        if i == 1 => Console.writeLine(text)\n        i += 1\nlet v = true\nrepeat(v)", "tick\n", "tick=3" },
        { "ScalarJoinCleanup", "func choose<T>(value?: ref/T, take?: bool) -> i32\n    return if take\n        let text = \"temporary\"\n        yield 42\n    else => 12\nlet v = true\nrequire choose(v, true) == 42 and choose(v, false) == 12 else => $abort(\"wrong\")", string.Empty, "temporary=1;wrong=0" },
        { "Shadow", "func writeLine(text?: string) => ()\nfunc inspect<T>(value?: ref/T)\n    writeLine(\"silent\")\nlet v = true\ninspect(v)", string.Empty, "silent=1" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ExecutesAndDestroysOnce(string name, string source, string stdout, string destructions)
    {
        name = "SharedString" + name;
        var ir = ScalarEmissionTest.EmitFixture(name, source, stdout);
        StringEmissionTest.WriteAuditedFixture(name, source, ir, stdout, destructions);
    }
}
