// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// The shared acceptance tests of G158 (3) (WORKFLOW Q11; the owner's order of 2026-10-26), written from the problem statement before either
// candidate: SPEC 6.7.1 (C layout is unavailable for an open or derived struct, an empty struct and a struct with a zero-sized Field; a use
// that needs such a layout is a Language error), 8.15.3 (checked where the program stores, creates, passes or returns a value of the Type by
// value or needs its size, over the whole checked set; not behind a borrow, raw pointer or object handle, nor in a Constraint; the error is
// at the use that needs the layout), 23.1.2 (each independent failure is reported; grouping is implementation-defined), 23.2.2 (check reports
// every Language error of the checked set, layouts included), 22.6 (a struct by value in an import, and ref/T over a Type that is not
// C-exchangeable, are Unsupported) and DECISIONS R4-M134 Q8 (Binding's declaration checks are conservative Language errors). Each row names
// its form and states every Error record of the program at check, the same on every pipeline (DECISIONS R5-M156), as `Code use`: the use is a text occurring once
// in the source (a local's declaration, the function whose parameter or result passes or returns the value, the call whose argument passes
// it, a raw read's declaration, an import's declaration, a Constraint-free written Type), and a record is at that use when its primary span
// starts within the text, so a parameter's record at the parameter (SPEC 8.15.3) or at its function (DECISIONS R5-M44) both pass. Each named
// use takes exactly one record of its code (`Code+ use` one or more, where SPEC 23.1.2 lets one function's two obligations group) and no
// other Error record exists, so the declaration side (the attribute, the Field, the struct, a written instance behind a borrow or raw pointer)
// has none and no use is reported twice. The twins without the unavailable layout keep their verdicts. A row that fails on dev fails by design (the commit message counts them per theory).
public partial class LayoutAttributeBindingTest
{
    private const string GenericP = "#Layout(\"C\")\nstruct P<T>\n    var n: i32\n    var value: T\n";

    private const string GenericInit = "#Layout(\"C\")\nstruct P<T>\n    public var n: i32\n    public var value: T\n" +
        "    public init(n: i32, value: T)\n        self.n = n\n        self.value = value@move\n";

    private const string ImportHead = "group Native\n    #LibraryImport(\"kernel32\", \"VirtualAlloc\")\n    public unsafe func imported(value: ";

    // The declarations whose C layout is unavailable (A: open, derived, empty; B: a direct zero-sized Field; W: a written instance of P<T>
    // storing one), the Type text X of their uses, and their twins (no #Layout("C"); P<i32>, P<[1 of i32]>).
    private static readonly (string Name, string Declaration, string Type, string TwinDeclaration, string TwinType)[] Unavailable =
    [
        ("open", "#Layout(\"C\")\nopen struct S\n    var n: i32\n", "S", "open struct S\n    var n: i32\n", "S"),
        ("derived", "open struct B\n    var n: i32\n#Layout(\"C\")\nstruct S: B\n    var m: i32\n", "S", "open struct B\n    var n: i32\nstruct S: B\n    var m: i32\n", "S"),
        ("empty", "#Layout(\"C\")\nstruct S\n", "S", "struct S\n", "S"),
        ("unit", "#Layout(\"C\")\nstruct S\n    var n: i32\n    var u: ()\n", "S", "struct S\n    var n: i32\n    var u: ()\n", "S"),
        ("zstruct", "struct Z\n#Layout(\"C\")\nstruct S\n    var n: i32\n    var z: Z\n", "S", "struct Z\nstruct S\n    var n: i32\n    var z: Z\n", "S"),
        ("zarray", "#Layout(\"C\")\nstruct S\n    var n: i32\n    var a: [0 of i32]\n", "S", "struct S\n    var n: i32\n    var a: [0 of i32]\n", "S"),
        ("w-unit", GenericP, "P<()>", GenericP, "P<i32>"),
        ("w-zstruct", "struct Z\n" + GenericP, "P<Z>", "struct Z\n" + GenericP, "P<i32>"),
        ("w-zarray", GenericP, "P<[0 of i32]>", GenericP, "P<[1 of i32]>"),
    ];

