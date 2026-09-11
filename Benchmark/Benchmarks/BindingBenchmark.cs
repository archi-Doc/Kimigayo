// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using BenchmarkDotNet.Attributes;
using Kimi.Compiler;

namespace Benchmark;

/// <summary>Measures Binding independently of parsing and the reserved Mod boundary.</summary>
[Config(typeof(BenchmarkConfig))]
public class BindingBenchmark
{
    private Compilation compilation = null!;

    /// <summary>Gets or sets the number of calls whose existing syntax is rebound.</summary>
    [Params(32, 512)]
    public int Calls { get; set; }

    /// <summary>Gets or sets the declaration and call workload.</summary>
    [Params("Calls", "Origins", "Capabilities", "Contracts", "Properties", "ConditionalConformances")]
    public string Scenario { get; set; } = "Calls";

    /// <summary>Creates and validates syntax and warms reusable semantic storage.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var source = new StringBuilder(this.Scenario switch
        {
            "Origins" => "struct View<T> origin a, b\n    let first: ref/T from a\n    let second: ref/T from b\n",
            "Capabilities" => "struct Box<T>\n    Self is Copy when T is Copy\n    let value: T\nvar input: Box<i32>\nfunc identity<T>(value: T) -> T\n    T is Copy and Owned\n    return value\n",
            "Contracts" => "contract Source\n    associate Element\n    func read(self: ref/Self) -> Element\ncontract IntSource: Source\n    Self.Source.Element is i32\nstruct SourceImpl\n    Self is IntSource\n    public func read(self: ref/Self) -> i32 => 1\nfunc use<T>(value: ref/T)\n    T is IntSource\n",
            "Properties" => "contract C\n    property item: i32 has get, set\n    property view: ref/i32 has get\n",
            "ConditionalConformances" => "contract A\n    associate E is i32\n    property item: E has get\ncontract B: A\n",
            _ => "func identity<T>(value: T) -> T => value\n",
        });
        for (var i = 0; i < this.Calls; i++)
        {
            if (this.Scenario == "ConditionalConformances")
            {
                source.Append("struct S").Append(i).Append("<T>\n    Self is A when T is Copy\n    Self is B when T is Owned\n    public var item: i32\n");
            }
            else if (this.Scenario == "Properties")
            {
                source.Append("struct S").Append(i).Append("\n    Self is C\n    public var view: i32\n    public var item: i32\n        get(self: ref/Self) -> i32 => storage\n        set(self: uniq/Self, value: i32) -> () => storage = value\n");
            }
            else if (this.Scenario == "Origins")
            {
                source.Append("func function").Append(i).Append(" origin a, b(x: View<i32> from (a => a, b => b), y: ref/(ref/i32 from a) from b) => ()\n");
            }
            else if (this.Scenario == "Capabilities")
            {
                source.Append("let result").Append(i).Append(" = identity(input)\n");
            }
            else if (this.Scenario == "Contracts")
            {
                source.Append("    value.read()\n");
            }
            else
            {
                source.Append("let result").Append(i).Append(" = identity(").Append(i).Append(")\n");
            }
        }

        this.compilation = Compilation.CreateForTest();
        if (!this.compilation.Prepare("x86_64-pc-windows-msvc"))
        {
            throw new InvalidOperationException("Benchmark environment must be prepared.");
        }

        this.compilation.Kotonoha.CreateCodeContext().Parse(this.compilation.Kotonoha.RootKoto, source.ToString());
        if (this.compilation.Kotonoha.DiagnosticCollection.GetArray().Length != 0 || !this.compilation.Bind().IsComplete)
        {
            throw new InvalidOperationException("Benchmark source must bind successfully.");
        }
    }

    /// <summary>Measures a complete semantic reset, declaration pass, and final Bind/check.</summary>
    /// <returns>The pass summary.</returns>
    [Benchmark]
    public BindingResult FinalBind() => this.compilation.Binding.Bind(BindingMode.Final);

    /// <summary>Measures the provisional-to-final pipeline on the same tree.</summary>
    /// <returns>The final summary.</returns>
    [Benchmark]
    public BindingResult Pipeline() => this.compilation.Bind();
}
