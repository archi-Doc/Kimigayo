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
        // SPEC 7: an unsafe function supports direct calls only.
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
        // SPEC 7: the rejection is the complement of the direct-call callee, not a position list.
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnsafeFunctionValue_Kd);
        Assert.False(c.Binding.Result.IsComplete);
    }

    // SPEC 7.6: Binding rejects the reference itself, a group's after selection (by a function item, a Function Type or a requirement
    // item), and a group no context selects is ambiguous; no later phase judges the form again, so the reference is the one record.
    [Theory]
    [InlineData("unsafe func raw() -> i32 => 1\nlet g = raw", "UnsafeFunctionValue_Kd", "raw")]
    [InlineData("unsafe func raw<T>(value: T) -> T => value\nfunc raw(value: i32, other: i32) -> i32 => value\nlet g = raw<i32>", "UnsafeFunctionValue_Kd", "raw<i32>")]
    [InlineData("unsafe func raw(value: i32) -> i32 => value\nfunc raw(value: bool) -> bool => value\nlet g: (i32) -> i32 = raw", "UnsafeFunctionValue_Kd", "raw")]
    [InlineData("contract Reader\n    unsafe func read(self: ref/Self) -> i32\nfunc use<T>(value: ref/T) -> i32\n    T is Reader\n    let f = T.read\n    return 0", "UnsafeFunctionValue_Kd", "T.read")]
    [InlineData("unsafe func raw(value: i32) -> i32 => value\nfunc raw(value: bool) -> bool => value\nlet g = raw", "AmbiguousBinding_Kd", "raw")]
    public void TheReferenceIsTheOneRecord(string source, string code, string text)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        c.ReportAnalysis();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(code, error.Code);
        Assert.Equal(text, error.Text);
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
