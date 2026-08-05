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
    [Params(10)]
    public int Length { get; set; }

    public ParseBenchmark()
    {
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
        var compilation = Compilation.CreateForTest();
        var kotonoha = new Kotonoha(compilation);
        var codeContext = kotonoha.CreateCodeContext();
        codeContext.Parse(kotonoha.RootKoto, $"""
            #If (true)
            public struct TestStruct: @Ia
                let x = 1
            """);

        return kotonoha.RootKoto;
    }
}