    // The declarations storing Never (C: a Never Field; WN: P<Never>), whose twins' by-value uses are a limit outside G158 (Unsupported_Kd).
    private static readonly (string Name, string Declaration, string Type, string TwinDeclaration, string TwinType)[] Never =
    [
        ("never", "#Layout(\"C\")\nstruct S\n    var n: i32\n    var v: Never\n", "S", "struct S\n    var n: i32\n    var v: Never\n", "S"),
        ("w-never", GenericP, "P<Never>", GenericP, "P<i32>"),
    ];

    // The uses over X (the problem statement's rows): the records SPEC 8.15.3 gives an unavailable layout (Uses) and the records a Never
    // Field's limit gives every by-value use of such a Type, with or without the Attribute (Limits).
    private static readonly (string Name, string Body, string Uses, string Limits)[] Contexts =
    [
        ("none", "let x = 1\n", string.Empty, string.Empty),
        ("local", "func f() -> i32\n    var s: X\n    return 0\n", "InvalidCLayout_Kd var s: X", "Unsupported_Kd var s: X"),
        ("param", "func f(s: X) => ()\n", "InvalidCLayout_Kd func f(s: X) => ()", "Unsupported_Kd func f(s: X) => ()"),
        ("ret", "func f() -> X => $abort(\"x\")\n", "InvalidCLayout_Kd func f() -> X => $abort(\"x\")", "Unsupported_Kd func f() -> X => $abort(\"x\")"),
        ("farray", "func f(a: [2 of X]) => ()\n", "InvalidCLayout_Kd func f(a: [2 of X]) => ()", "Unsupported_Kd func f(a: [2 of X]) => ()"),
        ("array", "func f() -> i32\n    let a: Array<X> = []\n    return 0\n", "InvalidCLayout_Kd let a: Array<X> = []", "Unsupported_Kd let a: Array<X> = []|Unsupported_Kd []"),
        (
            "arg",
            "func make() -> X => $abort(\"x\")\nfunc take(s: X) => ()\nfunc f() => take(make())\n",
            "InvalidCLayout_Kd func make() -> X => $abort(\"x\")|InvalidCLayout_Kd func take(s: X) => ()|InvalidCLayout_Kd take(make())",
            "Unsupported_Kd func make() -> X => $abort(\"x\")|Unsupported_Kd func take(s: X) => ()|Unsupported_Kd take(make())"),
        ("rawread", "func f(p: raw/X)\n    unsafe\n        let value = (*p)@move\n", "InvalidCLayout_Kd let value = (*p)@move", "Unsupported_Kd let value = (*p)@move|Unsupported_Kd *p"),
        ("fieldunused", "#Layout(\"C\")\nstruct H\n    var k: i32\n    var s: X\nlet x = 1\n", string.Empty, string.Empty),
        ("fieldused", "#Layout(\"C\")\nstruct H\n    var k: i32\n    var s: X\nfunc f(h: H) => ()\n", "InvalidCLayout_Kd func f(h: H) => ()", "Unsupported_Kd func f(h: H) => ()"),
        ("borrow", "func f(s: ref/X) => ()\n", string.Empty, string.Empty),
        ("raw", "func f(p: raw/X) => ()\n", string.Empty, string.Empty),
    ];

