// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 9.5, 9.5.1: a Name's function group gathered across the sources of one alias stage is one group for every use: a call, a
// function reference, a function item and a reference argument (SPEC 10.4, 10.5.1-10.5.3, 7.4.4). The rows are the coverage table
// of the unit that gave the group one representation (FunctionGroup); each alias order is its own row, named by its order.
public partial class ModuleBindingTest
{
    private const string ABool = "group A\n    public func pick(value: bool) -> i32 => 1\n";
    private const string AI32 = "group A\n    public func pick(value: i32) -> i32 => 1\n";
    private const string AGen = "group A\n    public func pick<T>(value: T) -> T => value@move\n";
    private const string ATwo = "group A\n    public func pick(value: bool) -> i32 => 1\n    public func pick(value: string) -> i32 => 2\n";
    private const string AUnsafe = "group A\n    public unsafe func pick(value: i32) -> i32 => value\n";
    private const string APrivate = "group A\n    private func pick(value: i32) -> i32 => 1\n";
    private const string AOne = "group A\n    public func pick(value: i32) -> i32 => value\n";
    private const string ABoth = "group A\n    public func pick(value: bool) -> i32 => 1\n    public func pick(value: i32) -> i32 => value\n";
    private const string APrivateFirst = "group A\n    private func pick(value: bool) -> i32 => 1\n    public func pick(value: i32) -> i32 => value\n";
    private const string APrivateLast = "group A\n    public func pick(value: i32) -> i32 => value\n    private func pick(value: bool) -> i32 => 1\n";
    private const string ASpec = "group A\n    public func pick<T>(value: T) -> T => value@move\n    specialize func pick<i32>(value: i32) -> i32 => value + 1\n";
    private const string ASpecFixed = "group A\n    public func pick<T>(value: T) -> i32 => 0\n    specialize func pick<i32>(value: i32) -> i32 => 1\n";
    private const string ATwice = "group A\n    public func apply(action: (bool) -> i32, value: bool) -> i32 => action(value)\n    public func twice(value: bool) -> i32 => 1\n";
    private const string BTwice = "group B\n    public func apply(action: (i32) -> i32, value: i32) -> i32 => action(value)\n    public func twice(value: i32) -> i32 => value\n";
    private const string RootSpec = "func pick<T>(value: T) -> i32 => 0\nspecialize func pick<i32>(value: i32) -> i32 => 1\n";
    private const string Twice = "func twice(value: i32) -> i32 => value\nfunc twice(value: bool) -> i32 => 1\n";
    private const string UseValue = "func use<T>(value: ref/T) -> i32\n";
    private const string SecondApply = "contract Second\n    func apply(self, action: (i32) -> i32, value: i32) -> i32\n";
    private const string BI32 = "group B\n    public func pick(value: i32) -> i32 => value\n";
    private const string BBool = "group B\n    public func pick(value: bool) -> i32 => 1\n";
    private const string BGen = "group B\n    public func pick<T>(value: T) -> T => value@move\n";
    private const string BGenFixed = "group B\n    public func pick<U>(value: U) -> i32 => 0\n";
    private const string BUniq = "group B\n    public func pick(value: uniq/i32) -> i32 => 2\n";
    private const string BUnsafe = "group B\n    public unsafe func pick(value: i32) -> i32 => value\n";
    private const string BVar = "group B\n    public var pick: i32 = 3\n";
    private const string D6Gen = "public group A\n    public func pick<T>(value: T) -> T => value@move\n";
    private const string D6I32 = "public group B\n    public func pick(value: i32) -> i32 => value + 100\n";
    private const string D6Apply = "func apply(action: (i32) -> i32, value: i32) -> i32 => action(value)\n";
    private const string ASpecBool = "group A\n    public func pick<T>(value: T) -> i32 => 0\n    specialize func pick<bool>(value: bool) -> i32 => 1\n";
    private const string BoolApply = "func applyB(action: (bool) -> i32, value: bool) -> i32 => action(value)\n";
    private const string CStr = "group C\n    public func pick(value: string) -> i32 => 2\n";
    private const string Box = "struct Box\n    public func pick(value: i32) -> i32 => value\n";
    private const string GenericBox = "struct Box<T>\n    public func pick(value: i32) -> i32 => value\n";
    private const string TwoEnvironments = "public struct Box<T>\n    public init() => ()\n    public func make() -> i32 => 1\npublic struct Other<T>\n    public init() => ()\n    public func make(x: i32) -> i32 => x\n";
    private const string Apply = "func apply(action: (i32) -> i32, value: i32) -> i32 => action(value)\n";
    private const string Identity = "func identity<T>(value: T) -> T => value@move\n";
    private const string Run = "func run<F>(action: F, value: i32) -> i32\n    F is Callable<(i32) -> i32>\n    return action(value)\n";
    private const string Keep = "func keep<F>(action: F) -> i32 => 0\n";
    private const string Open = "func use<T>(action: (T) -> i32) -> i32 => 0\nlet n = use(pick)";
    private const string Call = "let n = pick(7)";
    private const string Reference = "let f: (i32) -> i32 = pick";
    private const string Item = "let f = pick";
    private const string GenericItem = "let f = pick<i32>";
    private const string Argument = "let n = apply(pick, 3)";
    private const string Ambiguous = nameof(DiagnosticCode.AmbiguousBinding_Kd);
    private const string Shape = nameof(DiagnosticCode.ParameterShapeMismatch_Kd);
    private const string DirectCall = nameof(DiagnosticCode.DirectCallFunctionValue_Kd);
    private const string NoneApplies = nameof(DiagnosticCode.NoApplicableOverload_Kd);
    private const string Mismatch = nameof(DiagnosticCode.TypeMismatch_Kd);
    private const string Unsupported = nameof(DiagnosticCode.Unsupported_Kd);
    private const string Duplicate = nameof(DiagnosticCode.DuplicateBinding_Kd);
    private const string Unbound = nameof(DiagnosticCode.UnboundTypeArgument_Kd);

