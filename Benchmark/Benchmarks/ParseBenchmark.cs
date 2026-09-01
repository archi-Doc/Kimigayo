// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;

namespace Benchmark;

#pragma warning disable SA1118 // Parameter should not span multiple lines

[Config(typeof(BenchmarkConfig))]
public class ParseBenchmark
{
    private readonly Compilation compilation;
    private readonly string sourceText = $"""
            alias Kimi.Crypto
            alias Kimi.LowLevelInterface

            // Single-line comment
            /* Multi-line
               comment */

            rootgroup Playground.A

            // Type Semantics = s/T
            // owner, borrow, stack, ownerref, borrowref, rc, arc, unsafe,
            public struct Array<s/T>
                Self is all
                s is owning
                T is Comparable

                var count: isize
                var capacity: isize
                var buffer: ptr

            var array = Array<owner/StructA>.new()
            array[1] = StructA.new()
            var x = array[1] // borrow/StructA
            var last = array[^1] // Last element
            var middle = array[1..^1] // Excludes both element 0 and the last element
            var y = array.remove(at: 1) // owner/StructA
            func Set(index: isize, obj: s/T) -> ()
            func Get(index: isize) -> s/T

            var items: Array<Int> = [1, 2, 3]
            var items2 = [1, 2, 3, ]
            var map: Map<String, Int> = ["A": 1, "B": 2]
            var array: Array<owner/T> = new()

            #Description("Kernel32 helper")
            public group Kernel32 // shared (no instance)
                public let libraryName: string = "Kernel32.dll"
                public let count = 1 + 2 + 3 + 4 + 5 // readonly
                #LibraryImport(LibraryName) public func GetStdHandle(nStdHandle: u32) -> ptr

            public group Helper // namespace - alias
                public let Id: i32 = 123
                public func Method1() -> int32 // use PackageName, Helper
                    return 1

                func Method2() ->
                    #Condition(Os=="Windows")
                    var i = if (x == true) 1 else 0
                    var i2 = if (x == true)
                        1
                    else
                        yield 3

                    var j = match x
                        true => 1
                        false => 0
                    var k = match x
                        true =>
                            1
                        false =>
                            0
                    return
            """;

    private readonly string sourceText2 = $"""
            #If (true)
            public struct TestStruct: @Ia
                let x = 1
            #If (true)
            public struct TestStruct2: @Ib
                let x = 1
            #If (true)
            public struct TestStruct3: @Ic
                let x = 1
            #If (true)
            public struct TestStruct4: @Id
                let x = 1
            """;

    public ParseBenchmark()
    {
        this.compilation = Compilation.CreateForTest();
    }

    [GlobalSetup]
    public void Setup()
    {
    }

    [GlobalCleanup]
    public void Cleanup()
    {
    }

    [Benchmark]
    public Koto Test1()
    {
        var kotonoha = this.compilation.Kotonoha;
        var codeContext = kotonoha.CreateCodeContext();
        codeContext.Parse(kotonoha.RootKoto, this.sourceText);

        kotonoha.RootKoto.Clear();

        return kotonoha.RootKoto;
    }
}