    // Inferred instances P<T>, T := (), and the uses that need no layout (a borrow, a raw pointer, an uninstantiated body), each with its
    // twin (T := an integer); the records follow dev's class for the same failure under sel (the problem statement's §2).
    private static readonly (string Name, string Source, string Records, string Twin)[] Inferred =
    [
        ("i-local", GenericP + "func make<T>(value: T) -> i32\n    var p: P<T>\n    return 0\nlet r = make(())\n", "InvalidCLayout_Kd var p: P<T>", GenericP + "func make<T>(value: T) -> i32\n    var p: P<T>\n    return 0\nlet r = make(5)\n"),
        (
            "i-init",
            GenericInit + "func make<T>(value: T) -> i32\n    let p = P<T>.init(1, value@move)\n    return p.n\nlet r = make(())\n",
            "InvalidCLayout_Kd let p = P<T>.init(1, value@move)",
            GenericInit + "func make<T>(value: T) -> i32\n    let p = P<T>.init(1, value@move)\n    return p.n\nlet r = make(5)\n"),
        ("i-toplevel", GenericInit + "let q = P.init(1, ())\n", "InvalidCLayout_Kd let q = P.init(1, ())", GenericInit + "let q = P.init(1, 2)\n"),
        (
            "i-option",
            GenericP + "func g<T>(value: T) -> i32\n    let x: Option<P<T>> = .None\n    return 0\nlet r = g(())\n",
            "InvalidCLayout_Kd let x: Option<P<T>> = .None|InvalidCLayout_Kd .None",
            GenericP + "func g<T>(value: T) -> i32\n    let x: Option<P<T>> = .None\n    return 0\nlet r = g(1)\n"),
        ("i-param", GenericP + "func pass<T>(p: P<T>) => ()\nfunc g<T>(value: T) -> i32\n    let f = pass<T>\n    return 0\nlet r = g(())\n", "InvalidCLayout_Kd func pass<T>(p: P<T>) => ()", GenericP + "func pass<T>(p: P<T>) => ()\nfunc g<T>(value: T) -> i32\n    let f = pass<T>\n    return 0\nlet r = g(1)\n"),
        ("i-borrow", GenericP + "func g<T>(value: T) -> i32\n    let x: Option<ref/P<T>> = .None\n    return 0\nlet r = g(())\n", string.Empty, GenericP + "func g<T>(value: T) -> i32\n    let x: Option<ref/P<T>> = .None\n    return 0\nlet r = g(1)\n"),
        ("i-raw", GenericP + "func g<T>(value: T) -> i32\n    var p: raw/P<T> = null\n    return 0\nlet r = g(())\n", string.Empty, GenericP + "func g<T>(value: T) -> i32\n    var p: raw/P<T> = null\n    return 0\nlet r = g(1)\n"),
        ("i-unused", GenericP + "func g<T>(value: T) -> i32\n    var p: P<T>\n    return 0\nlet x = 1\n", string.Empty, GenericP + "func g<T>(value: T) -> i32\n    var p: P<T>\n    return 0\nlet x = g(1)\n"),
    ];

    // The written forms of P<()> only: a Constraint names the Type without a use; a Tuple component and an Option payload are uses.
    private static readonly (string Name, string Body, string Records)[] WrittenOnly =
    [
        ("w-constraint", "func g<T>() -> i32\n    T is X\n    return 0\n", string.Empty),
        ("w-nested", "func f(p: (X, i32)) => ()\n", "InvalidCLayout_Kd func f(p: (X, i32)) => ()"),
        ("w-option", "let x: Option<X> = .None\n", "InvalidCLayout_Kd let x: Option<X> = .None|InvalidCLayout_Kd .None"),
    ];

    // SPEC 6.7.1, 8.15.3: a declaration whose C layout is unavailable is no error until a use needs the layout: declared only, behind a
    // borrow or a raw pointer, a Field of a C-layout struct nothing uses by value, named in a Constraint, an inferred instance behind a borrow
    // or raw pointer or in a body nothing instantiates. Binding completes and no Error is published.
    public static TheoryData<string, string, string> AcceptedRows
    {
        get
        {
            var rows = new TheoryData<string, string, string>();
            foreach (var shape in Unavailable)
            {
                foreach (var context in Contexts.Where(static x => x.Uses.Length == 0))
                {
                    rows.Add($"{shape.Name}.{context.Name}", Program(shape.Declaration, context.Body, shape.Type), string.Empty);
                }
            }

            foreach (var shape in Unavailable.Where(static x => x.Name.StartsWith("w-", StringComparison.Ordinal)))
            {
                rows.Add($"{shape.Name}.constraint", Program(shape.Declaration, WrittenOnly[0].Body, shape.Type), string.Empty);
            }

            foreach (var inferred in Inferred.Where(static x => x.Records.Length == 0))
            {
                rows.Add(inferred.Name, inferred.Source, string.Empty);
            }

            return rows;
        }
    }