    // A family: its name, the aliases whose every order is a row, the declarations and the use, and the Error codes SPEC gives in
    // every order (none for a valid program).
    private static readonly (string Name, string Aliases, string Body, string Codes)[] GatheredFamilies =
    [
        // Calls (SPEC 10.4), the reader that always gathered.
        ("c1", "A", ABoth + Call, string.Empty),
        ("c2", "A B", ABool + BI32 + Call, string.Empty),
        ("c3", "A B C", ABool + BI32 + CStr + Call, string.Empty),
        ("c2t", "A B", AI32 + BI32 + Call, Ambiguous),
        ("c3t", "A B C", AI32 + BI32 + CStr + Call, Ambiguous),
        ("cp2", "A B", ABool + BI32 + "let n = (pick)(7)", string.Empty),
        ("cg2", "A B", ABool + BGen + "let n = pick<i32>(7)", string.Empty),
        ("cs2", "A B", AI32 + BUniq + Call, Shape),
        ("csg2", "A B", AGen + BUniq + "let n = pick<i32>(7)", Shape),
        ("mac2", "A", APrivateFirst + Call, string.Empty),
        ("euc2", "A Box<i32>", ABool + GenericBox + Call, string.Empty),
        ("fr-pg", "A B", ABool + BGen + "let n = (pick<i32>)(7)", string.Empty),
        ("fr-spc", "A B", ASpecFixed + BBool + Call, string.Empty), // SPEC 8.13.3: a specialization in a gathered source is no candidate.

        // References under a fixed Function Type S (SPEC 10.5.3).
        ("r1", "A", ABoth + Reference, string.Empty),
        ("r2", "A B", ABool + BI32 + Reference, string.Empty),
        ("rq2", "A B", ABool + BI32 + "let f: (i32) -> i32 = (pick)", string.Empty),
        ("r3", "A B C", ABool + BI32 + CStr + Reference, string.Empty),
        ("r2m", "A B", ATwo + BI32 + Reference, string.Empty),
        ("r2ma", "A B", ATwo + BI32 + "let f: (string) -> i32 = pick", string.Empty),
        ("rg2", "A B", ABool + BGen + Reference, string.Empty),
        ("rg3", "A B C", ABool + BGen + CStr + Reference, string.Empty),
        ("rp2", "A B", AGen + BI32 + Reference + "\nlet n = f(5)", string.Empty),
        ("rpu2", "A B", AGen + BUnsafe + Reference, DirectCall), // SPEC 10.4 step 3 selects B.pick; then SPEC 7.6, no reselection.
        ("r2t", "A B", AI32 + BI32 + Reference, Ambiguous),
        ("r3t", "A B C", AI32 + BI32 + CStr + Reference, Ambiguous),
        ("r2n", "A B", ABool + BI32 + "let f: (f64) -> i32 = pick", NoneApplies),
        ("r2s", "A B", AI32 + BUniq + Reference, Shape), // SPEC 7.4.4, 9.5.1: every use is an error.
        ("du2", "A B", AUnsafe + BBool + "let f: (bool) -> i32 = pick", string.Empty),
        ("du2x", "A B", AUnsafe + BBool + Reference, DirectCall),
        ("fv2", "A B", ABool + BI32 + Reference + "\nlet n = f(5)", string.Empty),
        ("rt2", "A B", ABool + BI32 + "func get() -> (i32) -> i32 => pick", string.Empty),
        ("tu2", "A B", ABool + BI32 + "let t: ((i32) -> i32, i32) = (pick, 1)", string.Empty),
        ("cf2", "A B", ABool + BI32 + "let flag = true\nlet f: (i32) -> i32 = if flag => pick else => pick", string.Empty),
        ("ms2", "A Box", ABool + Box + Reference, string.Empty),
        ("mar", "A", APrivateLast + Reference, string.Empty),
        ("mar2", "A", APrivateFirst + Reference, string.Empty),
        ("du1x", "A", AUnsafe + Reference, DirectCall),
        ("spg2", "A B", ASpec + BBool + Reference + "\nlet n = f(5)", string.Empty), // SPEC 8.13.3: A.pick<T> is the one applicable candidate.
        ("fr-spr", "A B", ASpecFixed + BBool + Reference, string.Empty),
        ("r2s2", "A B", AI32 + BUniq + Reference + "\nlet g = pick", Shape + "," + Shape), // One record for each use.

        // Function items: a reference without S (SPEC 10.5.3).
        ("i1", "A", AOne + Item, string.Empty),
        ("i1o", "A", ABoth + Item, Ambiguous),
        ("i2", "A B", ABool + BI32 + Item, Ambiguous),
        ("i3", "A B C", ABool + BI32 + CStr + Item, Ambiguous),
        ("ig2", "A B", ABool + BGen + GenericItem, string.Empty),
        ("ig3", "A B C", ABool + BGen + CStr + GenericItem, string.Empty),
        ("ig2t", "A B", AGen + BGen + GenericItem, Ambiguous),
        ("is2", "A B", AI32 + BUniq + Item, Shape),
        ("isg2", "A B", AGen + BUniq + GenericItem, Shape),
        ("ma", "A", APrivateLast + Item, string.Empty),
        ("spgi2", "A B", ASpec + BBool + Item, Ambiguous),
        ("fr-spi", "A B", ASpecFixed + BBool + Item, Ambiguous),

        // Reference arguments (SPEC 10.5.1, 10.5.2).
        ("a2", "A B", ABool + BI32 + Apply + Argument, string.Empty),
        ("a3", "A B C", ABool + BI32 + CStr + Apply + Argument, string.Empty),
        ("an2", "A B", ABool + BI32 + Apply + "let n = apply(action: pick, value: 3)", string.Empty),
        ("cl2", "A B", ABool + BI32 + Apply + "let g = func () -> i32 => apply(pick, 3)", string.Empty),
        ("cfa2", "A B", ABool + BI32 + Apply + "let flag = true\nlet n = apply(if flag => pick else => pick, 3)", string.Empty),
        ("ac2", "A B", ABool + BI32 + Run + "let n = run(pick, 3)", string.Empty),
        ("xr2", "A B", ABool + BGen + Run + "let n = run(pick<i32>, 3)", string.Empty),
        ("as2", "A B", AI32 + BUniq + Apply + Argument, Shape),
        ("cfas2", "A B", AI32 + BUniq + Apply + "let flag = true\nlet n = apply(if flag => pick else => pick, 3)", Shape + "," + Shape),
        ("ise2", "A B", AI32 + BUniq + Identity + "let g = identity(pick)", Shape),
        ("fr-bg", "A B", ATwice + BTwice + "let n = apply(twice, 3)", string.Empty), // The callee and its reference argument both gathered.
        ("ae1", "A", AOne + Identity + "let g = identity(pick)", string.Empty),
        ("ae1o", "A", ABoth + Identity + "let g = identity(pick)", Ambiguous),
        ("ae2", "A B", ABool + BI32 + Identity + "let g = identity(pick)", Ambiguous),
        ("kp2", "A B", ABool + BI32 + Keep + "let n = keep(pick)", Ambiguous),
        ("n8a", "A B", ABool + BI32 + Open, Unbound), // SPEC 10.5.1, 10.8: the gathered reference waits, so the open S binds no T.
        ("n8g", "A B", ABool + BGenFixed + Open, Unbound),
        ("fr-n8e", "A B", AOne + "group B\n    public func pick(value: i32) -> i64 => 2\n" + Open, Unbound), // The members agree on the parameter Type; still no evidence for T.
        ("fr-n8x", "A B", ABool + BGenFixed + "func use<T>(action: (T) -> i32) -> i32 => 0\nlet n = use(pick<i32>)", string.Empty), // SPEC 10.5.3, 10.1 step 1: the explicit argument leaves one candidate.
        ("tc2", "A B", "group A\n    public func apply(value: bool) -> i32 => 0\ngroup B\n    public func apply(action: (i32) -> i32, value: i32) -> i32 => action(value)\n" + Twice + "let n = apply(twice, 3)", string.Empty),

        // Lookup's own rules (SPEC 9.5), which the group reads and never decides.
        ("lk", "A B", ABool + BVar + Reference, Unsupported + "," + Ambiguous),
        ("la", "A B", APrivate + BI32 + Reference, string.Empty),
        ("lai", "A B", APrivate + BI32 + Item, string.Empty),

        // An opened generic Container's member beside a group: the reference reader's limit, in every order.
        ("eu1", "Box<i32>", GenericBox + Reference, Unsupported),
        ("eu2", "A Box<i32>", ABool + GenericBox + Reference, Unsupported),

        // PLAN G633: one group gathered from two opened environments is valid (SPEC 9.5.1, 7.4), but a use holds one imported
        // environment: the located Unsupported_Kd at the Name, a limit, in every order; its invalid twin keeps the limit's record.
        ("g633", "Box<i32> Other<i64>", TwoEnvironments + "let n = make(1)", Unsupported),
        ("g633i", "Box<i32> Other<i64>", TwoEnvironments + "let n = make(\"s\")", Unsupported),
    ];

