// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

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
            alias Playground.A

            // Single-line comment
            /* Multi-line
               comment */

            public rootgroup Playground.A
                public struct StructA
                    public var value: i32 = 0

                #if windows
                /// Kernel32 helper.
                public group Kernel32
                    public let count: i32 = 1 + 2 + 3 + 4 + 5

                    #LibraryImport("kernel32", "GetStdHandle")
                    public func getStdHandle(nStdHandle: u32) -> raw/()

                public group Helper
                    public let id: i32 = 123

                    public func set<T>(array: uniq/Array<T>, index: isize, obj: T)
                        array[index] = obj@move

                    public func get<T>(array: ref/Array<T>, index: isize) -> ref/T during array
                        return array[index]

                    public func method1() -> i32 => 1

                    public func method2(x: bool)
                        #if windows
                            let i = if x => 1 else => 0
                            let i2 = if x
                                yield 1
                            else
                                yield 3

                            let j = match x
                                true => 1
                                false => 0
                            let k = match x
                                true => 1
                                false
                                    yield 0
                        return

            var array: Array<StructA> = [StructA.init(), StructA.init(), StructA.init()]
            array[1] = StructA.init()

            do
                let x: ref/StructA = array[1]
                let last: ref/StructA = array[^1]
                let middle: Slice<StructA> = array[1..^1]

            let y: StructA = array.remove(1)

            let items: Array<i32> = [1, 2, 3]
            let items2 = [1, 2, 3,]
            let map: Dictionary<string, i32> = ["A": 1, "B": 2]
            var emptyArray: Array<StructA> = []

            Helper.set(array@uniq, 0, StructA.init())
            let first: ref/StructA = Helper.get(array, 0)
            Helper.method2(true)
            """;

    public ParseBenchmark()
    {
        this.compilation = Compilation.CreateForTest();
        if (!this.compilation.Prepare("x86_64-pc-windows-msvc"))
        {
            throw new InvalidOperationException("Benchmark environment must be prepared.");
        }
    }

    /// <summary>Rejects invalid input and checks repeated parsing before collecting measurements.</summary>
    [GlobalSetup]
    public void Setup()
    {
        for (var i = 0; i < 128; i++)
        {
            this.Test1();
        }

        this.Validate();
    }

    /// <summary>Checks that parsing succeeded and no per-invocation source or syntax remains retained.</summary>
    [GlobalCleanup]
    public void Validate()
    {
        var kotonoha = this.compilation.Kotonoha;
        if (this.compilation.Diagnostics.HasErrors || kotonoha.SourceDocuments.Count != 0 ||
            kotonoha.GeneratedFunction is not null || kotonoha.RootKoto.Members.Count != 0 || kotonoha.RootKoto.NestedContainers.Count != 0)
        {
            throw new InvalidOperationException("Parsing must succeed without retaining source snapshots or syntax between invocations.");
        }
    }

    [Benchmark]
    public Koto Test1()
    {
        var kotonoha = this.compilation.Kotonoha;
        var source = new SourceDocument("ParseBenchmark.kimi", this.sourceText);
        var codeContext = new CodeContext(kotonoha, sourceDocument: source);
        var tokenizer = new Tokenizer(codeContext.DiagnosticCollection, source);
        try
        {
            // Measure lexing and parsing, without adding each invocation to the module's serialization history.
            this.compilation.BeginSourceParsing(kotonoha);
            tokenizer.ReadAll();
            var reader = new TokenReader(codeContext, ref tokenizer);
            kotonoha.RootKoto.Parse(ref reader);
            return kotonoha.RootKoto;
        }
        finally
        {
            kotonoha.RootKoto.Clear();
            tokenizer.Dispose();
        }
    }
}
