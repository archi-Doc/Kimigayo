// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class GenericPointerEmissionTest
{
    [Fact]
    public void ConcreteReadsPreserveCopyMoveAndCleanup()
    {
        var source = """
            struct Resource
                public var value: i32
                drop
                    require self.value == 0 else => $abort("value")
                    Console.writeLine("drop")
            struct Empty
                drop => Console.writeLine("empty")
            enum Choice<T>
                None
                Some(T)
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/u8, size: u64, kind: u32, protect: u32) -> unsafe/u8
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/u8, size: u64, kind: u32) -> i32
            func take<E>(pointer: unsafe/E) -> E
                unsafe => return *pointer
            func at<E>(pointer: unsafe/E, index: isize) -> E
                unsafe => return pointer[index]
            func reads(bytes: unsafe/u8)
                unsafe
                    let integer = take(bytes@unsafe/i32)
                    require integer == 0 else => $abort("integer")
                    require take(bytes@unsafe/bool) == false else => $abort("bool")
                    require take(bytes@unsafe/unsafe/i32) == null else => $abort("pointer")
                    let item = at(bytes@unsafe/Resource, 1)
                    let tuple = take((bytes + 32)@unsafe/(Resource, Resource))
                    let array = at((bytes + 64)@unsafe/[2 of Resource], 1)
                    let empty = take((bytes + 128)@unsafe/Empty)
                    let unit = take(bytes@unsafe/())
                    let text = take((bytes + 160)@unsafe/string)
                    let choice = take((bytes + 224)@unsafe/Choice<Resource>)
                    Console.writeLine("scope")
            public func main()
                unsafe
                    let bytes = Native.allocate(null, 4096, 12288, 4)
                    require bytes != null else => $abort("allocate")
                    reads(bytes)
                    require Native.free(bytes, 0, 32768) != 0 else => $abort("free")
                Console.writeLine("done")
            """;
        ScalarEmissionTest.EmitFixture("GenericPointerRead", source, "scope\nempty\ndrop\ndrop\ndrop\ndrop\ndrop\ndone\n");
    }

    [Theory]
    [InlineData("*pointer")]
    [InlineData("pointer[index]")]
    public void AbstractOwnedPointeeCanBeAcquired(string expression)
    {
        var c = MinimalEmissionTest.Analyze($"func take<E>(pointer: unsafe/E, index: isize) -> E\n    unsafe => return {expression}\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        CompilationTestHelper.WriteIr(c);
    }

    [Theory]
    [InlineData("ref/i32 during static")]
    [InlineData("(ref/i32 during static, i32)")]
    public void DependentInstantiationsPreserveTheCompletePointee(string type)
    {
        var c = MinimalEmissionTest.Analyze($"func take<E>(pointer: unsafe/E) -> E\n    unsafe => return *pointer\nfunc read(pointer: unsafe/({type})) => take(pointer)\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        CompilationTestHelper.WriteIr(c);
    }

    [Theory]
    [InlineData("ref/i32 during static")]
    [InlineData("(ref/i32 during static, i32)")]
    public void VerifiedTemplateBuildsDependentConcreteReads(string type)
    {
        var c = MinimalEmissionTest.Analyze($"func take<E>(pointer: unsafe/E) -> E\n    unsafe => return *pointer\nfunc input(pointer: unsafe/({type})) => ()\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var generic = c.Ownership.Bodies.Single(x => x.Function.Name == "take");
        var input = c.Ownership.Bodies.Single(x => x.Function.Name == "input");
        var element = input.Function.Parameters[0].Type.BoundType!.Components[0];
        var call = new BoundCall();
        call.Set(generic.Function.BoundSymbol!, element, null, [0], [element]);
        Assert.NotNull(c.Ownership.AnalyzeInstance(generic, call));
    }

    [Fact]
    public void AcquiredGenericValueCannotBeMovedTwice()
    {
        var c = MinimalEmissionTest.Analyze("func take<E>(pointer: unsafe/E) -> E\n    unsafe\n        let value = *pointer\n        let moved = value@move\n        return value@move\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.ErrorCount > 0);
    }
}
