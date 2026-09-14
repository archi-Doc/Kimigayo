// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using BenchmarkDotNet.Attributes;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;

namespace Benchmark;

[Config(typeof(BenchmarkConfig))]
public class DirectiveBenchmark
{
    public const string IfSource = """
        func selected() -> i32
            #if windows and (pointerWidth == 64 or debug)
                #if os == "windows" and not linux
                    return 1
            #if linux
                var incomplete =
            return 0
        """;

    public const string SwitchSource = """
        func selected() -> i32
            #switch
                #case linux
                    return 0
                #case windows and pointerWidth == 64
                    #if not debug or release
                        return 1
                    return 2
                #case macos
                    return 3
                #case _
                    return 4
        """;

    private readonly Compilation compilation = Compilation.CreateForTest(true);
    private string source = IfSource;

    [Params(false, true)]
    public bool Switch { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        this.source = this.Switch ? SwitchSource : IfSource;
        if (!this.compilation.Prepare("x86_64-pc-windows-msvc"))
        {
            throw new InvalidOperationException("Benchmark environment must be prepared.");
        }

        var validation = Compilation.CreateForTest();
        validation.Prepare("x86_64-pc-windows-msvc");
        validation.Kotonoha.CreateCodeContext().Parse(validation.Kotonoha.RootKoto, this.source);
        if (validation.Kotonoha.DiagnosticCollection.GetArray().Length != 0)
        {
            throw new InvalidOperationException("Benchmark source must parse without diagnostics.");
        }
    }

    [Benchmark]
    public Koto TokenizeAndParse()
    {
        var kotonoha = this.compilation.Kotonoha;
        kotonoha.CreateCodeContext().Parse(kotonoha.RootKoto, this.source);
        kotonoha.RootKoto.Clear();
        return kotonoha.RootKoto;
    }
}