    // SPEC 8.15.3: each use that stores, creates, passes or returns a value of such a Type by value, or needs its size, is InvalidCLayout_Kd
    // at that use: a local, a parameter, a result, a fixed-array parameter, an empty Array local's element, a call's result, parameter and
    // argument (three independent records, SPEC 23.1.2), a move out of a raw read, the parameter of a C-layout struct storing the Type (the
    // struct's Field alone is none), a Tuple component, an Option payload with the .None that makes it; ref/X in an import is 22.6's
    // Unsupported_Kd alone; IMPL 21.1.2's keep is reported at keep.
    public static TheoryData<string, string, string> UseRows
    {
        get
        {
            var rows = new TheoryData<string, string, string>();
            foreach (var shape in Unavailable)
            {
                foreach (var context in Contexts.Where(static x => x.Uses.Length > 0))
                {
                    rows.Add($"{shape.Name}.{context.Name}", Program(shape.Declaration, context.Body, shape.Type), context.Uses.Replace("X", shape.Type, StringComparison.Ordinal));
                }

                rows.Add($"{shape.Name}.importref", Program(shape.Declaration, ImportHead + "ref/X) -> i32\n", shape.Type), "Unsupported_Kd ref/" + shape.Type);
            }

            foreach (var shape in Unavailable.Where(static x => x.Name.StartsWith("w-", StringComparison.Ordinal)))
            {
                foreach (var written in WrittenOnly.Where(static x => x.Records.Length > 0))
                {
                    rows.Add($"{shape.Name}.{written.Name[2..]}", Program(shape.Declaration, written.Body, shape.Type), written.Records.Replace("X", shape.Type, StringComparison.Ordinal));
                }
            }

            rows.Add(
                "impl-keep",
                "#Layout(\"C\")\nstruct Tagged\n    var tag: u8\n    var nothing: ()\nfunc keep(value: Tagged) -> Tagged => value@move\nlet zz = 1\n",
                "InvalidCLayout_Kd+ func keep(value: Tagged) -> Tagged => value@move");
            return rows;
        }
    }

    // SPEC 8.15.3 (the checked set): an inferred instance storing a zero-sized Field is InvalidCLayout_Kd at each use on every pipeline: a
    // generic body's local, its constructor call, a top-level constructor call, an Option local with
    // the .None that makes it, a function Item.
    public static TheoryData<string, string, string> InferredRows
    {
        get
        {
            var rows = new TheoryData<string, string, string>();
            foreach (var inferred in Inferred.Where(static x => x.Records.Length > 0))
            {
                rows.Add(inferred.Name, inferred.Source, inferred.Records);
            }

            return rows;
        }
    }

    // The twins keep today's verdicts: accepted, but a struct by value in an import (22.6, every twin), ref/S of a Kimigayo struct (22.6)
    // and each by-value use of a struct storing Never (a limit outside G158). The empty twin as a Field makes H itself store a zero-sized
    // Field (the problem statement's §2), so H's twin rows are B rows: accepted unused, InvalidCLayout_Kd at f's parameter when used.
    public static TheoryData<string, string, string> TwinRows
    {
        get
        {
            var rows = new TheoryData<string, string, string>();
            foreach (var shape in Unavailable.Concat(Never))
            {
                foreach (var context in Contexts)
                {
                    var records = shape.Name switch
                    {
                        "never" => context.Limits,
                        "empty" when context.Name.StartsWith("field", StringComparison.Ordinal) => context.Uses,
                        _ => string.Empty,
                    };
                    rows.Add($"{shape.Name}.{context.Name}.twin", Program(shape.TwinDeclaration, context.Body, shape.TwinType), records.Replace("X", shape.TwinType, StringComparison.Ordinal));
                }

                rows.Add($"{shape.Name}.import.twin", Program(shape.TwinDeclaration, ImportHead + "X) -> i32\n", shape.TwinType), "Unsupported_Kd " + ImportHead + shape.TwinType + ") -> i32");
                rows.Add($"{shape.Name}.importref.twin", Program(shape.TwinDeclaration, ImportHead + "ref/X) -> i32\n", shape.TwinType), shape.TwinType[0] == 'P' ? string.Empty : "Unsupported_Kd ref/" + shape.TwinType);
            }

            foreach (var written in WrittenOnly)
            {
                rows.Add($"{written.Name}.twin", Program(GenericP, written.Body, "P<i32>"), string.Empty);
            }

            foreach (var inferred in Inferred)
            {
                rows.Add($"{inferred.Name}.twin", inferred.Twin, string.Empty);
            }

            return rows;
        }
    }

