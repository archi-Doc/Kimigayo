// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using BenchmarkDotNet.Attributes;
using Kimi.Compiler.Helper;

namespace Benchmark;

[Config(typeof(BenchmarkConfig))]
public class NumberLiteralBenchmark
{
    private readonly string doubleString = "1.7976_931348_6237E+38";
    private readonly string i128String = "111_282_366";
    // private readonly string i128String2 = "111_282_366_920_938_463_463_374_607_431_768_211_456";
    private readonly string bString = "0b1110_1001_0011_0101_1011_1011_0000_1101";
    private readonly string hString = "0x1234_abCD_4567";
    private readonly string oString = "0o_1234_4567_4567_7654";

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
    public Int128 TryParseFloat()
    {
        NumberLiteralHelper.ParseNumberLiteral(this.doubleString, out var i128);
        return i128;
    }

    [Benchmark]
    public Int128 TryParseInteger()
    {
        NumberLiteralHelper.ParseNumberLiteral(this.i128String, out var i128);
        return i128;
    }

    [Benchmark]
    public Int128 TryParseBinary()
    {
        NumberLiteralHelper.ParseNumberLiteral(this.bString, out var i128);
        return i128;
    }

    [Benchmark]
    public Int128 TryParseHex()
    {
        NumberLiteralHelper.ParseNumberLiteral(this.hString, out var i128);
        return i128;
    }

    [Benchmark]
    public Int128 TryParseOct()
    {
        NumberLiteralHelper.ParseNumberLiteral(this.oString, out var i128);
        return i128;
    }
}
