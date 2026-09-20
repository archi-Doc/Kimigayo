// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class SharedNeverEmissionTest
{
    [Theory]
    [InlineData("Concrete", "func stop() -> Never => $abort(\"concrete\")\nfunc fail<T>(value?: ref/T) => stop()\nlet v = true\nfail(v)", 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: concrete\n")]
    [InlineData("Generic", "func fail<T>(value?: ref/T) -> Never => $abort(\"generic\")\nlet v = true\nfail(v)", 1, "Hello.kimi:1:40: abort KIMI_E_ABORT: generic\n")]
    [InlineData("Forwarded", "func fail<T>(value?: ref/T) -> Never => $abort(\"forwarded\")\nfunc relay<T>(value?: ref/T) -> Never => fail(value)\nlet v = true\nrelay(v)", 1, "Hello.kimi:1:40: abort KIMI_E_ABORT: forwarded\n")]
    [InlineData("Skipped", "func fail<T>(value?: ref/T) -> Never => $abort(\"bad\")\nfunc choose<T>(value?: ref/T, flag?: bool) -> i32 => if flag => 42 else => fail(value)\nlet v = true\nrequire choose(v, true) == 42 else => $abort(\"wrong\")", 0, "")]
    [InlineData("Specialization", "func fail<T>(value?: ref/T) -> Never => $abort(\"ordinary\")\nspecialize func fail<i32>(value: ref/i32) -> Never => $abort(\"selected\")\nfunc relay<T>(value?: ref/T) -> Never => fail(value)\nlet v: i32 = 1\nrelay(v)", 1, "Hello.kimi:2:55: abort KIMI_E_ABORT: selected\n")]
    public void Executes(string name, string source, int exit, string stderr)
        => ScalarEmissionTest.EmitFixture("SharedNever" + name, source, string.Empty, exit, stderr);

    [Fact]
    public void DivergesWithoutAResultOrNormalReturn()
    {
        const string Source = "func spin<T>(value?: ref/T) -> Never\n    loop => continue\nlet v = true\nspin(v)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var entry = Assert.Single(module.SharedEntries);
        Assert.True(entry.Abi.NoReturn);
        Assert.False(entry.Abi.ResultSlot);
        Assert.Null(entry.Result);
        ScalarEmissionTest.EmitFixture("SharedNeverDivergence", Source, string.Empty, timeoutMilliseconds: 300);
    }
}
