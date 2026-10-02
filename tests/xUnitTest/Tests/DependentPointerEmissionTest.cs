// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DependentPointerEmissionTest
{
    [Theory]
    [InlineData("Shared", "let value = 42\nlet entries = [1: value@ref]\nfor (key, item) in entries@move => require item == 42 else => $abort(\"shared\")")]
    [InlineData("Exclusive", "var value = 0\nlet entries = [1: value@uniq]\nfor (key, item) in entries@move => item@follow = 42\nrequire value == 42 else => $abort(\"exclusive\")")]
    [InlineData("NamedInput", "func run(value: uniq/i32 during a)\n    let entries = [1: value@move]\n    for (key, item) in entries@move => item@follow = 42\nvar value = 0\nrun(value@uniq)\nrequire value == 42 else => $abort(\"named\")")]
    [InlineData("Tuple", "let value = 42\nlet entries = [1: (value@ref, \"ok\")]\nfor (key, item) in entries@move => require item.0 == 42 else => $abort(\"tuple\")")]
    [InlineData("Array", "var value = 0\nlet entries = [value@uniq]\nfor item in entries@move => item@follow = 42\nrequire value == 42 else => $abort(\"array\")")]
    [InlineData("Fixed", "var value = 0\nlet entries: [1 of uniq/i32] = [value@uniq]\nfor item in entries@move => item@follow = 42\nrequire value == 42 else => $abort(\"fixed\")")]
    [InlineData("KeyAndValue", "let key = 1\nlet value = 42\nlet entries = [key@ref: value@ref]\nfor (key, item) in entries@move => require key == 1 and item == 42 else => $abort(\"pair\")")]
    public void OwningStorageTransfersTheCompleteElementType(string name, string source)
        => ScalarEmissionTest.EmitFixture("DependentPointer" + name, source, string.Empty);

    [Theory]
    [InlineData("ref/i32 during a")]
    [InlineData("uniq/i32 during a")]
    [InlineData("(ref/i32 during a, string)")]
    [InlineData("[2 of ref/i32 during a]")]
    public void RawReadsAndReplacementKeepExplicitPointeeOrigins(string type)
    {
        var source = "func read(pointer: raw/(" + type + ")) -> " + type + "\n    unsafe => return *pointer\nfunc replace(pointer: raw/(" + type + "), value: " + type + ")\n    unsafe => *pointer = value@move\npublic func main() => ()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        CompilationTestHelper.WriteIr(c);
    }

    [Fact]
    public void RemainingDependentElementsAreDestroyedInReverseOrder()
    {
        const string source = """
            struct Observer {a}
                let value: ref/i32 during a
                let name: string
                public init(value: ref/i32 during a, name: string)
                    self.value = value
                    self.name = name@move
                drop
                    require self.value == 42 else => $abort("referent")
                    Console.writeLine(self.name)
            let value = 42
            let entries = [1: Observer.init(value@ref, "one"), 2: Observer.init(value@ref, "two"), 3: Observer.init(value@ref, "three")]
            for (key, item) in entries@move => exit
            """;
        ScalarEmissionTest.EmitFixture("DependentPointerCleanup", source, "one\nthree\ntwo\n");
    }

    [Fact]
    public void RawAcquisitionCannotExtendThePointeesOrigin()
    {
        var c = MinimalEmissionTest.Analyze("func read(pointer: raw/(ref/i32 during a)) -> ref/i32 during static\n    unsafe => return *pointer");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void AReferenceTransferredThroughOwningIterationProtectsItsReferent()
    {
        var c = MinimalEmissionTest.Analyze("var value = 42\nlet entries = [1: value@ref]\nfor (key, item) in entries@move\n    value = 99\n    require item == 42 else => $abort(\"reference\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ReferenceIterationAllocatesOnlyTheEntryBuffer()
        => NativeAllocationAudit.WriteFixture("DependentPointerCost", "var value = 0\nlet entries = [1: value@uniq]\nfor (key, item) in entries@move => item@follow = 42\nrequire value == 42 else => $abort(\"value\")", 1, 1, 128);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmDependentIterationCompilationAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("var value = 0\nlet entries = [1: value@uniq]\nfor (key, item) in entries@move => item@follow = 42\nrequire value == 42 else => $abort(\"value\")");
        void Compile()
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        for (var i = 0; i < 8; i++)
        {
            Compile();
        }

        Assert.Equal(0, AllocationMeasurement.Measure(Compile));
    }
}
