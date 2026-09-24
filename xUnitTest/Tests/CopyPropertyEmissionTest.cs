// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class CopyPropertyEmissionTest
{
    internal const string Meter = "struct Meter\n    public var raw: i32 = 2\n    public var level: i32\n        get() -> i32\n            Console.writeLine(\"get\")\n            return storage\n        set(value: i32) -> ()\n            Console.writeLine(\"set\")\n            storage = value\n    public computed doubled: i32\n        get() -> i32 => self.raw * 2\n        set(value: i32) -> () => self.raw = value / 2\n    public init() => self.level = 3\n";

    [Fact]
    public void CallsCopyAccessorsAfterDirectConstruction()
        => ScalarEmissionTest.EmitFixture("CopyPropertyBasic", Meter + "var m = Meter.init()\nm.level = 8\nrequire m.level == 8 else => $abort(\"level\")\nm.doubled = 12\nrequire m.doubled == 12 and m.raw == 6 else => $abort(\"computed\")", "set\nget\n");

    [Fact]
    public void SimpleAssignmentSecuresInputBeforeReceiver()
        => ScalarEmissionTest.EmitFixture("CopyPropertyOrder", Meter + "func receiver(m: uniq/Meter during source) -> uniq/Meter during source\n    Console.writeLine(\"receiver\")\n    return m\nfunc input() -> i32\n    Console.writeLine(\"input\")\n    return 8\nvar m = Meter.init()\nreceiver(m@uniq).level = input()\nrequire m.level == 8 else => $abort(\"level\")", "input\nreceiver\nset\nget\n");
}
