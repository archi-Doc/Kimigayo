// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ProjectedBorrowEmissionTest
{
    private const string Batch = "struct Batch\n    let prefix: i64\n    let values: [2 of i32]\n    public init(values: [2 of i32])\n        self.prefix = 99\n        self.values = values\n    public func view(self: ref/Self) -> ref/[2 of i32] during self => self.values@ref/[2 of i32]\n";

    [Theory]
    [InlineData("Field", "let b = Batch.init(values)\nlet r = b.view()\nrequire r[0] == 6 and r[1] == 7 else => $abort(\"projection\")")]
    [InlineData("Forward", "func forward(b: ref/Batch) -> ref/[2 of i32] during b => b.view()\nlet b = Batch.init(values)\nrequire forward(b@ref)[1] == 7 else => $abort(\"forward\")")]
    [InlineData("Repeated", "let b = Batch.init(values)\nvar n = 0\nfor i in b.view().indices => n += b.view()[i]\nrequire n == 13 else => $abort(\"repeat\")")]
    public void Executes(string name, string source)
        => ScalarEmissionTest.EmitFixture("ProjectedBorrow" + name, Batch + "let values: [2 of i32] = [6, 7]\n" + source, string.Empty);

    [Theory]
    [InlineData("Array", "[2 of i32]", "[6, 7]", "require r[1] == 7 else => $abort(\"generic\")")]
    [InlineData("Empty", "[0 of i32]", "[]", "require r.length == 0 else => $abort(\"generic\")")]
    public void GenericField(string name, string type, string value, string check)
    {
        var source = "struct Box<T>\n    let prefix: isize\n    let values: T\n    public init(prefix: isize, values: T)\n        self.prefix = prefix\n        self.values = values@move\n    public func view(self: ref/Self) -> ref/T during self => self.values@ref/T\n" +
            $"let values: {type} = {value}\nlet b = Box<{type}>.init(99, values)\nlet r = b.view()\n{check}";
        ScalarEmissionTest.EmitFixture("ProjectedBorrowGeneric" + name, source, string.Empty);
    }

    // BodyLowering validates every lowered body, including each monomorphized instance (SPEC 21.3.1);
    // the corrupt projected address is rejected on the ordinary body that owns it.
    [Fact]
    public void RejectsCorruptProjectedAddressAndReanalysisRecovers()
    {
        var source = Batch + "let values: [2 of i32] = [6, 7]\nlet b = Batch.init(values)\nlet r = b.view()\nlet n = r[1]";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies.Single(x => x.Function.BoundSymbol?.Name == "view");
        var address = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Borrow);
        body.Values[address] = body.Values[address] with { Constant = 0 };
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.Validate(out error), error);
    }

    [Theory]
    [InlineData("var b = Batch.init(values)\nlet r = b.view()\nb = Batch.init(values)\nlet n = r[0]")]
    [InlineData("func escape() -> ref/[2 of i32]\n    let values: [2 of i32] = [6, 7]\n    let b = Batch.init(values)\n    return b.view()")]
    [InlineData("let b = Batch.init(values)\nlet r = b.view()\nr[0] = 9")]
    public void RejectsInvalidLifetimeAndMutation(string source)
    {
        var c = MinimalEmissionTest.Analyze(Batch + "let values: [2 of i32] = [6, 7]\n" + source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }
}
