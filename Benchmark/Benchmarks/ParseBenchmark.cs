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
            #If (true)
            public struct TestStruct: @Ia
                let x = 1
            #If (true)
            public struct TestStruct2: @Ib
                let x = 1
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
        codeContext.Parse(kotonoha.RootKoto, this.sourceText2);

        kotonoha.RootKoto.Clear();

        return kotonoha.RootKoto;
    }

    // [Benchmark]
    public void Test2()
    {
        var kotonoha = this.compilation.Kotonoha;
        var codeContext = kotonoha.CreateCodeContext();
        codeContext.Test(kotonoha.RootKoto, this.sourceText);
    }
}
