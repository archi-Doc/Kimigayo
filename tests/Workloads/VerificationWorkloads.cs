// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace Verification;

// Linked into tests and benchmarks so their language inputs cannot drift apart.
internal static class VerificationWorkloads
{
    internal const string ObjectItem = "struct Item\n    public let id: i32 = 7\n    public func read(self: ref/Self) -> i32 => self.id\n    drop => Console.writeLine(\"drop\")\n";

    internal static string ObjectView(bool stored)
        => ObjectItem + "let owner = Kimi.Intrinsics.makeObj(Item.init())\n" +
            (stored ? "let stored = (owner@move, 1)\nrequire stored.0.id == 7" : "require owner.id == 7") + " else => $abort(\"read\")";

    internal static string InspectionLoans(int count, bool stored = false)
    {
        var source = new StringBuilder("func inspect(value: ref/string) -> bool => value == \"x\"\nfunc check()\n");
        for (var i = 0; i < count; i++)
        {
            source.Append("    var value").Append(i).Append(" = \"x\"\n");
            if (stored)
            {
                source.Append("    let loan").Append(i).Append(" = value").Append(i).Append("@ref\n");
            }

            source.Append("    let read").Append(i).Append(" = inspect(").Append(stored ? "loan" : "value").Append(i).Append(")\n");
            source.Append("    require read").Append(i).Append(" else => $abort(\"inspection\")\n");
        }

        return source.Append("check()").ToString();
    }

    internal static string GuardHistories(int count)
    {
        var arms = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            arms.Append("true if check(x = 2, c) => return\n                ");
        }

        return "func check(effect: (), value: bool) -> bool => value\nfunc stop() -> Never => $abort(\"stop\")\nfunc f(c: bool)\n    var x = 1\n    do\n        loop\n            if c => return else => exit\n            match c\n                " + arms + "_ => x = 3\n            x = 4\n        stop()\n    let y = x\nf(true)";
    }

    internal static string LivePartLoans(int count, bool checking)
    {
        var source = new StringBuilder("struct Counter\n    public var value: i32 = 1\nfunc relay(p: uniq/Counter) -> uniq/Counter during p => p\nfunc check()\n");
        if (checking)
        {
            source.Append("    return\n");
        }

        for (var i = 0; i < count; i++)
        {
            source.Append("    var p").Append(i).Append(" = (Counter.init(), Counter.init())\n    let a").Append(i).Append(" = relay(p").Append(i).Append(".0@uniq)\n    let m").Append(i).Append(" = p").Append(i).Append(".1@move\n");
        }

        for (var i = 0; i < count; i++)
        {
            source.Append("    a").Append(i).Append(".value += m").Append(i).Append(".value\n    require p").Append(i).Append(".0.value == 2 else => $abort(\"value\")\n");
        }

        source.Append("check()");
        return source.ToString();
    }
}
