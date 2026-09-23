// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class DynamicArrayCleanupTest
{
    private const string Task = "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit\n        match self.id\n            1 => Console.writeLine(\"drop 1\")\n            2 => Console.writeLine(\"drop 2\")\n            _ => Console.writeLine(\"drop 3\")\n";

    [Theory]
    [InlineData("Isize", "0@isize")]
    [InlineData("Index", "Index.init(0)")]
    public void AbandonedLaterIndexDestroysAcquiredValue(string name, string indexValue)
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayCleanupArgument" + name,
            Task + "var values: Array<Task> = []\nlet completed = work: do\n    values@uniq.insert(value: Task.init(1), index: (index: do\n        exit to work: false\n        exit to index: " + indexValue + "\n    ))\n    exit to work: true\nrequire not completed and values.length == 0 else => $abort(\"abandon\")\nConsole.writeLine(\"done\")",
            "drop 1\ndone\n");

    [Fact]
    public void OrdinaryExitDestroysOnlyCommittedLiteralElements()
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayCleanupLiteral",
            Task + "let completed = work: do\n    let values: Array<Task> = [Task.init(1), (exit to work: false), Task.init(3)]\n    exit to work: true\nrequire not completed else => $abort(\"literal\")\nConsole.writeLine(\"done\")",
            "drop 1\ndone\n");

    [Theory]
    [InlineData("Copy", "func count(values: ref/Array<i32>) -> isize => values.length\nfunc no() -> bool\n    Console.writeLine(\"no\")\n    return false\nrequire not (no() and count([1]) == 1) else => $abort(\"skip\")\nrequire true and count([1, 2]) == 2 else => $abort(\"run\")\nConsole.writeLine(\"done\")", "no\ndone\n")]
    [InlineData("NonCopy", Task + "func has(values: ref/Array<Task>) -> bool => values.length == 1\nrequire false or has([Task.init(1)]) else => $abort(\"run\")\nrequire true or has([Task.init(2)]) else => $abort(\"skip\")\nConsole.writeLine(\"done\")", "drop 1\ndone\n")]
    [InlineData("Local", Task + "func pick(flag: bool) -> isize\n    let values: Array<Task>\n    if flag => values = [Task.init(2)]\n    return 0\nrequire pick(true) == 0 and pick(false) == 0 else => $abort(\"pick\")\nConsole.writeLine(\"done\")", "drop 2\ndone\n")]
    [InlineData("Replacement", Task + "func pick(flag: bool)\n    var values: Array<Task>\n    if flag => values = [Task.init(1)]\n    values = [Task.init(2)]\npick(true)\npick(false)\nConsole.writeLine(\"done\")", "drop 1\ndrop 2\ndrop 2\ndone\n")]
    public void AConditionallyConstructedArrayIsDestroyedOnlyWhenLive(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("DynamicArrayCleanupConditional" + name, source, stdout);

    [Theory]
    [InlineData("Local", "var values: Array<Task> = [Task.init(1)]\nvalues = [Task.init(2)]\nConsole.writeLine(\"replaced\")", "drop 1\nreplaced\ndrop 2\n")]
    [InlineData("Parameter", "func reset(values: Array<Task>)\n    var owned = values@move\n    owned = [Task.init(2)]\n    Console.writeLine(\"replaced\")\nreset([Task.init(1)])", "drop 1\nreplaced\ndrop 2\n")]
    [InlineData("Copy", "var values: Array<i32> = [1]\nvalues = [2, 3]\nrequire values.length == 2 and values[1] == 3 else => $abort(\"copy\")\nConsole.writeLine(\"replaced\")", "replaced\n")]
    public void WholeReplacementDestroysTheOldArrayFirst(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("DynamicArrayCleanupReplace" + name, (name == "Copy" ? string.Empty : Task) + source, stdout);

    [Fact]
    public void ClearAbortSkipsRemainingDestructionAndContinuation()
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayCleanupAbort",
            "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit\n        if self.id == 2 => $abort(\"stop\")\n        Console.writeLine(\"drop\")\nvar values: Array<Task> = [Task.init(1), Task.init(2), Task.init(3)]\nvalues@uniq.clear()\nConsole.writeLine(\"after\")",
            "drop\n",
            1,
            "Hello.kimi:5:28: abort KIMI_E_ABORT: stop\n");
}
