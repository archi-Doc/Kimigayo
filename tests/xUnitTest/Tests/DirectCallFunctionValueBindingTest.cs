// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

public class DirectCallFunctionValueBindingTest
{
    // A group's import, a struct's import and a conforming struct's import (SPEC 22.6).
    private const string Native = "group Native\n    #LibraryImport(\"kernel32\", \"GetLastError\")\n    public func value() -> u32\n";
    private const string Win = "struct Win\n    #LibraryImport(\"kernel32\", \"GetLastError\")\n    public func now() -> u32\n";
    private const string Clock = "contract Clock\n    func now() -> u32\nstruct Win\n    Self is Clock\n" +
        "    #LibraryImport(\"kernel32\", \"GetLastError\")\n    public func now() -> u32\n";

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
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.DirectCallFunctionValue_Kd);
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
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.DirectCallFunctionValue_Kd);
        Assert.False(c.Binding.Result.IsComplete);
    }

    // SPEC 7.6, 22.6: Binding rejects the reference itself, a group's after selection (by a function item, a Function Type or a requirement
    // item), and a group no context selects is ambiguous; no later phase judges the form again, so the reference is the one record. An
    // import is rejected by each declaration form and name (its group's, a struct's, a conforming struct's own, unqualified, Self's, the
    // library's) and after its group's selection; the positions are the unsafe rows' (the complement of the callee).
    [Theory]
    [InlineData("unsafe func raw() -> i32 => 1\nlet g = raw", "DirectCallFunctionValue_Kd", "raw")]
    [InlineData(
        "unsafe func raw<T>(value: T) -> T => value@move\nfunc raw(value: i32, other: i32) -> i32 => value\nlet g = raw<i32>",
        "DirectCallFunctionValue_Kd",
        "raw<i32>")]
    [InlineData(
        "unsafe func raw(value: i32) -> i32 => value\nfunc raw(value: bool) -> bool => value\nlet g: (i32) -> i32 = raw",
        "DirectCallFunctionValue_Kd",
        "raw")]
    [InlineData(
        "contract Reader\n    unsafe func read(self: ref/Self) -> i32\nfunc use<T>(value: ref/T) -> i32\n    T is Reader\n" +
        "    let f = T.read\n    return 0",
        "DirectCallFunctionValue_Kd",
        "T.read")]
    [InlineData("unsafe func raw(value: i32) -> i32 => value\nfunc raw(value: bool) -> bool => value\nlet g = raw", "AmbiguousBinding_Kd", "raw")]
    [InlineData(Native + "let f = Native.value", "DirectCallFunctionValue_Kd", "Native.value")]
    [InlineData(Native + "    public func get() -> u32\n        let f = value\n        return f()", "DirectCallFunctionValue_Kd", "value")]
    [InlineData(Win + "let g: () -> u32 = Win.now", "DirectCallFunctionValue_Kd", "Win.now")]
    [InlineData(Clock + "let f = Win.now", "DirectCallFunctionValue_Kd", "Win.now")]
    [InlineData(Clock + "    public func twice() -> u32\n        let f = Self.now\n        return f()", "DirectCallFunctionValue_Kd", "Self.now")]
    [InlineData("let f = Kimi.Windows.queryPerformanceCounter", "DirectCallFunctionValue_Kd", "Kimi.Windows.queryPerformanceCounter")]
    [InlineData(Native + "    public func value(seed: u32) -> u32 => seed\nlet g: () -> u32 = Native.value", "DirectCallFunctionValue_Kd", "Native.value")]
    [InlineData(
        Native + "    public func value(seed: u32) -> u32 => seed\nfunc run<F>(action: F) -> u32\n    F is Callable<() -> u32>\n" +
        "    return action()\nlet n = run(Native.value)",
        "DirectCallFunctionValue_Kd",
        "Native.value")]
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
    [InlineData("unsafe func raw() -> i32 => 1\nunsafe => _ = raw()")]
    [InlineData("unsafe func raw() -> i32 => 1\nunsafe => _ = (raw)()")]
    [InlineData("group N\n    public unsafe func raw() -> i32 => 1\nunsafe => _ = N.raw()")]
    [InlineData("unsafe func raw(value: i32) -> i32 => value\nunsafe => _ = raw(1)")]
    [InlineData("func safe() -> i32 => 1\nlet g: () -> i32 = safe")]
    [InlineData(Native + "let n = Native.value()")]
    [InlineData(Native + "    public func value(seed: u32) -> u32 => seed\nlet g: (u32) -> u32 = Native.value")]
    [InlineData(Clock + "func item<C>() -> u32\n    C is Clock\n    let now = C.now\n    return now()\nlet n = item<Win>()")]
    [InlineData(Clock + "func erased<C>() -> u32\n    C is Clock\n    C is Owned\n    let now: () -> u32 = C.now\n    return now()\nlet n = erased<Win>()")]
    [InlineData(Clock + "func apply(action: () -> u32) -> u32 => action()\nfunc keep<T>(value: T) -> T => value@move\nfunc both<C>() -> u32\n" +
        "    C is Clock\n    C is Owned\n    let kept = keep(C.now)\n    return apply(C.now) + kept()\nlet n = both<Win>()")]
    public void DirectCallsAndSafeAcquisitionKeepTheirBinding(string source)
    {
        // SPEC 7.6, 22.6, 7.7.4: a direct call, a group's other member and a requirement's Function Item, whatever its witness (an
        // import included: taken, erased, passed), are accepted.
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }
}
