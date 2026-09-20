// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class OwnedPatternLiteralTest
{
    [Fact]
    public void NestedStringPatternsCompareExactUtf8BeforeAcquisition()
    {
        const string Source = """
            enum Packet
                Empty
                Text(string, i32)
            func route(value?: Packet) -> i32
                return match value
                    .Text("", _) => 1
                    .Text("é", let n) if n > 0 => n
                    .Text("e\u(301)", _) => 3
                    .Text("x\0y", _) => 4
                    .Text(_, _) => 5
                    .Empty => 6
            if route(.Text("", 0)) != 1 => $abort("empty")
            if route(.Text("é", 2)) != 2 => $abort("composed")
            if route(.Text("e\u(301)", 0)) != 3 => $abort("decomposed")
            if route(.Text("x\0y", 0)) != 4 => $abort("nul")
            if route(.Empty) != 6 => $abort("inactive")
            if route(.Text("é", 0)) != 5 => $abort("false guard")
            let pair = (("left", "right"), 9)
            let selected = match pair
                (("left", "wrong"), _) => 0
                (("left", "right"), let n) => n
                _ => 1
            if selected != 9 => $abort("nested tuple")
            Console.writeLine("ok")
            """;
        const string Name = "OwnedPatternWindowUtf8";
        var ir = ScalarEmissionTest.EmitFixture(Name, Source, "ok\n");
        StringEmissionTest.WriteAuditedFixture(Name, Source, ir, "ok\n", "=1;é=2;e\u0301=1;x\0y=1;left=1;right=1;wrong=0;ok=1");
    }
}
