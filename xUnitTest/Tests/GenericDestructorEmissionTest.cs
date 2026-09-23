// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class GenericDestructorEmissionTest
{
    [Fact]
    public void SharedBodyRunsBeforeInstantiatedFieldCleanup()
        => ScalarEmissionTest.EmitFixture(
            "GenericDestructorOrder",
            "struct Child\n    deinit => Console.writeLine(\"child\")\nstruct Box<T>\n    let value: T\n    public init(value: T) => self.value = value@move\n    deinit => Console.writeLine(\"box\")\nlet a = Box<Child>.init(Child.init())\nlet b = Box<i32>.init(7)\nConsole.writeLine(\"body\")",
            "body\nbox\nbox\nchild\n");

    [Fact]
    public void FieldDependentBodyCannotUseErasedReceiver()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    let value: T\n    let tag: i32\n    public init(value: T)\n        self.value = value@move\n        self.tag = 1\n    deinit\n        if self.tag == 1 => Console.writeLine(\"tag\")\nlet value = Box<i32>.init(7)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Ownership.Result.IsVerified);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }
}
