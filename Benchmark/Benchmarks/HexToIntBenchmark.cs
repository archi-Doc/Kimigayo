// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;

namespace Benchmark;

[Config(typeof(BenchmarkConfig))]
public class HexToIntBenchmark
{
    private static readonly sbyte[] HexTable = CreateHexTable();
    private char x = 'A';
    private char y = '4';
    private char z = 'Z';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToHexValue(char c)
    {
        return c < 128 ? HexTable[c] : -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int HexToInt(char c)
    {
        var value = c - '0';
        if ((uint)value <= 9)
        {
            return value;
        }

        value = (c | 0x20) - 'a';
        return (uint)value <= 5 ? value + 10 : -1;
    }

    private static sbyte[] CreateHexTable()
    {
        var table = new sbyte[128];
        for (int i = 0; i < 128; i++)
        {
            table[i] = -1;
        }

        for (int i = '0'; i <= '9'; i++)
        {
            table[i] = (sbyte)(i - '0');
        }

        for (int i = 'a'; i <= 'f'; i++)
        {
            table[i] = (sbyte)(i - 'a' + 10);
        }

        for (int i = 'A'; i <= 'F'; i++)
        {
            table[i] = (sbyte)(i - 'A' + 10);
        }

        return table;
    }

    public HexToIntBenchmark()
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
    public int Table()
    {
        var r = ToHexValue(this.x) + ToHexValue(this.y) + ToHexValue(this.z);
        this.x = (char)r;
        return r;
    }

    [Benchmark]
    public int Calc()
    {
        var r = HexToInt(this.x) + HexToInt(this.y) + HexToInt(this.z);
        this.x = (char)r;
        return r;
    }
}
