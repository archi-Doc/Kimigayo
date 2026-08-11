// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;
/*
public record class IrTarget
{
    private static readonly Dictionary<string, int> PointerWidthByArchitecture;

    static TargetTriple()
    {
        PointerWidthByArchitecture = new(StringComparer.OrdinalIgnoreCase)
        {
            ["avr"] = 16,
            ["msp430"] = 16,

            ["aarch64_32"] = 32,
            ["amdil"] = 32,
            ["arc"] = 32,
            ["arm"] = 32,
            ["armeb"] = 32,
            ["csky"] = 32,
            ["dxil"] = 32,
            ["hexagon"] = 32,
            ["hsail"] = 32,
            ["kalimba"] = 32,
            ["lanai"] = 32,
            ["loongarch32"] = 32,
            ["m68k"] = 32,
            ["mips"] = 32,
            ["mipsel"] = 32,
            ["nvptx"] = 32,
            ["ppc"] = 32,
            ["ppcle"] = 32,
            ["r600"] = 32,
            ["renderscript32"] = 32,
            ["riscv32"] = 32,
            ["riscv32be"] = 32,
            ["shave"] = 32,
            ["sparc"] = 32,
            ["sparcel"] = 32,
            ["spir"] = 32,
            ["spirv32"] = 32,
            ["tce"] = 32,
            ["tcele"] = 32,
            ["thumb"] = 32,
            ["thumbeb"] = 32,
            ["wasm32"] = 32,
            ["x86"] = 32,
            ["xcore"] = 32,
            ["xtensa"] = 32,

            ["aarch64"] = 64,
            ["aarch64_be"] = 64,
            ["amdgpu"] = 64,
            ["amdil64"] = 64,
            ["bpfeb"] = 64,
            ["bpfel"] = 64,
            ["hsail64"] = 64,
            ["loongarch64"] = 64,
            ["mips64"] = 64,
            ["mips64el"] = 64,
            ["nvptx64"] = 64,
            ["ppc64"] = 64,
            ["ppc64le"] = 64,
            ["renderscript64"] = 64,
            ["riscv64"] = 64,
            ["riscv64be"] = 64,
            ["sparcv9"] = 64,
            ["spirv"] = 64,
            ["spir64"] = 64,
            ["spirv64"] = 64,
            ["tcele64"] = 64,
            ["systemz"] = 64,
            ["ve"] = 64,
            ["wasm64"] = 64,
            ["x86_64"] = 64,
        };
    }

    public bool TryCreate(TargetTriple targetTriple, out IrTarget irTarget)
    {
    }

    private IrTarget(int pointerWidth, string dataLayout)
    {
    }

    public int PointerWidth { get; }

    public string DataLayout { get; }
}

public record class TargetTriple
{
    // x86_64-pc-windows-msvc
    public static readonly TargetTriple Empty = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    public static bool TryParse(ReadOnlySpan<char> text, out TargetTriple triple)
    {
        triple = TargetTriple.Empty;
        if (text.IsEmpty)
        {
            return false;
        }

        Span<Range> parts = stackalloc Range[5];
        var count = SplitByHyphen(text, parts);
        if (count < 3)
        {
            return false;
        }

        var architecture = text[parts[0]].ToString();
        var vendor = text[parts[1]].ToString();
        var operatingSystem = text[parts[2]].ToString();
        var environment = string.Empty;
        var abi = string.Empty;

        if (count >= 4)
        {
            var fourth = text[parts[3]];

            if (IsKnownAbi(fourth))
            {
                abi = fourth.ToString();
            }
            else
            {
                environment = fourth.ToString();
            }
        }

        if (count >= 5)
        {
            abi = text[parts[4]].ToString();
        }

        triple = new TargetTriple(architecture, vendor, operatingSystem, environment, abi);
        return true;
    }

    private static int SplitByHyphen(ReadOnlySpan<char> text, Span<Range> parts)
    {
        var count = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '-')
            {
                continue;
            }

            if (i == start)
            {
                return 0; // Empty component.
            }

            if ((uint)count >= (uint)parts.Length)
            {
                return 0; // Too many components.
            }

            parts[count++] = start..i;
            start = i + 1;
        }

        if (start >= text.Length)
        {
            return 0; // Ends with '-'.
        }

        if ((uint)count >= (uint)parts.Length)
        {
            return 0;
        }

        parts[count++] = start..text.Length;
        return count;
    }

    private static bool IsKnownAbi(ReadOnlySpan<char> text)
    {
        return text.Equals("eabi", StringComparison.Ordinal) ||
            text.Equals("eabihf", StringComparison.Ordinal) ||
            text.Equals("android", StringComparison.Ordinal) ||
            text.Equals("gnuabi64", StringComparison.Ordinal) ||
            text.Equals("gnueabi", StringComparison.Ordinal) ||
            text.Equals("gnueabihf", StringComparison.Ordinal) ||
            text.Equals("ilp32", StringComparison.Ordinal) ||
            text.Equals("musleabi", StringComparison.Ordinal) ||
            text.Equals("musleabihf", StringComparison.Ordinal);
    }

    public string Architecture { get; init; }

    public string Vendor { get; init; }

    public string OperatingSystem { get; init; }

    public string Environment { get; init; }

    public string Abi { get; init; }

    public TargetTriple(string architecture, string vendor, string operatingSystem, string environment, string abi)
    {
        this.Architecture = architecture;
        this.Vendor = vendor;
        this.OperatingSystem = operatingSystem;
        this.Environment = environment;
        this.Abi = abi;

        this.PointerWidth = 64;
        if (PointerWidthByArchitecture.TryGetValue(architecture, out var pointerWidth))
        {
            this.PointerWidth = pointerWidth;
        }
    }

    public override string ToString()
    {
        if (this.Abi.Length > 0)
        {
            return $"{this.Architecture}-{this.Vendor}-{this.OperatingSystem}-{this.Environment}-{this.Abi}";
        }

        if (this.Environment.Length > 0)
        {
            return $"{this.Architecture}-{this.Vendor}-{this.OperatingSystem}-{this.Environment}";
        }

        return $"{this.Architecture}-{this.Vendor}-{this.OperatingSystem}";
    }
}*/