    // A bodyless import's by-value parameter and result (the owner, 2026-10-29, 「みなす (推奨)」; DECISIONS R5-M150): SPEC 8.15.3 counts
    // passing and returning a value by value, an import is nongeneric (22.6) and checked as a nongeneric body is, called or not; 22.6 makes a
    // struct by value in an import Unsupported; 23.1.2 reports each independent failure. The rows expect both records. An import whose
    // Attribute fails as a whole (an empty symbol name) is no import: Binding checks no signature, so its InvalidLibraryImport_Kd stands alone.
    public static TheoryData<string, string, string> ImportRows
    {
        get
        {
            var rows = new TheoryData<string, string, string>();
            foreach (var shape in Unavailable.Concat(Never))
            {
                var declaration = ImportHead + shape.Type + ") -> i32";
                rows.Add($"{shape.Name}.import", Program(shape.Declaration, ImportHead + "X) -> i32\n", shape.Type), $"InvalidCLayout_Kd {declaration}|Unsupported_Kd {declaration}");
                var result = ImportHead + "i32) -> " + shape.Type;
                var returned = Program(shape.Declaration, ImportHead + "i32) -> X\n", shape.Type);
                rows.Add($"{shape.Name}.importret", returned, $"InvalidCLayout_Kd {result}|Unsupported_Kd {result}");
            }

            var unnamed = ImportHead.Replace("\"VirtualAlloc\"", "\"\"", StringComparison.Ordinal) + "X) -> i32\n";
            var unit = Array.Find(Unavailable, static x => x.Name == "unit").Declaration;
            rows.Add("unit.importname", Program(unit, unnamed, "S"), "InvalidLibraryImport_Kd #LibraryImport(\"kernel32\", \"\")");
            return rows;
        }
    }

    // Part (2) of the row, judged by Layout alone (DECISIONS BU-48): a Never Field. SPEC 6.7.1 names Never among the zero-sized Fields, so each by-value use of S or
    // P<Never> is InvalidCLayout_Kd beside the Never limit's Unsupported_Kd its twin keeps (23.1.2); the uses needing no layout stay
    // accepted; ref/X in an import is Unsupported_Kd alone.
    public static TheoryData<string, string, string> NeverRows
    {
        get
        {
            var rows = new TheoryData<string, string, string>();
            foreach (var shape in Never)
            {
                foreach (var context in Contexts)
                {
                    var records = string.Join('|', new[] { context.Uses, context.Limits }.Where(static x => x.Length > 0));
                    rows.Add($"{shape.Name}.{context.Name}", Program(shape.Declaration, context.Body, shape.Type), records.Replace("X", shape.Type, StringComparison.Ordinal));
                }

                rows.Add($"{shape.Name}.importref", Program(shape.Declaration, ImportHead + "ref/X) -> i32\n", shape.Type), "Unsupported_Kd ref/" + shape.Type);
            }

            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(AcceptedRows))]
    public void AnUnavailableCLayoutIsNoErrorUntilAUseNeedsIt(string form, string source, string records)
        => AssertUses(form, source, records);

    [Theory]
    [MemberData(nameof(UseRows))]
    public void AByValueUseOfAnUnavailableCLayoutIsARecordAtTheUse(string form, string source, string records)
        => AssertUses(form, source, records);

    [Theory]
    [MemberData(nameof(InferredRows))]
    public void AnInferredInstanceWithAnUnavailableCLayoutIsARecordAtEachUse(string form, string source, string records)
        => AssertUses(form, source, records);