    // SPEC 9.6: a member gathered from two Constraints is one group in either clause order. Each row: its name, the Contracts and other
    // declarations, the function header the clauses follow, its body and the Error codes.
    private static readonly (string Name, string Declarations, string Header, string Body, string Codes)[] ConstraintFamilies =
    [
        // The reference argument waits for the Function Type of the one requirement that applies (SPEC 10.1, 10.5.1-10.5.3).
        ("tcq", "contract First\n    func apply(self, value: bool) -> i32\n" + SecondApply + Twice, UseValue, "return value.apply(twice, 3)", string.Empty),
        ("tcqx", "contract First\n    func apply(self, value: bool) -> i32\n" + SecondApply + Twice, UseValue, "return value.apply(twice, true)", NoneApplies),
        ("tcq2", "contract First\n    func apply(self, action: (bool) -> i32) -> i32\n" + SecondApply + Twice, UseValue, "return value.apply(twice, 3)", string.Empty),

        // SPEC 8.6.4: a call that matches several requirements is ambiguous.
        ("fr-tcx", "contract First\n    func apply(self, action: (bool) -> i32, value: i32) -> i32\n" + SecondApply + Twice, UseValue, "return value.apply(twice, 3)", Ambiguous),

        // SPEC 7.4.4, 9.6: the Constraint group's own check, read through the group.
        ("tqs", "contract First\n    func apply(self, value: i32) -> i32\ncontract Second\n    func apply(self, value: uniq/i32) -> i32\n", UseValue, "return value.apply(3)", Shape),

        // SPEC 10.5.3: a requirement reference without S over two requirements.
        ("fr-tci", "contract First\n    func twice(value: bool) -> i32\ncontract Second\n    func twice(value: i32) -> i32\n", "func use<T>() -> i32\n", "let f = T.twice\n    return 0", Ambiguous),
    ];

