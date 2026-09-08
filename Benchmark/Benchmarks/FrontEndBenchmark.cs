// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using BenchmarkDotNet.Attributes;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;

namespace Benchmark;

[Config(typeof(BenchmarkConfig))]
public class FrontEndBenchmark
{
    public const string CommonSource = """
        group Collections
            struct Buffer<s/T>
                T is Comparable
                var count: i32
                func get(index: i32) -> T
                    if index < 0
                        return fallback
                    return values[index]
        func transform<T>(value: T) -> T
            T is Comparable
            let pair: (T, T) = (value, value)
            let text = "result: \(pair.0)"
            return value
        let values = [1, 2, 3, 4]
        let result = transform<i32>(values[1])
        """;

    public const string ExtendedSource = """
        enum Option<T>
            None
            Some(T)
        contract Sequence : Base
            associate Element
            func next(self: ref/Self) -> Element
            var count: i32 has get
        open struct Parent
        struct Child : Parent
            init(value: i32) : base(value)
                return
            deinit
                return
        func transform<length N, T>(values: [N of T]) -> [N of T]
            T is Comparable
            require valid else return fallback
            let visit = func[values@ref, var count@move](x) => x
            let value = match choice
                .Some(let x) if ready => x
                .None => 0
                _ => -1
            return values
        specialize func select<i32>(value: i32) => value
        let result: [(N + 1) of _] = transform<4, i32>([1, 2, 3, 4])
        """;

    private readonly Compilation compilation = Compilation.CreateForTest(true);
    private string source = CommonSource;

    [Params(false, true)]
    public bool Extended { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        this.source = this.Extended ? ExtendedSource : CommonSource;
        var validation = Compilation.CreateForTest().Kotonoha;
        var context = validation.CreateCodeContext();
        context.Parse(validation.RootKoto, this.source);
        if (context.DiagnosticCollection.GetArray().Length != 0)
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
