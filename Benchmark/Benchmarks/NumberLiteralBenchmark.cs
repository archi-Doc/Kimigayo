// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using BenchmarkDotNet.Attributes;
using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;

namespace Benchmark;

[Config(typeof(BenchmarkConfig))]
public class NumberLiteralBenchmark
{
    private const string Suffix = "#abc";
    private readonly string doubleString = "1.7976_931348_6237E+38";
    private readonly string i128String = "111_282_366";
    private readonly string bString = "0b1110_1001_0011_0101_1011_1011_0000_1101";
    private readonly string hString = "0x1234_abCD_4567";
    private readonly string oString = "0o_1234_4567_4567_7654";
    private readonly string doubleString2;
    private readonly string i128String2 = "111_282_366";
    private readonly string bString2 = "0b1110_1001_0011_0101_1011_1011_0000_1101";
    private readonly string hString2 = "0x1234_abCD_4567";
    private readonly string oString2 = "0o_1234_4567_4567_7654";

    public NumberLiteralBenchmark()
    {
        this.doubleString2 = this.doubleString + Suffix;
        this.i128String2 = this.i128String + Suffix;
        this.bString2 = this.bString + Suffix;
        this.hString2 = this.hString + Suffix;
        this.oString2 = this.oString + Suffix;
    }

    [GlobalSetup]
    public void Setup()
    {
    }

    [GlobalCleanup]
    public void Cleanup()
    {
    }

    /*[Benchmark]
    public int ScanFloat()
    {
        TokenHelper.ScanNumberLiteral(this.doubleString2, out var length);
        return length;
    }

    [Benchmark]
    public int ScanInteger()
    {
        TokenHelper.ScanNumberLiteral(this.i128String2, out var length);
        return length;
    }

    [Benchmark]
    public int ScanBinary()
    {
        TokenHelper.ScanNumberLiteral(this.bString2, out var length);
        return length;
    }

    [Benchmark]
    public int ScanHex()
    {
        TokenHelper.ScanNumberLiteral(this.hString2, out var length);
        return length;
    }

    [Benchmark]
    public int ScanOct()
    {
        TokenHelper.ScanNumberLiteral(this.oString2, out var length);
        return length;
    }*/

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

    /*[Benchmark]
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
    }*/
}