    [Theory]
    [MemberData(nameof(TwinRows))]
    public void TheTwinsWithoutTheUnavailableLayoutKeepTheirVerdicts(string form, string source, string records)
        => AssertUses(form, source, records);

    [Theory]
    [MemberData(nameof(ImportRows))]
    public void AByValueImportParameterIsAUse(string form, string source, string records)
        => AssertUses(form, source, records);

    [Theory]
    [MemberData(nameof(NeverRows))]
    public void ANeverFieldMakesCLayoutUnavailable(string form, string source, string records)
        => AssertUses(form, source, records);

    /// <summary>Checks a program and judges its Error records against the expected uses (see the file comment).</summary>
    /// <param name="form">The row's name.</param>
    /// <param name="source">The program.</param>
    /// <param name="records">The expected records as <c>Code use|Code use</c>; empty for an accepted program.</param>
    internal static void AssertUses(string form, string source, string records)
    {
        var uses = records.Length == 0 ? [] : records.Split('|').Select(x => Use.Parse(x, source)).ToArray();
        var c = Check(source);
        var errors = Errors(c);
        var counts = new int[uses.Length];
        var stray = new List<TestDiagnostic>();
        foreach (var error in errors)
        {
            var at = -1;
            for (var i = 0; i < uses.Length; i++)
            {
                if (uses[i].Code == error.Code && error.Span.Length > 0 && uses[i].Start <= error.Span.Start && error.Span.Start < uses[i].End && (at < 0 || uses[i].End - uses[i].Start < uses[at].End - uses[at].Start))
                {
                    at = i;
                }
            }

            if (at < 0)
            {
                stray.Add(error);
            }
            else
            {
                counts[at]++;
            }
        }

        var problems = new List<string>();
        for (var i = 0; i < uses.Length; i++)
        {
            if (uses[i].Many ? counts[i] == 0 : counts[i] != 1)
            {
                problems.Add($"{counts[i]} at [{uses[i].Code} {uses[i].Text.Split('\n')[0]}]");
            }
        }

        problems.AddRange(stray.Select(static x => $"stray {Show(x)}"));
        if (records.Length == 0 && !c.Binding.Result.IsComplete)
        {
            problems.Add("Binding incomplete");
        }

        Assert.True(
            problems.Count == 0,
            $"{form}: expected [{records.Replace("\n", "\\n", StringComparison.Ordinal)}]\n{string.Join("; ", problems)}; observed [{string.Join("; ", errors.Select(Show))}]");
    }

    private static Compilation Check(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        c.Binding.ReportDiagnostics();
        c.ReportAnalysis();
        return c;
    }

    private static TestDiagnostic[] Errors(Compilation c)
        => TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error).ToArray();

    private static string Show(TestDiagnostic x)
        => $"{x.Code} '{(x.Text ?? string.Empty).Split('\n')[0]}'@{x.Span.Start}";

    // The declaration, the uses over X, and a top-level statement where the uses have none.
    private static string Program(string declaration, string body, string type)
    {
        var uses = body.Replace("X", type, StringComparison.Ordinal);
        return declaration + uses + (uses.Contains("\nlet ", StringComparison.Ordinal) || uses.StartsWith("let ", StringComparison.Ordinal) ? string.Empty : "let zz = 1\n");
    }

    /// <summary>An expected record: its code and the use's text, which occurs once in the source.</summary>
    private readonly record struct Use(string Code, string Text, int Start, int End, bool Many)
    {
        public static Use Parse(string entry, string source)
        {
            var space = entry.IndexOf(' ', StringComparison.Ordinal);
            var code = entry[..space];
            var text = entry[(space + 1)..];
            var many = code.EndsWith('+');
            var start = source.IndexOf(text, StringComparison.Ordinal);
            Assert.True(start >= 0 && source.IndexOf(text, start + 1, StringComparison.Ordinal) < 0, $"The use '{text}' must occur once in the source.");
            return new(many ? code[..^1] : code, text, start, start + text.Length, many);
        }
    }
}
