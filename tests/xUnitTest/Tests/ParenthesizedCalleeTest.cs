// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 12.4.2: parentheses stay part of a referenced function name, so `(f)(1)` is the call `f(1)` under ordinary overload
// resolution; a parenthesized value is still called as a value.
public class ParenthesizedCalleeTest
{
    private const string Counter = "struct C\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func add(self, k: i32) -> i32 => self.n + k\n";

    [Theory]
    [InlineData("Sole", "func f(x: i32) -> i32 => x + 1\nrequire (f)(1) == 2 else => $abort(\"sole\")")]
    [InlineData("Overloads", "func g(x: i32) -> i32 => x + 1\nfunc g(x: bool) -> i32 => 7\nrequire (g)(1) == 2 and ((g))(true) == 7 else => $abort(\"overloads\")")]
    [InlineData("Generic", "func id<T>(x: T) -> T => x@move\nrequire (id)(1) == 1 and (id<i32>)(2) == 2 else => $abort(\"generic\")")]
    [InlineData("Qualified", "group Tools\n    public func inc(x: i32) -> i32 => x + 1\nrequire (Tools.inc)(1) == 2 else => $abort(\"qualified\")")]
    [InlineData("Method", Counter + "let c = C.init(n: 1)\nrequire (c.add)(4) == 5 and (C.add)(c@ref, 5) == 6 else => $abort(\"method\")")]
    [InlineData("Values", "let c = func (x: i32) => x + 1\nlet g: (i32) -> i32 = c\nrequire (c)(1) == 2 and (g)(2) == 3 else => $abort(\"values\")")]
    public void AParenthesizedNameIsCalledByName(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("ParenthesizedCallee" + name, source, string.Empty);
    }

    [Fact]
    public void AnInapplicableParenthesizedCallNamesItsCandidate()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32) -> i32 => x + 1\nlet r = (f)(true)");
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(nameof(DiagnosticCode.NoApplicableOverload_Kd), error.Code);
        Assert.Equal("(f)(true)", error.Text);
    }
}
