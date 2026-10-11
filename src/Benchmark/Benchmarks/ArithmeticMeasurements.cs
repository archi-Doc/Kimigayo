// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Verification;

namespace Benchmark;

internal static class ArithmeticMeasurements
{
    internal static void WriteNativeSources(string directory)
    {
        Directory.CreateDirectory(directory);
        var encoding = new UTF8Encoding(false);
        foreach (var name in ArithmeticWorkloads.Names)
        {
            File.WriteAllText(Path.Combine(directory, name + ".kimi"), ArithmeticWorkloads.Create(name, ArithmeticWorkloads.NativeIterations), encoding);
        }
    }
}
