// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class Utf8ReserveEffectTest
{
    private const string State = "group State\n    public var value: i32 = 0\n";
    private const string Writer = "struct Writer\n    Self is BufferWriter\n    public var local: i32 = 1\n    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>\n";

    [Theory]
    [InlineData("", "_ = minimum")]
    [InlineData("", "self.local += 1")]
    [InlineData("group Helpers\n    public func pure(value: i32) -> i32 => value + 1\n", "_ = Helpers.pure(self.local)")]
    [InlineData("group Constants\n    public let value: i32 = 1\n", "_ = Constants.value")]
    public void InputAuthorityAndImmutableStateAreAllowed(string prefix, string operation)
    {
        var c = Analyze(prefix, operation);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null) + string.Join("\n", c.Binding.Issues.Select(x => x.Code + ": " + x.Node)));
    }

    [Theory]
    [InlineData(State, "_ = State.value")]
    [InlineData(State, "State.value += 1")]
    [InlineData(State + "group Helpers\n    public func read() -> i32 => State.value\n", "_ = Helpers.read()")]
    [InlineData(State + "group Helpers\n    public func read() -> i32 => State.value\ngroup Cache\n    public let value: i32 = Helpers.read()\n", "_ = Cache.value")]
    [InlineData(State + "struct Noisy\n    public init() => ()\n    deinit\n        _ = State.value\n", "_ = Noisy.init()")]
    [InlineData(State + "struct Initializer\n    let value: i32 = State.value\n", "_ = Initializer.init()")]
    [InlineData("", "Console.writeLine(\"external\")")]
    public void AmbientOrUnknownEffectsInvalidateTheConformance(string prefix, string operation)
    {
        var c = Analyze(prefix, operation);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    private static Compilation Analyze(string prefix, string operation)
        => MinimalEmissionTest.Analyze(prefix + Writer + "        " + operation + "\n        return .Err(BufferFull.init())");
}
