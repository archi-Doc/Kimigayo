// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class EnumEmissionTest
{
    [Theory]
    [InlineData("Empty", "enum E\n    A\n    B\nlet value: E = .B")]
    [InlineData("Payload", "enum E<T>\n    A(T)\n    B\nlet value: E<(i32, bool)> = .A((12, true))")]
    [InlineData("Array", "enum E<T>\n    A(T)\n    B\nlet values: [2 of E<i64>] = [.A(7), .B]")]
    [InlineData("Move", "enum E\n    A(string)\n    B\nlet value: E = .A(\"payload\")\nlet moved = value")]
    [InlineData("Nested", "enum E<T>\n    A(T)\n    B\nlet value: E<E<i64>> = .A(.A(5))")]
    [InlineData("Return", "enum E<T>\n    A(T)\n    B\nfunc make() -> E<i64> => .A(7)\nlet value = make()")]
    [InlineData("Aligned", "enum E<T>\n    A(u8, T, u16)\n    B\nlet values: [2 of E<u128>] = [.A(3, 340282366920938463463374607431768211455, 9), .B]")]
    public void ConcreteStorage(string name, string source)
    {
        ScalarEmissionTest.EmitFixture("EnumStorage" + name, source + "\nConsole.writeLine(\"ok\")", "ok\n");
    }

    [Theory]
    [InlineData("Active", "enum E\n    A(string, string)\n    B(string)\nlet value: E = .A(\"a\", \"b\")", "a=1;b=1")]
    [InlineData("Inactive", "enum E\n    A(string)\n    B\nlet value: E = if true => .B else => .A(\"a\")", "a=0")]
    [InlineData("Replace", "enum E\n    A(string)\n    B\nvar value: E = .A(\"a\")\nvalue = .B", "a=1")]
    [InlineData("Conditional", "enum E\n    A(string)\n    B\nvar value: E = .A(\"a\")\nif true => value\nvalue = .A(\"b\")", "a=1;b=1")]
    public void ActivePayloadCleanup(string name, string source, string audit)
    {
        name = "EnumCleanup" + name;
        var ir = ScalarEmissionTest.EmitFixture(name, source, string.Empty);
        StringEmissionTest.WriteAuditedFixture(name, source, ir, string.Empty, audit, order: name == "EnumCleanupActive" ? [1, 0] : null);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConstructionPlanMustMatchItsCaseAndPayload(bool wrongCase)
    {
        var c = MinimalEmissionTest.Analyze("enum E\n    A(i32)\n    B(i32)\nlet a: E = .A(1)\nlet b: E = .B(2)");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies.Single(x => x.Constructions.Count == 2);
        var first = body.Constructions[0];
        body.ConstructionStorage[0] = wrongCase ? first with { Case = body.Constructions[1].Case } : first with { PayloadStart = body.Constructions[1].PayloadStart };
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.Validate(out error), error);
    }
}