    public static TheoryData<string, string, string, string> GatheredGroups
    {
        get
        {
            var rows = new TheoryData<string, string, string, string>();
            foreach (var family in GatheredFamilies)
            {
                foreach (var order in Orders(family.Aliases.Split(' ')))
                {
                    var name = order.Length == 1 ? family.Name : family.Name + "-" + string.Concat(order.Select(static x => x[0]));
                    rows.Add(name, string.Concat(order.Select(static x => "alias " + x + "\n")) + family.Body, string.Empty, family.Codes);
                }
            }

            // SPEC 9.5 stage 6: the default aliases together. Declaration order decides the first source, never the configured list,
            // so the rows swap which source declares which overload (df2, df2r) beside the list's order.
            foreach (var list in new[] { "A,B", "B,A" })
            {
                var order = "-" + list.Replace(",", string.Empty);
                rows.Add("df2" + order, "public " + ABool + "public " + BI32 + Reference, list, string.Empty);
                rows.Add("df2r" + order, "public " + AI32 + "public " + BBool + Reference, list, string.Empty);
                rows.Add("dx2" + order, "public " + ABool + "public " + BI32 + Item, list, Ambiguous);
                rows.Add("dx2r" + order, "public " + AI32 + "public " + BBool + Item, list, Ambiguous);
            }

            // Lookup stops at the document's own Name, at a qualified source and between stages (SPEC 9.5, 9.5.1).
            rows.Add("ls", "alias B\n" + BI32 + "func pick(value: bool) -> i32 => 1\n" + Reference, string.Empty, Mismatch);
            rows.Add("qa-v", "alias A\nalias B\n" + ABool + BI32 + "let f: (bool) -> i32 = A.pick", string.Empty, string.Empty);
            rows.Add("qa-x", "alias A\nalias B\n" + ABool + BI32 + "let f: (i32) -> i32 = A.pick", string.Empty, Mismatch);
            rows.Add("st56", "alias A\npublic " + ABool + "public " + BI32 + Reference, "B", Mismatch);

            // One declaration through two aliases, one source's duplicates, and a specialization beside its original (SPEC 9.5.1,
            // 8.13.3): no gathered group, the same reader.
            rows.Add("i2d-AA", "alias A\nalias A\n" + AOne + Item, string.Empty, string.Empty);
            rows.Add("r1t", "func pick(value: i32) -> i32 => 1\nfunc pick(value: i32) -> i32 => value\n" + Reference, string.Empty, Duplicate + "," + Ambiguous);
            rows.Add("sp-r", RootSpec + Reference, string.Empty, string.Empty);
            rows.Add("sp-i", RootSpec + Item, string.Empty, Unbound);
            rows.Add("n8s", "func pick<U>(value: U) -> i32 => 0\nspecialize func pick<i32>(value: i32) -> i32 => 1\n" + Open, string.Empty, Unbound);
            rows.Add("n8o", "func pick(value: bool) -> i32 => 1\nfunc pick(value: i32) -> i32 => value\n" + Open, string.Empty, Unbound);

            foreach (var (order, clauses) in new[] { ("FS", "    T is First\n    T is Second\n"), ("SF", "    T is Second\n    T is First\n") })
            {
                foreach (var family in ConstraintFamilies)
                {
                    rows.Add(family.Name + "-" + order, family.Declarations + family.Header + clauses + "    " + family.Body, string.Empty, family.Codes);
                }
            }

            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(GatheredGroups))]
    public void AGatheredGroupIsOneGroupForEveryUse(string name, string source, string defaults, string codes)
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.OutputKind = OutputKind.Application;
        if (defaults.Length != 0)
        {
            c.Project.ProjectFile.Alias = defaults.Split(',');
        }

        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("Main.kimi", source + "\nConsole.writeLine(\"ok\")\n"));
        c.Check();
        var errors = TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(codes == string.Join(',', errors.Select(static x => x.Code)), name + ": " + string.Join("; ", errors.Select(static x => x.ToString())));
    }

    // SPEC 10.4 step 3: a reference under S selects the non-generic declaration of a gathered group in either alias order. Acceptance
    // alone does not observe the selection (both candidates apply), so the selected declaration is read at Binding's own layer.
    [Theory]
    [InlineData("alias A\nalias B\n")]
    [InlineData("alias B\nalias A\n")]
    public void AGatheredReferenceSelectsTheMoreSpecificDeclaration(string aliases)
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.OutputKind = OutputKind.Application;
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("Main.kimi", aliases + AGen + BI32 + Reference + "\nConsole.writeLine(\"ok\")\n"));
        c.Check();
        Assert.DoesNotContain(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var use = nodes.Last(static x => x.ToString() == "pick");
        var expected = nodes.OfType<FunctionKoto>().Single(static x => x.ToString().Contains("pick(value: i32)", StringComparison.Ordinal));
        Assert.Same(expected, HirTables.Of(use).Symbols[use.SyntaxId]?.Declaration);
    }

    // SPEC 10.4 step 3 with 10.5.3 and 10.5.1: a value use of a gathered group, a reference fitted to a Function Type or an argument,
    // selects the non-generic declaration over a generic one (d6: two public groups under the project's default aliases, in either order) and over a generic one with an explicit
    // specialization, which is no candidate (8.13.3, sp-gb), in either order. Dev accepted these too but selected the generic
    // declaration (wrong code, the Middle's RP17 probes: d6 printed 5 for 105, sp-gb 1 for 2), so the selection is read at Binding's
    // layer, as above.
    [Theory]
    [InlineData("d6-ref-AB", "A,B", D6Gen + D6I32 + "let f: (i32) -> i32 = pick\nlet n = f(5)\n", "pick(value: i32)")]
    [InlineData("d6-ref-BA", "B,A", D6Gen + D6I32 + "let f: (i32) -> i32 = pick\nlet n = f(5)\n", "pick(value: i32)")]
    [InlineData("d6-arg-AB", "A,B", D6Gen + D6I32 + D6Apply + "let n = apply(pick, 5)\n", "pick(value: i32)")]
    [InlineData("d6-arg-BA", "B,A", D6Gen + D6I32 + D6Apply + "let n = apply(pick, 5)\n", "pick(value: i32)")]
    [InlineData("sp-gb-ref-AB", "", "alias A\nalias B\n" + ASpecBool + BBool + "let f: (bool) -> i32 = pick\nlet n = f(true)\n", "pick(value: bool)")]
    [InlineData("sp-gb-ref-BA", "", "alias B\nalias A\n" + ASpecBool + BBool + "let f: (bool) -> i32 = pick\nlet n = f(true)\n", "pick(value: bool)")]
    [InlineData("sp-gb-arg-AB", "", "alias A\nalias B\n" + ASpecBool + BBool + BoolApply + "let n = applyB(pick, true)\n", "pick(value: bool)")]
    [InlineData("sp-gb-arg-BA", "", "alias B\nalias A\n" + ASpecBool + BBool + BoolApply + "let n = applyB(pick, true)\n", "pick(value: bool)")]
    public void AGatheredValueUseSelectsTheNonGenericDeclaration(string name, string defaults, string source, string expected)
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.OutputKind = OutputKind.Application;
        if (defaults.Length != 0)
        {
            c.Project.ProjectFile.Alias = defaults.Split(',');
        }

        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("Main.kimi", source + "Console.writeLine(\"ok\")\n"));
        c.Check();
        Assert.DoesNotContain(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var use = nodes.Last(static x => x.ToString() == "pick");
        var declaration = nodes.OfType<FunctionKoto>().Single(x => x.ToString().Contains(expected, StringComparison.Ordinal));
        Assert.True(ReferenceEquals(declaration, HirTables.Of(use).Symbols[use.SyntaxId]?.Declaration), name);
    }

    private static IEnumerable<string[]> Orders(string[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }

        for (var i = 0; i < items.Length; i++)
        {
            foreach (var rest in Orders([.. items[..i], .. items[(i + 1)..]]))
            {
                yield return [items[i], .. rest];
            }
        }
    }
}
