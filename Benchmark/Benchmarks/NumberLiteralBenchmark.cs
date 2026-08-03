// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using BenchmarkDotNet.Attributes;
using Kimi.Compiler.Lexing;

namespace Benchmark;

[Config(typeof(BenchmarkConfig))]
public class NumberLiteralBenchmark
{
    [Params(10)]
    public int Length { get; set; }

    private readonly string doubleString = "1.7976_931348_6237E+38";

    public NumberLiteralBenchmark()
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
    public Int128 Test1()
    {
        TokenHelper.ParseNumberLiteral(this.doubleString, out var kind, out var i128, out var f64);
        i128 = BitConverter.DoubleToUInt64Bits(f64);
        return i128;
    }

    [Benchmark]
    public Int128 Test2()
    {
        TokenHelper.TryParseNumberLiteral(this.doubleString, out var i128);
        return i128;
    }
}
