// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

public class UnsafeFunctionValueBindingTest
{
    [Theory]
    [InlineData("unsafe func raw() -> i32 => 1\nlet g: () -> i32 = raw")]
    [InlineData("group N\n    public unsafe func raw() -> i32 => 1\nlet g: () -> i32 = N.raw")]
    [InlineData("unsafe func raw() -> i32 => 1\nfunc take(c: () -> i32) -> i32 => c()\nlet v = take(raw)")]
    [InlineData("unsafe func raw() -> i32 => 1\nvar g: () -> i32 = raw\ng = raw")]
    [InlineData("unsafe func raw() -> i32 => 1\nfunc give() -> () -> i32\n    return raw")]
    [InlineData("unsafe func raw() -> i32 => 1\nfunc give() -> () -> i32 => raw")]
    [InlineData("unsafe func raw() -> i32 => 1\nlet all = [raw]")]
    [InlineData("unsafe func raw() -> i32 => 1\nlet pair = (raw, 1)")]
    public void UnsafeFunctionsCannotBeAcquiredAsValues(string source)
    {
        // SPEC 7.7: an unsafe function supports direct calls only.
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnsafeFunctionValue_Kd);
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("unsafe func raw() -> i32 => 1\nlet v = if true => raw else => raw")]
    [InlineData("unsafe func raw() -> i32 => 1\nlet v = match 1\n    _ => raw")]
    [InlineData("unsafe func raw() -> i32 => 1\nlet v = raw == raw")]
    [InlineData("unsafe func raw() -> i32 => 1\nlet v = raw.length")]
    [InlineData("unsafe func raw() -> i32 => 1\nlet items = [1, 2]\nlet v = items[raw]")]
    [InlineData("unsafe func raw() -> i32 => 1\nlet v = raw..1")]
    [InlineData("unsafe func raw() -> i32 => 1\nfunc take(c: () -> i32 = raw) -> i32 => c()")]
    public void UnsafeFunctionsAreRejectedInEveryRemainingValuePosition(string source)
    {
        // SPEC 7.7: the rejection is the complement of the direct-call callee, not a position list.
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnsafeFunctionValue_Kd);
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("unsafe func raw() -> i32 => 1\nlet v = unsafe => raw()")]
    [InlineData("unsafe func raw() -> i32 => 1\nlet v = unsafe => (raw)()")]
    [InlineData("group N\n    public unsafe func raw() -> i32 => 1\nlet v = unsafe => N.raw()")]
    [InlineData("unsafe func raw(value: i32) -> i32 => value\nlet v = unsafe => raw(1)")]
    [InlineData("func safe() -> i32 => 1\nlet g: () -> i32 = safe")]
    public void DirectCallsAndSafeAcquisitionKeepTheirBinding(string source)
        => Assert.DoesNotContain(MinimalEmissionTest.Analyze(source).Binding.Issues, x => x.Code == DiagnosticCode.UnsafeFunctionValue_Kd);
}
