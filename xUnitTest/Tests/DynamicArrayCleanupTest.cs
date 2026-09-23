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

    [Fact]
    public void ClearAbortSkipsRemainingDestructionAndContinuation()
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayCleanupAbort",
            "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit\n        if self.id == 2 => $abort(\"stop\")\n        Console.writeLine(\"drop\")\nvar values: Array<Task> = [Task.init(1), Task.init(2), Task.init(3)]\nvalues@uniq.clear()\nConsole.writeLine(\"after\")",
            "drop\n",
            1,
            "Hello.kimi:5:28: abort KIMI_E_ABORT: stop\n");
}
