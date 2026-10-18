#:property Nullable=disable
// Executable prototype of the SPEC §15.6 Loan rules (plan §4.1-§4.3) with the S1 kill option (plan §5.2).
// Run: dotnet run proto.cs        (-v prints every error fact per mode)
// Modes: PROV = provenance Loan places, no Loan kill (current SPEC with one static Loan ID per site);
//        S1   = Loans named from their base, killed when the base is replaced (an exchange/swap target counts as
//               replaced); Move of a base is deep (S1 as written);
//        S1P  = S1′, the plan's S1 (e32dc0ef §4.1-§4.3): `ended` only on destroyed_in_place (=, replace, end of storage);
//               base_op = Move, exchange/swap target, exclusive borrow other than as a `replace` target.
//        A    = design A3 (rounds 3-9) plus round 10 (final) = the chosen 1b design diff (choice-1b.md §1) with its design
//               review's changes, in A mode only:
//               G119 (B's rule): inside a reservation's window (formation to activation), a write or destruction (a
//               destroying activation included) of a prefix of the reserved Loan's Place, across borrow Derefs, reaches that
//               Loan in its reservation holder (no chain fact: G92b's fold); 33c the derived excuse at c2's reservations;
//               G110 no Loan past a shared Deref (layers from the last shared Deref on), no obs cut; B1 (replaces 39o's row)
//               the closure of CS(ℓ) seeded by the direct Loans of the value ℓ stores (own Loan and carried R2 parents)
//               into a holder live after ℓ as transit reads it, that of CS_call(ℓ, t) by those of σ_in(t); the Index of a
//               Slice handle is a shared borrow Deref (Case.Inline holds A3's inline encoding); GuardOrigin: γ_H, the
//               guard-local Origin's marker, in guard candidates only, and err(ended) only at a guard candidate's EndBorrow;
//               the Call reads its reservation holders; a destroying call's destruction checked as A3's err(destroy), at
//               the call on CS_call(ℓ, t_K) (seeded by σ_in(t_K)), for the Loans its activation's own kind does not hit
//               (review r2 F1; DestroyAsDrop = the earlier Drop on CS).
//               Cases: rounds 3-10, the case 23a review's 50-94 (EndBorrow, PrefixKill and its mutations), and the 1b
//               design review's 100-112b and its round 2's 120-127b. Exit code = S1′ failures (unchanged mode; it
//               models none of round 10's rules).
//        Round 11 (A mode; the core lane's group review B1, B2, B4; A3 E13, E14): E13, a Call's inputs are its argument
//               operands and a Value callee's operand. (1) The accesses they make AT the Call (Node.Input: a read or Move of
//               a direct Place operand the MIR leaves at the Call; Node.AcquireAt: an exclusive PairAcquire Reborrow left at
//               the Call) are checked on CS_call(ℓ, −): holders live into ℓ, none excluded, a reservation in its own holder
//               `ref`; the Call's destination write and stores keep CS(ℓ). (2) Every reserved input activates at the Call,
//               a Value callee's included (Node.Callee). (3) A PairAcquire Reborrow secured into a temp before its Call is a
//               TwoPhaseUniq reservation in that temp (Node.Acquire). E14: a Call whose contract writes an input into storage
//               reached through a raw pointer stores the input's value into E (Node.RawStore: Raw.initialize). Variants:
//               CalleeNotActivated (B1), InputsOnCS (B2), PairAcquireLive (the live Uniq encoding), NoRawStore (B4),
//               Implemented1b (all four, the reviewed branch), SecureAcquire (round 11's finding: every PairAcquire that a
//               later input of its Call left at the Call follows is secured into a temp; Case.Secured).
//        Round 12 (A mode; A3 E13 as the owner chose on 2026-10-18, option R1: A5 handles operand evaluation order; the E13
//               design review's cases 134a-135c and its AlwaysSecure, merged first): (1) every operation's operands left at
//               ℓ (Node.Input, in evaluation order, SPEC 12.2: a receiver or Value callee first; Node.Operation marks an
//               operation other than a Call) are checked on CS_i(ℓ) = closure(σ_in of the holders live after ℓ that ℓ does not
//               overwrite, val_in of every earlier operand (attributed to its root's holder for mode), σ_in of every later
//               operand's root, σ_in(E)); the destination write and the stores keep CS(ℓ). (3) M′: every PairAcquire is
//               secured into its own temp (Case.Always, else Case.Secured, else the case), a marked one (an invocation input)
//               a reservation there (Node.Acquire), an unmarked one (a let, a control-flow result, an aggregate operand, a
//               capture) a live Uniq Borrow. Variants: Round11 (round 11's E13 on round 11's MIR), AcquireAtOperation (E13 (1)
//               on G123's MIR), ShapeReservation, IntrinsicAllOperands, NoEarlierValues, NoLaterRoots, EarlierRoots.
//               Planning session on the round's findings: CS_i holds val_in(o_j) for j ≤ i, the operand's own value included
//               (NoOwnValue: E13 as first stated, j < i), and a single-operand rvalue's operand (a Use of a Copy or Move, a Cast,
//               a UnaryOp; not a Ref) is checked against its own value on CS(ℓ) too (OwnValueEverywhere: the term on every
//               node Read or Move, an upper bound).
//        Round 13 (A mode; A3 E13 as rewritten after its second design review, the owner's "one rule for every access"; the
//               second design review's cases and variants merged first): each access at ℓ is checked on closure(σ_in of the
//               holders live after ℓ that ℓ does not overwrite ∪ σ_in(E) ∪ T), T by the access: an operand's as round 12; an
//               activation of a reservation in t and a destroying call's destroy: the values of the operands other than t (not
//               CS_call's live_in), seeded by σ_in(t); a Ref's or Reborrowing PairAcquire's borrow of X: its R2 parents as E3
//               bounds them, ⋃_{m≤j<k} content^j(σ_in(root(X))) (m the index of the last shared Deref, else 0), whatever its
//               destination's liveness, and E5's seed (no transit); an observing destruction of X: val(X) (no transit: MIR's
//               Drop is a terminator, the store follows it); the destination write and the stores: transit and E5's seed.
//               E13 (2): the MIR leaves Place operands in evaluation order (named arguments, a setter's value; captures by the
//               same rule). THE DESIGN is design A with E8's ending (PrefixKill): Analysis(c, Mode.A, x, prefixKill: true);
//               Abl.None alone keeps A3 §3's base kill (last base). Variants: NoDestroyValue, BorrowSeedOnly (R4-M57),
//               ParentsAsTransit (the review's RefParentsInTransit with a seed whatever the liveness), ActivationLiveIn,
//               CurrentBuilder (Case.Built: today's builder), KeepCSTerms, BorrowWithoutSeed (the round's first run: the borrow
//               row without E5's seed accepts 41o), ParentsEveryLayer (⋃_{j<k}) and OnePointDrop (Case.OnePoint).
using System.Text;
using Fact = (string H, string L);

Console.OutputEncoding = Encoding.UTF8; // round 10: UTF-8 output on any console code page
var verbose = args.Contains("-v");
var modes = new[] { Mode.PROV, Mode.S1, Mode.S1P };
if (args.Length == 2 && args[0] == "-dump") // dotnet run proto.cs -- -dump 8b : per-node carries, liveness and Loan places
{
    foreach (var m in modes.Append(Mode.A)) Console.WriteLine($"== {m}\n" + new Analysis(Cases.All().Single(c => c.Id == args[1]), m).Dump());
    Console.WriteLine("== the design (A with PrefixKill)\n" + new Analysis(Cases.All().Single(c => c.Id == args[1]), Mode.A, prefixKill: true).Dump()); // round 13
    return 0;
}
// Round 13 (A3 E13 rewritten; the second design review's -r13 run, folded into named variants): THE DESIGN is design A with
// E8's ending (PrefixKill). Its totals, the last-base mode, and each round-12 and round-13 variant on the design; then the
// cases 130+ under the design and each round-13 variant. -r13 prints only this section.
Analysis Design(Case c, Abl x = Abl.None) => new(c, Mode.A, x, prefixKill: true);
var round13Variants = new[] { Abl.NoDestroyValue, Abl.BorrowSeedOnly, Abl.ParentsAsTransit, Abl.ActivationLiveIn, Abl.CurrentBuilder, Abl.KeepCSTerms, Abl.BorrowWithoutSeed, Abl.ParentsEveryLayer, Abl.OnePointDrop };
void Round13()
{
    string Group(Analysis a, Case c) => Cls(c, a) is var s && s.StartsWith("(a)") ? "listed loss" : s;
    string Row(string name, Func<Case, Analysis> run)
    {
        var g = Cases.All().GroupBy(c => Group(run(c), c)).ToDictionary(x => x.Key, x => x.Select(c => c.Id).ToList());
        string N(string k, bool list) => g.TryGetValue(k, out var v) ? v.Count + (list ? " (" + string.Join(", ", v) + ")" : "") : "0";
        return $"| {name} | {N("same", false)} | {N("same verdict, error moves", false)} | {N("listed loss", false)} | {N("**(b) unlisted loss**", true)} | {N("**(c) UNSOUND**", true)} |";
    }
    Console.WriteLine($"\nRound 13 (E13 rewritten; {Cases.All().Count} cases):\n| Mode | Same | Earlier or moved error | Listed loss | Unlisted rejection | Unsound |\n| --- | ---: | ---: | ---: | ---: | ---: |");
    Console.WriteLine(Row("**The design** (design A with PrefixKill)", c => Design(c)));
    Console.WriteLine(Row("Design A (last base)", c => new Analysis(c, Mode.A)));
    foreach (var ab in round13Variants.Concat(new[] { Abl.NoOwnValue, Abl.NoEarlierValues, Abl.NoLaterRoots, Abl.EarlierRoots, Abl.AcquireAtOperation, Abl.ShapeReservation, Abl.IntrinsicAllOperands, Abl.OwnValueEverywhere, Abl.RefParentsInTransit, Abl.Round11, Abl.CalleeNotActivated, Abl.InputsOnCS, Abl.PairAcquireLive, Abl.NoRawStore, Abl.Implemented1b }))
        Console.WriteLine(Row("`" + ab + "`", c => Design(c, ab)));
    Console.WriteLine("\nRound 13 cases (130+) under the design:\n| Case | Program | SPEC | design | last base | " + string.Join(" | ", round13Variants) + " | class design |\n|" + string.Concat(Enumerable.Repeat(" --- |", round13Variants.Length + 6)));
    foreach (var c in Cases.All().Where(Cases.Round11))
    {
        string W(Analysis a) => V(a) + (c.Err && a.Errors.Count == 0 ? " **UNSOUND**" : !c.Err && a.Errors.Count > 0 ? " (over-strict)" : "");
        var d = Design(c);
        var spec = c.Err ? "error" + (c.At.Length > 0 ? " at `" + string.Join("`, `", c.At) + "`" : "") : "valid";
        Console.WriteLine($"| {c.Id} | {c.Title} | {spec} | {W(d)} | {W(new Analysis(c, Mode.A))} | {string.Join(" | ", round13Variants.Select(x => W(Design(c, x))))} | {Cls(c, d)} |");
    }
    foreach (var ab in round13Variants)
        Console.WriteLine($"The design with {ab}: " + string.Join("; ", Cases.All().Select(c => (c, a: Design(c, ab), b: Design(c))).Where(x => !x.a.ErrorNodes().SequenceEqual(x.b.ErrorNodes()))
            .Select(x => $"{x.c.Id}: {V(x.a)}" + (x.c.Err && x.a.Errors.Count == 0 ? " **UNSOUND**" : !x.c.Err && x.a.Errors.Count > 0 ? " (over-strict)" : "")).DefaultIfEmpty("no change")));
    if (verbose)
        foreach (var c in Cases.All().Where(Cases.Round11))
            Console.WriteLine($"{c.Id,-8} D   : " + (Design(c) is var a && a.Errors.Count == 0 ? "no errors" : string.Join("; ", a.Errors.Select(e => $"{e.L} live at `{a.LabelAt(e.P)}` vs {e.What}"))));
}
if (args.Contains("-r13")) { Round13(); return 0; }
var table3 = new StringBuilder("| Case | SPEC | S1′ | design A | Class |\n| --- | --- | --- | --- | --- |\n");
string V(Analysis a) => a.Errors.Count == 0 ? "valid" : "error at `" + string.Join("`, `", a.ErrorNodes()) + "`";
void VerboseA(Case c, Analysis am) // design A and its first-Deref base (AF), every error fact; round 11: SA = SecureAcquire's encoding;
{                                  // round 12: R11 = Round11, AO = AcquireAtOperation, NV = NoOwnValue (cases 130+)
    foreach (var (tag, a) in new[] { ("A", am), ("AF", new Analysis(c, Mode.A, Abl.FirstDerefBase)) }.Concat(c.Secured != null ? [("SA", new Analysis(c, Mode.A, Abl.SecureAcquire))] : [])
        .Concat(Cases.Round11(c) ? [("R11", new Analysis(c, Mode.A, Abl.Round11)), ("AO", new Analysis(c, Mode.A, Abl.AcquireAtOperation)), ("NV", new Analysis(c, Mode.A, Abl.NoOwnValue))] : []))
        Console.WriteLine($"{c.Id,-4} {tag,-4}: " + (a.Errors.Count == 0 ? "no errors" : string.Join("; ", a.Errors.Select(e => $"{e.L} live at `{a.LabelAt(e.P)}` vs {e.What}"))));
}
string Cls(Case c, Analysis a)
{
    bool ae = a.Errors.Count > 0;
    // Round 12: a round-11/12 case's other MIR form (Secured, Always, Shape, IntrinsicRow) names its own nodes, so its own
    // error location is the expected one (the same source operation); every other case keeps the case's.
    var at = (Cases.Round11(c) || a.Enc == c.OnePoint) && a.Enc.At.Length > 0 ? a.Enc.At : c.At; // round 13: OnePoint names its own
    if (ae == c.Err) return !ae || a.ErrorNodes().ToHashSet().SetEquals(at) ? "same" : "same verdict, error moves";
    if (!ae) return "**(c) UNSOUND**";
    return c.S1Err == true ? "(a) S1′ loss" : c.ALoss != null ? "(a) " + c.ALoss : "**(b) unlisted loss**";
}
if (args.Contains("-cases")) // -cases [variant ...]: one tab-separated row per case and variant (round 12: variants by name;
{                             // round 13: a name ending in +PK runs on the design, e.g. None+PK, NoDestroyValue+PK)
    var named = args.SkipWhile(x => x != "-cases").Skip(1).ToArray();
    foreach (var c in Cases.All())
    {
        foreach (var name in named.Length > 0 ? named : ["None", "PrefixKill", "FirstDerefBase"])
        {
            bool pk = name.EndsWith("+PK");
            var ab = Enum.Parse<Abl>(pk ? name[..^3] : name);
            var a = new Analysis(c, Mode.A, ab, prefixKill: pk);
            var facts = string.Join("; ", a.Errors.Select(e => $"{e.L}@{a.LabelAt(e.P)} vs {e.What}").OrderBy(x => x, StringComparer.Ordinal));
            Console.WriteLine($"{c.Id}	{name}	{(c.Err ? "error" : "valid")}	{V(a)}	{Cls(c, a)}	{facts}");
        }
    }
    return 0;
}
var table = new StringBuilder();
table.AppendLine("| Case | Program | SPEC | PROV | S1 intended | S1 as written | S1′ (final rules) |");
table.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
int finalFails = 0;
foreach (var c in Cases.All())
{
    if (Cases.NoS1P(c)) // EndBorrow and err(ended) exist in design A mode only, so S1′ does not run these (as in the reviews)
    {
        var a10 = new Analysis(c, Mode.A); table3.AppendLine($"| {c.Id} | {(c.Err ? "error" : "valid")} | (not run) | {V(a10)} | {Cls(c, a10)} |");
        if (verbose) VerboseA(c, a10);
        continue;
    }
    var r = modes.ToDictionary(m => m, m => new Analysis(c, m));
    bool s1Err = c.S1Err ?? c.Err; var s1At = c.S1At ?? c.At;
    string Exp(bool e, string[] at) => e ? "error" + (at.Length > 0 ? " at `" + string.Join("`, `", at) + "`" : "") : "valid";
    (string Text, bool Ok) Got(Analysis a, bool e, string[] at)
    {
        var nodes = a.ErrorNodes().ToList();
        var extra = c.Extra?.Invoke(a);
        bool ok = (nodes.Count > 0) == e && (!e || at.Length == 0 || nodes.ToHashSet().SetEquals(at)) && extra == null;
        var tag = ok ? "pass" : e && nodes.Count == 0 ? "**FAIL: UNSOUND**" : "FAIL";
        var v = nodes.Count == 0 ? "valid" : "error at `" + string.Join("`, `", nodes) + "`";
        return ($"{v} ({tag}{(extra != null ? ": " + extra : "")})", ok);
    }
    var prov = Got(r[Mode.PROV], c.Err, c.At); var s1f = Got(r[Mode.S1P], s1Err, s1At);
    var s1 = c.S1POnly ? Got(r[Mode.S1], c.Err, c.At) : Got(r[Mode.S1], s1Err, s1At);
    if (!s1f.Ok) finalFails++;
    var am = new Analysis(c, Mode.A);
    table3.AppendLine($"| {c.Id} | {(c.Err ? "error" : "valid")} | {V(r[Mode.S1P])} | {V(am)} | {Cls(c, am)} |");
    if (verbose) VerboseA(c, am);
    table.AppendLine($"| {c.Id} | {c.Title} | {Exp(c.Err, c.At)} | {prov.Text} | {Exp(s1Err, s1At)}{(c.S1POnly ? " (S1′ only)" : "")} | {s1.Text} | {s1f.Text} |");
    if (verbose)
        foreach (var m in modes)
            Console.WriteLine($"{c.Id,-4} {m,-4}: " + (r[m].Errors.Count == 0 ? "no errors" :
                string.Join("; ", r[m].Errors.Select(e => $"{e.L} live at `{c.Nodes[e.P].Label}` vs {e.What}"))));
}
Console.WriteLine(table);
Console.WriteLine(finalFails == 0 ? "S1′: all cases pass." : $"S1′: {finalFails} case(s) fail.");
Console.WriteLine("\nDesign A (round 3):\n" + table3);

// Ablations: drop one refinement of the final rules (S1′) and list the cases whose verdict changes, judged by the SPEC.
Console.WriteLine("\n| Variant of S1′ | Cases that change |\n| --- | --- |");
var aVar = Cases.All().Select(c => (c, a: new Analysis(c, Mode.A, Abl.AIssueNotEnded), b: new Analysis(c, Mode.A)))
    .Where(x => !x.a.ErrorNodes().SequenceEqual(x.b.ErrorNodes())).Select(x => $"{x.c.Id}: {V(x.a)}");
Console.WriteLine("Design A without the round-3 fix (issued Loan not ended at its base's destruction): " + string.Join("; ", aVar.DefaultIfEmpty("none")));
// Round 4: exclusion by Loan id (old) against exclusion by holder (new), in S1′ and A modes; design A's letter (AText).
Console.WriteLine("\nRound 4 (rows that differ, plus the loop cases):\n| Case | SPEC | S1′ by id | S1′ by holder | A by id | A text | A by holder |\n| --- | --- | --- | --- | --- | --- | --- |");
foreach (var c in Cases.All().Where(c => !Cases.Round11(c))) // S1′ models none of round 11's rules
{
    var row = new[] { new Analysis(c, Mode.S1P, Abl.IdExclusion), new Analysis(c, Mode.S1P), new Analysis(c, Mode.A, Abl.IdExclusion), new Analysis(c, Mode.A, Abl.AText), new Analysis(c, Mode.A) };
    string W(Analysis a) => V(a) + (c.Err && a.Errors.Count == 0 ? " **UNSOUND**" : "");
    if (c.Id.StartsWith("20") || !row[0].ErrorNodes().SequenceEqual(row[1].ErrorNodes()) || !row[2].ErrorNodes().SequenceEqual(row[4].ErrorNodes()) || !row[3].ErrorNodes().SequenceEqual(row[4].ErrorNodes()))
        Console.WriteLine($"| {c.Id} | {(c.Err ? "error" : "valid")} | {string.Join(" | ", row.Select(W))} |");
}
var phantomVariants = new[] { Abl.PhantomAlone, Abl.PhantomKeepsReceiver, Abl.PhantomParentsAtCall };
// Round 10 (the chosen 1b design diff): each variant removes, or restores, one rule in design A mode.
var round10Variants = new[] { Abl.NoRouteReach, Abl.RouteWriteOnly, Abl.RouteAllParents, Abl.ExcuseAtCallOnly, Abl.LoanPastShared,
    Abl.ObsSharedCut, Abl.NoFreshClosure, Abl.RefParentsInTransit, Abl.SliceIndexInline, Abl.PrefixKill, Abl.EndedByLoan, Abl.GammaGuardsOnly, Abl.FreshParents, Abl.FreshOnly, Abl.DestroyAsDrop, Abl.SeedLiveOutOnly };
var reviewMutations = new[] { Abl.NoP3A, Abl.NoParentsA };
// Round 11 (E13, E14): each variant restores one behavior of the reviewed 1b branch, or applies round 11's fix (SecureAcquire).
// Round 12: the first four apply to round 12's design A; Implemented1b keeps the reviewed branch's MIR (G123); SecureAcquire and
// AlwaysSecure are round 11's E13 on round 11's M and on M′ (the E13 design review's run).
var round11Variants = new[] { Abl.CalleeNotActivated, Abl.InputsOnCS, Abl.PairAcquireLive, Abl.NoRawStore, Abl.Implemented1b, Abl.SecureAcquire, Abl.AlwaysSecure };
// Round 12 (E13 as the owner chose, R1): each variant restores an earlier rule or MIR form, or removes one clause of CS_i.
var round12Variants = new[] { Abl.Round11, Abl.AcquireAtOperation, Abl.ShapeReservation, Abl.IntrinsicAllOperands, Abl.NoEarlierValues, Abl.NoLaterRoots, Abl.EarlierRoots, Abl.NoOwnValue, Abl.OwnValueEverywhere };
foreach (var ab in new[] { Abl.KillAcrossDeref, Abl.FirstDerefBase, Abl.NoTransit }.Concat(phantomVariants).Concat(round10Variants).Concat(round11Variants).Concat(round12Variants))
{
    var aKill = Cases.All().Select(c => (c, a: new Analysis(c, Mode.A, ab), b: new Analysis(c, Mode.A)))
        .Where(x => !x.a.ErrorNodes().SequenceEqual(x.b.ErrorNodes())).Select(x => $"{x.c.Id}: {V(x.a)}" + (x.c.Err && x.a.Errors.Count == 0 ? " **UNSOUND**" : !x.c.Err && x.a.Errors.Count > 0 ? " (over-strict)" : ""));
    Console.WriteLine($"Design A with {ab}: " + string.Join("; ", aKill.DefaultIfEmpty("no change")));
}
// Round 9 (G113): the F6 cases under design A, its first-Deref variant and each rejected F6 option; class counts.
Console.WriteLine("\nRound 9 (F6 cases):\n| Case | SPEC | A | A FirstDerefBase | " + string.Join(" | ", phantomVariants) + " |\n| --- | --- | --- | --- | --- | --- | --- |");
foreach (var c in Cases.All().Where(c => c.Id.StartsWith("33") || c.Id.StartsWith("36")))
{
    string W(Analysis a) => V(a) + (c.Err && a.Errors.Count == 0 ? " **UNSOUND**" : "");
    var row = new[] { Abl.None, Abl.FirstDerefBase }.Concat(phantomVariants).Select(x => W(new Analysis(c, Mode.A, x)));
    Console.WriteLine($"| {c.Id} | {(c.Err ? "error" : "valid")} | {string.Join(" | ", row)} |");
}
// Round 10: the 1b rules' cases under design A, its first-Deref base and the ablation of each rule.
var r10cols = new[] { Abl.None, Abl.FirstDerefBase, Abl.NoRouteReach, Abl.RouteWriteOnly, Abl.LoanPastShared, Abl.FreshOnly, Abl.FreshParents, Abl.SliceIndexInline, Abl.EndedByLoan, Abl.DestroyAsDrop, Abl.SeedLiveOutOnly };
Console.WriteLine("\nRound 10 (1b rules):\n| Case | SPEC | A | A FirstDerefBase | " + string.Join(" | ", r10cols.Skip(2)) + " |\n|" + string.Concat(Enumerable.Repeat(" --- |", r10cols.Length + 2)));
foreach (var c in Cases.All().Where(c => c.Id is "10a" or "10b" or "10c" or "10d" or "13d" or "13e" or "19a" or "21a" or "26a" or "26b" or "27" || new[] { "33", "36", "38", "39", "40", "41", "42", "43", "44" }.Any(c.Id.StartsWith) || Cases.Review1b(c)))
{
    string W(Analysis a) => V(a) + (c.Err && a.Errors.Count == 0 ? " **UNSOUND**" : !c.Err && a.Errors.Count > 0 ? " (over-strict)" : "");
    Console.WriteLine($"| {c.Id} | {(c.Err ? "error" : "valid")} | {string.Join(" | ", r10cols.Select(x => W(new Analysis(c, Mode.A, x))))} |");
}
// Round 11 (E13, E14): the core review's B1, B2 and B4 cases under design A, PrefixKill and each round-11 variant.
Console.WriteLine("\nRound 11 (E13, E14):\n| Case | Program | SPEC | A | A PrefixKill | " + string.Join(" | ", round11Variants) + " | class A | class PrefixKill |\n|"
    + string.Concat(Enumerable.Repeat(" --- |", round11Variants.Length + 7)));
foreach (var c in Cases.All().Where(Cases.Round11))
{
    string W(Analysis a) => V(a) + (c.Err && a.Errors.Count == 0 ? " **UNSOUND**" : !c.Err && a.Errors.Count > 0 ? " (over-strict)" : "");
    var a0 = new Analysis(c, Mode.A); var pk = new Analysis(c, Mode.A, Abl.PrefixKill);
    var spec = c.Err ? "error" + (c.At.Length > 0 ? " at `" + string.Join("`, `", c.At) + "`" : "") : "valid";
    Console.WriteLine($"| {c.Id} | {c.Title} | {spec} | {W(a0)} | {W(pk)} | {string.Join(" | ", round11Variants.Select(x => W(new Analysis(c, Mode.A, x))))} | {Cls(c, a0)} | {Cls(c, pk)} |");
}
// Round 12 (E13, owner's option R1): the cases 130+ under design A, PrefixKill and each round-12 variant, with each one's class.
Console.WriteLine("\nRound 12 (E13, operand order):\n| Case | Program | SPEC | A | A PrefixKill | " + string.Join(" | ", round12Variants) + " | class A | class PrefixKill | class Round11 | class NoOwnValue |\n|"
    + string.Concat(Enumerable.Repeat(" --- |", round12Variants.Length + 9)));
foreach (var c in Cases.All().Where(Cases.Round11))
{
    string W(Analysis a) => V(a) + (c.Err && a.Errors.Count == 0 ? " **UNSOUND**" : !c.Err && a.Errors.Count > 0 ? " (over-strict)" : "");
    var a0 = new Analysis(c, Mode.A); var pk = new Analysis(c, Mode.A, Abl.PrefixKill);
    var spec = c.Err ? "error" + (c.At.Length > 0 ? " at `" + string.Join("`, `", c.At) + "`" : "") : "valid";
    Console.WriteLine($"| {c.Id} | {c.Title} | {spec} | {W(a0)} | {W(pk)} | {string.Join(" | ", round12Variants.Select(x => W(new Analysis(c, Mode.A, x))))} | {Cls(c, a0)} | {Cls(c, pk)} | {Cls(c, new Analysis(c, Mode.A, Abl.Round11))} | {Cls(c, new Analysis(c, Mode.A, Abl.NoOwnValue))} |");
}
// Case 23a review (cases 50-94 and their G110 forms): A, FirstDerefBase, PrefixKill (no base: a destroyed Move Path ends
// every Loan whose path it prefixes exactly) and round 2's KillAcrossDeref.
Console.WriteLine("\nCase 23a review cases (round-10 rules):\n| Case | Program | SPEC | A | A FirstDerefBase | A PrefixKill | A KillAcrossDeref | class A | class FirstDerefBase |\n| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
foreach (var c in Cases.All().Where(Cases.Review23a))
{
    string W(Analysis a) => V(a) + (c.Err && a.Errors.Count == 0 ? " **UNSOUND**" : "");
    var a0 = new Analysis(c, Mode.A); var fd = new Analysis(c, Mode.A, Abl.FirstDerefBase);
    var spec = c.Err ? "error" + (c.At.Length > 0 ? " at `" + string.Join("`, `", c.At) + "`" : "") : "valid";
    Console.WriteLine($"| {c.Id} | {c.Title} | {spec} | {W(a0)} | {W(fd)} | {W(new Analysis(c, Mode.A, Abl.PrefixKill))} | {W(new Analysis(c, Mode.A, Abl.KillAcrossDeref))} | {Cls(c, a0)} | {Cls(c, fd)} |");
}
var pkDiff = Cases.All().Select(c => (c, a: new Analysis(c, Mode.A, Abl.PrefixKill), b: new Analysis(c, Mode.A, Abl.FirstDerefBase)))
    .Where(x => !x.a.ErrorNodes().SequenceEqual(x.b.ErrorNodes())).Select(x => $"{x.c.Id}: PrefixKill {V(x.a)}, FirstDerefBase {V(x.b)}");
Console.WriteLine("PrefixKill against FirstDerefBase on all cases: " + string.Join("; ", pkDiff.DefaultIfEmpty("no difference")));
var notSubset = Cases.All().Where(c => !new Analysis(c, Mode.A, Abl.FirstDerefBase).Errors.All(e => new Analysis(c, Mode.A).Errors.Contains(e))).Select(c => c.Id);
Console.WriteLine("Cases where FirstDerefBase reports an error fact design A does not: " + string.Join(", ", notSubset.DefaultIfEmpty("none (FirstDerefBase only removes errors)")));
foreach (var ab in new[] { Abl.None, Abl.FirstDerefBase, Abl.PrefixKill })
    Console.WriteLine($"Review cases, design A{(ab == Abl.None ? "" : " with " + ab)}: " + string.Join(", ", Cases.All().Where(Cases.Review23a)
        .GroupBy(c => Cls(c, new Analysis(c, Mode.A, ab)) is var s && s.StartsWith("(a)") ? "listed loss" : s).OrderBy(g => g.Key)
        .Select(g => $"{g.Count()} {g.Key} ({string.Join(", ", g.Select(c => c.Id))})")));
// Mutations (review N4): FirstDerefBase with one safeguard removed; the error cases each one newly accepts.
foreach (var ab in reviewMutations.Concat([Abl.NoTransit, Abl.IdExclusion]))
{
    var lost = Cases.All().Where(c => c.Err && new Analysis(c, Mode.A, Abl.None, true).Errors.Count > 0 && new Analysis(c, Mode.A, ab, true).Errors.Count == 0).Select(c => c.Id);
    Console.WriteLine($"FirstDerefBase with {ab}: newly UNSOUND " + string.Join(", ", lost.DefaultIfEmpty("none")));
}
Round13(); // round 13: the design (A with PrefixKill), its variants and the cases 130+
Console.WriteLine("\nSummary (all cases):");
foreach (var (ab, fd) in new[] { (Abl.None, false), (Abl.FirstDerefBase, false), (Abl.PrefixKill, false), (Abl.PhantomParentsAtCall, false),
    (Abl.EndedByLoan, false), (Abl.EndedByLoan, true), (Abl.GammaGuardsOnly, false), (Abl.GammaGuardsOnly, true), (Abl.FreshParents, false), (Abl.FreshParents, true), (Abl.FreshOnly, false), (Abl.FreshOnly, true), (Abl.DestroyAsDrop, false), (Abl.DestroyAsDrop, true), (Abl.SeedLiveOutOnly, false), (Abl.SeedLiveOutOnly, true),
    (Abl.Implemented1b, false), (Abl.Implemented1b, true), (Abl.SecureAcquire, false), (Abl.SecureAcquire, true), (Abl.AlwaysSecure, false), (Abl.AlwaysSecure, true),
    (Abl.CalleeNotActivated, false), (Abl.InputsOnCS, false), (Abl.PairAcquireLive, false), (Abl.NoRawStore, false),
    (Abl.Round11, false), (Abl.Round11, true), (Abl.AcquireAtOperation, false), (Abl.AcquireAtOperation, true), (Abl.ShapeReservation, false), (Abl.IntrinsicAllOperands, false),
    (Abl.NoEarlierValues, false), (Abl.NoLaterRoots, false), (Abl.EarlierRoots, false), (Abl.NoOwnValue, false), (Abl.NoOwnValue, true), (Abl.OwnValueEverywhere, false), (Abl.OwnValueEverywhere, true) })
    Console.WriteLine($"Design A{(fd ? " with FirstDerefBase" : "")}{(ab == Abl.None ? "" : (fd ? " and " : " with ") + ab)}: " + string.Join(", ", Cases.All()
        .GroupBy(c => Cls(c, new Analysis(c, Mode.A, ab, fd)) is var s && s.StartsWith("(a)") ? "listed loss" : s).OrderBy(g => g.Key)
        .Select(g => $"{g.Count()} {g.Key}" + (g.Key != "same" ? " (" + string.Join(", ", g.Select(c => c.Id)) + ")" : ""))));
Console.WriteLine();
foreach (var ab in Enum.GetValues<Abl>().Skip(1).Where(x => x is not (Abl.AIssueNotEnded or Abl.IdExclusion or Abl.AText or Abl.LastDerefBase or Abl.FirstDerefBase) && !phantomVariants.Contains(x) && !round10Variants.Contains(x) && !reviewMutations.Contains(x) && !round11Variants.Contains(x) && !round12Variants.Contains(x) && !round13Variants.Contains(x)))
{
    string At(Analysis a) => "`" + string.Join("`, `", a.ErrorNodes()) + "`";
    var changed = Cases.All().Where(c => !Cases.NoS1P(c)).Select(c => (c, a: new Analysis(c, Mode.S1P, ab), b: new Analysis(c, Mode.S1P)))
        .Where(x => !x.a.ErrorNodes().SequenceEqual(x.b.ErrorNodes()))
        .Select(x => x.c.Id + ": " + (x.a.Errors.Count == 0
            ? x.c.Err ? "**UNSOUND** (accepted)" : "accepted (SPEC-valid, a loss recovered)"
            : x.c.Err ? "error moves to " + At(x.a) : (x.c.S1Err == true ? "rejected (an intended loss) at " : "over-strict: error at ") + At(x.a)));
    Console.WriteLine($"| {ab} | {string.Join("; ", changed.DefaultIfEmpty("none"))} |");
}
return finalFails;

// One refinement removed each. NoMoveBaseOp, NoUniqBaseOp, ExchangeKills and NoReplaceException change one S1′ clause;
// BaseOpMovePathsOnly limits base_op's K to move paths (plan §4.3 read literally). S1ResolvePlaces is the alternative fix:
// no exclusive-borrow base_op, but Loan places resolved at use.
enum Abl { None, KillKeepOnly, NoMaterialize, SyntacticAccess, NoAuthority, NoClosureFeedback, FlatFlow, NoReservation,
    NoMoveBaseOp, NoUniqBaseOp, BaseOpMovePathsOnly, ExchangeKills, NoReplaceException, S1ResolvePlaces, AIssueNotEnded,
    IdExclusion, AText, // round 4: the pre-fix by-id rule (S1′ and A), and design A's letter (A mode)
    KillAcrossDeref,    // round 5: `ended` on the first move path entered (round 2), not only with no Deref between B and K
    NoTransit,          // round 5: the round-4 rule without the in-transit facts
    LastDerefBase,      // round 6: A mode with the plan's B (last Deref) instead of A2's root-most B
    NoSharedCut,        // round 6: an observing destruction reaches exclusively past shared Derefs
    SharedBaseOp,       // round 6: S1′ plus P3 (a shared borrow of the base or an ancestor is a base operation vs an exclusive child)
    P3RootMost,         // round 6: S1′ plus P3 plus the root-most kill (KillAcrossDeref)
    FirstDerefBase,     // round 8: A mode with the first-Deref B; K an exact prefix of B, any index after K (case 23a proposal)
    PhantomAlone,       // round 9: A mode with rounds 7-8's F6 encoding: the result keeps Φ_c without the receiver's Loans
    PhantomKeepsReceiver, // round 9: A mode, the result keeps the receiver's whole value, its own Loan R included
    PhantomParentsAtCall, // round 9: A mode, Φ_c issued at the call with R2's parents of recv(c), not the receiver's value
    // Round 10 (the chosen 1b design diff). Each removes, or restores, one round-10 rule in design A mode:
    NoRouteReach,       // G119 off: a reserved Loan is reached only as `hits` says (round 9's A3; B's RouteShallow)
    RouteWriteOnly,     // G119 as B's prototype wrote it: Write and Destroy kinds only, not a destroying activation (38g)
    RouteAllParents,    // G119 option B of designer A: every Loan a pending reservation holder carries is reached (not chosen)
    ExcuseAtCallOnly,   // 33c: A3's letter before the round-7 erratum (no derived excuse at the argument reservations)
    LoanPastShared,     // G110 off: A3's letter, a Loan at every Ref, past a shared Deref too, all R2 parents, and the obs cut
    ObsSharedCut,       // G110 on, but the obs cut (`throughShared`) restored: expected vacuous
    NoFreshClosure,     // B1 off: no seed; the closure starts only from entry-set Loans (A3's letter)
    RefParentsInTransit, // a reading of A3's val_in(Ref X): the Ref's R2 parents count as stored values (transit)
    SliceIndexInline,   // Slice index off: an Index of a Slice handle is an inline step (A3's letter)
    // Case 23a review: PrefixKill = no base, a destroyed Move Path K ends every Loan whose whole path K prefixes exactly;
    // mutations of FirstDerefBase: no R2 parents (NoParentsA), Moves and Borrows shallow (NoP3A).
    PrefixKill, NoParentsA, NoP3A,
    EndedByLoan,        // finding 1 off: err(ended) by the Loans issued into H for every EndBorrow (A3 §5's row); no γ_H
    GammaGuardsOnly,    // GuardOrigin, but every other EndBorrow keeps the Loan row
    FreshParents,       // B1 without the CS_call seed: a stored Ref's own Loan and R2 parents seed every check at ℓ
    FreshOnly,          // 39o's row (the chosen 1b rule before the design review): only a stored Ref's own direct Loan seeds
    DestroyAsDrop,      // a destroying call's destruction as a Drop checked on CS, with the drop's write (rounds 6-10's encoding)
    SeedLiveOutOnly,    // E5's stored value only into a holder in live_out(ℓ) or E, not one made live by the closure
    // Round 11 (E13, E14). Each restores one behavior of the reviewed 1b branch (claude/a5-1b-core 8961f577), in A mode:
    CalleeNotActivated, // B1: a Value callee's reservation is never activated (it stays `ref` in its holder)
    InputsOnCS,         // B2: the accesses a Call's inputs make at the Call are checked on CS(ℓ), the holders live after ℓ
    PairAcquireLive,    // E13 (3) off: a PairAcquire Reborrow is a live Uniq Loan wherever it is formed (no reservation)
    NoRawStore,         // B4: a raw-storing intrinsic stores nothing (A3 §4: every operand → result)
    Implemented1b,      // all four above: the reviewed branch
    SecureAcquire,      // round 11's finding: the MIR also secures a PairAcquire that a later operand left at its Call follows
    AlwaysSecure,       // reviewer (E13 M'): every PairAcquire operand is secured into its own temp (Case.Always)
    // Round 12 (A3 E13, the owner's option R1: A5 handles operand evaluation order). Design A runs E13 as stated on the round-12
    // builder's MIR (M′ and the input marker). Each variant restores an earlier rule or MIR form, or changes one clause of CS_i:
    Round11,            // round 11's E13: a Call's inputs on CS_call(ℓ, −), any other operation's operands on CS(ℓ); round 11's MIR (G123)
    AcquireAtOperation, // E13 (1) on G123's MIR: a PairAcquire an operation's later operand follows is left at ℓ as an operand (no M′)
    ShapeReservation,   // no marker: A5 derives a PairAcquire's reservation from the MIR shape `$t = acquire X` moved into a Call (Case.Shape)
    IntrinsicAllOperands, // A3 §4's intrinsic row that E14 replaced: every operand → result, Value (Case.IntrinsicRow)
    NoEarlierValues,    // CS_i without ⋃_{j<i} val_in(o_j)
    NoLaterRoots,       // CS_i without ⋃_{j>i} σ_in(root(o_j))
    EarlierRoots,       // CS_i with σ_in(root(o_j)) for j<i instead of val_in(o_j) (round 11's CS_call(ℓ, −) at every operation)
    NoOwnValue,         // E13 as first stated: CS_i with val_in(o_j) for j < i only, and no own value at a single-operand rvalue
    OwnValueEverywhere, // the own-value term on every node Read or Move (Calls, BinaryOps, discards, Discriminant and Len reads
                        // included): an upper bound for the operands rounds 1-11 encode without marking them
    // Round 13 (A3 E13 rewritten, "one rule for every access"; the second design review's flags DOV, ROP, AV, ROS+RPT folded in).
    // The design takes each term; each variant restores the clause it superseded, or tries the review's alternative:
    NoDestroyValue,     // DOV off: an observing destruction of X does not take val(X) (round 12: CS(ℓ), CS_call(ℓ, t_K))
    BorrowSeedOnly,     // ROP off: DECISIONS R4-M57, a borrow's stored value only seeds (E5), into a live destination
    ParentsAsTransit,   // ROP off, the review's other fix: the R2 parents' holders in transit (RefParentsInTransit) and the
                        // stored value seeding whatever the destination's liveness (ROS)
    ActivationLiveIn,   // AV off: an activation and a destroying call's destroy on CS_call(ℓ, t)'s live_in(ℓ) (round 12)
    CurrentBuilder,     // today's builder MIR (Case.Built): named arguments in slot order, captures without G123 or M′
    KeepCSTerms,        // a borrow and an observing Drop also take CS(ℓ)'s transit and E5's seed (the review's additive run)
    BorrowWithoutSeed,  // round 13's first run: the borrow row without E5's seed (its own Loan into a live destination): 41o accepted
    ParentsEveryLayer,  // the borrow row's parents over every layer, ⋃_{j<k}, instead of E3's bound ⋃_{m≤j<k} (round 13's first run)
    OnePointDrop }      // the one-point encodings of 24c, 29b and 91 (Case.OnePoint): a drop and a store at one location

enum K { Read, Write, Move, SBorrow, UBorrow, Destroy }
enum Mode { PROV, S1, S1P, A }
// SharedAt (round 10, G110): the number of borrow Derefs of Place up to and including its last shared one (ref, objref,
// a ref-case pair layer); 0 when the path has no shared Deref.
// Marker (round 10, finding 1, GuardOrigin): γ_H, the guard-local Origin g of a guard candidate H (SPEC 14.8.3); it names no
// Place and never conflicts.
// Pair (round 11, E13 (3)): a PairAcquire Reborrow secured into a temp, a reservation there.
// OpAccess (round 12, AcquireAtOperation's MIR): a Borrow left at its operation as an operand; its access is that operand's.
record Issue(string Id, string Place, bool Uniq, string Into, bool Reserve, bool Param, bool InPlace = false, bool Phantom = false, string Recv = null, int SharedAt = 0, bool Marker = false, bool Pair = false, bool OpAccess = false);
// InPlace: the exclusive borrow is a `replace` target. Input (round 11, E13 (1)): an access a Call's input makes at the Call.
// From (round 13): the Issue whose borrow access this is (a Ref's or a Reborrowing PairAcquire's borrow of its Place).
record Acc(K Kind, string Place, string Except, bool InPlace = false, bool Input = false, Issue From = null);
record Res(HashSet<string> Names, HashSet<string> Leaves, HashSet<string> Auth);

// Holder and Place paths: segments joined by '.', "*" = follow a reference, "[]" = non-path index, digits = constant index.
static class Path
{
    public static string[] Segs(string p) => p.Split('.');
    static bool IsIdx(string s) => s == "[]" || s.All(char.IsDigit);
    static bool SegEq(string a, string b) => a == b || (a == "[]" && IsIdx(b)) || (b == "[]" && IsIdx(a));
    public static bool Within(string k, string h) { var a = Segs(k); var b = Segs(h); return a.Length >= b.Length && b.Select((s, i) => SegEq(a[i], s)).All(x => x); }
    public static bool Overlap(string a, string b) => Within(a, b) || Within(b, a);
    public static string Rest(string k, string h) => string.Join('.', Segs(k).Skip(Segs(h).Length));
    public static bool DerefBelow(string k, string h) => Segs(k).Skip(Segs(h).Length).Contains("*");
    // Access a against Loan place l. Inline parts exclude referents (§15.6.2).
    public static bool Hits(string a, string l) => Within(a, l) || (Within(l, a) && !DerefBelow(l, a));
    public static bool RunsThrough(string l, string h) => Within(l, h) && DerefBelow(l, h);
    public static string Join(string t, string rest) => rest.Length == 0 ? t : t + "." + rest;
    public static string Cap(string p) => string.Join('.', Segs(p).Take(8)); // PROV widening: a prefix overlaps more
    public static string Before(string p, string seg) { var s = Segs(p); int i = Array.IndexOf(s, seg); return i < 0 ? p : string.Join('.', s.Take(i)); }
    public static bool Escaped(string h) => h.StartsWith('^') || Within(h, "$ret");
}

class Node
{
    public readonly string Label; public readonly int Ix;
    public readonly List<Issue> Issues = new(); public readonly List<(string From, string To)> Flows = new();
    public readonly List<string> Defs = new(), Uses = new(), Activates = new(), Assumed = new(), Exchanges = new(), Disturbs = new(), Excuses = new(), EndBorrows = new();
    // Case 23a review: A3's EndBorrow H (guard cleanup): a use of H, then σ(H) = ∅; err(ended): a Loan issued into H still checked.
    public Node EndBorrow(string h, bool guard = true) { EndBorrows.Add(h); if (guard) GuardEnds.Add(h); Uses.Add(h); return this; }
    public readonly List<string> GuardEnds = new(); // round 10: EndBorrows of guard candidates (MirLocalRole.GuardCandidate)
    // F6 (S2, A3 §5): Φ_c, a phantom uniq Loan on the receiver's referent carried by the result; a disturbing point
    // turns Φ_c into Φ_c^dist in every set; a later preserves call on the same receiver excuses an undisturbed Φ_c.
    // recv: the receiver's reservation Loan R. Round 9 (G113): the result keeps the receiver's value with R replaced
    // by Φ_c, so Φ_c's values carry its parent Loans, as a child's do.
    public Node Phantom(string id, string place, string into, string recv) { Issues.Add(new(id, place, true, into, false, false, false, true, recv)); return this; }
    public Node Disturb(string id) { Disturbs.Add(id); return this; }
    public Node Excuse(string id) { Excuses.Add(id); return this; }
    public readonly List<(string Place, string Src)> Throughs = new(); public readonly List<Acc> Accs = new();
    public List<string> Succ;
    public Node(string label, int ix) { Label = label; Ix = ix; }
    public Node Use(params string[] h) { Uses.AddRange(h.Select(p => Path.Before(p, "*"))); return this; }
    public Node Def(params string[] h) { Defs.AddRange(h); return this; }                        // replaced(H, P)
    public Node Flow(string from, string to) { Flows.Add((from, to)); return Use(from); }          // flow(H1, H2, P)
    public Node Borrow(string id, string place, bool uniq, string into, bool reserve = false, bool inPlace = false, int sharedAt = 0) // issued(L, place, mode, P, H)
    { Issues.Add(new(id, place, uniq, into, reserve, false, inPlace, false, null, sharedAt)); return place.Contains('*') ? Use(place) : this; }
    public Node Param(string id, string h) { Issues.Add(new(id, "^" + h, true, h, false, true)); return Def(h); } // escaped referent
    public Node A(K kind, string place) { Accs.Add(new(kind, place, null)); return place.Contains('*') ? Use(place) : this; }
    public Node Assign(string target) { Def(target); return A(K.Write, target); }               // whole write of a move path
    public Node Through(string place, string src) { Throughs.Add((place, src)); A(K.Write, place); return Use(src); } // written_through
    public Node Activate(string id) { Activates.Add(id); return this; }                          // reserved(L, _, P) ends here
    public Node Exchange(string h) { Exchanges.Add(h); return this; } // exchange/swap target: wholly replaced, old value moved out
    public Node Drop(string h) => A(K.Destroy, h);                    // destroying h's old value runs a user drop that may reach through it
    // Review r2: A3's err(destroy) at a destroying call, checked on CS_call(ℓ, t_K) (live into the call, t_K excluded, seeded by σ_in(t_K)).
    public Node DestroyAt(string h, string reservation) { Accs.Add(new(K.Destroy, h, reservation)); return this; }
    // Round 11 (E13): a Call's inputs. Input: the access an input makes AT the Call (a Copy or Move of a direct Place operand
    // left at the Call), checked on CS_call(ℓ, −); AcquireAt: an exclusive PairAcquire Reborrow left at the Call, whose
    // formation and activation coincide there; Acquire: one secured into a temp, a TwoPhaseUniq reservation issued into it
    // (activated by Activate at its Call); Callee: a Value callee's reservation, activated at the Call like an argument's.
    // Round 12 (E13 (1)): Input marks every operand the MIR leaves at its operation ℓ, a Call's or any other's, in evaluation
    // order (the order of the Input calls); a Move of a secured temp included. Operation marks an operation other than a Call
    // (an Aggregate, a Closure, a BinaryOp), whose operands Round11 checks on CS(ℓ) as before round 11.
    public Node Input(K kind, string place) { Accs.Add(new(kind, place, null, Input: true)); return Use(place); }
    public Node AcquireAt(string place) => Input(K.UBorrow, place);
    public bool NotCall;
    public Node Operation() { NotCall = true; return this; }
    // Round 12 (AcquireAtOperation's MIR): a PairAcquire left at a non-Call operation as its operand, a live Uniq Loan stored
    // into `into` (the aggregate), its access the operand's, in order.
    public Node AcquireOp(string id, string place, string into) { Issues.Add(new(id, place, true, into, false, false, OpAccess: true)); return Input(K.UBorrow, place); }
    public Node Acquire(string id, string place, string into) { Issues.Add(new(id, place, true, into, true, false, Pair: true)); return Use(place); }
    public readonly List<string> Callees = new();
    public Node Callee(string id) { Callees.Add(id); return Activate(id); }
    // Round 11 (E14): a Call whose contract writes the value of `src` into storage reached through the raw pointer `ptr`
    // stores it into E, the referent of a raw pointer (A3 §5: val(raw-derived Place) ∪= {Φ_E}; SPEC 15.6).
    public readonly List<(string Ptr, string Src)> RawStores = new();
    public Node RawStore(string ptr, string src) { RawStores.Add((ptr, src)); return Use(ptr, src); }
    public Node Goto(params string[] labels) { Succ = labels.ToList(); return this; }
    public Node Assume(string label) { Assumed.Add(label); return this; }                        // assumed edge (§14.10.3)
}

class Case
{
    public readonly string Id, Title; public readonly List<Node> Nodes = new();
    public bool Err; public string[] At = []; public bool? S1Err; public string[] S1At; public bool S1POnly; public Func<Analysis, string> Extra;
    public string ALoss; // the listed design-A loss that explains an extra rejection (round 3)
    public Case Inline;  // round 10: the same program with a Slice Index as an inline step (SliceIndexInline, A3's letter)
    public Case AsDrop;  // round 10: the same program with a destroying call's destruction as a Drop on CS (DestroyAsDrop)
    public Case Always;  // reviewer: M', every PairAcquire secured into its own temp (AlwaysSecure)
    public Case Secured; // round 11: the same program with a PairAcquire that a later at-Call input follows secured (SecureAcquire)
    public Case Shape;        // round 12: the same program with A5 deriving a reservation from the MIR shape (ShapeReservation)
    public Case IntrinsicRow; // round 12: the same program with A3 §4's intrinsic row, every operand → result (IntrinsicAllOperands)
    public Case Built;        // round 13: the MIR today's builder makes where E13 (2) prescribes another order (CurrentBuilder)
    public Case OnePoint;     // round 13: rounds 5-12's encoding with a drop and a store at one location (OnePointDrop)
    public Case(string id, string title) { Id = id; Title = title; }
    public Node N(string label) { var n = new Node(label, Nodes.Count); Nodes.Add(n); return n; }
    public Case Error(params string[] at) { Err = true; At = at; return this; }
    public Case Valid() { Err = false; return this; }
    public Case S1(bool err, params string[] at) { S1Err = err; S1At = at; return this; }
}

class Analysis
{
    readonly Mode M; readonly Abl X; readonly bool F; readonly List<Node> ns; readonly int N; // F: FirstDerefBase under another variant (review)
    readonly bool Pk; // round 13: E8's ending (PrefixKill) under another variant; the design is Mode.A with it
    readonly Dictionary<string, Issue> issue = new(); readonly Dictionary<string, int> issueAt = new(), activeAt = new();
    readonly Dictionary<string, HashSet<string>> places = new();
    readonly HashSet<string>[] excusedAt;
    readonly HashSet<string> gamma = new(); // the holders with a region marker
    HashSet<Fact>[] In; HashSet<string>[] liveIn; List<int>[] succ;
    public readonly List<(string L, int P, string What)> Errors = new();
    public readonly Case Enc; // round 12: the MIR form analysed (the case itself, or one of its other forms)

    public Analysis(Case c, Mode m, Abl x = Abl.None, bool firstDeref = false, bool prefixKill = false)
    {
        // Round 12: the MIR form. Design A and its variants run the round-12 builder's MIR (M′: Always, else Secured, else the
        // case's own); Round11, Implemented1b and AcquireAtOperation run round 11's (G123: the case's own); SecureAcquire runs M.
        // Round 13: CurrentBuilder runs today's builder MIR where E13 (2) prescribes another one (Case.Built).
        M = m; X = x; F = firstDeref; Pk = prefixKill;
        Enc = x == Abl.CurrentBuilder && c.Built != null ? c.Built : x == Abl.OnePointDrop && c.OnePoint != null ? c.OnePoint : x == Abl.SliceIndexInline && c.Inline != null ? c.Inline : x == Abl.DestroyAsDrop && c.AsDrop != null ? c.AsDrop
            : x == Abl.ShapeReservation && c.Shape != null ? c.Shape : x == Abl.IntrinsicAllOperands && c.IntrinsicRow != null ? c.IntrinsicRow
            : x == Abl.SecureAcquire ? c.Secured ?? c : x is Abl.Round11 or Abl.Implemented1b or Abl.AcquireAtOperation ? c
            : c.Always ?? c.Secured ?? c;
        ns = Enc.Nodes; N = ns.Count;
        foreach (var n in ns)
        {
            foreach (var s0 in n.Issues)
            {
                var s = x == Abl.NoReservation || s0.Pair && PairLive ? s0 with { Reserve = false } : s0; // PairLive: a live Uniq Loan
                issue[s.Id] = s; issueAt[s.Id] = n.Ix; places[s.Id] = new(); if (s.Param || m != Mode.PROV) places[s.Id].Add(s.Place);
            }
            if (x != Abl.NoReservation) foreach (var a in n.Activates) activeAt[a] = n.Ix;
        }
        if (PairLive) foreach (var a in activeAt.Keys.Where(a => issue[a].Pair).ToList()) activeAt.Remove(a); // never reserved, never activated
        foreach (var d in ns.SelectMany(n => n.Disturbs).Distinct()) // Φ_c^dist: the same phantom, no longer excusable
        { issue[d + "^dist"] = issue[d] with { Id = d + "^dist" }; issueAt[d + "^dist"] = issueAt[d]; places[d + "^dist"] = new(places[d]); }
        // Round 10, 33c (A3 §5 excused, second row, the round-7 erratum): excused(L, ℓ) also where ℓ forms an argument
        // reservation of a Call c2 with excused(L, c2). Derived from c2's activations, never stated per formation.
        // Round 10 (finding 1, planning-session decision, GuardOrigin): a guard candidate H (MirLocalRole.GuardCandidate) has
        // the marker γ_H of its guard-local Origin,
        // issued into H by every Ref and every whole Copy/Move into H, carried like a Loan, never ended, never conflicting.
        if (m == Mode.A && x != Abl.EndedByLoan)
            foreach (var h in ns.SelectMany(n => n.GuardEnds).Select(Root).Distinct())
            { gamma.Add(h); issue["γ" + h] = new("γ" + h, "γ" + h, false, h, false, false, Marker: true); places["γ" + h] = ["γ" + h]; }
        excusedAt = ns.Select(n => n.Excuses.ToHashSet()).ToArray();
        if (m == Mode.A && x != Abl.ExcuseAtCallOnly)
            foreach (var n in ns) foreach (var r in n.Activates) excusedAt[issueAt[r]].UnionWith(n.Excuses);
        BuildEdges(); Carries();
        if (m == Mode.A) { LivenessA(); CheckA(); } else { Liveness(); Check(); }
    }

    // Round 11: the reviewed 1b branch's behaviors (each variant restores one; Implemented1b all four).
    bool CalleeOff => X is Abl.CalleeNotActivated or Abl.Implemented1b;
    bool InputsOnCS => X is Abl.InputsOnCS or Abl.Implemented1b;
    bool PairLive => X is Abl.PairAcquireLive or Abl.Implemented1b;
    bool NoRawStore => X is Abl.NoRawStore or Abl.Implemented1b;
    // Round 12: round 11's E13 (1) (a Call's inputs on CS_call(ℓ, −), any other operation's operands on CS(ℓ)), kept by the
    // variants that reproduce round 11's runs.
    bool R11Rule => X is Abl.Round11 or Abl.SecureAcquire or Abl.AlwaysSecure;
    // Round 12 (planning session on finding 1): an operand's value, its own included, is used by its operation after the
    // access (SPEC 15.6; rustc E0505). Off in the variants that reproduce earlier rules.
    bool OwnValue => X is not (Abl.NoOwnValue or Abl.Round11 or Abl.SecureAcquire or Abl.AlwaysSecure or Abl.Implemented1b);
    // Round 13 (E13 rewritten): the values each access takes. Off in the variants that reproduce rounds 11-12's runs.
    bool Earlier => X is Abl.Round11 or Abl.SecureAcquire or Abl.AlwaysSecure or Abl.Implemented1b;
    bool DestroyValue => X != Abl.NoDestroyValue && !Earlier;                                   // an observing destruction takes val(X)
    bool BorrowParents => X is not (Abl.BorrowSeedOnly or Abl.ParentsAsTransit) && !Earlier;    // a borrow takes its R2 parents
    bool ActivationOnValues => X != Abl.ActivationLiveIn && !Earlier;                           // ⋃_{o_j≠t} val(o_j), not live_in(ℓ)
    // E13 (1): a borrow and an observing Drop take no transit; a borrow keeps E5's seed (Seed), a Drop takes none. The variants
    // that restore round 12's check for one of them restore its CS(ℓ) (transit and seed) with it; KeepCSTerms keeps CS(ℓ) and adds
    // the own term (the review's additive run).
    bool OwnTerm(Acc a) => !Earlier && X != Abl.KeepCSTerms && (a.From != null ? X is not (Abl.BorrowSeedOnly or Abl.ParentsAsTransit)
        : a.Kind == K.Destroy && a.Except == null && X != Abl.NoDestroyValue);

    int IxOf(string l) { int i = ns.FindIndex(n => n.Label == l); return i >= 0 ? i : throw new Exception("no label " + l); }

    // edge(P, Q): runtime edges, plus assumed edges into nodes no runtime path reaches (§14.10.3, DEC R4-M20).
    void BuildEdges()
    {
        succ = ns.Select(n => n.Succ?.Select(IxOf).ToList() ?? (n.Ix + 1 < N ? [n.Ix + 1] : new List<int>())).ToArray();
        var reach = new bool[N]; var st = new Stack<int>(); st.Push(0);
        while (st.Count > 0) { var i = st.Pop(); if (reach[i]) continue; reach[i] = true; foreach (var q in succ[i]) st.Push(q); }
        foreach (var n in ns) foreach (var l in n.Assumed) if (!reach[IxOf(l)]) succ[n.Ix].Add(IxOf(l));
    }

    // carries(H, L, P) on entry to P; naive forward fixpoint, union at joins (may-facts).
    void Carries()
    {
        In = Enumerable.Range(0, N).Select(_ => new HashSet<Fact>()).ToArray();
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int i = 0; i < N; i++) { var o = M == Mode.A ? TransferA(i) : Transfer(i); foreach (var q in succ[i]) foreach (var t in o) changed |= In[q].Add(t); }
            if (M == Mode.PROV) // provenance: a Loan's place is the set of targets its borrow path resolves to
                foreach (var (id, at) in issueAt) if (!issue[id].Param)
                    foreach (var t in Resolve(issue[id].Place, at).Leaves) changed |= places[id].Add(Path.Cap(t));
        }
    }

    HashSet<Fact> Transfer(int i)
    {
        var n = ns[i]; var o = new HashSet<Fact>();
        bool KillH(string h) => n.Defs.Concat(n.Exchanges).Any(r => Path.Within(h, r));                      // killed(K, P)
        // destroyed_in_place(K, P): =, replace, end of storage (Defs). S1 as written also counts exchange/swap targets.
        IEnumerable<string> dip = M == Mode.S1 || X == Abl.ExchangeKills ? n.Defs.Concat(n.Exchanges) : M == Mode.S1P ? n.Defs : [];
        Func<string, bool> Ended = l => EndedBy(l, dip);                                                     // ended(L, P)
        foreach (var t in In[i]) if (!KillH(t.H) && !Ended(t.L)) o.Add(t);                                    // keep
        if (X == Abl.KillKeepOnly) Ended = _ => false;      // only `keep` sees `ended`
        var inn = In[i].Where(t => !Ended(t.L)).ToList();   // an ended Loan propagates through no rule at P
        foreach (var (from, to) in n.Flows)                                                                  // part-preserving flow
            foreach (var s in !from.Contains('*') ? [from] : X == Abl.FlatFlow ? [Path.Before(from, "*")] : Resolve(from, i).Leaves)
                foreach (var t in inn)
                    if (Path.Within(t.H, s)) o.Add((Path.Join(to, Path.Rest(t.H, s)), t.L));
                    else if (Path.Within(s, t.H)) o.Add((to, t.L));                                          // ancestor's whole-value Loans
        foreach (var (place, src) in n.Throughs)                                                             // written_through + reaches
        {
            var reach = Resolve(place, i).Leaves.Select(p => Path.Before(p, "[]")).ToList();
            foreach (var t in inn) if (Path.Overlap(t.H, src)) foreach (var r in reach) o.Add((r, t.L));
        }
        foreach (var s in n.Issues)                                                                          // issue + child+ materialization
        {
            o.Add((s.Into, s.Id));
            if (!s.Param && !s.Phantom && X != Abl.NoMaterialize) foreach (var l in Resolve(s.Place, i).Auth) if (!Ended(l)) o.Add((s.Into, l));
            if (s.Phantom) // round 9 (G113): the receiver's value, R replaced by Φ_c
                foreach (var t in inn) if (t.L != s.Recv && Path.Overlap(t.H, issue[s.Recv].Into)) o.Add((s.Into, t.L));
        }
        return Disturbed(n, o);
    }

    // base(L, B) (plan fe1a5b34): the Place storing the reference the borrow follows last, i.e. the prefix before the
    // last Deref step (null for a plain Loan). r.* → r; rr.*.* → rr.*; cursor.*.children.[] → cursor.
    string Base(string l)
    {
        var s = Path.Segs(issue[l].Place); int j = Array.LastIndexOf(s, "*");
        return j < 0 ? null : string.Join('.', s.Take(j));
    }

    // ended(L, P) :- base(L, B), destroyed_in_place(K, P), within(B, K), on move paths only: no Deref step between B and
    // K. KillAcrossDeref restores round 2's rule (the first move path entered may be destroyed).
    bool EndedBy(string l, IEnumerable<string> dip)
    {
        // A Move Path takes no own step (SPEC 15.1.3; the owner kept SPEC's letter on case 75, 2026-10-18), so a K through an obj
        // or rc `payload` is no Move Path and ends nothing.
        if (X == Abl.PrefixKill || Pk) // review N1: direct Loans too; Defs are move paths, so K holds no Deref (round 13: the design)
        {
            var ps = Path.Segs(issue[l].Place);
            return dip.Select(Path.Segs).Any(ks => !ks.Contains("[]") && !ks.Contains("*") && !ks.Contains("payload") && ps.Length >= ks.Length && ps.Take(ks.Length).SequenceEqual(ks));
        }
        var b = Base(l);
        if (b == null) return false;
        // Both modes use the plan's kill (round 6 correction): B is a move path, K is B or an ancestor with no Deref
        // between, and Index steps only for identical constant indexes. KillAcrossDeref is the root-most kill with the
        // may-overlap Within, which lets a constant index kill a dynamic one (unsound, 34a/34b). FirstDerefBase takes the
        // same B with the definite matcher: K is a move path and an exact prefix of B, whose later steps may be any index
        // (destroying arr destroys arr[i], 34c).
        if (X is Abl.KillAcrossDeref or Abl.P3RootMost) { b = Path.Before(b, "*"); return dip.Any(k => Path.Within(b, k)); }
        if (X == Abl.FirstDerefBase || F)
        {
            var fs = Path.Segs(Path.Before(b, "*"));
            return dip.Select(Path.Segs).Any(ks => !ks.Contains("[]") && !ks.Contains("payload") && fs.Length >= ks.Length && fs.Take(ks.Length).SequenceEqual(ks));
        }
        if (b.Contains('*')) return false;
        var bs = Path.Segs(b);
        return dip.Select(Path.Segs).Any(ks => bs.Length >= ks.Length && bs.Take(ks.Length).SequenceEqual(ks) && !bs.Skip(ks.Length).Contains("[]"));
    }

    // Round 6 (review r2, finding 3): an observing destruction of K reaches exclusively only down to the first shared
    // Deref. The Deref below K on Loan place p is shared when every Loan its holder carries is a shared Loan.
    bool SharedCut(string k, string p, int i)
    {
        if (X == Abl.NoSharedCut || M == Mode.A && X is not (Abl.LoanPastShared or Abl.ObsSharedCut)) return false; // round 10: G110 deletes it
        var s = Path.Segs(p); int from = Path.Segs(k).Length;
        int j = from <= s.Length ? Array.IndexOf(s, "*", from) : -1;
        if (j < 0) return false;
        var prefix = string.Join('.', s.Take(j));
        var holders = M == Mode.A ? [Root(prefix)] : Resolve(prefix, i).Leaves;
        var f = In[i].Where(t => holders.Any(h => M == Mode.A ? t.H == h : Path.Overlap(t.H, h))).ToList();
        return f.Count > 0 && f.All(t => !issue[t.L].Uniq && !issue[t.L].Param);
    }

    // Provenance resolution of a Place through the references it follows, at P. Names: every name of the Place
    // (base-relative and resolved); Leaves: targets with no further Loan; Auth: Loans of the references followed.
    Res Resolve(string path, int i, int depth = 0)
    {
        var r = new Res(new() { path }, new(), new());
        var segs = Path.Segs(path); int j = Array.IndexOf(segs, "*");
        var loans = j < 0 ? new List<string>() :
            In[i].Where(t => Path.Overlap(t.H, string.Join('.', segs.Take(j)))).Select(t => t.L).Distinct().ToList();
        if (loans.Count == 0 || depth > 10) { r.Leaves.Add(path); return r; }
        var rest = string.Join('.', segs.Skip(j + 1));
        foreach (var l in loans)
        {
            r.Auth.Add(l);
            foreach (var t in places[l].ToList())
            { var s = Resolve(Path.Join(t, rest), i, depth + 1); r.Names.UnionWith(s.Names); r.Leaves.UnionWith(s.Leaves); r.Auth.UnionWith(s.Auth); }
        }
        return r;
    }

    IEnumerable<string> Names(string l, int i) => M == Mode.PROV ? places[l] : places[l].SelectMany(p => Resolve(p, i).Leaves);

    // live(H, P) on entry to P: backward from uses, closed through live references: live(T) :- live(R), carries(R, L), names(L, T).
    void Liveness()
    {
        liveIn = Enumerable.Range(0, N).Select(_ => new HashSet<string>()).ToArray();
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int i = N - 1; i >= 0; i--)
            {
                var n = ns[i];
                var s = succ[i].SelectMany(q => liveIn[q]).Where(h => !n.Defs.Concat(n.Exchanges).Any(r => Path.Within(h, r))).ToHashSet();
                s.UnionWith(n.Uses);
                if (X != Abl.NoClosureFeedback) Close(s, i);
                foreach (var x in s) changed |= liveIn[i].Add(x);
            }
        }
        if (X == Abl.NoClosureFeedback) for (int i = 0; i < N; i++) Close(liveIn[i], i); // closure per point only
    }

    void Close(HashSet<string> s, int i)
    {
        var work = new Queue<string>(s);
        while (work.Count > 0)
        {
            var r = work.Dequeue();
            foreach (var t in In[i]) if (Path.Overlap(t.H, r)) foreach (var x in Names(t.L, i)) if (s.Add(x)) work.Enqueue(x);
        }
    }

    // error(L, P) :- live_loan(L, P), access(P, place, kind), overlap, conflicts(kind, mode(L, P)).
    void Check()
    {
        for (int i = 0; i < N; i++)
        {
            var n = ns[i]; var accs = n.Accs.ToList();
            accs.AddRange(n.Issues.Where(s => !s.Param && !s.Phantom).Select(s => new Acc(s.Uniq && !s.Reserve ? K.UBorrow : K.SBorrow, s.Place, null, s.InPlace)));
            accs.AddRange(n.Activates.Select(id => new Acc(K.UBorrow, issue[id].Place, id, issue[id].InPlace)));
            if (M == Mode.S1P) accs.AddRange(n.Exchanges.Select(h => new Acc(K.Move, h, null))); // exchanged/swapped: the old value moves out
            var after = succ[i].SelectMany(q => liveIn[q]).ToHashSet();
            bool LiveIn(string h) => Path.Escaped(h) || liveIn[i].Any(x => Path.Overlap(h, x));
            bool LiveAfter(string h) => Path.Escaped(h) || after.Any(x => Path.Overlap(h, x));
            bool Dest(string h) => n.Defs.Concat(n.Exchanges).Any(d => Path.Within(h, d));
            // Round 5: a value P stores (a flow into a holder live after P, or a write through a reference) is in transit
            // during P, so its source's entry facts count even if the source dies at P (24c).
            var transit = X == Abl.NoTransit ? [] : n.Flows.Where(f => LiveAfter(f.To))
                .SelectMany(f => f.From.Contains('*') ? Resolve(f.From, i).Leaves : [f.From])
                .Concat(n.Throughs.Where(t => Resolve(t.Place, i).Leaves.Any(r => LiveAfter(Path.Before(r, "[]")))).Select(t => t.Src)).ToList();
            foreach (var a in accs)
            {
                var res = X == Abl.SyntacticAccess ? new Res([a.Place], [a.Place], []) : Resolve(a.Place, i);
                if (X == Abl.NoAuthority) res.Auth.Clear();
                // base_op(P, K): a Move (S1, S1′), or an exclusive borrow other than as a `replace` target (S1′).
                // (H) plan: destroying an old value whose drop may reach the base is an exclusive use (K.Destroy, from Drop).
                bool baseOp = M != Mode.PROV && (a.Kind == K.Move ? X != Abl.NoMoveBaseOp
                    : a.Kind == K.Destroy ? M == Mode.S1P
                    : a.Kind == K.UBorrow && M == Mode.S1P && X is not (Abl.NoUniqBaseOp or Abl.S1ResolvePlaces) && (!a.InPlace || X == Abl.NoReplaceException));
                // Round 4, by holder: entry facts over holders live after P minus the destination's old value; an activation
                // over holders live into P (the call protects its arguments) minus the reservation's own holder.
                var facts = X == Abl.IdExclusion ? In[i].Where(t => LiveIn(t.H))
                    : a.Except != null ? In[i].Where(t => LiveIn(t.H) && !Path.Within(t.H, issue[a.Except].Into))
                    : In[i].Where(t => LiveAfter(t.H) && !Dest(t.H) || transit.Any(s => Path.Overlap(t.H, s)));
                foreach (var (h, l) in facts)
                {
                    if ((X == Abl.IdExclusion && l == a.Except) || res.Auth.Contains(l) || issue[l].Param || n.Excuses.Contains(l)) continue;
                    bool uniq = issue[l].Uniq && !Reserved(h, l, i);
                    // SharedBaseOp: P3 for S1′, a shared borrow of the base or an ancestor while an exclusive child is live.
                    bool op = baseOp || ((X is Abl.SharedBaseOp or Abl.P3RootMost) && M == Mode.S1P && a.Kind == K.SBorrow && !a.InPlace && uniq);
                    // error(L, P) :- live_loan(L, P), base(L, B), base_op(P, K), runs_through(B, K): K is B or a prefix of B
                    // across Deref steps, syntactically; an Index step matches unless both are distinct constants.
                    if (op && Base(l) is { } bb && res.Names.Any(k => Path.Within(bb, k) && (X != Abl.BaseOpMovePathsOnly || !k.Contains('*'))
                            && !(a.Kind == K.Destroy && SharedCut(k, issue[l].Place, i))))
                    { Err(l, i, $"base_op {a.Kind} {a.Place}"); continue; }
                    if (!uniq && a.Kind is K.Read or K.SBorrow) continue;
                    var lp = X == Abl.S1ResolvePlaces ? places[l].SelectMany(p => Resolve(p, i).Names).ToHashSet() : places[l];
                    if (res.Names.Any(x => lp.Any(p => Path.Hits(x, p)))) Err(l, i, $"{a.Kind} {a.Place}");
                }
            }
        }
    }

    // ---- Design A (design-A.md §3, §5): root holders + E (P1), deep shared borrows (P3), syntactic access Places,
    // layer-blind following (P10), content = holders named by direct Loans (+ E), check-time liveness closure.
    static string Root(string h) => h.StartsWith('^') ? "E" : Path.Segs(h)[0];
    static int Steps(string p) => Path.Segs(p).Count(s => s == "*");
    bool Direct(string l) => !issue[l].Param && !issue[l].Marker && !issue[l].Place.Contains('*');
    static HashSet<string> Sigma(IEnumerable<Fact> s, string h) => s.Where(t => t.H == h).Select(t => t.L).ToHashSet();
    HashSet<string> Refs(HashSet<string> S)
    {
        var r = S.Where(Direct).Select(l => Root(issue[l].Place)).ToHashSet();
        if (S.Any(l => issue[l].Param) || !S.Any(Direct)) r.Add("E"); // Φ_E, or no direct Loan
        return r;
    }
    HashSet<string> Content(IEnumerable<Fact> s, HashSet<string> S) => Refs(S).SelectMany(t => Sigma(s, t)).ToHashSet();

    HashSet<Fact> TransferA(int i)
    {
        var n = ns[i]; var o = new HashSet<Fact>();
        var inn = In[i].Where(t => !EndedBy(t.L, n.Defs)).ToList(); // ended (plan fe1a5b34), on every carrying rule
        var whole = n.Defs.Concat(n.Exchanges).Concat(n.EndBorrows).Concat(n.Accs.Where(a => a.Kind == K.Move).Select(a => a.Place)).Where(h => !h.Contains('.')).ToHashSet();
        foreach (var t in inn) if (!whole.Contains(t.H)) o.Add(t);   // P1: only a whole-root write, Move or storage end ends carrying
        HashSet<string> Val(string x) { var S = Sigma(inn, Root(x)); for (int k = Steps(x); k > 0; k--) S = Content(inn, S); return S; }
        foreach (var (from, to) in n.Flows) { foreach (var l in Val(from)) o.Add((Root(to), l)); if (gamma.Contains(Root(to))) o.Add((Root(to), "γ" + Root(to))); }
        foreach (var (place, src) in n.Throughs)                    // P10: every holder the carried direct Loans name
        {
            var S = Sigma(inn, Root(place)); for (int k = Steps(place); k > 1; k--) S = Content(inn, S);
            var targets = Steps(place) == 0 ? [Root(place)] : Refs(S);
            foreach (var l in Sigma(inn, Root(src))) foreach (var t in targets) o.Add((t, l));
        }
        // Round 11 (E14): a raw store of a call stores its input's value into E, ret(i, Value) = val ∪ content(val) (the
        // cases' values are references; content is empty there). NoRawStore: the intrinsic stores nothing (B4).
        if (!NoRawStore)
            foreach (var (_, src) in n.RawStores) { var v = Sigma(inn, Root(src)); v.UnionWith(Content(inn, v)); foreach (var l in v) o.Add(("E", l)); }
        foreach (var s in n.Issues)                                 // issue + parents of every followed layer
        {
            if (gamma.Contains(Root(s.Into))) o.Add((Root(s.Into), "γ" + Root(s.Into))); // finding 1: besides its Loan, if any
            // Round 10 (G110, SPEC 15.6.3 S1): a Borrow past a shared Deref forms no Loan of its own. It derives only from
            // the reference of its last shared Deref (layer SharedAt - 1) and those dereferenced after it, and carries
            // their Loans: content^j(σ(root)) for SharedAt - 1 ≤ j < k. Without a shared Deref (SharedAt = 0): unchanged.
            int sh = X == Abl.LoanPastShared || s.Param || s.Phantom ? 0 : s.SharedAt;
            if (sh > 0)
            {
                var D = Sigma(inn, Root(s.Place));
                for (int j = 0; j < Steps(s.Place); j++) { if (j >= sh - 1) foreach (var l in D) o.Add((Root(s.Into), l)); D = Content(inn, D); }
                continue;
            }
            // Fix found in round 3: a child Loan whose base this point destroys in place is ended here too (19a).
            if (X != Abl.AIssueNotEnded && EndedBy(s.Id, n.Defs)) { } else o.Add((Root(s.Into), s.Id));
            // Round 9 (G113): the result keeps the receiver's value with its own Loan R replaced by Φ_c, so Φ_c's
            // values carry its parent Loans (the receiver Ref's R2 parents), as a child's do.
            if (s.Phantom && X is not (Abl.PhantomAlone or Abl.PhantomParentsAtCall))
                foreach (var l in Sigma(inn, Root(issue[s.Recv].Into))) if (l != s.Recv || X == Abl.PhantomKeepsReceiver) o.Add((Root(s.Into), l));
            if (s.Param || (s.Phantom && X != Abl.PhantomParentsAtCall)) continue;
            var S = Sigma(inn, Root(s.Place));
            if (X != Abl.NoParentsA) for (int j = 0; j < Steps(s.Place); j++) { foreach (var l in S) o.Add((Root(s.Into), l)); S = Content(inn, S); }
        }
        return Disturbed(n, o);
    }

    void LivenessA() // backward over root holders; borrowing T uses T; no feedback from the closure
    {
        liveIn = Enumerable.Range(0, N).Select(_ => new HashSet<string>()).ToArray();
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int i = N - 1; i >= 0; i--)
            {
                var n = ns[i];
                var kill = n.Defs.Concat(n.EndBorrows).Concat(n.Accs.Where(a => a.Kind == K.Move).Select(a => a.Place)).Where(h => !h.Contains('.')).ToHashSet();
                var s = succ[i].SelectMany(q => liveIn[q]).Where(h => !kill.Contains(h)).ToHashSet();
                s.UnionWith(n.Uses.Select(Root));
                s.UnionWith(n.Issues.Where(x => !x.Param).Select(x => Root(x.Place)));
                s.UnionWith(n.Accs.Where(a => a.Kind != K.Write && a.Except == null).Select(a => Root(a.Place))); // review r2: a call's destruction (DestroyAt) uses only its operand
                // Round 10 (encoding fix): the Call's operand reads (Moves) the reservation holder, as A3's use(H, ℓ)
                // says; earlier rounds left it out where a case named no Use, so a holder looked dead in its window.
                s.UnionWith(n.Activates.Select(r => Root(issue[r].Into)));
                foreach (var x in s) changed |= liveIn[i].Add(x);
            }
        }
    }

    void CheckA()
    {
        for (int i = 0; i < N; i++)
        {
            var n = ns[i]; var accs = n.Accs.ToList();
            // A2 keeps the `replace` exemption: a destroying Ref is a shallow read, and its activation is not deep.
            // Round 11: PairLive forms a PairAcquire as a live Uniq Loan (issue[].Reserve is false), never activated;
            // CalleeOff never activates a Value callee's reservation (B1).
            // Round 12: an OpAccess Borrow (a PairAcquire left at its operation) makes its access as that operation's operand.
            // Round 13: each such borrow access names its Issue (From), so E13 (1) gives it its R2 parents.
            accs.AddRange(n.Issues.Where(s => !s.Param && !s.Phantom && !s.OpAccess).Select(s => new Acc(s.InPlace ? K.Read : s.Uniq && !issue[s.Id].Reserve ? K.UBorrow : K.SBorrow, s.Place, null, s.InPlace, From: s)));
            accs.AddRange(n.Activates.Where(id => !(CalleeOff && n.Callees.Contains(id)) && !(PairLive && issue[id].Pair))
                .Select(id => new Acc(K.UBorrow, issue[id].Place, id, issue[id].InPlace)));
            accs.AddRange(n.Exchanges.Select(h => new Acc(K.Move, h, null)));
            var after = succ[i].SelectMany(q => liveIn[q]).ToHashSet();
            var dest = n.Defs.Concat(n.Exchanges).Where(h => !h.Contains('.')).ToHashSet();
            var outSet = X == Abl.AText ? TransferA(i) : null;
            static bool Esc(string h) => h is "E" or "$ret";
            HashSet<string> SrcHolders(string x) // the holders a flow's value is read from (k reference layers)
            {
                if (Steps(x) == 0) return [Root(x)];
                var S = Sigma(In[i], Root(x)); for (int k = Steps(x); k > 1; k--) S = Content(In[i], S);
                return Refs(S);
            }
            HashSet<string> Targets(string place) // the holders a write through `place` reaches (P10)
            {
                if (Steps(place) == 0) return [Root(place)];
                var S = Sigma(In[i], Root(place)); for (int k = Steps(place); k > 1; k--) S = Content(In[i], S);
                return Refs(S);
            }
            // Transit clause (owner decision): σ_in of every value ℓ stores into a holder live after ℓ, into E, or through
            // a reference with a live destination.
            var transit = X == Abl.NoTransit ? [] : n.Flows.Where(f => after.Contains(Root(f.To)) || Esc(Root(f.To)))
                .SelectMany(f => SrcHolders(f.From))
                .Concat(n.Throughs.Where(t => Targets(t.Place).Any(h => after.Contains(h) || Esc(h))).Select(t => Root(t.Src)))
                .Concat(NoRawStore ? [] : n.RawStores.Select(t => Root(t.Src))).ToHashSet(); // round 11 (E14): a store into E
            // Round 13: ParentsAsTransit, the review's other fix: the stored value seeds whatever its destination's liveness (ROS),
            // and the Ref's parent holders are in transit (RefParentsInTransit, RPT).
            var stored = n.Issues.Where(s => !s.Param && !s.Phantom && (after.Contains(Root(s.Into)) || Esc(Root(s.Into)) || X == Abl.ParentsAsTransit)).ToList();
            if (X is Abl.RefParentsInTransit or Abl.ParentsAsTransit) // the holders each Ref's carried parents are read from (layers from the last shared on)
                foreach (var s in stored)
                {
                    var S = Sigma(In[i], Root(s.Place)); var hs = new HashSet<string> { Root(s.Place) };
                    for (int j = 0; j < Steps(s.Place); j++) { if (j >= Math.Max(s.SharedAt - 1, 0)) transit.UnionWith(hs); hs = Refs(S); S = Content(In[i], S); }
                }
            // Round 10 final (B1, the design review's rule replacing 39o's row; SPEC 15.6: "a value is also used wherever a
            // value that carries a Loan on a Place overlapping the first value's holder is used"): the closure of CS(ℓ) is
            // seeded with σ_in(T) for each T named by a direct Loan (E for Φ_E) of the value ℓ stores into a holder live after
            // ℓ or E: the Ref's own Loan and its R2 parents from the layers it carries (G110's start layer); the closure of
            // CS_call(ℓ, t) with those of σ_in(t). Conflict candidates are unchanged (the seeds only feed the closure).
            void Name(HashSet<string> into, string l) { if (issue[l].Param) into.Add("E"); else if (Direct(l)) into.Add(Root(issue[l].Place)); }
            HashSet<string> SeedOf(List<Issue> st)
            {
                var seed = new HashSet<string>();
                if (X != Abl.NoFreshClosure)
                    foreach (var s in st)
                    {
                        if (Direct(s.Id)) seed.Add(Root(s.Place)); // the Ref's own Loan (none past a shared Deref)
                        if (X == Abl.FreshOnly) continue;
                        var S = Sigma(In[i], Root(s.Place));
                        for (int j = 0; j < Steps(s.Place); j++) { if (j >= Math.Max(s.SharedAt - 1, 0)) foreach (var l in S) Name(seed, l); S = Content(In[i], S); }
                    }
                return seed;
            }
            var seedStore = SeedOf(stored);
            // A3 E5: "a holder live after ℓ" as transit reads it: in live_out(ℓ) or made live by the closure, so a holder named
            // by a direct Loan of a value live after ℓ (seeds included) also receives a stored value; to a fixpoint (120b).
            // SeedLiveOutOnly: live_out(ℓ) and E only.
            if (X is not (Abl.SeedLiveOutOnly or Abl.NoFreshClosure))
            {
                var live = new HashSet<string>();
                for (bool grew = true; grew;)
                {
                    grew = false;
                    var Fc = In[i].Where(t => Esc(t.H) || after.Contains(t.H) && !dest.Contains(t.H) || transit.Contains(t.H) || seedStore.Contains(t.H)).ToHashSet();
                    for (var work = new Queue<Fact>(Fc); work.Count > 0;)
                    {
                        var l0 = work.Dequeue().L;
                        if (!Direct(l0)) continue;
                        var T = Root(issue[l0].Place);
                        if (!after.Contains(T) && live.Add(T)) grew = true;
                        foreach (var t in In[i].Where(t => t.H == T)) if (Fc.Add(t)) work.Enqueue(t);
                    }
                    if (grew)
                    {
                        stored = n.Issues.Where(s => !s.Param && !s.Phantom && (after.Contains(Root(s.Into)) || Esc(Root(s.Into)) || live.Contains(Root(s.Into)))).ToList();
                        seedStore = SeedOf(stored);
                    }
                }
            }
            HashSet<string> Seed(Acc a) // CS_call(ℓ, t): the direct Loans of σ_in(t); FreshOnly, FreshParents: the store seed everywhere
            {
                if (a.Input && !InputsOnCS && !(R11Rule && n.NotCall)) return []; // round 11 (E13): CS_call(ℓ, −) excludes no holder, so it needs no seed; round 12: CS_i states none
                // Round 13 (E13 (1)): E5's seed belongs to the destination write, the stores and a borrow (BorrowWithoutSeed: not the
                // borrow); an observing Drop takes only val(X) (KeepCSTerms: CS(ℓ)'s seed too).
                if (OwnTerm(a) && (a.From == null || X == Abl.BorrowWithoutSeed)) return []; // a borrow keeps E5's seed (41o)
                if (a.Except == null || X is Abl.NoFreshClosure or Abl.FreshOnly or Abl.FreshParents) return seedStore;
                var t = new HashSet<string>(); foreach (var l in Sigma(In[i], issue[a.Except].Into)) Name(t, l); return t;
            }
            // Round 12: val_in(o): the value operand o reads at ℓ, from σ_in: a Copy or Move of a Place with k borrow Derefs
            // reads content^k(σ_in(root)); a Borrow left at ℓ (AcquireAtOperation) carries its R2 parents, its own Loan being
            // formed at ℓ and in no entry set.
            HashSet<string> OpVal(Acc o)
            {
                if (o.Kind is K.UBorrow or K.SBorrow) return Parents(o.Place);
                var S = Sigma(In[i], Root(o.Place));
                for (int k = Steps(o.Place); k > 0; k--) S = Content(In[i], S);
                return S;
            }
            // Round 13 (E13 (1)): a borrow's R2 parents over its k borrow Derefs, as E3 bounds them: ⋃_{m≤j<k} content^j(σ_in(root(X))),
            // m the index of the last shared Deref (SharedAt - 1), else 0. ParentsEveryLayer: m = 0, every layer.
            HashSet<string> Parents(string x, int sharedAt = 0)
            {
                int m = sharedAt > 0 && X != Abl.ParentsEveryLayer ? sharedAt - 1 : 0;
                var S = Sigma(In[i], Root(x)); var P = new HashSet<string>();
                for (int j = 0; j < Steps(x); j++) { if (j >= m) P.UnionWith(S); S = Content(In[i], S); }
                return P;
            }
            var baseSet = In[i].Where(t => Esc(t.H) || after.Contains(t.H) && !dest.Contains(t.H)).ToHashSet(); // live_out(ℓ) not overwritten, E
            bool marked = n.Accs.Any(o => o.Input); // the node marks its operands (rounds 11+)
            HashSet<Fact> OperandSet(Acc a)
            {
                var ops = n.Accs.Where(o => o.Input).ToList(); int at = ops.FindIndex(o => ReferenceEquals(o, a));
                var cs = In[i].Where(t => Esc(t.H) || after.Contains(t.H) && !dest.Contains(t.H)).ToHashSet();
                for (int j = 0; j < ops.Count; j++)
                {
                    var h = Root(ops[j].Place);
                    if (j < at && X == Abl.EarlierRoots || j > at && X != Abl.NoLaterRoots) cs.UnionWith(In[i].Where(t => t.H == h));
                    else if (j < at && X != Abl.NoEarlierValues || j == at && OwnValue) foreach (var l in OpVal(ops[j])) cs.Add((h, l));
                }
                return cs;
            }
            foreach (var h0 in n.EndBorrows)
            {
                // Round 10 (finding 1, GuardOrigin): err(ended) :- EndBorrow H at ℓ, H a guard candidate, γ_H ∈ CS(ℓ), CS over the
                // values live after ℓ other than H, transit included. Any other EndBorrow (a comparison operand, an abandoned
                // reservation) has no ended check. GammaGuardsOnly keeps the Loan row there; EndedByLoan uses it everywhere.
                bool byMarker = gamma.Contains(h0);
                if (!byMarker && X is not (Abl.GammaGuardsOnly or Abl.EndedByLoan)) continue;
                var F0 = In[i].Where(t => t.H != h0 && (Esc(t.H) || after.Contains(t.H) || byMarker && transit.Contains(t.H))).ToHashSet();
                for (var work = new Queue<Fact>(F0); work.Count > 0;)
                {
                    var l0 = work.Dequeue().L; var T = issue[l0].Param ? "E" : Direct(l0) ? Root(issue[l0].Place) : null;
                    if (T != null) foreach (var t in In[i].Where(t => t.H == T)) if (F0.Add(t)) work.Enqueue(t);
                }
                if (!byMarker) { foreach (var (_, l) in F0) if (!issue[l].Param && !issue[l].Marker && Root(issue[l].Into) == h0) Err(l, i, "ended " + h0); }
                else if (F0.Any(t => t.L == "γ" + h0)) Err("γ" + h0, i, "ended " + h0);
            }
            foreach (var a in accs)
            {
                // LS facts (see Check). IdExclusion: the old by-id rule. AText: design A's letter (σ after ℓ over live_out,
                // "L not issued at ℓ", activation L ≠ R), both by Loan id.
                HashSet<Fact> F;
                if (X == Abl.IdExclusion) F = In[i].Where(t => Esc(t.H) || liveIn[i].Contains(t.H)).ToHashSet();
                // Round 13 (E13 (1), the review's AV): an activation of a reservation in t, and a destroying call's destroy, take the
                // values of the operation's operands other than t, ⋃_{o_j≠t} val(o_j), on live_out(ℓ) without t, seeded by σ_in(t)
                // (Seed, E5); this replaces CS_call(ℓ, t)'s live_in(ℓ). A node whose operands rounds 1-10 do not mark keeps live_in(ℓ),
                // whose closure bounds the operands' values from above; ActivationLiveIn keeps it everywhere.
                else if (a.Except != null && ActivationOnValues && marked)
                {
                    F = baseSet.Where(t => t.H != issue[a.Except].Into).ToHashSet();
                    foreach (var o in n.Accs.Where(o => o.Input && Root(o.Place) != issue[a.Except].Into)) foreach (var l in OpVal(o)) F.Add((Root(o.Place), l));
                }
                else if (a.Except != null) F = In[i].Where(t => Esc(t.H) || liveIn[i].Contains(t.H) && (X == Abl.AText || t.H != issue[a.Except].Into)).ToHashSet();
                // Round 11 (E13 (1)): an access a Call's input makes at the Call happens before the call, while the holders of
                // the Call's other inputs are live: CS_call(ℓ, −), every holder live into ℓ (no exclusion; a reservation in its
                // own holder stays `ref` through `Reserved`), and E. InputsOnCS: CS(ℓ), the reviewed branch (B2).
                // Round 12: round 11's rule (R11Rule) checks only a Call's inputs so; another operation's operands keep CS(ℓ).
                else if (a.Input && !InputsOnCS && R11Rule && !n.NotCall) F = In[i].Where(t => Esc(t.H) || liveIn[i].Contains(t.H)).ToHashSet();
                // Round 12 (E13 (1), the owner's option R1; SPEC 12.2): operand i is evaluated before the operation and after
                // operands 1..i-1, so it is checked on CS_i(ℓ): the holders live after ℓ that ℓ does not overwrite, E, the values
                // of the earlier operands (attributed to their roots' holders, so a reservation in its own holder stays `ref`)
                // and the holders of the later ones, the operand's own value included (j ≤ i; NoOwnValue: j < i). No transit
                // and no seed.
                else if (a.Input && !InputsOnCS && !(R11Rule && n.NotCall)) F = OperandSet(a);
                else if (X == Abl.AText) F = outSet.Where(t => Esc(t.H) || after.Contains(t.H)).ToHashSet();
                // Round 13 (E13 (1)): a borrow (From) and an observing Drop take their own term, added below, and no transit (MIR
                // never destroys and stores at one location). KeepCSTerms: CS(ℓ) as before, the own term added (the review's run).
                else if (OwnTerm(a)) F = baseSet;
                else F = In[i].Where(t => Esc(t.H) || after.Contains(t.H) && !dest.Contains(t.H) || transit.Contains(t.H)).ToHashSet();
                // Round 12 (planning session on finding 2): a single-operand rvalue (a Use of a Copy or Move, a Cast, a UnaryOp;
                // not a Ref, whose access is a Borrow) uses its operand's value at ℓ too, so its operand's access is also checked
                // against val_in(o) on CS(ℓ). A round 1-11 node marks no operand; the prototype takes a node's Read or Move whose
                // Place is the source of one of its flows (the value ℓ stores). OwnValueEverywhere adds the term to every node Read
                // or Move; the term only adds Loans, so it bounds every finer reading from above.
                if (OwnValue && !a.Input && a.Except == null && a.Kind is K.Read or K.Move && n.Accs.Any(o => ReferenceEquals(o, a))
                    && (X == Abl.OwnValueEverywhere || n.Flows.Any(f => f.From == a.Place)))
                    foreach (var l in OpVal(a)) F.Add((Root(a.Place), l));
                // Round 13 (E13 (1), the review's DOV): an observing destruction of X, a Drop or a destroying call's destroy, takes
                // val(X) (SPEC 15.6: a Loan is live including an observation by its destruction; 15.6.6).
                if (DestroyValue && a.Kind == K.Destroy) foreach (var l in OpVal(a)) F.Add((Root(a.Place), l));
                // Round 13 (E13 (1), the review's ROP): a Ref's or Reborrowing PairAcquire's borrow of X takes its R2 parents,
                // whatever its destination's liveness (supersedes R4-M57's "a borrow's stored value only seeds": BorrowSeedOnly).
                if (BorrowParents && a.From != null) foreach (var l in Parents(a.Place, a.From.SharedAt)) F.Add((Root(a.Place), l));
                IEnumerable<Fact> basis = X == Abl.AText && a.Except == null ? outSet : In[i];
                var seed = Seed(a); F.UnionWith(basis.Where(t => seed.Contains(t.H)));
                for (var work = new Queue<Fact>(F); work.Count > 0;) // a live direct Loan naming T makes σ(T) live
                {
                    var l0 = work.Dequeue().L; var T = issue[l0].Param ? "E" : Direct(l0) ? Root(issue[l0].Place) : null;
                    if (T != null) foreach (var t in basis.Where(t => t.H == T)) if (F.Add(t)) work.Enqueue(t);
                }
                // S1′ + P3; an observing Drop is deep; not a destroying Ref. NoP3A (review mutation): Moves and Borrows shallow.
                bool deep = (a.Kind == K.Destroy || a.Kind is K.Move or K.SBorrow or K.UBorrow && X != Abl.NoP3A) && !a.InPlace;
                // G119's write or destruction: a write, a destruction, or a destroying activation (A3: "a destroying R hits as
                // write"; `replace(cur@uniq, …)` destroys cur's old value in place). RouteWriteOnly drops the last (B's letter).
                bool shallowExclusive = a.Kind is K.Write or K.Destroy || a.Except != null && a.InPlace && X != Abl.RouteWriteOnly;
                bool byId = X is Abl.IdExclusion or Abl.AText;
                foreach (var (h, l) in F)
                {
                    if ((byId && l == a.Except) || issue[l].Param || issue[l].Marker || excusedAt[i].Contains(l)) continue;
                    if (X == Abl.AText && a.Except == null && n.Issues.Any(s => s.Id == l)) continue; // "L not issued at ℓ"
                    bool uniq = issue[l].Uniq && !Reserved(h, l, i);
                    if (!uniq && a.Kind is K.Read or K.SBorrow) continue;
                    var p = issue[l].Place; // syntactic: no resolution, no authority
                    // Review r2 F1 (A3 §5 err(destroy), corrected from ¬hits(…, uborrow), which never holds): a destroying call's
                    // destruction reports only the obs part beyond its activation, so skip a Loan the activation's own kind hits
                    // (write for an InPlace target such as `replace`, uborrow otherwise, as for `clear`).
                    if (a.Kind == K.Destroy && a.Except != null && (Path.Within(a.Place, p) || Path.Within(p, a.Place) && (!issue[a.Except].InPlace || !Path.DerefBelow(p, a.Place)))) continue;
                    // Round 10 (G110): no Loan lies past a shared Deref, so the obs cut is gone (SharedCut is false unless
                    // LoanPastShared or ObsSharedCut restores it).
                    bool reach = deep && !(a.Kind == K.Destroy && SharedCut(a.Place, p, i));
                    // Round 10 (G119, SPEC 15.6.7; B's rule): inside a reservation's window (formation to activation), a write
                    // or destruction of a prefix of the reserved Loan's Place, across borrow Derefs, reaches that Loan in its
                    // reservation holder. Checked here on σ_in, so before any `ended` the access causes.
                    if (X != Abl.NoRouteReach && shallowExclusive && (InWindow(h, l, i) || X == Abl.RouteAllParents && PendingHolder(h, i))) reach = true;
                    if (Path.Within(a.Place, p) || (Path.Within(p, a.Place) && (reach || !Path.DerefBelow(p, a.Place)))) Err(l, i, $"{a.Kind} {a.Place}");
                }
            }
        }
    }

    // Round 10 (G119): strictly between the reservation's formation and its activation; the Call's own effects (its
    // destination write) come after the activation (19a, 38f, 40f).
    bool InWindow(string h, string l, int i) => activeAt.TryGetValue(l, out var at) && h == issue[l].Into && issueAt[l] < i && i < at;

    // RouteAllParents (designer A's option 2, not chosen): h holds a reservation whose window contains i.
    bool PendingHolder(string h, int i) => issue.Values.Any(r => r.Reserve && r.Into == h && InWindow(h, r.Id, i));

    void Err(string l, int i, string what) { if (!Errors.Contains((l, i, what))) Errors.Add((l, i, what)); }

    // disturbed at ℓ ⇒ Φ_c is replaced by Φ_c^dist in every σ (by holder, like ended).
    static HashSet<Fact> Disturbed(Node n, HashSet<Fact> o) =>
        n.Disturbs.Count == 0 ? o : o.Select(t => n.Disturbs.Contains(t.L) ? (t.H, t.L + "^dist") : t).ToHashSet();

    // A reserved Loan acts as shared until activation. Round 4: only in its own reservation holder, inside the call's
    // window; any other holder carries an earlier, activated incarnation of the site (loops). By id: by position only.
    bool Reserved(string h, string l, int i) => activeAt.TryGetValue(l, out var at) &&
        (X is Abl.IdExclusion or Abl.AText ? i <= at : h == issue[l].Into && issueAt[l] < i && i <= at);

    public IEnumerable<string> ErrorNodes() => Errors.Select(e => ns[e.P].Label).Distinct();
    public string LabelAt(int p) => ns[p].Label; // the analysed encoding's node (Inline, AsDrop and Secured have their own)
    public bool CarriesAt(string label, string h, string l) => In[IxOf(label)].Contains((h, l));
    public string Dump() => string.Join("\n", ns.Select(n => $"{n.Label,-44} carries {{{string.Join(" ", In[n.Ix].OrderBy(t => t.H).Select(t => t.H + ":" + t.L))}}} live {{{string.Join(" ", liveIn[n.Ix].Order())}}}"))
        + "\nplaces: " + string.Join("; ", places.Select(p => p.Key + " = {" + string.Join(", ", p.Value) + "}"));
}

// Hand-encoded facts. Each node is one program point; labels are the source lines; `$t` names a temporary.
static class Cases
{
    const bool Ref = false, Uniq = true;

    public static List<Case> All() => [
        Replacement(), Join(), Slot("3", copy: true, early: false), SlotReplaced(), Slot("4", copy: false, early: false), Slot("4b", copy: false, early: true),
        Index("5", nonPath: true, rewrite: false), Index("5b", nonPath: false, rewrite: true), Index("5c", nonPath: true, rewrite: true),
        Reborrow("6", write: true), Reborrow("6b", write: false), Partial("7", inspectP: false), Partial("7b", inspectP: true),
        Deepest("8a", clear: true, check: false), Deepest("8b", clear: false, check: false), Deepest("8c", clear: false, check: true),
        CopyChild(), StoreChild(), TwoBases(), Loop("9d", "x = 3"), Loop("9d'", "y = 3"), FieldBase("9e", partial: false), FieldBase("9e'", partial: true),
        ThroughCall(), Exchange(), ExchangeThenReplace(), ExchangeReplaces(), SwapBase(), StealThroughW(), SharedBase(), Nested(), MoveBase(), UniqBase(),
        Replace(), AppendRemove(), Insert(), Swap(), Unreachable(), UnreachableJoin(),
        MoveBan("12a", rewrite: true), MoveBan("12b", rewrite: false),
        RefBase("13a", shared: false, "outer"), RefBase("13b", shared: false, "inner"), RefBase("13c", shared: false, "through"),
        RefBase("13d", shared: true, "outer"), RefBase("13e", shared: true, "inner"),
        MoveAncestor(), ReplaceBase("15a", write: false), ReplaceBase("15b", write: true),
        Alias("16a", "reborrow"), Alias("16b", "copy"), Alias("16c", "aggregate"), Alias("16d", "stored"), Alias("16e", "inner"),
        DeadHolder("17a", chain: true), DeadHolder("17b", chain: false), PartialMove("18a", restore: true), PartialMove("18b", restore: false),
        InPlace("19a", parent: false), InPlace("19b", parent: true),
        LoopSite("20a", "uniq"), LoopSite("20b", "reserved"), LoopSite("20c", "refkept"), LoopSite("20d", "uniqkept"),
        LoopSite("20e", "child"), LoopSite("20f", "window"),
        TempStep("21a", clear: false), TempStep("21b", clear: true), IndexBan("22a", constant: false), IndexBan("22b", constant: true),
        OuterThenWrite(), ParamOuter(), DropBase("24a", userDrop: true), DropBase("24b", userDrop: false), DropMerged(),
        SelfStore("25a", "live"), SelfStore("25b", "dead"), SelfStore("25c", "enum"), Destroying("26a", clear: false), Destroying("26b", clear: true),
        SharedField(), Args("28a", "uniqref"), Args("28b", "refref"), Args("28c", "fields"), Args("28d", "stored"),
        CrossingKill(), EscapedTransit(), PartTransit(), LinkedList("30a", "obj"), LinkedList("30b", "uniq"), LinkedList("30c", "conflict"), LinkedList("30d", "peek"),
        SwapParams("31a", caller: false), SwapParams("31b", caller: true), CopyThrough("32a", childLive: false), CopyThrough("32b", childLive: true),
        PreservesResults("33a", "two"), PreservesResults("33b", "across"), PreservesResults("33c", "callonly"),
        IndexKill("34a", deref2: false), IndexKill("34b", deref2: true), IndexKill("34c", deref2: false, whole: true),
        Walk("35a", loop: false), Walk("35b", loop: true),
        PhantomParents("36a", direct: false, retain: false), PhantomParents("36b", direct: false, retain: true),
        PhantomParents("36c", direct: true, retain: false), PhantomParents("36d", direct: true, retain: true), SplitOuter(),
        PhantomRoot("36e"), PhantomRoot("36f"), PhantomRoot("36g"), PhantomRoot("36h"),
        Route("38a"), Route("38b"), Route("38c"), Route("38d"), Route("38e"), Route("38f"), Route("38g"), Route("38h"), Route("38i"),
        Route("38j"), Route("38k"), PreservesResults("33d", "other"), PreservesResults("33e", "inwindow"),
        SharedLayer("39a"), SharedLayer("39b"), SharedLayer("39c"), SharedLayer("39d"), SharedLayer("39e"), SharedLayer("39f"),
        SharedLayer("39g"), SharedLayer("39h"), SharedLayer("39i"), SharedLayer("39j"), SharedLayer("39k"), SharedLayer("39l"), SharedLayer("39m"), SharedLayer("39n"),
        // Round 10 (prototype round 10): G92b's fold, designer B's cases (38→40, 39→41) without the exact duplicates of A's
        // (40a = 38c, 40b = 38b, 41a = 39a, 41b = 39b, 41h = 39k, 41j = 39n), the Slice index, and G110 × guard candidates.
        Fold("38l"), Fold("38m"), Fold("38n"),
        .. new[] { "40c", "40d", "40e", "40f", "40g", "40h" }.Select(RouteWrite),
        .. new[] { "41c", "41d", "41e", "41f", "41g", "41i", "41k", "41l", "41m", "41n", "41o", "41p", "41q" }.Select(SharedPast),
        Slice("42a"), Slice("42b"), SliceCase("42c", inline: false), Slice("42d"), GuardExample(), Abandoned("44a"), Abandoned("44b"), Abandoned("44c"),
        // Case 23a review (cases 50-94, unchanged), and 92/93 with G110 step classes (92g, 93g)
        CrossingOrder("50", "reverse"), CrossingOrder("51", "recvAfter"), CrossingOrder("52", "recvBefore"), CrossingOrder("53", "arg"),
        Siblings("54", "both"), Siblings("55", "old"), Siblings("56", "conflict"),
        Conditional("57", "one"), Conditional("58", "both"), BackEdge("59"), Conditional("60", "loopOne"), Conditional("61", "loopValid"),
        Transfer2("62", "move"), Transfer2("63", "exchange"), Transfer2("64", "swap"), Transfer2("65", "replace"), Transfer2("66", "replaceThrough"),
        IndexMatch("67", "same"), IndexMatch("68", "other"), IndexMatch("69", "runtimeWrite"), IndexMatch("70", "runtimeBoth"),
        IndexMatch("71", "nestedPrefix"), IndexMatch("72", "nestedOther"), FieldMatch("73", same: true), FieldMatch("74", same: false),
        OwnStep("75", "objField"), OwnStep("76", "objWhole"), OwnStep("77", "objOld"), OwnStep("78", "rcClone"), OwnStep("79", "rcOld"), OwnStep("80", "rcDrop"),
        EscapedChild(), Stride2(), DirectStep(), ReservedRoute("84", retained: true), ReservedRoute("85", retained: false),
        AfterOuterEnd("86", "grandchild"), AfterOuterEnd("87", "oldSlot"), TempBase(), SamePoint("89", oldRoute: false), SamePoint("90", oldRoute: true), DropMerged2(), Guard("92", viaSubject: true), Guard("93", viaSubject: false), Guard("92g", viaSubject: true, g110: true), Guard("93g", viaSubject: false, g110: true), AfterOuterEnd("94", "reborrowSlot"),
        // Design review of the 1b choice (cases 100+)
        .. new[] { "100", "100b", "100c", "100d", "101", "102", "103", "104", "105", "106", "107", "108", "109", "110", "111", "112", "112b" }.Select(c => c == "105" ? ReviewSlice() : Review(c)),
        // Design review round 2 of the 1b choice (cases 120+)
        .. new[] { "120", "120b", "121", "122", "123", "123b", "123c", "124", "125", "126", "127", "127b" }.Select(Review2),
        // Round 11 (A3 E13, E14; the core lane's group review B1, B2, B4)
        .. new[] { "130a", "130b", "130c", "130d", "130e", "130f", "130g" }.Select(ClosureCall),
        .. new[] { "131a", "131b", "131c", "131d", "131e", "131f", "131g" }.Select(CallInputs),
        .. new[] { "132a", "132b", "132c", "132d", "132e", "132f", "132g", "132h", "132i", "132j", "132k", "132l", "132m", "132n" }.Select(PairInputs),
        .. new[] { "133a", "133b", "133c", "133d" }.Select(RawInit),
        .. new[] { "134a", "134b", "134c", "134d", "134e", "134f", "134g", "135a", "135b", "135c" }.Select(Reviewer.Make),
        // Round 12 (A3 E13, the owner's option R1: operand evaluation order)
        .. Round12.All(),
        // Round 13 (A3 E13 rewritten): the second design review's cases and this round's (ids r13…, family 141)
        .. R13.All()];

    // ---- Round 11 (A3 E13, E14; the core lane's group review B1, B2, B4) ----
    // E13: a Call's inputs are its argument operands and a Value callee's operand. Input: an access the MIR leaves AT the Call
    // (checked on CS_call(ℓ, −)); Callee: the Value callee's reservation, activated at the Call; Acquire: a PairAcquire
    // Reborrow secured into a temp (a reservation there); AcquireAt: one left at the Call. E14: RawStore.

    // B1: closure calls. bump = func [r] (k: i32) => r@follow += k is an Exclusive call: `$7 = &uniq two-phase bump;
    // bump-call(move $7, …)` (MIR forms the callee's receiver as a reservation); peek = func [r] () => inspect(r@follow) is a
    // Shared call: `$7 = &peek`.
    static Case ClosureCall(string id)
    {
        var c = new Case(id, "B1: " + id switch
        {
            "130a" => "`let v = bump@ref; bump(); inspect(v)` (the review's program)",
            "130b" => "`let u = bump@uniq; let v = u@ref; u(); inspect(v)`",
            "130c" => "a Shared call: `let v = peek@ref; peek(); inspect(v)`",
            "130d" => "`bump(do { let v = bump@ref; inspect(v); 1 })` (v ends before the activation)",
            "130e" => "`let v = bump@ref; bump(n); inspect(v)` (n: a direct Place operand at the Call)",
            "130f" => "`let u = bump@uniq; var keep = other@ref; u(do { keep = u@ref; 1 }); inspect(keep)` (the argument's borrow stays live)",
            _ => "`bump(value)` (`bump-call(move $7, copy value)`: the callee's holder carries r's Loan on value as a parent)",
        });
        bool shared = id == "130c";
        var f = shared ? "peek" : "bump";
        c.N("var value: i32 = 1; var n = 2; var other = 3").Def("value", "n", "other");
        c.N(shared ? "let r = value@ref" : "let r = value@uniq").Def("r").Borrow("Lr", "value", !shared, "r");
        c.N(shared ? "var peek = func [r] () => inspect(r@follow)" : "var bump = func [r] (k: i32) => r@follow += k").Def(f).A(K.Move, "r").Flow("r", f);
        string call;
        switch (id)
        {
            case "130a": case "130c": case "130e":
                c.N($"let v = {f}@ref").Def("v").Borrow("Lv", f, Ref, "v");
                if (shared) c.N("$7 = &peek (callee, Shared call)").Def("$7").Borrow("L7", "peek", Ref, "$7");
                else c.N("$7 = &uniq two-phase bump (callee, reserved)").Def("$7").Borrow("L7", "bump", Uniq, "$7", reserve: true);
                var cn = c.N(call = id == "130e" ? "bump-call(move $7, copy n)" : $"{f}-call(move $7)").Input(K.Move, "$7");
                if (!shared) cn.Callee("L7");
                if (id == "130e") cn.Input(K.Read, "n");
                c.N("inspect(v)").A(K.Read, "v.*");
                return shared ? c.Valid() : c.Error(call);
            case "130b":
                c.N("let u = bump@uniq").Def("u").Borrow("Lu", "bump", Uniq, "u");
                c.N("let v = u@ref").Def("v").Borrow("Lv", "u", Ref, "v");
                c.N("$7 = &uniq two-phase (*u) (callee, reserved)").Def("$7").Borrow("L7", "u.*", Uniq, "$7", reserve: true);
                c.N(call = "u-call(move $7)").Input(K.Move, "$7").Callee("L7");
                c.N("inspect(v)").A(K.Read, "v.*");
                return c.Error(call);
            case "130d":
                c.N("$7 = &uniq two-phase bump (callee, reserved)").Def("$7").Borrow("L7", "bump", Uniq, "$7", reserve: true);
                c.N("  let v = bump@ref (argument)").Def("v").Borrow("Lv", "bump", Ref, "v");
                c.N("  inspect(v)").A(K.Read, "v.*");
                c.N("  $k = 1 (end of do: StorageDead v)").Def("$k", "v");
                c.N("bump-call(move $7, move $k)").Input(K.Move, "$7").Input(K.Move, "$k").Callee("L7");
                return c.Valid();
            case "130g":
                c.N("$7 = &uniq two-phase bump (callee, reserved)").Def("$7").Borrow("L7", "bump", Uniq, "$7", reserve: true);
                c.N(call = "bump-call(move $7, copy value)").Input(K.Move, "$7").Callee("L7").Input(K.Read, "value");
                return c.Error(call);
            default: // 130f
                c.N("let u = bump@uniq").Def("u").Borrow("Lu", "bump", Uniq, "u");
                c.N("var keep = other@ref").Def("keep").Borrow("Lo", "other", Ref, "keep");
                c.N("$7 = &uniq two-phase (*u) (callee, reserved)").Def("$7").Borrow("L7", "u.*", Uniq, "$7", reserve: true);
                c.N("  keep = u@ref (argument)").Assign("keep").Borrow("Lk", "u", Ref, "keep");
                c.N("  $k = 1").Def("$k");
                c.N(call = "u-call(move $7, move $k)").Input(K.Move, "$7").Input(K.Move, "$k").Callee("L7");
                c.N("inspect(keep)").A(K.Read, "keep.*");
                return c.Error(call);
        }
    }

    // B2: the accesses a Call's own operands make AT the Call. give(a: uniq/i32, b: i32); giveR(a: ref/i32, b: i32);
    // giveS(a: uniq/i32, b: ref/i32); S {n: i32} with set(self: uniq/S, n: i32). A Place operand that no later operand's code
    // follows is read at the Call (G123, DECISIONS R4-M55).
    static Case CallInputs(string id)
    {
        var c = new Case(id, "B2: " + id switch
        {
            "131a" => "`let r = x@uniq; give(r, x)` (MIR: `$4 = &uniq two-phase (*r)`, carrying r's Loan on x; `give(move $4, copy x)`)",
            "131b" => "`let r = x@uniq; giveR(r, x)`, giveR(a: ref/i32, b: i32) (`$4 = &(*r)`; `giveR(move $4, copy x)`)",
            "131c" => "`let r = x@uniq; giveS(r, x)`, giveS(a: uniq/i32, b: ref/i32) (`$5 = &x` is formed before the Call)",
            "131d" => "`give(x@uniq, x)` (the read precedes the activation)",
            "131e" => "`let r = x@uniq; give(r, r@follow)` (a read of the reserved referent at the Call)",
            "131f" => "`s.set(s.n)` (`$r = &uniq two-phase s`; `set(move $r, copy s.n)`)",
            _ => "`Kimi.Intrinsics.replace(x@uniq, with: x)` (SPEC 15.6.7's form with the read left at the Call)",
        });
        c.N("var x = 1; var s = S(n: 1)").Def("x", "s");
        string call;
        switch (id)
        {
            case "131d":
                c.N("$4 = x@uniq (reserved)").Def("$4").Borrow("L4", "x", Uniq, "$4", reserve: true);
                c.N("give(move $4, copy x)").Input(K.Move, "$4").Activate("L4").Input(K.Read, "x");
                return c.Valid();
            case "131g":
                c.N("$t = x@uniq (reserved, replace target)").Def("$t").Borrow("Lt", "x", Uniq, "$t", reserve: true, inPlace: true);
                c.N("replace(move $t, copy x)").Input(K.Move, "$t").Activate("Lt").Input(K.Read, "x").Through("$t.*", "x");
                return c.Valid();
            case "131f":
                c.N("$r = s (set's receiver, reserved)").Def("$r").Borrow("Lr", "s", Uniq, "$r", reserve: true);
                c.N("set(move $r, copy s.n)").Input(K.Move, "$r").Activate("Lr").Input(K.Read, "s.n");
                return c.Valid();
        }
        c.N("let r = x@uniq").Def("r").Borrow("Lr", "x", Uniq, "r");
        switch (id)
        {
            case "131a": case "131e":
                c.N("$4 = &uniq two-phase (*r) (reserved)").Def("$4").Borrow("L4", "r.*", Uniq, "$4", reserve: true);
                c.N(call = id == "131a" ? "give(move $4, copy x)" : "give(move $4, copy (*r))").Input(K.Move, "$4").Activate("L4").Input(K.Read, id == "131a" ? "x" : "r.*");
                return id == "131a" ? c.Error(call) : c.Valid();
            case "131b":
                c.N("$4 = &(*r)").Def("$4").Borrow("L4", "r.*", Ref, "$4");
                c.N(call = "giveR(move $4, copy x)").Input(K.Move, "$4").Input(K.Read, "x");
                return c.Error(call);
            default: // 131c
                c.N("$4 = &uniq two-phase (*r) (reserved)").Def("$4").Borrow("L4", "r.*", Uniq, "$4", reserve: true);
                c.N(call = "$5 = &x").Def("$5").Borrow("L5", "x", Ref, "$5");
                c.N("giveS(move $4, move $5)").Input(K.Move, "$4").Input(K.Move, "$5").Activate("L4");
                return c.Error(call);
        }
    }

    // B2 with pair layers (the Semantics view decides a PairAcquire: a Copy in the ref view, a Reborrow in the uniq view).
    // inner2<s/T>(a: ref/(s/T), b: s/T); inner3<s/T>(a: s/T, b: ref/(s/T)); inner7<s/S>(a: s/S, b: i32); bumpS<s/S>(q: s/S)
    // -> i32; inner8<s/S>(b: i32, a: s/S); inner9<s/T>(a: s/T, b: s/T); inner11<s/S>(a: s/S, b: i32); innerW<s/T>(a: s/T,
    // b: W<s/T>); s is ref or uniq; S {n: i32}; W<s/T> {a: s/T}. The caller is generic: outer<s/T>(p: s/T). G123 (R4-M55,
    // MirBuilder.Builds on claude/a5-1b-mir) secures an operand into a temp when a later operand builds code: every operand
    // builds code but a constant and a Place read as it is (Copied or Moved), so a PairAcquire (HirAcquire.ByCase) builds code
    // and a Copy or Move of a Place does not. Secured (SecureAcquire): the same program with the PairAcquire secured.
    static Case PairInputs(string id) { var c = PairInputsCase(id, false); if (id is "132j" or "132m") c.Secured = PairInputsCase(id, true); c.Always = Reviewer.PairAlways(id); return c; }

    static Case PairInputsCase(string id, bool secure)
    {
        bool uniqView = id is not ("132b" or "132d" or "132i" or "132l" or "132n");
        var c = new Case(id, "B2 pair, " + (uniqView ? "uniq view: " : "ref view: ") + id switch
        {
            "132a" or "132b" => "`var v = p; inner2(v@ref, v)` (`$4 = &v`; `inner2(move $4, acquire v)`, the PairAcquire left at the Call)",
            "132c" or "132d" => "`var v = p; inner3(v, v@ref)` (G123 secures the PairAcquire: `$3 = acquire v`; `$4 = &v`)",
            "132e" => "`inner7(p, p.n + 1)` (`$3 = acquire p`, a reservation; a shared read of p.n during it)",
            "132f" => "`inner7(p, bumpS(p))` (a second exclusive acquisition of p during the reservation)",
            "132g" => "`inner8(p.n + 1, p)` (`$3 = p.n + 1`; `inner8(move $3, acquire p)`)",
            "132h" or "132i" => "`var v = p; inner9(v, v)` (the second PairAcquire builds code, so `$3 = acquire v` is secured; `inner9(move $3, acquire v)`)",
            "132j" => "`inner11(p, p.n)` (`inner11(acquire p, copy (*p).n)`: both left at the Call)",
            "132m" or "132n" => "`var v = p; let u = v@follow@ref; innerR(v, u)`, innerR<s/T>(a: s/T, b: ref/T) (`innerR(acquire v, copy u)`: both left at the Call)",
            _ => "`var w = W(a: p); innerW(w.a, w@move)` (round 13, the builder's MIR: `w@move` builds code, so `$4 = acquire w.a; $5 = move w; innerW(move $4, move $5)`; round 12: the valid spelling of round 11's `innerW(w.a, w)`, a TransferRequired_Kd)",
        } + (secure ? " [secured: SecureAcquire]" : ""));
        c.N("func outer<s/T>(p: s/T), " + (uniqView ? "uniq view" : "ref view")).Param("Lp", "p");
        string call;
        Node AtCall(Node n, string place) => uniqView ? n.AcquireAt(place + ".*") : n.Input(K.Read, place); // PairAcquire
        switch (id)
        {
            case "132a": case "132b":
                c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
                c.N("$4 = v@ref").Def("$4").Borrow("L4", "v", Ref, "$4");
                AtCall(c.N(call = "inner2(move $4, acquire v)").Input(K.Move, "$4"), "v");
                return uniqView ? c.Error(call) : c.Valid();
            case "132c": case "132d":
                c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
                if (uniqView) c.N("$3 = acquire v (secured: a reservation)").Def("$3").Acquire("L3", "v.*", "$3");
                else c.N("$3 = acquire v (secured: a Copy)").Def("$3").A(K.Read, "v").Flow("v", "$3");
                c.N("$4 = v@ref").Def("$4").Borrow("L4", "v", Ref, "$4");
                var n3 = c.N(call = "inner3(move $3, move $4)").Input(K.Move, "$3").Input(K.Move, "$4");
                if (uniqView) n3.Activate("L3");
                return uniqView ? c.Error(call) : c.Valid();
            case "132e": case "132f":
                c.N("$3 = acquire p (secured: a reservation)").Def("$3").Acquire("L3", "p.*", "$3");
                if (id == "132e") c.N("$4 = p.n + 1").Def("$4").A(K.Read, "p.*.n");
                else c.N(call = "$4 = bumpS(acquire p)").Def("$4").AcquireAt("p.*");
                c.N("inner7(move $3, move $4)").Input(K.Move, "$3").Input(K.Move, "$4").Activate("L3");
                return id == "132e" ? c.Valid() : c.Error("$4 = bumpS(acquire p)");
            case "132g":
                c.N("$3 = p.n + 1").Def("$3").A(K.Read, "p.*.n");
                c.N("inner8(move $3, acquire p)").Input(K.Move, "$3").AcquireAt("p.*");
                return c.Valid();
            case "132h": case "132i":
                c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
                call = "Call inner9(v, v)";
                if (uniqView) c.N("$3 = acquire v (secured: a reservation)").Def("$3").Acquire("L3", "v.*", "$3");
                else c.N("$3 = acquire v (secured: a Copy)").Def("$3").A(K.Read, "v").Flow("v", "$3");
                var n9 = AtCall(c.N(call).Input(K.Move, "$3"), "v");
                if (uniqView) n9.Activate("L3");
                return uniqView ? c.Error(call) : c.Valid();
            case "132j":
                call = "Call inner11(p, p.n)";
                if (secure) { c.N("$3 = acquire p (secured: a reservation)").Def("$3").Acquire("L3", "p.*", "$3"); c.N(call).Input(K.Move, "$3").Activate("L3").Input(K.Read, "p.*.n"); }
                else c.N(call).AcquireAt("p.*").Input(K.Read, "p.*.n");
                return c.Valid();
            case "132m": case "132n": // a Copy after an at-Call PairAcquire: its Loans sit in its source holder, live into ℓ
                c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
                c.N("let u = v@follow@ref").Def("u").Borrow("Lu", "v.*", Ref, "u", sharedAt: uniqView ? 0 : 1);
                call = "Call innerR(v, u)";
                if (secure) { c.N("$3 = acquire v (secured: a reservation)").Def("$3").Acquire("L3", "v.*", "$3"); c.N(call).Input(K.Move, "$3").Activate("L3").Input(K.Read, "u"); }
                else AtCall(c.N(call), "v").Input(K.Read, "u");
                return uniqView ? c.Error(call) : c.Valid();
            default: // 132k, 132l
                if (uniqView) c.N("var w = W(a: p) (a Reborrow of *p, stored)").Def("w").Borrow("Lw", "p.*", Uniq, "w.a");
                else c.N("var w = W(a: p) (a Copy of p)").Def("w").A(K.Read, "p").Flow("p", "w.a");
                // Round 13 (the second design review's MIR dump p1/a132k): an explicit `@move` builds code, so G123 secures the
                // PairAcquire before it, and the Move is its own Use (rounds 11-12 left both at the Call).
                if (uniqView) c.N("$4 = acquire w.a (secured: a reservation)").Def("$4").Acquire("L4", "w.a.*", "$4");
                else c.N("$4 = acquire w.a (secured: a Copy)").Def("$4").A(K.Read, "w.a").Flow("w.a", "$4");
                c.N(call = "$5 = move w").Def("$5").A(K.Move, "w").Flow("w", "$5");
                var nw = c.N("innerW(move $4, move $5)").Input(K.Move, "$4").Input(K.Move, "$5"); if (uniqView) nw.Activate("L4");
                return uniqView ? c.Error(call) : c.Valid();
        }
    }

    // B4 (E14): Raw.initialize(storage: raw/T, value: T) stores value into the referent of a raw storage operand, E (SPEC
    // 15.6: storage the body does not own, "a value stored there may be used after every point"). p[0] reads E.
    static Case RawInit(string id)
    {
        var c = new Case(id, "B4: " + id switch
        {
            "133a" => "`let p: raw/(ref/i32) = Raw.allocate(1); Raw.initialize(p, x@ref); x = 2; let q = p[0]; inspect(q)` (the review's program)",
            "133b" => "133a with `x = 2` before the initialize",
            "133c" => "133a without the write: `…; let q = p[0]; inspect(q); inspect(x)`",
            _ => "`let p: raw/(uniq/i32) = …; Raw.initialize(p, x@uniq); inspect(x)` (the reservation is retained into E)",
        });
        c.N("var x = 1").Def("x");
        c.N("let p = Raw.allocate(1)").Def("p");
        if (id == "133b") c.N("x = 2").Assign("x");
        if (id == "133d") c.N("$a = x@uniq (reserved)").Def("$a").Borrow("La", "x", Uniq, "$a", reserve: true);
        else c.N("$a = x@ref").Def("$a").Borrow("La", "x", Ref, "$a");
        var init = c.N("Raw.initialize(copy p, move $a)").Input(K.Read, "p").Input(K.Move, "$a").RawStore("p", "$a");
        if (id == "133d") { init.Activate("La"); c.N("inspect(x)").A(K.Read, "x"); return c.Error("inspect(x)"); }
        if (id == "133a") c.N("x = 2").Assign("x");
        c.N("let q = p[0]").Def("q").Flow("p.*.0", "q");
        c.N("inspect(q)").A(K.Read, "q.*");
        if (id == "133c") c.N("inspect(x)").A(K.Read, "x");
        return id == "133a" ? c.Error("x = 2") : c.Valid();
    }

    static Case Review2(string id) { var c = Review2Case(id, false); if (id is "123" or "123c") c.AsDrop = Review2Case(id, true); return c; }

    // ---- Design review round 2 (cases 120+): the own-value seed (A3 E5) against a store into a holder live only by the
    // closure, writeback through a reference, a retaining store, a destroying call, a loop, γ and the prefix kill.
    // S3 {r: ref/i32, n: i32}; H {a: i32, b: uniq/S3}; S {r: uniq/i32, q: ref/i32} with a drop that writes through self.r.
    static Case Review2Case(string id, bool asDrop)
    {
        var c = new Case(id, "review r2: " + id switch
        {
            "120" => "100 stored into a part: `let p = h.a@ref; h.b = r@follow@uniq; inspect(p@follow)` (h used after only through p's Loan; MIR: value temp, then Move)",
            "120b" => "120 with the Reborrow formed directly into h.b (no MIR form today)",
            "121" => "writeback through a reference: `let r = s@uniq; r.r = r.n@ref; r.n += 1` (r and s dead after; Rust E0506)",
            "122" => "retaining store: `stash(into: h@uniq, item: r@follow@uniq)` after `r.r = r.n@ref` (stash stores item into (*into).b)",
            "123" => "destroying call on a self-referential holder: `s.q = s.r@follow@ref; replace(s@uniq, with: S(…))`, s dead after (A3's err(destroy); DestroyAsDrop: a Drop on CS)",
            "123b" => "123 with the destruction checked as A3's err(destroy): CS_call(ℓ, $t) seeded by σ_in($t) (the r2 review's form; 123 equals it since round 10 final)",
            "123c" => "123 with s used after the replace (`inspect(s)`)",
            "124" => "loop: `while c { let q = r@follow@uniq; q.n = 5; inspect(q.r@follow); r.r = r.n@ref }` (second iteration)",
            "125" => "loop, valid: 124 with a shared Reborrow, `let q = r@follow@ref; inspect(q.r@follow)`",
            "126" => "guard: the candidate stored into a part of a holder used after only through p: `let p = holder.a@ref; … if (do { holder.saved = d; true }) …; use(p)`",
            "127" => "prefix kill: `var r = s@uniq; r.r = r.n@ref; r = s2@uniq; s.n = 5; inspect(s.r@follow)` (Lc ended with r; s.r keeps Lc's parent Lr)",
            _ => "127 without the last read (s dead after `s.n = 5`): valid",
        });
        void Self(Case k) { k.N("var z = 0; var s = S3(r: z@ref, n: 1)").Def("z", "s").Borrow("Lz", "z", Ref, "s.r"); }
        switch (id)
        {
            case "120": case "120b":
                Self(c); c.N("var w = 0; var s2 = S3(r: w@ref, n: 1)").Def("w", "s2").Borrow("Lw", "w", Ref, "s2.r");
                c.N("let r = s@uniq").Def("r").Borrow("Lr", "s", Uniq, "r");
                c.N("$t = r.n@ref").Def("$t").Borrow("Lc", "r.*.n", Ref, "$t");
                c.N("r.r = $t").Through("r.*.r", "$t");
                c.N("var h = H(a: 0, b: s2@uniq)").Def("h").Borrow("Ls2", "s2", Uniq, "h.b");
                c.N("let p = h.a@ref").Def("p").Borrow("Lha", "h.a", Ref, "p");
                string at;
                if (id == "120") { c.N(at = "$v = r@follow@uniq").Def("$v").Borrow("Lq", "r.*", Uniq, "$v"); c.N("h.b = move $v").A(K.Write, "h.b").A(K.Move, "$v").Flow("$v", "h.b"); }
                else c.N(at = "h.b = r@follow@uniq").A(K.Write, "h.b").Borrow("Lq", "r.*", Uniq, "h.b");
                c.N("inspect(p@follow)").A(K.Read, "p.*");
                return c.Error(at);
            case "121":
                Self(c);
                c.N("let r = s@uniq").Def("r").Borrow("Lr", "s", Uniq, "r");
                c.N("$t = r.n@ref").Def("$t").Borrow("Lc", "r.*.n", Ref, "$t");
                c.N("r.r = $t").Through("r.*.r", "$t");
                c.N("$w = TwoPhaseUniq((*r).n) (writeback)").Def("$w").Borrow("Lw", "r.*.n", Uniq, "$w", reserve: true);
                c.N("$v = (*$w) + 1").Def("$v").A(K.Read, "$w.*");
                c.N("(*$w) = $v (activates)").Activate("Lw").Through("$w.*", "$v");
                return c.Error("(*$w) = $v (activates)");
            case "122":
                Self(c); c.N("var w = 0; var s2 = S3(r: w@ref, n: 1)").Def("w", "s2").Borrow("Lw", "w", Ref, "s2.r");
                c.N("let r = s@uniq").Def("r").Borrow("Lr", "s", Uniq, "r");
                c.N("$t = r.n@ref").Def("$t").Borrow("Lc", "r.*.n", Ref, "$t");
                c.N("r.r = $t").Through("r.*.r", "$t");
                c.N("var h = H(a: 0, b: s2@uniq)").Def("h").Borrow("Ls2", "s2", Uniq, "h.b");
                c.N("$a = h@uniq (into, reserved)").Def("$a").Borrow("La", "h", Uniq, "$a", reserve: true);
                c.N("$b = r@follow@uniq (item, reserved)").Def("$b").Borrow("Lb", "r.*", Uniq, "$b", reserve: true);
                c.N("stash($a, $b) (stores item into (*into).b)").Activate("La").Activate("Lb").Through("$a.*.b", "$b");
                c.N("h.b@follow.n = 5").A(K.Write, "h.b.*.n"); c.N("inspect(h.b@follow.r@follow)").A(K.Read, "h.b.*.r.*");
                return c.Error("stash($a, $b) (stores item into (*into).b)");
            case "123": case "123b": case "123c":
                c.N("var x = 1; var y = 2; var z = 0").Def("x", "y", "z");
                c.N("var s = S(r: x@uniq, q: z@ref)").Def("s").Borrow("L1", "x", Uniq, "s.r").Borrow("Lz", "z", Ref, "s.q");
                c.N("s.q = s.r@follow@ref").Assign("s.q").Borrow("Lq", "s.r.*", Ref, "s.q");
                c.N("$t = s@uniq (replace target, reserved; destroying)").Def("$t").Borrow("Lt", "s", Uniq, "$t", reserve: true, inPlace: true);
                c.N("$v = S(r: y@uniq, q: z@ref)").Def("$v").Borrow("Ly", "y", Uniq, "$v.r").Borrow("Lz2", "z", Ref, "$v.q");
                const string rep = "replace($t, with: $v)";
                var n = c.N(rep).Activate("Lt").Through("$t.*", "$v");
                if (!asDrop) n.DestroyAt("s", "Lt"); else n.Drop("s").A(K.Write, "s.r.*"); // AsDrop: 26a's old encoding, a Drop on CS and the drop's write
                if (id == "123c") c.N("inspect(s)").Use("s");
                return c.Error(rep);
            case "124": case "125":
                Self(c);
                c.N("let r = s@uniq").Def("r").Borrow("Lr", "s", Uniq, "r");
                c.N("while c").Goto("  let q = r@follow@uniq", "()");
                c.N("  let q = r@follow@uniq").Def("q").Borrow("Lq", "r.*", id == "124", "q");
                if (id == "124") c.N("  q.n = 5").A(K.Write, "q.*.n");
                c.N("  inspect(q.r@follow)").A(K.Read, "q.*.r.*");
                c.N("  $t = r.n@ref").Def("$t").Borrow("Lc", "r.*.n", Ref, "$t");
                c.N("  r.r = $t").Through("r.*.r", "$t").Goto("while c");
                c.N("()");
                return id == "124" ? c.Error("  let q = r@follow@uniq") : c.Valid();
            case "126":
                c.N("var fallback = Data(); var packet: Packet = .Data(Data())").Def("fallback", "packet");
                c.N("var holder = H(a: 0, saved: fallback@ref)").Def("holder").Borrow("Lf", "fallback", Ref, "holder.saved");
                c.N("let p = holder.a@ref").Def("p").Borrow("Lha", "holder.a", Ref, "p");
                c.N("$s = packet@ref (Subject, shared mode)").Def("$s").Borrow("Lsub", "packet", Ref, "$s");
                c.N("d = $s.data (candidate)").Def("d").Borrow("Ld", "$s.*.data", Ref, "d", sharedAt: 1);
                c.N("guard: holder.saved = d").Assign("holder.saved").Flow("d", "holder.saved");
                const string end = "guard cleanup: EndBorrow d";
                c.N(end).EndBorrow("d");
                c.N("use(p)").A(K.Read, "p.*");
                return c.Error(end);
            default: // 127, 127b
                Self(c); c.N("var s2 = S3(r: z@ref, n: 2)").Def("s2").Borrow("Lz3", "z", Ref, "s2.r");
                c.N("var r = s@uniq").Def("r").Borrow("Lr", "s", Uniq, "r");
                c.N("$t = r.n@ref").Def("$t").Borrow("Lc", "r.*.n", Ref, "$t");
                c.N("r.r = $t").Through("r.*.r", "$t");
                c.N("r = s2@uniq").Assign("r").Borrow("Lr2", "s2", Uniq, "r");
                c.N("s.n = 5").A(K.Write, "s.n");
                if (id == "127b") return c.Valid();
                c.N("inspect(s.r@follow)").A(K.Read, "s.r.*");
                return c.Error("s.n = 5");
        }
    }

    // ---- Design review of the 1b choice (cases 100+) ----
    // S3 {r: ref/i32, n: i32}. 100-101: a holder used after ℓ only through the value ℓ stores or activates (39o's family):
    // the closure at ℓ must see it. 100: a Reborrow whose R2 parent names s; 100d: a retained reservation; 101: a direct
    // reservation of a self-referential s at an argument, s dead after the call.
    static Case Review(string id)
    {
        var c = new Case(id, "review: " + id switch
        {
            "100" => "`let r = s@uniq; r.r = r.n@ref; let q = r@follow@uniq; q.n = 5; inspect(q.r@follow)` (s used after only through q's parent Loan)",
            "100b" => "100 with `let q = r@follow@ref; inspect(q.r@follow)`",
            "100c" => "100 with `let q = r@move`",
            "100d" => "100 through a retaining call: `let q = keep(r@follow@uniq); q.n = 5; inspect(q.r@follow)`",
            "101" => "`s.r = s.n@ref; setN(s@uniq, 5)` (s not used after the call; setN writes s.n)",
            "102" => "guard stores the candidate through a live reference: `let w = holder@uniq; match packet .Data(let d) if (do { w.saved = d; true }) => (); use(w)`",
            "103" => "guard copies a Copy value out of the candidate: `if (do { n = d@follow.count; true })`; `use(n)`",
            "104" => "guard candidate stored into tmp, overwritten before use, in a loop: `while c { match … if (do { tmp = d; true }) => (); tmp = other@ref }; use(tmp)`",
            "106" => "`first = cur.inner.takeWith(do { let old = exchange(cur@uniq, with: b); 0 })`",
            "109" => "writeback of a self-referential holder's part: `s.r = s.n@ref; s.n += 1` (s not used after; SPEC 15.6's \"also used\" rule; rustc E0506)",
            "110" => "`x = y@ref; f(x@uniq)` (x holds a reference to y; x dead after)",
            "111" => "`s.r = s.n@ref; setM(s.m@uniq)` (a sibling of the self-referenced part)",
            "112" => "G94 guard: `return values[reset(values@uniq)]` (the receiver is modified while the index is evaluated)",
            "112b" => "G94 guard: `return values[peek(values@ref)]` (a read during the index)",
            "107" => "route through a parameter: `p@follow.inner.takeWith(do { p@follow = o; 0 })`, p: uniq/(uniq/H)",
            _ => "guard returns the candidate: `.Data(let d) if (do { return d })`",
        });
        switch (id)
        {
            case "100": case "100b": case "100c": case "100d":
                c.N("var z = 0").Def("z"); c.N("var s = S3(r: z@ref, n: 1)").Def("s").Borrow("Lz", "z", Ref, "s.r");
                c.N("let r = s@uniq").Def("r").Borrow("Lr", "s", Uniq, "r");
                c.N("$t = r.n@ref").Def("$t").Borrow("Lc", "r.*.n", Ref, "$t");
                c.N("r.r = $t").Through("r.*.r", "$t");
                string at;
                if (id == "100") c.N(at = "let q = r@follow@uniq").Def("q").Borrow("Lq", "r.*", Uniq, "q");
                else if (id == "100b") { c.N("let q = r@follow@ref").Def("q").Borrow("Lq", "r.*", Ref, "q"); c.N("inspect(q.r@follow)").A(K.Read, "q.*.r.*"); return c.Valid(); }
                else if (id == "100c") c.N(at = "let q = r@move").Def("q").A(K.Move, "r").Flow("r", "q");
                else
                {
                    c.N("$a = r@follow@uniq (keep's argument, reserved)").Def("$a").Borrow("La", "r.*", Uniq, "$a", reserve: true);
                    c.N(at = "let q = keep($a) (retains)").Activate("La").Def("q").Flow("$a", "q");
                }
                c.N("q.n = 5").A(K.Write, "q.*.n"); c.N("inspect(q.r@follow)").A(K.Read, "q.*.r.*");
                return c.Error(at);
            case "101":
                c.N("var z = 0").Def("z"); c.N("var s = S3(r: z@ref, n: 1)").Def("s").Borrow("Lz", "z", Ref, "s.r");
                c.N("s.r = s.n@ref").Assign("s.r").Borrow("Ln", "s.n", Ref, "s.r");
                c.N("$a = s@uniq (setN's argument, reserved)").Def("$a").Borrow("La", "s", Uniq, "$a", reserve: true);
                c.N("setN($a, 5)").Activate("La").Through("$a.*.n", "z");
                return c.Error("setN($a, 5)");
            case "102": case "103": case "104": case "108":
            {
                c.N("var fallback = Data(); var other = Data(); var packet: Packet = .Data(Data()); var n = 0").Def("fallback", "other", "packet", "n");
                if (id == "102") { c.N("var holder = H(saved: fallback@ref)").Def("holder").Borrow("Lf", "fallback", Ref, "holder.saved"); c.N("let w = holder@uniq").Def("w").Borrow("Lw", "holder", Uniq, "w"); }
                if (id == "104") { c.N("var tmp = other@ref").Def("tmp").Borrow("Lo", "other", Ref, "tmp"); c.N("while c").Goto("  $s = packet@ref (Subject, shared mode)", "use(tmp)"); }
                var ind = id == "104" ? "  " : "";
                c.N(ind + "$s = packet@ref (Subject, shared mode)").Def("$s").Borrow("Lsub", "packet", Ref, "$s");
                c.N(ind + "d = $s.data (candidate)").Def("d").Borrow("Ld", "$s.*.data", Ref, "d", sharedAt: 1);
                var end = ind + "guard cleanup: EndBorrow d";
                if (id == "102") { c.N("guard: w.saved = d").Through("w.*.saved", "d"); c.N(end).EndBorrow("d"); c.N("use(w)").Use("w"); return c.Error(end); }
                if (id == "103") { c.N("guard: n = d@follow.count").Assign("n").A(K.Read, "d.*.count").Flow("d.*.count", "n"); c.N(end).EndBorrow("d"); c.N("use(n)").Use("n"); return c.Valid(); }
                if (id == "108") { c.N("guard: _0 = d (return d)").Flow("d", "$ret"); c.N(end).EndBorrow("d"); c.N("return").Use("$ret"); return c.Error(end); }
                c.N("  guard: tmp = d").Assign("tmp").Flow("d", "tmp"); c.N(end).EndBorrow("d");
                c.N("  tmp = other@ref").Assign("tmp").Borrow("Lo2", "other", Ref, "tmp").Goto("while c");
                c.N("use(tmp)").Use("tmp");
                return c.Valid();
            }
            case "109":
                c.N("var z = 0").Def("z"); c.N("var s = S3(r: z@ref, n: 1)").Def("s").Borrow("Lz", "z", Ref, "s.r");
                c.N("s.r = s.n@ref").Assign("s.r").Borrow("Ln", "s.n", Ref, "s.r");
                c.N("$w = TwoPhaseUniq(s.n) (writeback)").Def("$w").Borrow("Lw", "s.n", Uniq, "$w", reserve: true);
                c.N("$v = (*$w) + 1").Def("$v").A(K.Read, "$w.*");
                c.N("(*$w) = $v (activates)").Activate("Lw").Through("$w.*", "$v");
                return c.Error("(*$w) = $v (activates)");
            case "110": case "111":
                c.N("var y = 0; var z = 0").Def("y", "z");
                if (id == "110")
                {
                    c.N("var x = y@ref").Def("x").Borrow("Ly", "y", Ref, "x");
                    c.N("$a = x@uniq (reserved)").Def("$a").Borrow("La", "x", Uniq, "$a", reserve: true);
                    c.N("f($a)").Activate("La");
                    return c.Valid();
                }
                c.N("var s = S3(r: z@ref, n: 1, m: 2)").Def("s").Borrow("Lz", "z", Ref, "s.r");
                c.N("s.r = s.n@ref").Assign("s.r").Borrow("Ln", "s.n", Ref, "s.r");
                c.N("$a = s.m@uniq (reserved)").Def("$a").Borrow("La", "s.m", Uniq, "$a", reserve: true);
                c.N("setM($a)").Activate("La").Through("$a.*", "z");
                return c.Valid();
            case "112": case "112b":
                c.N("var values: [3 of i32] = [1, 2, 3]").Def("values");
                c.N("$g = values@ref (G94 guard, where located)").Def("$g").Borrow("Lg", "values", Ref, "$g");
                var call = id == "112" ? "reset($a) (index)" : "peek($a) (index)";
                c.N("$a = values@" + (id == "112" ? "uniq (reserved)" : "ref")).Def("$a").Borrow("La", "values", id == "112", "$a", reserve: id == "112");
                c.N(call).Def("$i").Use("$a"); if (id == "112") ns(c).Activate("La");
                c.N("EndBorrow $g (index evaluated)").EndBorrow("$g", guard: false);
                c.N("_0 = values[$i]").A(K.Read, "values.[]").Use("$i");
                return id == "112" ? c.Error(call) : c.Valid();
            case "106":
                c.N("func f(a: uniq/H, b: uniq/H)").Param("La", "a").Param("Lb", "b");
                c.N("var cur = a").Def("cur").Borrow("L0", "a.*", Uniq, "cur");
                c.N("$r1 = cur.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "cur.*.inner.*", Uniq, "$r1", reserve: true);
                c.N("$t2 = cur@uniq (exchange target, reserved)").Def("$t2").Borrow("Lt2", "cur", Uniq, "$t2", reserve: true);
                c.N("$u = b (Reborrow at a value parameter, reserved)").Def("$u").Borrow("Lu", "b.*", Uniq, "$u", reserve: true);
                const string x = "do { let old = exchange($t2, with: $u); 0 } (argument)";
                c.N(x).Activate("Lt2").Activate("Lu").Def("old").Through("$t2.*", "$u");
                c.N("let first = takeWith($r1, …)").Activate("Lr1").Def("first");
                c.N("return first").Flow("first", "$ret");
                return c.Error(x);
            default: // 107
                c.N("func f(p: uniq/(uniq/H), o: uniq/H)").Param("Lp", "p").Param("Lo", "o");
                c.N("$r1 = p@follow.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "p.*.*.inner.*", Uniq, "$r1", reserve: true);
                c.N("$o = o (Reborrow)").Def("$o").Borrow("Lo2", "o.*", Uniq, "$o");
                const string w = "do { p@follow = $o; 0 } (argument)";
                c.N(w).A(K.Move, "$o").Through("p.*", "$o");
                c.N("let first = takeWith($r1, …)").Activate("Lr1").Def("first");
                c.N("return first").Flow("first", "$ret");
                return c.Error(w);
        }
    }

    static Node ns(Case c) => c.Nodes[^1];

    // 105: a Copy read of a Slice element that stores a reference carries the element's Loans (the Slice index as a shared
    // borrow Deref: content of the handle's backing), not the backing Loan alone (the inline step).
    static Case ReviewSlice() { var c = ReviewSliceCase(false); c.Inline = ReviewSliceCase(true); return c; }

    static Case ReviewSliceCase(bool inline)
    {
        var c = new Case("105", "review: Slice element Copy: `var arr = [x@ref, y@ref]; var s = arr[..]; let e = s[i]; x = 5; inspect(e)`");
        var el = inline ? "$sc.[]" : "$sc.*.[]";
        c.N("var x = 1; var y = 2; let i: isize = pick()").Def("x", "y", "i");
        c.N("var arr: [2 of ref/i32] = [x@ref, y@ref]").Def("arr").Borrow("Lx", "x", Ref, "arr.0").Borrow("Ly", "y", Ref, "arr.1");
        c.N("var s = arr[..]").Def("s").Borrow("La", "arr", Ref, "s");
        c.N("$sc = s (G94 copy)").Def("$sc").A(K.Read, "s").Flow("s", "$sc");
        c.N("let e = $sc[i]").Def("e").A(K.Read, el).Flow(el, "e");
        c.N("StorageDead $sc").A(K.Write, "$sc").Def("$sc");
        c.N("x = 5").Assign("x"); c.N("inspect(e)").Use("e");
        return c.Error("x = 5");
    }

    static int Num(Case c) => c.Id.StartsWith("r13") ? 141 : char.IsDigit(c.Id[0]) ? int.Parse(new string(c.Id.TakeWhile(char.IsDigit).ToArray())) : 0; // round 13: r13… is family 141
    public static bool NoS1P(Case c) => Num(c) >= 50 || c.Nodes.Any(n => n.EndBorrows.Count > 0); // reviews' cases: design A only
    public static bool Review23a(Case c) => Num(c) is >= 50 and < 100;
    public static bool Review1b(Case c) => Num(c) is >= 100 and < 130;
    public static bool Round11(Case c) => Num(c) >= 130;

    // ---- Round 10 (1b rules, designer A) ----
    // G119 (SPEC 15.6.7): during a call reservation its Place, owner and the references it was reached through are
    // protected; a write, Move, destruction or exclusive operation on them is an error, shared reads coexist.
    // H {var inner: uniq/S}; takeWith(self: uniq/S, f: i32) is a concrete call here (its F6 phantom changes nothing).
    static Case Route(string id)
    {
        var c = new Case(id, id switch
        {
            "38a" => "`Kimi.Intrinsics.replace(r@follow@uniq, with: do { r = y@uniq; 5 })`: writes the reference r the target was reached through",
            "38b" => "`first = cur.inner.takeWith(do { cur.inner = s2; 0 })`: writes the Place holding the reached reference",
            "38c" => "`first = cur.inner.takeWith(do { cur@follow = H(inner: s2); 0 })`: writes the owner of that Place",
            "38d" => "`h@follow@uniq.c.bump(do { h = y@uniq; 1 })`, G92b folded: one reservation $c on `(*h).c`, none on `h.*`",
            "38e" => "`var cur = a; first = cur.inner.takeWith(do { a = b; 0 })`: a is not on the route (precision)",
            "38f" => "21a with indexUniq's receiver reservation: `$r = cursor.children; _t = indexUniq($r, 0); cursor = move _t`",
            "38g" => "review R1: `first = cur.inner.takeWith(do { replace(cur@uniq, with: b); 0 })`",
            "38h" => "`replace(arr[0]@follow@uniq, with: do { arr[1] = z@uniq; 5 })`: distinct constant indices",
            "38j" => "alias: `let pc = cur@uniq; first = cur.inner.takeWith(do { pc@follow = b; 0 })`",
            "38k" => "another name: `var rr = hh@uniq; first = rr.inner.takeWith(do { hh.inner = s2; 0 })`",
            _ => "`replace(arr[i]@follow@uniq, with: do { arr[j] = z@uniq; 5 })`: indices may be equal",
        });
        switch (id)
        {
            case "38a":
                c.N("var x = 1; var y = 2").Def("x", "y");
                c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
                c.N("$t = r@follow@uniq (replace target, reserved)").Def("$t").Borrow("Lt", "r.*", Uniq, "$t", reserve: true, inPlace: true);
                c.N("do { r = y@uniq; 5 } (argument)").Assign("r").Borrow("Ly", "y", Uniq, "r");
                c.N("$five = 5").Def("$five"); c.N("replace($t, with: $five)").Activate("Lt").Through("$t.*", "$five");
                c.N("inspect(r)").Use("r");
                return c.Error("do { r = y@uniq; 5 } (argument)");
            case "38b": case "38c": case "38e": case "38g":
                c.N("func f(a: uniq/H, b: uniq/H, s2: uniq/S)").Param("La", "a").Param("Lb", "b").Param("Ls", "s2");
                c.N("var cur = a").Def("cur").Borrow("L0", "a.*", Uniq, "cur");
                c.N("$r1 = cur.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "cur.*.inner.*", Uniq, "$r1", reserve: true);
                string w;
                if (id == "38b") { c.N("$s = s2 (Reborrow)").Def("$s").Borrow("Ls2", "s2.*", Uniq, "$s"); w = "do { cur.inner = $s; 0 } (argument)"; c.N(w).Through("cur.*.inner", "$s"); }
                else if (id == "38c")
                {
                    c.N("$s = s2 (Reborrow)").Def("$s").Borrow("Ls2", "s2.*", Uniq, "$s");
                    c.N("$h = H(inner: $s)").Def("$h").A(K.Move, "$s").Flow("$s", "$h.inner");
                    w = "do { cur@follow = $h; 0 } (argument)"; c.N(w).A(K.Move, "$h").Through("cur.*", "$h");
                }
                else if (id == "38e") { w = null; c.N("do { a = b; 0 } (argument)").Assign("a").Borrow("Lb2", "b.*", Uniq, "a"); }
                else
                {
                    c.N("$t2 = cur@uniq (replace target, reserved)").Def("$t2").Borrow("Lt2", "cur", Uniq, "$t2", reserve: true, inPlace: true);
                    c.N("$u = b (Reborrow at a value parameter, reserved)").Def("$u").Borrow("Lu", "b.*", Uniq, "$u", reserve: true);
                    w = "do { replace($t2, with: $u); 0 } (argument)"; c.N(w).Activate("Lt2").Activate("Lu").Through("$t2.*", "$u");
                }
                c.N("let first = takeWith($r1, …)").Activate("Lr1").Def("first").Phantom("Phi1", "cur.*.inner.*", "first", "Lr1");
                c.N("return first").Flow("first", "$ret");
                return w == null ? c.Valid() : c.Error(w);
            case "38d":
                c.N("var x = C(n: 0); var y = C(n: 1)").Def("x", "y");
                c.N("var h = x@uniq").Def("h").Borrow("L1", "x", Uniq, "h");
                c.N("$c = TwoPhaseUniq((*h).c) (receiver of bump)").Def("$c").Borrow("Rc", "h.*.c", Uniq, "$c", reserve: true);
                c.N("do { h = y@uniq; 1 } (argument)").Assign("h").Borrow("Ly", "y", Uniq, "h");
                c.N("bump($c, 1)").Activate("Rc");
                return c.Error("do { h = y@uniq; 1 } (argument)");
            case "38j":
                c.N("func f(a: uniq/H, b: uniq/H)").Param("La", "a").Param("Lb", "b");
                c.N("var cur = a").Def("cur").Borrow("L0", "a.*", Uniq, "cur");
                c.N("let pc = cur@uniq").Def("pc").Borrow("Lpc", "cur", Uniq, "pc");
                c.N("$r1 = cur.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "cur.*.inner.*", Uniq, "$r1", reserve: true);
                c.N("$b = b (Reborrow)").Def("$b").Borrow("Lb2", "b.*", Uniq, "$b");
                c.N("do { pc@follow = $b; 0 } (argument)").A(K.Move, "$b").Through("pc.*", "$b");
                c.N("let first = takeWith($r1, …)").Activate("Lr1").Def("first").Phantom("Phi1", "cur.*.inner.*", "first", "Lr1");
                c.N("return first").Flow("first", "$ret");
                return c.Error("$r1 = cur.inner (receiver, reserved)");
            case "38k":
                c.N("func f(s1: uniq/S, s2: uniq/S)").Param("Ls1", "s1").Param("Ls2", "s2");
                c.N("var hh = H(inner: s1)").Def("hh").Borrow("Li", "s1.*", Uniq, "hh.inner");
                c.N("var rr = hh@uniq").Def("rr").Borrow("Lrr", "hh", Uniq, "rr");
                c.N("$r1 = rr.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "rr.*.inner.*", Uniq, "$r1", reserve: true);
                c.N("$s = s2 (Reborrow)").Def("$s").Borrow("Ls2b", "s2.*", Uniq, "$s");
                c.N("do { hh.inner = $s; 0 } (argument)").Assign("hh.inner").A(K.Move, "$s").Flow("$s", "hh.inner");
                c.N("let first = takeWith($r1, …)").Activate("Lr1").Def("first").Phantom("Phi1", "rr.*.inner.*", "first", "Lr1");
                c.N("return first").Flow("first", "$ret");
                return c.Error("do { hh.inner = $s; 0 } (argument)");
            case "38f":
                c.N("func descend(root: uniq/Tree)").Param("Lroot", "root");
                c.N("var cursor = root").Def("cursor").Borrow("L0", "root.*", Uniq, "cursor");
                c.N("while cursor.children.length > 0").A(K.Read, "cursor.*.children").Goto("  $r = cursor.children (indexUniq receiver, reserved)", "cursor.value = 1");
                c.N("  $r = cursor.children (indexUniq receiver, reserved)").Def("$r").Borrow("Lr", "cursor.*.children", Uniq, "$r", reserve: true);
                c.N("  _t = indexUniq($r, 0) (retains $r)").Activate("Lr").Def("_t").Flow("$r", "_t");
                c.N("  cursor = move _t").Assign("cursor").A(K.Move, "_t").Flow("_t", "cursor").Goto("while cursor.children.length > 0");
                c.N("cursor.value = 1").A(K.Write, "cursor.*.value");
                return c.Valid();
            default:
                bool constant = id == "38h";
                c.N("var x = 1; var y = 2; var z = 3; let i: isize = pick(); let j: isize = pick()").Def("x", "y", "z", "i", "j");
                c.N("var arr: [2 of uniq/i32] = [x@uniq, y@uniq]").Def("arr").Borrow("Lx", "x", Uniq, "arr.0").Borrow("Ly", "y", Uniq, "arr.1");
                c.N($"$t = arr[{(constant ? "0" : "i")}]@follow@uniq (replace target, reserved)").Def("$t").Borrow("Lt", constant ? "arr.0.*" : "arr.[].*", Uniq, "$t", reserve: true, inPlace: true);
                var write = $"do {{ arr[{(constant ? "1" : "j")}] = z@uniq; 5 }} (argument)";
                if (constant) c.N(write).Assign("arr.1").Borrow("Lz", "z", Uniq, "arr.1");
                else { c.N("$z = z@uniq").Def("$z").Borrow("Lz", "z", Uniq, "$z"); c.N(write).Through("arr.[]", "$z"); }
                c.N("$five = 5").Def("$five"); c.N("replace($t, with: $five)").Activate("Lt").Through("$t.*", "$five");
                c.N("inspect(arr)").Use("arr");
                return constant ? c.Valid() : c.Error(write);
        }
    }

    // G110 (SPEC 15.6.3, S1): a Borrow past a shared Deref forms no Loan; it carries the Loans of its last shared reference
    // and of the references dereferenced after it. S {r: ref/i32, n: i32}; S3 {rs: ref/(uniq/i32)}; Item an obj class.
    static Case SharedLayer(string id)
    {
        var c = new Case(id, id switch
        {
            "39a" => "PLAN G110: `let v = s.r@follow@ref; let t = s@move; inspect(v)`",
            "39b" => "39a, then `x = 5` before `inspect(v)`",
            "39c" => "shared layer under an exclusive parent: `var p = s@uniq; let v = p.r@follow@ref; s.n = 1; inspect(v)`",
            "39d" => "39c with `x = 5` instead of `s.n = 1`",
            "39e" => "`let v = s.r@follow@ref; s.r = y@ref; inspect(v)`: the stored shared reference is written",
            "39f" => "39e, then `x = 5`",
            "39g" => "objref: `let o = item@objref; let v = o.name@ref; o = other@objref; inspect(v)`",
            "39h" => "objref: `let o = item@objref; let v = o.name@ref; let t = item@move; inspect(v)`",
            "39i" => "pair layer `p: s/i32`, ref view: `let v = s.p@follow@ref; let t = s@move; inspect(v)`",
            "39j" => "pair layer `p: s/i32`, uniq view: the same program",
            "39k" => "activation: `g(s@uniq, s.r@follow@ref)`",
            "39n" => "S1 §2.1: `var s = S(p: t@uniq); let q = s@ref; let e = q.p@follow@ref; t = [4]; inspect(e@follow)`",
            "39l" => "S3 under an exclusive parent: `var p = s@uniq; let v = p.rs@follow@follow@ref; slot@follow = 5; inspect(v)`",
            _ => "39l with `s.rs = other@ref` instead of the write through slot",
        });
        if (id == "39n") // S {o} has var p: uniq/Array<i32> during o; past the shared q, e carries q's Loan on s and s.p's on t
        {
            c.N("var t = [1, 2, 3]").Def("t");
            c.N("var s = S(p: t@uniq)").Def("s").Borrow("Lt", "t", Uniq, "s.p");
            c.N("let q = s@ref").Def("q").Borrow("Lq", "s", Ref, "q");
            c.N("let e = q.p@follow@ref").Def("e").Borrow("Le", "q.*.p.*", Ref, "e", sharedAt: 1);
            c.N("t = [4]").Assign("t");
            c.N("inspect(e@follow)").A(K.Read, "e.*");
            return c.Error("t = [4]");
        }
        if (id is "39g" or "39h")
        {
            c.N("var item = Item.init(); var other = Item.init()").Def("item", "other");
            c.N("let o = item@objref").Def("o").Borrow("Lo", "item", Ref, "o");
            c.N("let v = o.name@ref").Def("v").Borrow("Lv", "o.*.name", Ref, "v", sharedAt: 1);
            if (id == "39g") c.N("o = other@objref").Assign("o").Borrow("Lo2", "other", Ref, "o");
            else c.N("let t = item@move").Def("t").A(K.Move, "item").Flow("item", "t");
            c.N("inspect(v)").Use("v");
            return id == "39g" ? c.Valid() : c.Error("let t = item@move");
        }
        if (id is "39l" or "39m")
        {
            c.N("var x = 1; var o = 2").Def("x", "o");
            c.N("var slot = x@uniq").Def("slot").Borrow("L1", "x", Uniq, "slot");
            c.N("var s = S3(rs: slot@ref)").Def("s").Borrow("Lo", "slot", Ref, "s.rs");
            c.N("var p = s@uniq").Def("p").Borrow("Lp", "s", Uniq, "p");
            c.N("let v = p.rs@follow@follow@ref").Def("v").Borrow("Lv", "p.*.rs.*.*", Ref, "v", sharedAt: 2);
            if (id == "39l") c.N("slot@follow = 5").A(K.Write, "slot.*");
            else c.N("s.rs = o@ref").Assign("s.rs").Borrow("Lo3", "o", Ref, "s.rs");
            c.N("inspect(v)").Use("v");
            return id == "39l" ? c.Error("slot@follow = 5") : c.Valid();
        }
        bool pairUniq = id == "39j";
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N(pairUniq ? "var s = S(p: x@uniq, n: 0)" : "var s = S(r: x@ref, n: 0)").Def("s").Borrow("Lx", "x", pairUniq, "s.r");
        if (id == "39k")
        {
            c.N("$1 = s@uniq (reserved)").Def("$1").Borrow("L1", "s", Uniq, "$1", reserve: true);
            c.N("$2 = s.r@follow@ref").Def("$2").Borrow("L2", "s.r.*", Ref, "$2", sharedAt: 1);
            c.N("g($1, $2)").Activate("L1").Use("$2");
            return c.Valid();
        }
        if (id is "39c" or "39d")
        {
            c.N("var p = s@uniq").Def("p").Borrow("Lp", "s", Uniq, "p");
            c.N("let v = p.r@follow@ref").Def("v").Borrow("Lv", "p.*.r.*", Ref, "v", sharedAt: 2);
            if (id == "39c") c.N("s.n = 1").Assign("s.n"); else c.N("x = 5").Assign("x");
            c.N("inspect(v)").Use("v");
            return id == "39c" ? c.Valid() : c.Error("x = 5");
        }
        c.N(id is "39i" or "39j" ? "let v = s.p@follow@ref" : "let v = s.r@follow@ref").Def("v").Borrow("Lv", "s.r.*", Ref, "v", sharedAt: pairUniq ? 0 : 1);
        if (id is "39e" or "39f") c.N("s.r = y@ref").Assign("s.r").Borrow("Ly", "y", Ref, "s.r");
        else c.N("let t = s@move").Def("t").A(K.Move, "s").Flow("s", "t");
        if (id is "39b" or "39f") c.N("x = 5").Assign("x");
        c.N("inspect(v)").Use("v");
        if (id is "39b" or "39f") return c.Error("x = 5");
        return pairUniq ? c.Error("let t = s@move") : c.Valid();
    }

    // ---- Round 10 (prototype round 10 of the chosen 1b design diff) ----
    // G92b (b2) fold (choice §2): on a preparation path a written exclusive borrow the path continues through is not formed;
    // the input has one reservation at its lending point. Holder {var items: Array<i32>; var count: isize}.
    static Case Fold(string id)
    {
        var c = new Case(id, "G92b fold: " + id switch
        {
            "38l" => "`holder@uniq.items.append(holder.count)`: one reservation on `holder.items`, a shared read of `holder.count` in it",
            "38m" => "`let r = holder.count@ref; holder@uniq.items.append(1); inspect(r)` ((b1) would reject it)",
            _ => "`let v = holder.items@ref; holder@uniq.items.append(1); inspect(v)`",
        });
        c.N("var holder = Holder(items: [], count: 0)").Def("holder");
        if (id == "38m") c.N("let r = holder.count@ref").Def("r").Borrow("Lc", "holder.count", Ref, "r");
        if (id == "38n") c.N("let v = holder.items@ref").Def("v").Borrow("Lv", "holder.items", Ref, "v");
        c.N("$ra = TwoPhaseUniq(holder.items) (append receiver; no Ref of holder)").Def("$ra").Borrow("Lr", "holder.items", Uniq, "$ra", reserve: true);
        if (id == "38l") { c.N("$n = holder.count").Def("$n").A(K.Read, "holder.count"); c.N("append($ra, $n)").Activate("Lr").Use("$n"); return c.Valid(); }
        const string call = "append($ra, 1)";
        c.N(call).Activate("Lr");
        c.N(id == "38m" ? "inspect(r)" : "inspect(v)").Use(id == "38m" ? "r" : "v");
        return id == "38m" ? c.Valid() : c.Error(call);
    }

    // Slice index (choice §1; MIR-B finding (ii)): the Index of a Slice handle is a shared borrow
    // Deref, since a Slice element is the handle's referent (SPEC 4.8.1: element references retain the backing and "do not
    // borrow the handle variable"). A range's value carries its backing borrow (R4-M40). G94 copies a Slice receiver into a
    // temp where it is located ($sc); its StorageDead at the end of the statement is a shallow destroy (A3: StorageDead →
    // destroy), encoded as a Write. c.Inline: the same program with the Index as an inline step. UniqSlice has no built-in
    // Index (review N1): its selections are `index`/`indexUniq` calls (42c).
    static Case Slice(string id) { var c = SliceCase(id, inline: false); c.Inline = SliceCase(id, inline: true); return c; }

    static Case SliceCase(string id, bool inline)
    {
        var c = new Case(id, "Slice index: " + id switch
        {
            "42a" => "`var s = arr[..]; let e = s[i]@ref; s = other; arr[0] = 5; inspect(e)`",
            "42b" => "`var s = arr[..]; let e = s[i]@ref; s = other; inspect(e)`",
            "42c" => "UniqSlice (review N1: `index`/`indexUniq` calls, UniqSlice.kimi:36,42): `var u = arr.sliceUniq(); let m = u[i]@uniq; inspect(u[j]); m@follow = 1`",
            _ => "SPEC 4.8.2: `func first<T>(s: Slice<T>) -> ref/T during s.source => return s[0]@ref`",
        });
        string El(string handle, string index) => handle + (inline ? "." : ".*.") + index; // the element Place
        int sh = inline ? 0 : 1;                                                              // a Slice handle's Deref is shared
        switch (id)
        {
            case "42a": case "42b":
                c.N("var arr = [1, 2, 3]; var arr2 = [4]; let i: isize = pick()").Def("arr", "arr2", "i");
                c.N("var s = arr[..]").Def("s").Borrow("La", "arr", Ref, "s");
                c.N("var other = arr2[..]").Def("other").Borrow("Lb", "arr2", Ref, "other");
                c.N("$sc = s (G94: the handle copied where located)").Def("$sc").A(K.Read, "s").Flow("s", "$sc");
                c.N("let e = $sc[i]@ref").Def("e").Borrow("Le", El("$sc", "[]"), Ref, "e", sharedAt: sh);
                c.N("StorageDead $sc (end of the statement)").A(K.Write, "$sc").Def("$sc");
                c.N("s = other").Assign("s").A(K.Read, "other").Flow("other", "s");
                if (id == "42a") c.N("arr[0] = 5").Assign("arr.0");
                c.N("inspect(e)").Use("e");
                return id == "42a" ? c.Error("arr[0] = 5") : c.Valid();
            case "42c": // a call lowering, no Slice index: the same nodes under SliceIndexInline
                c.N("var arr = [1, 2, 3]; let i: isize = pick(); let j: isize = pick()").Def("arr", "i", "j");
                c.N("var u = arr.sliceUniq()").Def("u").Borrow("Lu", "arr", Uniq, "u");
                c.N("$r = u@uniq (indexUniq's receiver, reserved)").Def("$r").Borrow("Lr", "u", Uniq, "$r", reserve: true);
                c.N("let m = indexUniq($r, i) (retains its receiver)").Activate("Lr").Def("m").Flow("$r", "m");
                c.N("inspect(u[j]): $q = u@ref (index's receiver)").Def("$q").Borrow("Lq", "u", Ref, "$q");
                c.N("inspect(index($q, j)) (retains its receiver)").Def("$e").Flow("$q", "$e").Use("$e");
                c.N("m@follow = 1").A(K.Write, "m.*");
                return c.Error("inspect(u[j]): $q = u@ref (index's receiver)");
            default:
                c.N("func first<T>(s: Slice<T>)").Param("Ls", "s");
                c.N("$sc = s (G94 copy)").Def("$sc").A(K.Read, "s").Flow("s", "$sc");
                c.N("_0 = $sc[0]@ref").Borrow("Le", El("$sc", "0"), Ref, "$ret", sharedAt: sh);
                c.N("StorageDead $sc (end of the statement)").A(K.Write, "$sc").Def("$sc");
                c.N("return").Goto();
                return c.Valid();
        }
    }

    // Finding 1 checks: the builder's other EndBorrow, a pending reference argument temp ended on a transfer edge
    // (MirBuilder.Control.cs:144-152): an abandoned reservation, an abandoned comparison operand, and 38f's retained
    // reservation whose index argument may break (the earlier iterations' values flowed out of $r through its Call).
    static Case Abandoned(string id)
    {
        var c = new Case(id, "abandoned EndBorrow: " + id switch
        {
            "44a" => "`f(x@uniq, do { if p => return 0; 1 }); inspect(x)` (reservation)",
            "44b" => "`let same = x == do { if p => return 0; y }; x = 3; inspect(same)` (comparison operand)",
            _ => "38f with `cursor = indexUniq(cursor.children, do { if stop() => break; 0 })` (retained reservation in a loop)",
        });
        switch (id)
        {
            case "44a":
                c.N("var x = 1").Def("x");
                c.N("$r = x@uniq (f's argument, reserved)").Def("$r").Borrow("Lr", "x", Uniq, "$r", reserve: true);
                c.N("if p (in the next argument)").Goto("  return 0: EndBorrow $r", "f($r, 1)");
                c.N("  return 0: EndBorrow $r").EndBorrow("$r", guard: false).Goto("exit");
                c.N("f($r, 1)").Activate("Lr");
                c.N("inspect(x)").A(K.Read, "x");
                c.N("exit").Goto();
                return c.Valid();
            case "44b":
                c.N("var x = 1; var y = 2").Def("x", "y");
                c.N("$a = x@ref (comparison operand)").Def("$a").Borrow("La", "x", Ref, "$a");
                c.N("if p (in the right operand)").Goto("  return 0: EndBorrow $a", "$b = y@ref");
                c.N("  return 0: EndBorrow $a").EndBorrow("$a", guard: false).Goto("exit");
                c.N("$b = y@ref").Def("$b").Borrow("Lb", "y", Ref, "$b");
                c.N("let same = eq($a, $b)").Def("same").Use("$a", "$b");
                c.N("x = 3").Assign("x");
                c.N("inspect(same)").Use("same");
                c.N("exit").Goto();
                return c.Valid();
            default:
                c.N("func descend(root: uniq/Tree)").Param("Lroot", "root");
                c.N("var cursor = root").Def("cursor").Borrow("L0", "root.*", Uniq, "cursor");
                c.N("while cursor.children.length > 0").A(K.Read, "cursor.*.children").Goto("  $r = cursor.children (indexUniq receiver, reserved)", "cursor.value = 1");
                c.N("  $r = cursor.children (indexUniq receiver, reserved)").Def("$r").Borrow("Lr", "cursor.*.children", Uniq, "$r", reserve: true);
                c.N("  if stop() (in the index argument)").Goto("  break: EndBorrow $r", "  _t = indexUniq($r, 0) (retains $r)");
                c.N("  break: EndBorrow $r").EndBorrow("$r", guard: false).Goto("cursor.value = 1");
                c.N("  _t = indexUniq($r, 0) (retains $r)").Activate("Lr").Def("_t").Flow("$r", "_t");
                c.N("  cursor = move _t").Assign("cursor").A(K.Move, "_t").Flow("_t", "cursor").Goto("while cursor.children.length > 0");
                c.N("cursor.value = 1").A(K.Write, "cursor.*.value");
                return c.Valid();
        }
    }

    // G110 × guard candidates: SPEC 14.8.3's example. A shared-mode Subject is a shared borrow ($s: ref/Packet,
    // MirBuilder.Match.cs:27-28), and each candidate is a Ref of a Place selected under it (:82-84, :104), so the
    // candidate's Ref lies past a shared Deref and, under G110, forms no Loan for err(ended) to find at its EndBorrow.
    // keep(slot: uniq/(ref/Data during s), value: ref/Data during s) stores value into slot's referent.
    static Case GuardExample()
    {
        var c = new Case("43a", "SPEC 14.8.3: `var saved = fallback@ref; match packet .Data(let d) if keep(saved@uniq, d) => (); use(saved)`");
        c.N("var fallback = Data(); var packet: Packet = .Data(Data())").Def("fallback", "packet");
        c.N("var saved = fallback@ref").Def("saved").Borrow("Lf", "fallback", Ref, "saved");
        c.N("$s = packet@ref (Subject, shared mode)").Def("$s").Borrow("Lsub", "packet", Ref, "$s");
        c.N("d = $s.data (candidate)").Def("d").Borrow("Ld", "$s.*.data", Ref, "d", sharedAt: 1);
        c.N("$a = saved@uniq (reserved)").Def("$a").Borrow("La", "saved", Uniq, "$a", reserve: true);
        c.N("keep($a, d) (stores d into saved)").Activate("La").Through("$a.*", "d");
        const string end = "guard cleanup: EndBorrow d";
        c.N(end).EndBorrow("d");
        c.N("use(saved)").Use("saved");
        return c.Error(end);
    }

    // ---- Round 10 (designer B's cases, renumbered 38->40, 39->41) ----
    // G119, SPEC 15.6.7: during a reservation, its Place, owner and the references it was reached through are protected.
    // H {var inner: uniq/S; var other: i32}. 36h's shape with other writes in the `do` argument (40a-40d); 40e reserves
    // through a reborrow cur2, so cur is not on the route; 40f writes the receiver's route as the Call's destination.
    static Case RouteWrite(string id)
    {
        var c = new Case(id, "G119: " + id switch
        {
            "40a" => "`first = cur.inner.takeWith(do { cur@follow = H(inner: s2, other: 0); 0 })`: writes the owner",
            "40b" => "`first = cur.inner.takeWith(do { cur.inner = s2; 0 })`: writes the reference last dereferenced",
            "40c" => "`first = cur.inner.takeWith(do { cur.other = 5; 0 })`: a sibling Field",
            "40d" => "`first = cur.inner.takeWith(do { inspect(cur@ref); 0 })`: a shared Borrow of the route",
            "40e" => "`var cur2 = cur; first = cur2.inner.takeWith(do { cur = b; 0 })`: cur is not on the route",
            "40f" => "`var node = head; node = node.next(); node.value = 1`, next(self: uniq/Node) -> uniq/Node during self",
            "40g" => "`refs[i]@follow.push(do { refs[j] = s3; 0 })` (a concrete call, no F6): the route through an index",
            _ => "40g with `refs[0]` and `refs[1]`",
        });
        if (id is "40g" or "40h")
        {
            var (ci, cj) = id == "40g" ? ("[]", "[]") : ("0", "1");
            c.N("func f(r1: uniq/S, r2: uniq/S, s3: uniq/S); let i, j = pick()").Param("L1", "r1").Param("L2", "r2").Param("L3", "s3").Def("i", "j");
            c.N("var refs: [2 of uniq/S] = [r1, r2]").Def("refs").Borrow("Lr1", "r1.*", Uniq, "refs.0").Borrow("Lr2", "r2.*", Uniq, "refs.1");
            c.N($"$r = refs[{(ci == "[]" ? "i" : ci)}]@follow (receiver, reserved)").Def("$r").Borrow("Lr", $"refs.{ci}.*", Uniq, "$r", reserve: true);
            c.N("$s = s3 (Reborrow)").Def("$s").Borrow("Ls", "s3.*", Uniq, "$s");
            var w = $"refs[{(cj == "[]" ? "j" : cj)}] = move $s";
            c.N(w).Through($"refs.{cj}", "$s").A(K.Move, "$s");
            c.N("push($r, 0)").Activate("Lr").Use("$r");
            c.N("inspect(refs)").Use("refs");
            return id == "40g" ? c.Error(w) : c.Valid();
        }
        if (id == "40f")
        {
            c.N("func walk(head: uniq/Node)").Param("Lh", "head");
            c.N("var node = head").Def("node").Borrow("L0", "head.*", Uniq, "node");
            c.N("$r = node (receiver, reserved)").Def("$r").Borrow("Lr", "node.*", Uniq, "$r", reserve: true);
            c.N("node = next($r)").Activate("Lr").Assign("node").Flow("$r", "node");
            c.N("node.value = 1").A(K.Write, "node.*.value");
            return c.Valid();
        }
        c.N("func f(a: uniq/H, b: uniq/H, s2: uniq/S)").Param("La", "a").Param("Lb", "b").Param("Ls2", "s2");
        c.N("var cur = a").Def("cur").Borrow("L0", "a.*", Uniq, "cur");
        var holder = "cur";
        if (id == "40e") { c.N("var cur2 = cur").Def("cur2").Borrow("Lc2", "cur.*", Uniq, "cur2"); holder = "cur2"; }
        c.N($"$r1 = {holder}.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", holder + ".*.inner.*", Uniq, "$r1", reserve: true);
        string write = null;
        switch (id)
        {
            case "40a":
                c.N("$h = H(inner: s2, other: 0)").Def("$h").Borrow("Lh", "s2.*", Uniq, "$h");
                write = "cur@follow = move $h"; c.N(write).Through("cur.*", "$h").A(K.Move, "$h"); break;
            case "40b":
                c.N("$s = s2 (Reborrow)").Def("$s").Borrow("Ls", "s2.*", Uniq, "$s");
                write = "cur.inner = move $s"; c.N(write).Through("cur.*.inner", "$s").A(K.Move, "$s"); break;
            case "40c": c.N("cur.other = 5").A(K.Write, "cur.*.other"); break;
            case "40d": c.N("$q = cur@ref").Def("$q").Borrow("Lq", "cur", Ref, "$q"); c.N("inspect($q)").Use("$q"); break;
            default: c.N("do { cur = b; 0 } (argument)").Assign("cur").Borrow("Lb2", "b.*", Uniq, "cur"); break;
        }
        c.N("let first = takeWith($r1, …)").Activate("Lr1").Def("first").Phantom("Phi1", holder + ".*.inner.*", "first", "Lr1");
        c.N("return first").Flow("first", "$ret");
        return write != null ? c.Error(write) : c.Valid();
    }

    // G110, SPEC 15.6.3 (S1): a Borrow past a shared Deref forms no Loan; it carries the Loans of the reference of its
    // last shared Deref and of those it dereferences after it. S {var r: ref/i32}; S2 {var p: uniq/Array<i32>};
    // S3 {var r: ref/i32; var n: i32}; H {var view: objref/Item}.
    // Round 10: each declaration has its own node before the Borrow of the declared variable (B combined them; PrefixKill,
    // which also ends direct Loans, then ended a Loan at its own issue because the same node declared its Place).
    static Case SharedPast(string id)
    {
        var c = new Case(id, "G110: " + id switch
        {
            "41a" => "`let v = s.r@follow@ref; let t = s@move; inspect(v)` (S1 §9)",
            "41b" => "41a, then `x = 5` before `inspect(v)`",
            "41c" => "`var p = s@uniq; let v = p.r@follow@ref; let t = s@move; inspect(v)` (shared layer under an exclusive parent)",
            "41d" => "`var p = s@uniq; let v = p.r@follow@ref; p.r = y@ref; x = 5; inspect(v); inspect(p)`",
            "41e" => "41d without `x = 5`: the stored shared reference is written while v lives",
            "41f" => "`var r = x@ref; let v = r@follow@ref; let w = r@uniq; w@follow = y@ref; inspect(v)`",
            "41g" => "41f, then `x = 5` before `inspect(v)`",
            "41h" => "`g(s@uniq, s.r@follow@ref)`",
            "41i" => "`g(x@uniq, s.r@follow@ref)` with `s.r = x@ref`",
            "41j" => "S1 §2.1: `let q = s@ref; let e = q.p@follow@ref; t = [4]; inspect(e@follow)`",
            "41k" => "41j with `s.p = t2@uniq` instead of `t = [4]`",
            "41l" => "objref: `let f = h.view@follow.count@ref; let g = h@move; inspect(f)`",
            "41m" => "objref: `let f = h.view@follow.count@ref; item.count = 5; inspect(f)`",
            "41n" => "self-reference: `s.r = s.n@ref; let v = s.r@follow@ref; s.n = 5; inspect(v)`",
            "41o" => "self-reference at formation: `s.r = s.n@ref; let p = s@uniq; p.n = 5; inspect(p.r@follow)`",
            "41q" => "self-reference, shared: `s.r = s.n@ref; let q = s@ref; inspect(q.r@follow)`",
            _ => "loop: `var keep = z@ref; while c { keep = r@follow@ref; r = y@ref }; x = 5; inspect(keep)`",
        });
        switch (id)
        {
            case "41a": case "41b":
                c.N("var x = 1").Def("x"); c.N("var s = S(r: x@ref)").Def("s").Borrow("Lx", "x", Ref, "s.r");
                c.N("let v = s.r@follow@ref").Def("v").Borrow("Lv", "s.r.*", Ref, "v", sharedAt: 1);
                c.N("let t = s@move").Def("t").A(K.Move, "s").Flow("s", "t");
                if (id == "41b") { c.N("x = 5").Assign("x"); c.N("inspect(v)").Use("v"); return c.Error("x = 5"); }
                c.N("inspect(v)").Use("v"); return c.Valid();
            case "41c": case "41d": case "41e":
                c.N("var x = 1; var y = 2").Def("x", "y"); c.N("var s = S(r: x@ref)").Def("s").Borrow("Lx", "x", Ref, "s.r");
                c.N("var p = s@uniq").Def("p").Borrow("Lp", "s", Uniq, "p");
                c.N("let v = p.r@follow@ref").Def("v").Borrow("Lv", "p.*.r.*", Ref, "v", sharedAt: 2);
                if (id == "41c") { c.N("let t = s@move").Def("t").A(K.Move, "s").Flow("s", "t"); c.N("inspect(v)").Use("v"); return c.Valid(); }
                c.N("$t = y@ref").Def("$t").Borrow("Ly", "y", Ref, "$t");
                c.N("p.r = $t").Through("p.*.r", "$t");
                if (id == "41d") c.N("x = 5").Assign("x");
                c.N("inspect(v)").Use("v"); c.N("inspect(p)").Use("p");
                return id == "41d" ? c.Error("x = 5") : c.Valid();
            case "41f": case "41g":
                c.N("var x = 1; var y = 2").Def("x", "y"); c.N("var r = x@ref").Def("r").Borrow("Lx", "x", Ref, "r");
                c.N("let v = r@follow@ref").Def("v").Borrow("Lv", "r.*", Ref, "v", sharedAt: 1);
                c.N("let w = r@uniq").Def("w").Borrow("Lw", "r", Uniq, "w");
                c.N("$t = y@ref").Def("$t").Borrow("Ly", "y", Ref, "$t");
                c.N("w@follow = $t").Through("w.*", "$t");
                if (id == "41g") { c.N("x = 5").Assign("x"); c.N("inspect(v)").Use("v"); return c.Error("x = 5"); }
                c.N("inspect(v)").Use("v"); return c.Valid();
            case "41h": case "41i":
                c.N("var x = 1").Def("x"); c.N("var s = S(r: x@ref)").Def("s").Borrow("Lx", "x", Ref, "s.r");
                c.N(id == "41h" ? "$1 = s@uniq (reserved)" : "$1 = x@uniq (reserved)").Def("$1").Borrow("L1", id == "41h" ? "s" : "x", Uniq, "$1", reserve: true);
                c.N("$2 = s.r@follow@ref").Def("$2").Borrow("L2", "s.r.*", Ref, "$2", sharedAt: 1);
                c.N("g($1, $2)").Activate("L1").Use("$1", "$2");
                return id == "41h" ? c.Valid() : c.Error("g($1, $2)");
            case "41j": case "41k":
                c.N("var t = [1, 2, 3]; var t2 = [5]").Def("t", "t2"); c.N("var s = S2(p: t@uniq)").Def("s").Borrow("Lt", "t", Uniq, "s.p");
                c.N("let q = s@ref").Def("q").Borrow("Lq", "s", Ref, "q");
                c.N("let e = q.p@follow@ref").Def("e").Borrow("Le", "q.*.p.*", Ref, "e", sharedAt: 1);
                var w = id == "41j" ? "t = [4]" : "s.p = t2@uniq";
                if (id == "41j") c.N(w).Assign("t"); else c.N(w).Assign("s.p").Borrow("Lt2", "t2", Uniq, "s.p");
                c.N("inspect(e@follow)").A(K.Read, "e.*");
                return c.Error(w);
            case "41l": case "41m":
                c.N("var item = makeItem()").Def("item"); c.N("var h = H(view: item@objref)").Def("h").Borrow("Li", "item", Ref, "h.view");
                c.N("let f = h.view@follow.count@ref").Def("f").Borrow("Lf", "h.view.*.count", Ref, "f", sharedAt: 1);
                if (id == "41l") { c.N("let g = h@move").Def("g").A(K.Move, "h").Flow("h", "g"); c.N("inspect(f)").Use("f"); return c.Valid(); }
                c.N("item.count = 5").A(K.Write, "item.count"); c.N("inspect(f)").Use("f");
                return c.Error("item.count = 5");
            case "41n": case "41o": case "41q":
                c.N("var z = 0").Def("z"); c.N("var s = S3(r: z@ref, n: 1)").Def("s").Borrow("Lz", "z", Ref, "s.r");
                c.N("s.r = s.n@ref").Assign("s.r").Borrow("Ln", "s.n", Ref, "s.r");
                if (id == "41n")
                {
                    c.N("let v = s.r@follow@ref").Def("v").Borrow("Lv", "s.r.*", Ref, "v", sharedAt: 1);
                    c.N("s.n = 5").Assign("s.n"); c.N("inspect(v)").Use("v");
                    return c.Error("s.n = 5");
                }
                if (id == "41q") { c.N("let q = s@ref").Def("q").Borrow("Lq", "s", Ref, "q"); c.N("inspect(q.r@follow)").A(K.Read, "q.*.r.*"); return c.Valid(); }
                c.N("let p = s@uniq").Def("p").Borrow("Lp", "s", Uniq, "p");
                c.N("p.n = 5").A(K.Write, "p.*.n"); c.N("inspect(p.r@follow)").A(K.Read, "p.*.r.*");
                return c.Error("let p = s@uniq");
            default:
                c.N("var x = 1; var y = 2; var z = 3").Def("x", "y", "z");
                c.N("var r = x@ref; var keep = z@ref").Def("r", "keep").Borrow("Lx", "x", Ref, "r").Borrow("Lz", "z", Ref, "keep");
                c.N("while c").Goto("  keep = r@follow@ref", "x = 5");
                c.N("  keep = r@follow@ref").Assign("keep").Borrow("Lk", "r.*", Ref, "keep", sharedAt: 1);
                c.N("  r = y@ref").Assign("r").Borrow("Ly", "y", Ref, "r").Goto("while c");
                c.N("x = 5").Assign("x"); c.N("inspect(keep)").Use("keep");
                return c.Error("x = 5");
        }
    }


    // ---- Case 23a review (independent review of the case 23a proposal, 2026-10-17): copied unchanged; 92g/93g added ----
    // S {var p: uniq/i32}: 29a in the reverse order, and through a retaining receiver or argument (proposal §6 row 2).
    static Case CrossingOrder(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "reverse" => "29a reversed: `w = q@follow@ref; c = q.p@follow@uniq; q = o@uniq; c@follow = 5; inspect(w.p@follow)`",
            "recvAfter" => "`c = q.p@follow@uniq; w = q.view()` (shared receiver retained); `q = o@uniq; c@follow = 5; inspect(w.p@follow)`",
            "recvBefore" => "`w = q.view()` (shared receiver retained); `c = q.p@follow@uniq; q = o@uniq; c@follow = 5; inspect(w.p@follow)`",
            _ => "`c = q.p@follow@uniq; w = pass(q)` (Reborrow at a uniq parameter, retained); `q = o@uniq; w.p@follow = 9; c@follow = 5`",
        });
        c.N("var t = 1; var u = 2").Def("t", "u");
        c.N("var s = S(p: t@uniq); var o = S(p: u@uniq)").Def("s", "o").Borrow("Lt", "t", Uniq, "s.p").Borrow("Lu", "u", Uniq, "o.p");
        c.N("var q = s@uniq").Def("q").Borrow("Lq", "s", Uniq, "q");
        string[] err;
        switch (kind)
        {
            case "reverse":
                c.N("let w = q@follow@ref").Def("w").Borrow("Lw", "q.*", Ref, "w");
                c.N("let c = q.p@follow@uniq").Def("c").Borrow("Lc", "q.*.p.*", Uniq, "c");
                err = ["let c = q.p@follow@uniq"]; break;
            case "recvAfter":
                c.N("let c = q.p@follow@uniq").Def("c").Borrow("Lc", "q.*.p.*", Uniq, "c");
                c.N("$r = q (receiver of view, shared)").Def("$r").Borrow("Lr", "q.*", Ref, "$r");
                c.N("let w = view($r) (retains its receiver)").Def("w").Flow("$r", "w");
                err = ["$r = q (receiver of view, shared)"]; break;
            case "recvBefore":
                c.N("$r = q (receiver of view, shared)").Def("$r").Borrow("Lr", "q.*", Ref, "$r");
                c.N("let w = view($r) (retains its receiver)").Def("w").Flow("$r", "w");
                c.N("let c = q.p@follow@uniq").Def("c").Borrow("Lc", "q.*.p.*", Uniq, "c");
                err = ["let c = q.p@follow@uniq"]; break;
            default:
                c.N("let c = q.p@follow@uniq").Def("c").Borrow("Lc", "q.*.p.*", Uniq, "c");
                c.N("$a = q (Reborrow at a uniq parameter, reserved)").Def("$a").Borrow("La", "q.*", Uniq, "$a", reserve: true);
                c.N("let w = pass($a) (retains it)").Activate("La").Def("w").Flow("$a", "w");
                err = ["$a = q (Reborrow at a uniq parameter, reserved)", "let w = pass($a) (retains it)"]; break;
        }
        c.N("q = o@uniq").Assign("q").Borrow("Lo", "o", Uniq, "q");
        if (kind == "arg") { c.N("w.p@follow = 9").A(K.Write, "w.*.p.*"); c.N("c@follow = 5").A(K.Write, "c.*"); }
        else { c.N("c@follow = 5").A(K.Write, "c.*"); c.N("inspect(w.p@follow)").A(K.Read, "w.*.p.*"); }
        return c.Error(err);
    }

    // S2 {var a: uniq/i32, var b: uniq/i32}: two children of q share the first base q and end together (§6 row 5).
    static Case Siblings(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "both" => "`c1 = q.a@follow@uniq; c2 = q.b@follow@uniq; q = t@uniq; q.a@follow = 7; q.b@follow = 8; c1@follow = 1; c2@follow = 2`",
            "old" => "54 without the writes through q; `s.a@follow = 9` (old route to c1's x) before `c1@follow = 1`",
            _ => "conflicting siblings: `c1 = q.a@follow@uniq; c2 = q.a@follow@uniq; q = t@uniq; c1@follow = 1; c2@follow = 2`",
        });
        c.N("var x = 1; var y = 2; var z = 3; var w = 4").Def("x", "y", "z", "w");
        c.N("var s = S2(a: x@uniq, b: y@uniq); var t = S2(a: z@uniq, b: w@uniq)").Def("s", "t")
            .Borrow("Lx", "x", Uniq, "s.a").Borrow("Ly", "y", Uniq, "s.b").Borrow("Lz", "z", Uniq, "t.a").Borrow("Lw", "w", Uniq, "t.b");
        c.N("var q = s@uniq").Def("q").Borrow("Lq", "s", Uniq, "q");
        c.N("let c1 = q.a@follow@uniq").Def("c1").Borrow("L1", "q.*.a.*", Uniq, "c1");
        var second = kind == "conflict" ? "let c2 = q.a@follow@uniq" : "let c2 = q.b@follow@uniq";
        c.N(second).Def("c2").Borrow("L2", kind == "conflict" ? "q.*.a.*" : "q.*.b.*", Uniq, "c2");
        c.N("q = t@uniq").Assign("q").Borrow("Lt", "t", Uniq, "q");
        if (kind == "both") { c.N("q.a@follow = 7").A(K.Write, "q.*.a.*"); c.N("q.b@follow = 8").A(K.Write, "q.*.b.*"); }
        if (kind == "old") c.N("s.a@follow = 9").A(K.Write, "s.a.*");
        c.N("c1@follow = 1").A(K.Write, "c1.*");
        c.N("c2@follow = 2").A(K.Write, "c2.*");
        if (kind == "both") { c.ALoss = "23a family (siblings past the first base)"; return c.Valid(); }
        return c.Error(kind == "old" ? "s.a@follow = 9" : second);
    }

    // 23a's replacement on one branch, on both, conditionally inside a loop, or before and inside a loop (§6 row 6).
    static Case Conditional(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "one" => "`c = rr@follow@follow@uniq; if cond => rr = slot2@uniq`; `rr@follow@follow = 7; c@follow = 1`",
            "both" => "`c = rr@follow@follow@uniq; if cond => rr = slot2@uniq else => rr = slot3@uniq`; `rr@follow@follow = 7; c@follow = 1`",
            "loopOne" => "`c = …; while cond: c@follow = 1; if p => rr = slot2@uniq`; after the loop `rr@follow@follow = 7; c@follow = 2`",
            _ => "`c = …; rr = slot2@uniq; while cond: rr@follow@follow = 7; c@follow = 1; rr = slot3@uniq`; `c@follow = 2`",
        });
        c.N("var x = 1; var z = 3; var v = 4").Def("x", "z", "v");
        c.N("var slot = x@uniq; var slot2 = z@uniq; var slot3 = v@uniq").Def("slot", "slot2", "slot3")
            .Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2").Borrow("Lv", "v", Uniq, "slot3");
        c.N("var rr = slot@uniq").Def("rr").Borrow("Lo", "slot", Uniq, "rr");
        c.N("let c = rr@follow@follow@uniq").Def("c").Borrow("L2", "rr.*.*", Uniq, "c");
        const string w = "rr@follow@follow = 7";
        switch (kind)
        {
            case "one":
                c.N("if cond").Goto("  rr = slot2@uniq", w);
                c.N("  rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr");
                c.N(w).A(K.Write, "rr.*.*"); c.N("c@follow = 1").A(K.Write, "c.*");
                return c.Error(w);
            case "both":
                c.N("if cond").Goto("  rr = slot2@uniq", "  rr = slot3@uniq");
                c.N("  rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr").Goto(w);
                c.N("  rr = slot3@uniq").Assign("rr").Borrow("Lo3", "slot3", Uniq, "rr");
                c.N(w).A(K.Write, "rr.*.*"); c.N("c@follow = 1").A(K.Write, "c.*");
                c.ALoss = "23a family (replacement on both branches)"; return c.Valid();
            case "loopOne":
                c.N("while cond").Goto("  c@follow = 1", w);
                c.N("  c@follow = 1").A(K.Write, "c.*");
                c.N("  if p").Goto("    rr = slot2@uniq", "while cond");
                c.N("    rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr").Goto("while cond");
                c.N(w).A(K.Write, "rr.*.*"); c.N("c@follow = 2").A(K.Write, "c.*");
                return c.Error(w);
            default:
                c.N("rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr");
                c.N("while cond").Goto("  " + w, "c@follow = 2");
                c.N("  " + w).A(K.Write, "rr.*.*");
                c.N("  c@follow = 1").A(K.Write, "c.*");
                c.N("  rr = slot3@uniq").Assign("rr").Borrow("Lo3", "slot3", Uniq, "rr").Goto("while cond");
                c.N("c@follow = 2").A(K.Write, "c.*");
                c.ALoss = "23a family (replacement before and inside a loop)"; return c.Valid();
        }
    }

    // A value from an earlier iteration (keep) is live across the back edge; rr is re-pointed at slot2 every iteration,
    // so from the third iteration c and keep would both reach z (§6 row 6).
    static Case BackEdge(string id)
    {
        var c = new Case(id, "loop: `c = rr@follow@follow@uniq; keep@follow = 1; keep = c; rr = slot2@uniq`");
        c.N("var x = 1; var z = 3; var v = 0").Def("x", "z", "v");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var rr = slot@uniq; var keep = v@uniq").Def("rr", "keep").Borrow("Lo", "slot", Uniq, "rr").Borrow("Lv", "v", Uniq, "keep");
        c.N("while cond").Goto("  let c = rr@follow@follow@uniq", "exit");
        c.N("  let c = rr@follow@follow@uniq").Def("c").Borrow("L2", "rr.*.*", Uniq, "c");
        c.N("  keep@follow = 1").A(K.Write, "keep.*");
        c.N("  keep = c").Assign("keep").Flow("c", "keep");
        c.N("  rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr").Goto("while cond");
        c.N("exit").Goto();
        return c.Error("  rr = slot2@uniq");
    }

    // 23a's child, then the outer holder is Moved into a call, exchanged, swapped, replaced, or replaced through rr (§6 row 8).
    static Case Transfer2(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "move" => "`c = rr@follow@follow@uniq; stash(rr@move)` (keeps and uses it); `rr = slot2@uniq; c@follow = 1`",
            "exchange" => "`c = rr@follow@follow@uniq; old = exchange(rr@uniq, with: slot2@uniq); old@follow@follow = 7; c@follow = 1`",
            "swap" => "`c = rr@follow@follow@uniq; swap(rr@uniq, rr2@uniq); rr2@follow@follow = 7; c@follow = 1`",
            "replace" => "`c = rr@follow@follow@uniq; replace(rr@uniq, with: slot2@uniq); rr@follow@follow = 7; c@follow = 1`",
            _ => "`c = rr@follow@follow@uniq; replace(rr@follow@uniq, with: z@uniq)` (target behind a Deref); `rr@follow@follow = 7; c@follow = 1` (rustc accepts)",
        });
        c.N("var x = 1; var z = 3").Def("x", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var rr = slot@uniq").Def("rr").Borrow("Lo", "slot", Uniq, "rr");
        if (kind == "swap") c.N("var rr2 = slot2@uniq").Def("rr2").Borrow("Lo2", "slot2", Uniq, "rr2");
        c.N("let c = rr@follow@follow@uniq").Def("c").Borrow("L2", "rr.*.*", Uniq, "c");
        switch (kind)
        {
            case "move":
                c.N("stash(rr@move): Move rr").Def("$v").A(K.Move, "rr").Flow("rr", "$v");
                c.N("stash(rr@move): callee keeps v").A(K.UBorrow, "$v.*.*").Flow("$v", "^stash");
                c.N("rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr");
                c.N("c@follow = 1").A(K.Write, "c.*");
                return c.Error("stash(rr@move): Move rr");
            case "exchange":
                c.N("$t = rr@uniq (reserved)").Def("$t").Borrow("Lr", "rr", Uniq, "$t", reserve: true);
                c.N("$u = slot2@uniq (reserved)").Def("$u").Borrow("Lu", "slot2", Uniq, "$u", reserve: true);
                c.N("let old = exchange($t, with: $u)").Activate("Lr").Activate("Lu").Def("old").Flow("rr", "old").Exchange("rr").Flow("$u", "rr");
                c.N("old@follow@follow = 7").A(K.Write, "old.*.*");
                c.N("c@follow = 1").A(K.Write, "c.*");
                return c.Error("$t = rr@uniq (reserved)", "let old = exchange($t, with: $u)");
            case "swap":
                c.N("$a = rr@uniq (reserved)").Def("$a").Borrow("La", "rr", Uniq, "$a", reserve: true);
                c.N("$b = rr2@uniq (reserved)").Def("$b").Borrow("Lb", "rr2", Uniq, "$b", reserve: true);
                c.N("swap($a, $b)").Activate("La").Activate("Lb").Flow("rr", "rr2").Flow("rr2", "rr").Exchange("rr").Exchange("rr2");
                c.N("rr2@follow@follow = 7").A(K.Write, "rr2.*.*");
                c.N("c@follow = 1").A(K.Write, "c.*");
                return c.Error("$a = rr@uniq (reserved)", "swap($a, $b)");
            case "replace":
                c.N("$t = rr@uniq (reserved, replace target)").Def("$t").Borrow("Lr", "rr", Uniq, "$t", reserve: true, inPlace: true);
                c.N("$u = slot2@uniq (reserved)").Def("$u").Borrow("Lu", "slot2", Uniq, "$u", reserve: true);
                c.N("replace($t, with: $u)").Activate("Lr").Activate("Lu").Def("rr").Flow("$u", "rr");
                c.N("rr@follow@follow = 7").A(K.Write, "rr.*.*");
                c.N("c@follow = 1").A(K.Write, "c.*");
                c.ALoss = "23a family (`replace` of the outer holder)"; return c.Valid();
            default:
                c.N("$t = rr@follow@uniq (reserved, replace target)").Def("$t").Borrow("Lr", "rr.*", Uniq, "$t", reserve: true, inPlace: true);
                c.N("$u = z@uniq (reserved)").Def("$u").Borrow("Lu2", "z", Uniq, "$u", reserve: true);
                c.N("replace($t, with: $u)").Activate("Lr").Activate("Lu2").Through("rr.*", "$u");
                c.N("rr@follow@follow = 7").A(K.Write, "rr.*.*");
                c.N("c@follow = 1").A(K.Write, "c.*");
                return c.Error("rr@follow@follow = 7"); // both SPEC texts: a target behind a Deref is not a Move Path
        }
    }

    // [2 of uniq/uniq/i32] and [2 of [2 of uniq/uniq/i32]]: the matcher on constant and runtime indexes (§6 row 9).
    static Case IndexMatch(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "same" => "`c = arr[0]@follow@follow@uniq; arr[0] = s3@uniq; arr[0]@follow@follow = 7; c@follow = 1` (rustc rejects)",
            "other" => "`c = arr[0]@follow@follow@uniq; arr[1] = s3@uniq; arr[0]@follow@follow = 7; c@follow = 1`",
            "runtimeWrite" => "`c = arr[0]@follow@follow@uniq; arr[j] = s3@uniq; arr[0]@follow@follow = 7; c@follow = 1`",
            "runtimeBoth" => "`c = arr[i]@follow@follow@uniq; arr[j] = s3@uniq; arr[i]@follow@follow = 7; c@follow = 1`",
            "nestedPrefix" => "`c = m[0][i]@follow@follow@uniq; m[0] = [s5@uniq, s6@uniq]; m[0][i]@follow@follow = 7; c@follow = 1`",
            _ => "`c = m[i][0]@follow@follow@uniq; m[0] = [s5@uniq, s6@uniq]; m[i][0]@follow@follow = 7; c@follow = 1`",
        });
        c.N("var x = 1; var y = 2; var z = 3; let i: isize = pick(); let j: isize = pick()").Def("x", "y", "z", "i", "j");
        c.N("var s1 = x@uniq; var s2 = y@uniq; var s3 = z@uniq").Def("s1", "s2", "s3").Borrow("Lx", "x", Uniq, "s1").Borrow("Ly", "y", Uniq, "s2").Borrow("Lz", "z", Uniq, "s3");
        string place, src;
        if (kind.StartsWith("nested"))
        {
            c.N("var s4 = …; var s5 = …; var s6 = …").Def("s4", "s5", "s6");
            c.N("var m: [2 of [2 of uniq/uniq/i32]] = [[s1@uniq, s2@uniq], [s3@uniq, s4@uniq]]").Def("m")
                .Borrow("M00", "s1", Uniq, "m.0.0").Borrow("M01", "s2", Uniq, "m.0.1").Borrow("M10", "s3", Uniq, "m.1.0").Borrow("M11", "s4", Uniq, "m.1.1");
            (place, src) = kind == "nestedPrefix" ? ("m.0.[]", "m[0][i]") : ("m.[].0", "m[i][0]");
            c.N($"let c = {src}@follow@follow@uniq").Def("c").Borrow("L2", place + ".*.*", Uniq, "c");
            c.N("m[0] = [s5@uniq, s6@uniq]").Assign("m.0").Borrow("M5", "s5", Uniq, "m.0.0").Borrow("M6", "s6", Uniq, "m.0.1");
        }
        else
        {
            c.N("var arr: [2 of uniq/uniq/i32] = [s1@uniq, s2@uniq]").Def("arr").Borrow("A0", "s1", Uniq, "arr.0").Borrow("A1", "s2", Uniq, "arr.1");
            (place, src) = kind == "runtimeBoth" ? ("arr.[]", "arr[i]") : ("arr.0", "arr[0]");
            c.N($"let c = {src}@follow@follow@uniq").Def("c").Borrow("L2", place + ".*.*", Uniq, "c");
            if (kind == "same") c.N("arr[0] = s3@uniq").Assign("arr.0").Borrow("A3", "s3", Uniq, "arr.0");
            else if (kind == "other") c.N("arr[1] = s3@uniq").Assign("arr.1").Borrow("A3", "s3", Uniq, "arr.1");
            else { c.N("$t = s3@uniq").Def("$t").Borrow("A3", "s3", Uniq, "$t"); c.N("arr[j] = $t").Through("arr.[]", "$t"); }
        }
        var write = $"{src}@follow@follow = 7";
        c.N(write).A(K.Write, place + ".*.*");
        c.N("c@follow = 1").A(K.Write, "c.*");
        if (kind is "same" or "nestedPrefix") { c.ALoss = "23a family (an exact constant-index or Field prefix)"; return c.Valid(); }
        return c.Error(write);
    }

    // P {var a: uniq/uniq/i32, var b: uniq/uniq/i32}: the matcher on Fields.
    static Case FieldMatch(string id, bool same)
    {
        var w = same ? "p.a = sz@uniq" : "p.b = sz@uniq";
        var c = new Case(id, $"`c = p.a@follow@follow@uniq; {w}; p.a@follow@follow = 7; c@follow = 1`");
        c.N("var x = 1; var y = 2; var z = 3").Def("x", "y", "z");
        c.N("var sa = x@uniq; var sb = y@uniq; var sz = z@uniq").Def("sa", "sb", "sz").Borrow("Lx", "x", Uniq, "sa").Borrow("Ly", "y", Uniq, "sb").Borrow("Lz", "z", Uniq, "sz");
        c.N("var p = P(a: sa@uniq, b: sb@uniq)").Def("p").Borrow("La", "sa", Uniq, "p.a").Borrow("Lb", "sb", Uniq, "p.b");
        c.N("let c = p.a@follow@follow@uniq").Def("c").Borrow("L2", "p.a.*.*", Uniq, "c");
        c.N(w).Assign(same ? "p.a" : "p.b").Borrow("Lp", "sz", Uniq, same ? "p.a" : "p.b");
        c.N("p.a@follow@follow = 7").A(K.Write, "p.a.*.*");
        c.N("c@follow = 1").A(K.Write, "c.*");
        if (same) { c.ALoss = "23a family (an exact constant-index or Field prefix)"; return c.Valid(); }
        return c.Error("p.a@follow@follow = 7");
    }

    // H {var r: uniq/uniq/i32}: own steps (`payload` of obj and rc; A3 §3: handle storage) before the first borrow Deref.
    // rc payloads are shared-only, so the child is `ref`; 80's H has a drop that writes through self.r (§16.3.3).
    // 75 is an error by SPEC's letter (owner, 2026-10-18; PLAN G147 (6)): a Loan ends only where a Move Path is destroyed in place
    // (§15.6.3), and the payload is an own step that no Move Path takes (§15.1.3, §15.6.2), so replacing `h@follow.r` ends nothing.
    static Case OwnStep(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "objField" => "`h: obj/H`; `c = h@follow.r@follow@follow@uniq; h@follow.r = slot2@uniq; h@follow.r@follow@follow = 7; c@follow = 1`",
            "objWhole" => "`h: obj/H`; `c = h@follow.r@follow@follow@uniq; h = obj(H(r: slot2@uniq)); h@follow.r@follow@follow = 7; c@follow = 1`",
            "objOld" => "`h: obj/H`; `c = …@uniq; h = obj(H(r: slot2@uniq)); slot@follow = 5; c@follow = 1`",
            "rcClone" => "`h: rc/H; h2 = h` (clone); `c = h@follow.r@follow@follow@ref; h = rc(H(r: slot2@uniq)); inspect(h2@follow.r@follow@follow); inspect(c@follow)`",
            "rcOld" => "`h: rc/H`; `c = h@follow.r@follow@follow@ref; h = rc(H(r: slot2@uniq)); slot@follow = 5; inspect(c@follow)`",
            _ => "`h: rc/H`, H's drop writes through self.r; `h2 = h; c = …@ref; h = rc(H(r: slot2@uniq))` (the release may be final); `inspect(c@follow)`",
        });
        bool rc = kind.StartsWith("rc");
        c.N("var x = 1; var z = 3").Def("x", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N($"var h = {(rc ? "rc" : "obj")}(H(r: slot@uniq))").Def("h").Borrow("Lo", "slot", Uniq, "h.payload.r");
        if (kind is "rcClone" or "rcDrop") c.N("var h2 = h (clone)").Def("h2").A(K.Read, "h").Flow("h", "h2");
        c.N($"let c = h@follow.r@follow@follow@{(rc ? "ref" : "uniq")}").Def("c").Borrow("L2", "h.payload.r.*.*", !rc, "c");
        string err = null;
        switch (kind)
        {
            case "objField":
                c.N("h@follow.r = slot2@uniq").Assign("h.payload.r").Borrow("Lo2", "slot2", Uniq, "h.payload.r");
                err = "h@follow.r@follow@follow = 7"; c.N(err).A(K.Write, "h.payload.r.*.*"); break;
            case "objWhole":
                c.N("h = obj(H(r: slot2@uniq))").Assign("h").Borrow("Lo2", "slot2", Uniq, "h.payload.r");
                c.N("h@follow.r@follow@follow = 7").A(K.Write, "h.payload.r.*.*"); break;
            case "objOld": case "rcOld":
                c.N($"h = {(rc ? "rc" : "obj")}(H(r: slot2@uniq))").Assign("h").Borrow("Lo2", "slot2", Uniq, "h.payload.r");
                err = "slot@follow = 5"; c.N(err).A(K.Write, "slot.*"); break;
            case "rcClone":
                c.N("h = rc(H(r: slot2@uniq))").Assign("h").Borrow("Lo2", "slot2", Uniq, "h.payload.r");
                c.N("inspect(h2@follow.r@follow@follow)").A(K.Read, "h2.payload.r.*.*"); break;
            default:
                err = "h = rc(H(r: slot2@uniq)) (the old release may be final: drop writes self.r@follow@follow)";
                c.N(err).Assign("h").Drop("h").A(K.Write, "h.payload.r.*.*").Borrow("Lo2", "slot2", Uniq, "h.payload.r");
                c.N("inspect(h2)").Use("h2"); break;
        }
        c.N(rc ? "inspect(c@follow)" : "c@follow = 1").A(rc ? K.Read : K.Write, "c.*");
        if (err != null) return c.Error(err);
        if (kind != "rcClone") c.ALoss = "23a family (own steps before the first borrow Deref)";
        return c.Valid();
    }

    // 23b with the child stored into the caller's storage (E) before the outer holder is replaced.
    static Case EscapedChild()
    {
        var c = new Case("81", "`q: uniq/(uniq/i32), out: uniq/(uniq/i32)`; `q2 = q; c = q2@follow@follow@uniq; out@follow = c@move; q2 = s2@uniq; q@follow@follow = 5`");
        c.N("func f(q: uniq/(uniq/i32), out: uniq/(uniq/i32))").Param("Lq", "q").Param("Lout", "out");
        c.N("var o = 5; var s2 = o@uniq").Def("o", "s2").Borrow("Ls2", "o", Uniq, "s2");
        c.N("var q2 = q").Def("q2").Borrow("Lq2", "q.*", Uniq, "q2");
        c.N("let c = q2@follow@follow@uniq").Def("c").Borrow("L2", "q2.*.*", Uniq, "c");
        c.N("out@follow = c@move").A(K.Move, "c").Through("out.*", "c");
        c.N("q2 = s2@uniq").Assign("q2").Borrow("Lq3", "s2", Uniq, "q2");
        c.N("q@follow@follow = 5").A(K.Write, "q.*.*");
        return c.Error("q@follow@follow = 5");
    }

    // ll2 with a stride of two links (three borrow Derefs): 35a's shape.
    static Case Stride2()
    {
        var c = new Case("82", "ll2 stride 2: `node = node.next…next…; b = node.value@uniq; node = node.next…next…; b@follow = 1`");
        c.N("func f(head: uniq/Node)").Param("Lh", "head");
        c.N("var node = head").Def("node").Borrow("L0", "head.*", Uniq, "node");
        c.N("_t = node.next.some@follow.next.some@follow@uniq").Def("_t").Borrow("Ln1", "node.*.next.some.*.next.some.*", Uniq, "_t");
        c.N("node = move _t").Assign("node").A(K.Move, "_t").Flow("_t", "node");
        c.N("let b = node.value@uniq").Def("b").Borrow("Lb", "node.*.value", Uniq, "b");
        c.N("_t2 = node.next.some@follow.next.some@follow@uniq").Def("_t2").Borrow("Ln2", "node.*.next.some.*.next.some.*", Uniq, "_t2");
        c.N("node = move _t2").Assign("node").A(K.Move, "_t2").Flow("_t2", "node");
        c.N("b@follow = 1").A(K.Write, "b.*");
        c.ALoss = "23a family (stride-2 walk)"; return c.Valid();
    }

    // The direct form: the Loan is issued into its own first base, ended at once (19a), and only parents remain.
    static Case DirectStep()
    {
        var c = new Case("83", "`node = node.next…next…@uniq` (issued into its own first base); `head@follow…value = 9` while node is live");
        c.N("func f(head: uniq/Node)").Param("Lh", "head");
        c.N("var node = head").Def("node").Borrow("L0", "head.*", Uniq, "node");
        c.N("node = node.next.some@follow.next.some@follow@uniq").Assign("node").Borrow("Ln", "node.*.next.some.*.next.some.*", Uniq, "node");
        const string w = "head@follow.next.some@follow.next.some@follow.value = 9";
        c.N(w).A(K.Write, "head.*.next.some.*.next.some.*.value");
        c.N("node.value = 1").A(K.Write, "node.*.value");
        return c.Error(w);
    }

    // 36h through two borrow Derefs: SPEC 15.6.7 rejects the write in the argument (G119). With the receiver retained,
    // design A rejects later only because the reservation's Loan never ends; the first-dereference base ends it there.
    static Case ReservedRoute(string id, bool retained)
    {
        var c = new Case(id, "`res = rr@follow@follow.keepWith(do { rr = slot2@uniq; 0 })`" + (retained ? " (retains the receiver); `rr@follow@follow = 7; inspect(res@follow)`" : "; `rr@follow@follow = 7`"));
        c.N("var x = 1; var z = 3").Def("x", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var rr = slot@uniq").Def("rr").Borrow("Lo", "slot", Uniq, "rr");
        c.N("$r = rr@follow@follow (receiver, reserved)").Def("$r").Borrow("Lr", "rr.*.*", Uniq, "$r", reserve: true);
        const string arg = "do { rr = slot2@uniq; 0 } (argument)";
        c.N(arg).Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr");
        var call = c.N("let res = keepWith($r, …)").Activate("Lr").Def("res");
        if (retained) call.Flow("$r", "res");
        c.N("rr@follow@follow = 7").A(K.Write, "rr.*.*");
        if (retained) c.N("inspect(res@follow)").A(K.Read, "res.*");
        return c.Error(arg);
    }

    // A guard stores a candidate-derived reference and replaces the subject's outer holder (SPEC 14.8.3: a value that
    // depends on g cannot leave the guard; A3 encodes g's end as EndBorrow + err(ended)). 92: the candidate is reached
    // through the internal Subject Place $s (SPEC 15.1.6); 93: a lowering that names the candidate through rr.
    static Case Guard(string id, bool viaSubject, bool g110 = false)
    {
        var c = new Case(id, "`match rr@follow@follow .Data(let d) if (do { saved = d@follow@ref; rr = slot2@uniq; true }) => ()`; `inspect(saved)`"
            + (viaSubject ? " (candidate through the Subject Place $s)" : " (candidate Loan named through rr)")
            + (g110 ? "; round-10 step classes: $s and d are shared references, so the Refs through them are past a shared Deref (G110)" : ""));
        c.N("var x: E = .Data(n: 1); var z: E = .Empty; var other = 0").Def("x", "z", "other");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var rr = slot@uniq; var saved = other@ref").Def("rr", "saved").Borrow("Lo", "slot", Uniq, "rr").Borrow("Lsv", "other", Ref, "saved");
        if (viaSubject)
        {
            c.N("$s = rr@follow@follow (Subject Place, shared)").Def("$s").Borrow("Lsub", "rr.*.*", Ref, "$s");
            c.N("d = $s.data (candidate)").Def("d").Borrow("Ld", "$s.*.data", Ref, "d", sharedAt: g110 ? 1 : 0);
        }
        else c.N("d = rr@follow@follow.data (candidate)").Def("d").Borrow("Ld", "rr.*.*.data", Ref, "d");
        c.N("guard: saved = d@follow@ref").Assign("saved").Borrow("Ld2", "d.*", Ref, "saved", sharedAt: g110 ? 1 : 0);
        c.N("guard: rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr");
        const string end = "guard cleanup: EndBorrow d";
        c.N(end).EndBorrow("d");
        c.N("inspect(saved)").Use("saved");
        return c.Error(end);
    }

    // After 23a's outer end: a child of c, a write through the old slot, or a reborrow of it (§6 row 4, §3.3).
    static Case AfterOuterEnd(string id, string kind)
    {
        var c = new Case(id, "23a, then " + (kind == "grandchild" ? "`d = c@follow@uniq; c@follow = 1; d@follow = 2`"
            : kind == "oldSlot" ? "`slot@follow = 5; c@follow = 1` (rustc E0506)" : "`t = slot@follow@uniq; t@follow = 5; c@follow = 1`"));
        c.N("var x = 1; var z = 3").Def("x", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var rr = slot@uniq").Def("rr").Borrow("Lo", "slot", Uniq, "rr");
        c.N("let c = rr@follow@follow@uniq").Def("c").Borrow("L2", "rr.*.*", Uniq, "c");
        c.N("rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr");
        if (kind == "grandchild")
        {
            c.N("let d = c@follow@uniq").Def("d").Borrow("Ld", "c.*", Uniq, "d");
            c.N("c@follow = 1").A(K.Write, "c.*");
            c.N("d@follow = 2").A(K.Write, "d.*");
            return c.Error("c@follow = 1");
        }
        if (kind == "reborrowSlot")
        {
            c.N("let t = slot@follow@uniq").Def("t").Borrow("Lt", "slot.*", Uniq, "t");
            c.N("t@follow = 5").A(K.Write, "t.*");
            c.N("c@follow = 1").A(K.Write, "c.*");
            return c.Error("let t = slot@follow@uniq");
        }
        c.N("slot@follow = 5").A(K.Write, "slot.*");
        c.N("c@follow = 1").A(K.Write, "c.*");
        return c.Error("slot@follow = 5");
    }

    // Array<uniq/i32>: the element reference lives in a temporary first base that dies; c keeps the receiver Loan.
    static Case TempBase()
    {
        var c = new Case("88", "`Array<uniq/i32>`: `$e = indexUniq(arr, i); c = $e@follow@follow@uniq; StorageDead $e; $e2 = indexUniq(arr, j); $e2@follow@follow = 7; c@follow = 1`");
        c.N("var x = 1; var y = 2; let i: isize = pick(); let j: isize = pick()").Def("x", "y", "i", "j");
        c.N("var arr: Array<uniq/i32> = [x@uniq, y@uniq]").Def("arr").Borrow("Lx", "x", Uniq, "arr").Borrow("Ly", "y", Uniq, "arr");
        c.N("$r = arr (indexUniq receiver, reserved)").Def("$r").Borrow("Lr", "arr", Uniq, "$r", reserve: true);
        c.N("$e = indexUniq($r, i) (retains the receiver)").Activate("Lr").Def("$e").Flow("$r", "$e");
        c.N("let c = $e@follow@follow@uniq").Def("c").Borrow("Lc", "$e.*.*", Uniq, "c");
        c.N("StorageDead $e").Def("$e");
        const string second = "$r2 = arr (indexUniq receiver, reserved)";
        c.N(second).Def("$r2").Borrow("Lr2", "arr", Uniq, "$r2", reserve: true);
        c.N("$e2 = indexUniq($r2, j)").Activate("Lr2").Def("$e2").Flow("$r2", "$e2");
        c.N("$e2@follow@follow = 7").A(K.Write, "$e2.*.*");
        c.N("c@follow = 1").A(K.Write, "c.*");
        return c.Error(second, "$e2 = indexUniq($r2, j)");
    }

    // 24c through two borrow Derefs: T's drop writes through self.r@follow@follow; the old value's drop and the store of
    // a value carrying the child happen at one point, so the check (transit) precedes the first-dereference ending.
    // Round 13: as MIR builds it, the Drop terminator (MirTerminatorKind.Drop), then the store at the next location (the second
    // design review's dump fD2: `drop _2 -> bb3; _2 = move _7`); rounds 5-12's one-point encoding is Case.OnePoint (OnePointDrop).
    static Case DropMerged2() { var c = DropMerged2Case(false); c.OnePoint = DropMerged2Case(true); return c; }

    static Case DropMerged2Case(bool onePoint)
    {
        var c = new Case("91", "`t = T(r: slot2@uniq, c: t.r@follow@follow@uniq)`, T's drop writes through self.r@follow@follow (" + (onePoint ? "drop and store at one point)" : "MIR: `drop t`, then `t = move $v`; the case 23a review: one point)"));
        c.N("var x = 1; var y = 2; var z = 3").Def("x", "y", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var t = T(r: slot@uniq, c: y@uniq)").Def("t").Borrow("Lo", "slot", Uniq, "t.r").Borrow("Ly", "y", Uniq, "t.c");
        c.N("$c = t.r@follow@follow@uniq").Def("$c").Borrow("L2", "t.r.*.*", Uniq, "$c");
        c.N("$v = T(r: slot2@uniq, c: $c)").Def("$v").Borrow("Lo2", "slot2", Uniq, "$v.r").Flow("$c", "$v.c");
        string st;
        if (onePoint) c.N(st = "t = move $v (drops old t)").Assign("t").Drop("t").A(K.Write, "t.r.*.*").A(K.Move, "$v").Flow("$v", "t");
        else { c.N(st = "drop t (old value)").Drop("t").A(K.Write, "t.r.*.*"); c.N("t = move $v").Assign("t").A(K.Move, "$v").Flow("$v", "t"); }
        c.N("inspect(t)").Use("t");
        return c.Error(st);
    }

    // T {var r: uniq/uniq/i32, var c: uniq/i32}: the child flows into the holder whose destruction ends it, at that point.
    static Case SamePoint(string id, bool oldRoute)
    {
        var c = new Case(id, "`t = T(r: slot2@uniq, c: t.r@follow@follow@uniq)` (one store); " + (oldRoute ? "`slot@follow = 5; t.c@follow = 1`" : "`t.r@follow@follow = 7; t.c@follow = 1`"));
        c.N("var x = 1; var y = 2; var z = 3").Def("x", "y", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var t = T(r: slot@uniq, c: y@uniq)").Def("t").Borrow("Lo", "slot", Uniq, "t.r").Borrow("Ly", "y", Uniq, "t.c");
        c.N("$c = t.r@follow@follow@uniq").Def("$c").Borrow("L2", "t.r.*.*", Uniq, "$c");
        c.N("$v = T(r: slot2@uniq, c: $c)").Def("$v").Borrow("Lo2", "slot2", Uniq, "$v.r").Flow("$c", "$v.c");
        c.N("t = move $v").Assign("t").A(K.Move, "$v").Flow("$v", "t");
        if (oldRoute) { c.N("slot@follow = 5").A(K.Write, "slot.*"); c.N("t.c@follow = 1").A(K.Write, "t.c.*"); return c.Error("slot@follow = 5"); }
        c.N("t.r@follow@follow = 7").A(K.Write, "t.r.*.*");
        c.N("t.c@follow = 1").A(K.Write, "t.c.*");
        c.ALoss = "23a family (the child stored into its own outer holder)"; return c.Valid();
    }

    static Case Replacement()
    {
        var c = new Case("1", "§15.6 example: `r = b@ref` ends the Loan on a");
        c.N("var a = 1; var b = 2").Def("a", "b");
        c.N("var r = a@ref").Def("r").Borrow("L1", "a", Ref, "r");
        c.N("r = b@ref").Assign("r").Borrow("L2", "b", Ref, "r");
        c.N("a = 3").Assign("a");
        c.N("inspect(r)").Use("r");
        return c.Valid();
    }

    static Case Join()
    {
        var c = new Case("2", "§14.9.2 example: a joined result carries both Loans");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("if ready").Goto("  => x@ref", "  => y@ref");
        c.N("  => x@ref").Def("r").Borrow("Lx", "x", Ref, "r").Goto("x = 3");
        c.N("  => y@ref").Def("r").Borrow("Ly", "y", Ref, "r");
        c.N("x = 3").Assign("x");
        c.N("use(r)").Use("r");
        return c.Error("x = 3");
    }

    // 3: copy of the referent's reference; 4: read through two layers; 4b: the Move precedes the borrow of the slot.
    static Case Slot(string id, bool copy, bool early)
    {
        var c = new Case(id, copy ? "`let copy = r@follow` takes slot's Loan, not r's" : early ? "4 with `consume` before `let r = slot@ref`" : "`inspect(r@follow@follow)`: liveness through references");
        c.N("var data = makeData()").Def("data");
        c.N("var slot: ref/Data = data@ref").Def("slot").Borrow("Ld", "data", Ref, "slot");
        if (early) c.N("consume(data@move)").A(K.Move, "data");
        c.N("let r = slot@ref").Def("r").Borrow("Ls", "slot", Ref, "r");
        if (copy) c.N("let copy = r@follow").Def("copy").A(K.Read, "r.*").Flow("r.*", "copy");
        if (!early) c.N("consume(data@move)").A(K.Move, "data");
        if (copy) c.N("inspect(copy)").Use("copy"); else c.N("inspect(r@follow@follow)").A(K.Read, "r.*.*");
        return c.Error("consume(data@move)");
    }

    // §15.7.3: "a Copy of a stored ref/Data keeps only its external Loan", so replacing slot is valid.
    static Case SlotReplaced()
    {
        var c = new Case("3b", "`let copy = r@follow; slot = other@ref; inspect(copy)`");
        c.N("var data = makeData(); var other = makeData()").Def("data", "other");
        c.N("var slot: ref/Data = data@ref").Def("slot").Borrow("Ld", "data", Ref, "slot");
        c.N("let r = slot@ref").Def("r").Borrow("Ls", "slot", Ref, "r");
        c.N("let copy = r@follow").Def("copy").A(K.Read, "r.*").Flow("r.*", "copy");
        c.N("slot = other@ref").Assign("slot").Borrow("Lo", "other", Ref, "slot");
        c.N("inspect(copy)").Use("copy");
        return c.Valid();
    }

    static Case Index(string id, bool nonPath, bool rewrite)
    {
        var target = nonPath ? "refs.[]" : "refs.0"; var spelled = nonPath ? "refs[n]" : "refs[0]";
        var c = new Case(id, $"`{spelled} = a@ref`" + (rewrite ? $"; `{spelled} = z@ref`" : "") + "; `a = 3`; `inspect(refs)`");
        c.N("var z = 0; var a = 1; let n: isize = pick()").Def("z", "a", "n");
        c.N("var refs: [2 of ref/i32] = [z@ref, z@ref]").Def("refs").Borrow("Lz0", "z", Ref, "refs.0").Borrow("Lz1", "z", Ref, "refs.1");
        foreach (var (src, loan) in rewrite ? new[] { ("a", "La"), ("z", "Lz2") } : [("a", "La")])
            if (nonPath) { c.N($"$t = {src}@ref ({loan})").Def("$t").Borrow(loan, src, Ref, "$t"); c.N($"{spelled} = $t ({loan})").Through(target, "$t"); }
            else c.N($"{spelled} = {src}@ref").Assign(target).Borrow(loan, src, Ref, target);
        c.N("a = 3").Assign("a");
        c.N("inspect(refs)").Use("refs");
        c.ALoss = "P1 (listed in A §2: prototype 5b)"; return nonPath ? c.Error("a = 3") : c.Valid();
    }

    static Case Reborrow(string id, bool write)
    {
        var c = new Case(id, "reborrow `c = r@follow@uniq`; `r = y@uniq`" + (write ? "; `x = 3`" : "") + "; `inspect(c)`");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r");
        if (write) c.N("x = 3").Assign("x");
        c.N("inspect(c)").Use("c");
        return write ? c.Error("x = 3") : c.Valid();
    }

    // struct Pair {left: ref/i32, right: ref/i32} with `Self is Copy` (§3.5.2).
    static Case Partial(string id, bool inspectP)
    {
        var c = new Case(id, $"`var q = p; q.left = c@ref; a = 3; inspect({(inspectP ? "p" : "q")})`");
        c.N("var a = 1; var b = 2; var c = 3").Def("a", "b", "c");
        c.N("var p = Pair{left: a@ref, right: b@ref}").Def("p").Borrow("La", "a", Ref, "p.left").Borrow("Lb", "b", Ref, "p.right");
        c.N("var q = p").Def("q").Flow("p", "q");
        c.N("q.left = c@ref").Assign("q.left").Borrow("Lc", "c", Ref, "q.left");
        c.N("a = 3").Assign("a");
        c.N(inspectP ? "inspect(p)" : "inspect(q)").Use(inspectP ? "p" : "q");
        c.ALoss = "P1 (listed in A §2: prototype 7)"; return inspectP ? c.Error("a = 3") : c.Valid();
    }

    // Plan §5.2. `cursor = next` is a bare acquisition of a uniq Place, so it Reborrows (§3.5).
    static Case Deepest(string id, bool clear, bool check)
    {
        var c = new Case(id, check ? "8b: the returned cursor still carries root's Loan L0" : clear ? "deepest with `cursor.children.clear()`" : "deepest as written");
        c.N("func deepest(root: uniq/Tree)").Param("Lroot", "root");
        c.N("var cursor = root").Def("cursor").Borrow("L0", "root.*", Uniq, "cursor");
        c.N("while cursor.children.length > 0").A(K.Read, "cursor.*.children").Goto("  let next = cursor.children[0]@uniq", "return cursor");
        c.N("  let next = cursor.children[0]@uniq").Def("next").Borrow("Ln", "cursor.*.children.[]", Uniq, "next");
        if (clear) c.N("  cursor.children.clear()").A(K.UBorrow, "cursor.*.children");
        c.N("  cursor = next").Assign("cursor").Borrow("L5", "next.*", Uniq, "cursor");
        c.N("  (end of body: StorageDead next)").Def("next").Goto("while cursor.children.length > 0");
        c.N("return cursor").Flow("cursor", "$ret");
        c.N("exit").Goto();
        if (check) c.Extra = a => a.CarriesAt("exit", "$ret", "L0") ? null : "$ret lacks L0";
        return clear ? c.Error("  cursor.children.clear()") : c.Valid();
    }

    // ---- 9: adversarial cases for S1's soundness hypothesis ----
    static Case CopyChild()
    {
        var c = new Case("9a", "copy a shared child, then replace the base");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@ref").Def("c").Borrow("L2", "r.*", Ref, "c");
        c.N("let d = c").Def("d").Flow("c", "d");
        c.N("r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r");
        c.N("x = 3").Assign("x");
        c.N("inspect(d)").Use("d");
        return c.Error("x = 3");
    }

    static Case StoreChild()
    {
        var c = new Case("9b", "store the child in an aggregate, then replace the base");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("var box = Box{item: c@move}").Def("box").A(K.Move, "c").Flow("c", "box.item");
        c.N("r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r");
        c.N("x = 3").Assign("x");
        c.N("inspect(box)").Use("box");
        return c.Error("x = 3");
    }

    static Case TwoBases()
    {
        var c = new Case("9c", "reborrow through r into s, through s into d; replace s, then r");
        c.N("var x = 1; var y = 2; var z = 4").Def("x", "y", "z");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("var s = r@follow@uniq").Def("s").Borrow("L2", "r.*", Uniq, "s");
        c.N("let d = s@follow@uniq").Def("d").Borrow("L3", "s.*", Uniq, "d");
        c.N("s = z@uniq").Assign("s").Borrow("Lz", "z", Uniq, "s");
        c.N("r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r");
        c.N("x = 3").Assign("x");
        c.N("inspect(d)").Use("d");
        return c.Error("x = 3");
    }

    static Case Loop(string id, string probe)
    {
        var c = new Case(id, $"replace the base inside a loop; `{probe}` after it");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("var c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("while cond").Goto("  inspect(c)", probe);
        c.N("  inspect(c)").Use("c");
        c.N("  r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r").Goto("while cond");
        c.N(probe).Assign(probe[..1]);
        c.N("inspect(c)").Use("c");
        return probe == "x = 3" ? c.Error(probe) : c.Valid();
    }

    // struct H {link: uniq/i32, n: i32}
    static Case FieldBase(string id, bool partial)
    {
        var c = new Case(id, partial ? "reborrow through `h.link`; `h.n = 5`; write through `h.link`" : "reborrow through `h.link`, then replace the ancestor `h`");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var h = H{link: x@uniq, n: 0}").Def("h").Borrow("L1", "x", Uniq, "h.link");
        c.N("let c = h.link@follow@uniq").Def("c").Borrow("L2", "h.link.*", Uniq, "c");
        if (partial) { c.N("h.n = 5").Assign("h.n"); c.N("h.link@follow = 7").A(K.Write, "h.link.*"); }
        else { c.N("h = H{link: y@uniq, n: 1}").Assign("h").Borrow("Ly", "y", Uniq, "h.link"); c.N("x = 3").Assign("x"); }
        c.N("inspect(c)").Use("c");
        return c.Error(partial ? "h.link@follow = 7" : "x = 3");
    }

    // func pass(v: uniq/i32) -> uniq/i32 during v: the result retains v (a call flow).
    static Case ThroughCall()
    {
        var c = new Case("9f", "pass the child through a retaining call, then replace the base");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("$a = c (Reborrow at a uniq parameter, reserved)").Def("$a").Borrow("L3", "c.*", Uniq, "$a", reserve: true);
        c.N("let d = pass($a)").Activate("L3").Def("d").Flow("$a", "d");
        c.N("r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r");
        c.N("x = 3").Assign("x");
        c.N("inspect(d)").Use("d");
        return c.Error("x = 3");
    }

    // exchange<T>(target: uniq/T ! with: T) -> T: the result is the old contents, the target receives `with`.
    static Case Exchange()
    {
        var c = new Case("9g", "`exchange(r@uniq, with: y@uniq)` while the child is live; write through the old value");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("$t = r@uniq (reserved)").Def("$t").Borrow("Lr", "r", Uniq, "$t", reserve: true);
        c.N("$u = y@uniq (reserved)").Def("$u").Borrow("Ly", "y", Uniq, "$u", reserve: true);
        c.N("let old = exchange($t, with: $u)").Activate("Lr").Activate("Ly").Def("old").Flow("$t.*", "old").Through("$t.*", "$u");
        c.N("old@follow = 7").A(K.Write, "old.*");
        c.N("inspect(c)").Use("c");
        return c.Error("old@follow = 7").S1(true, "let old = exchange($t, with: $u)");
    }

    // Coordinator's case: exchange lowered as a complete replacement of the move path r whose old value moves to `old`.
    static Case ExchangeReplaces()
    {
        var c = new Case("9n", "`old = exchange(r@uniq, with: y@uniq)` as a complete replacement of r; `old@follow = 3; c@follow = 4`");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("$t = r@uniq (reserved)").Def("$t").Borrow("Lr", "r", Uniq, "$t", reserve: true);
        c.N("$u = y@uniq (reserved)").Def("$u").Borrow("Ly", "y", Uniq, "$u", reserve: true);
        c.N("let old = exchange($t, with: $u)").Activate("Lr").Activate("Ly").Def("old").Flow("r", "old").Exchange("r").Flow("$u", "r");
        c.N("old@follow = 3").A(K.Write, "old.*");
        c.N("c@follow = 4").A(K.Write, "c.*");
        return c.Error("old@follow = 3").S1(true, "let old = exchange($t, with: $u)");
    }

    // swap(first: uniq/T, second: uniq/T): both targets wholly replaced, old values exchanged.
    static Case SwapBase()
    {
        var c = new Case("9o", "`swap(r@uniq, s@uniq)` while c is live; `s@follow = 3; c@follow = 4`");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("var s = y@uniq").Def("s").Borrow("Ls", "y", Uniq, "s");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("$a = r@uniq (reserved)").Def("$a").Borrow("La", "r", Uniq, "$a", reserve: true);
        c.N("$b = s@uniq (reserved)").Def("$b").Borrow("Lb", "s", Uniq, "$b", reserve: true);
        c.N("swap($a, $b)").Activate("La").Activate("Lb").Flow("r", "s").Flow("s", "r").Exchange("r").Exchange("s");
        c.N("s@follow = 3").A(K.Write, "s.*");
        c.N("c@follow = 4").A(K.Write, "c.*");
        return c.Error("s@follow = 3").S1(true, "swap($a, $b)");
    }

    // 9g, then a real replacement of the base kills L2 before the old value is used.
    static Case ExchangeThenReplace()
    {
        var c = new Case("9m", "9g, then `r = z@uniq` before writing through the old value");
        c.N("var x = 1; var y = 2; var z = 3").Def("x", "y", "z");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("$t = r@uniq (reserved)").Def("$t").Borrow("Lr", "r", Uniq, "$t", reserve: true);
        c.N("$u = y@uniq (reserved)").Def("$u").Borrow("Ly", "y", Uniq, "$u", reserve: true);
        c.N("let old = exchange($t, with: $u)").Activate("Lr").Activate("Ly").Def("old").Flow("$t.*", "old").Through("$t.*", "$u");
        c.N("r = z@uniq").Assign("r").Borrow("Lz", "z", Uniq, "r");
        c.N("old@follow = 7").A(K.Write, "old.*");
        c.N("inspect(c)").Use("c");
        return c.Error("old@follow = 7").S1(true, "let old = exchange($t, with: $u)");
    }

    // func steal(slot: uniq/(uniq/i32 during a), with: uniq/i32 during a) -> uniq/i32 during a => exchange(slot, with: with)
    static Case StealThroughW()
    {
        var c = new Case("9h", "child through `w = r@uniq`; a user call exchanges `w@follow` and returns the old value");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let w = r@uniq").Def("w").Borrow("Lw", "r", Uniq, "w");
        c.N("let c = w@follow@follow@uniq").Def("c").Borrow("L2", "w.*.*", Uniq, "c");
        c.N("$t = w (Reborrow at a uniq parameter, reserved)").Def("$t").Borrow("Lt", "w.*", Uniq, "$t", reserve: true);
        c.N("$u = y@uniq (reserved)").Def("$u").Borrow("Ly", "y", Uniq, "$u", reserve: true);
        c.N("let old = steal($t, with: $u)").Activate("Lt").Activate("Ly").Def("old").Flow("$t.*", "old").Flow("$u", "old").Through("$t.*", "$u");
        c.N("old@follow = 7").A(K.Write, "old.*");
        c.N("inspect(c)").Use("c");
        return c.Error("old@follow = 7").S1(true, "let old = steal($t, with: $u)");
    }

    static Case SharedBase()
    {
        var c = new Case("9i", "shared borrow of the base; read through it while the child is live");
        c.N("var x = 1").Def("x");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("let z = r@ref").Def("z").Borrow("Lz", "r", Ref, "z");
        c.N("inspect(z@follow@follow)").A(K.Read, "z.*.*");
        c.N("inspect(c)").Use("c");
        return c.Error("inspect(z@follow@follow)");
    }

    static Case Nested()
    {
        var c = new Case("9j", "reborrow through two references `r@follow@follow@uniq`; replace the outer base");
        c.N("var x = 1; var o = 5").Def("x", "o");
        c.N("var slot = x@uniq").Def("slot").Borrow("L1", "x", Uniq, "slot");
        c.N("var r = slot@uniq").Def("r").Borrow("Lr", "slot", Uniq, "r");
        c.N("let c = r@follow@follow@uniq").Def("c").Borrow("L2", "r.*.*", Uniq, "c");
        c.N("r = o@uniq").Assign("r").Borrow("Lo", "o", Uniq, "r");
        c.N("x = 3").Assign("x");
        c.N("inspect(c)").Use("c");
        return c.Error("x = 3");
    }

    static Case MoveBase()
    {
        var c = new Case("9k", "Move the base while its child is live (the loss S1 lists)");
        c.N("var x = 1").Def("x");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("let q = r@move").Def("q").A(K.Move, "r").Flow("r", "q");
        c.N("inspect(c)").Use("c");
        c.N("inspect(q)").Use("q");
        return c.Valid().S1(true, "let q = r@move");
    }

    // Valid by the current SPEC: the write replaces r's reference value, not x (§15.6.3, §15.7.3). S1′ rejects it.
    static Case UniqBase()
    {
        var c = new Case("9l", "`let w = r@uniq; w@follow = y@uniq` while the child is live (the loss the fix adds)");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("let w = r@uniq").Def("w").Borrow("Lw", "r", Uniq, "w");
        c.N("$t = y@uniq").Def("$t").Borrow("Ly", "y", Uniq, "$t");
        c.N("w@follow = $t").Through("w.*", "$t");
        c.N("inspect(c)").Use("c");
        var k = c.Valid().S1(true, "let w = r@uniq"); k.S1POnly = true; return k;
    }

    // ---- Round 2 ----
    // stash(v: uniq/i32) keeps v in storage the body does not own (^stash); a uniq parameter lets the callee use v
    // exclusively during the call (§15.6.4: a uniq requirement creates an exclusive Loan).
    static Case MoveBan(string id, bool rewrite)
    {
        var after = rewrite ? "r = y@uniq" : "c@follow = 1";
        var c = new Case(id, "`if cond => stash(r@move)`" + (rewrite ? "; `r = y@uniq`" : "") + "; `c@follow = 1`");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("if cond").Goto("  stash(r@move): Move r", after);
        c.N("  stash(r@move): Move r").Def("$v").A(K.Move, "r").Flow("r", "$v");
        c.N("  stash(r@move): callee uses v, keeps it").A(K.UBorrow, "$v.*").Flow("$v", "^stash").Goto(after);
        if (rewrite) c.N("r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r");
        c.N("c@follow = 1").A(K.Write, "c.*");
        return c.Error("  stash(r@move): callee uses v, keeps it").S1(true, "  stash(r@move): Move r");
    }

    // The parent is reached through another reference: c = o@follow@follow@m, base o (the first move path entered).
    static Case RefBase(string id, bool shared, string kind)
    {
        var o = shared ? "rs" : "rr"; var m = shared ? "ref" : "uniq";
        var write = kind == "outer" ? $"{o} = slot2@{m}" : kind == "inner" ? "slot = y@uniq" : $"{o}@follow = y@uniq";
        var c = new Case(id, $"`var {o} = slot@{m}; let c = {o}@follow@follow@{m}`; `{write}`; use c");
        c.N("var x = 1; var y = 2; var z = 3").Def("x", "y", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N($"var {o} = slot@{m}").Def(o).Borrow("Lo", "slot", !shared, o);
        c.N($"let c = {o}@follow@follow@{m}").Def("c").Borrow("L2", o + ".*.*", !shared, "c", sharedAt: shared ? 1 : 0);
        if (kind == "outer") c.N(write).Assign(o).Borrow("Lo2", "slot2", !shared, o);
        else if (kind == "inner") c.N(write).Assign("slot").Borrow("Ly", "y", Uniq, "slot");
        else { c.N("$t = y@uniq").Def("$t").Borrow("Ly", "y", Uniq, "$t"); c.N(write).Through(o + ".*", "$t"); }
        c.N(shared ? "inspect(c@follow)" : "c@follow = 1").A(shared ? K.Read : K.Write, "c.*");
        return kind == "inner" ? c.Error(write) : c.Valid();
    }

    static Case MoveAncestor()
    {
        var c = new Case("14", "`let g = h@move` (an ancestor of base `h.link`) while c is live");
        c.N("var x = 1").Def("x");
        c.N("var h = H{link: x@uniq, n: 0}").Def("h").Borrow("L1", "x", Uniq, "h.link");
        c.N("let c = h.link@follow@uniq").Def("c").Borrow("L2", "h.link.*", Uniq, "c");
        c.N("let g = h@move").Def("g").A(K.Move, "h").Flow("h", "g");
        c.N("c@follow = 1").A(K.Write, "c.*");
        c.N("inspect(g)").Use("g");
        return c.Valid().S1(true, "let g = h@move");
    }

    // replace<T>(target: uniq/T ! with: T): the old value is destroyed in place, so `ended` applies and no base_op.
    static Case ReplaceBase(string id, bool write)
    {
        var c = new Case(id, "`Kimi.Intrinsics.replace(r@uniq, with: y@uniq)` while c is live" + (write ? "; `x = 3`" : "") + "; `c@follow = 1`");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("$t = r@uniq (reserved, replace target)").Def("$t").Borrow("Lr", "r", Uniq, "$t", reserve: true, inPlace: true);
        c.N("$u = y@uniq (reserved)").Def("$u").Borrow("Ly", "y", Uniq, "$u", reserve: true);
        c.N("replace($t, with: $u)").Activate("Lr").Activate("Ly").Def("r").Flow("$u", "r");
        if (write) c.N("x = 3").Assign("x");
        c.N("c@follow = 1").A(K.Write, "c.*");
        c.ALoss = "A §2 counts a replace target as an exclusive borrow; plan e32dc0ef exempts it"; return write ? c.Error("x = 3") : c.Valid();
    }

    // ---- Round 3: adversarial cases for design A ----
    // Syntactic access without authority: aliases formed through a reborrow, a Copy, an aggregate, a write through a reference.
    static Case Alias(string id, string how)
    {
        var c = new Case(id, how switch
        {
            "reborrow" => "`let a = r` (Reborrow); `let c = a@follow@uniq`; `r@follow = 5`; `inspect(c)`",
            "copy" => "`let z = r@follow@ref; let z2 = z`; `let c = r@follow@uniq`; `inspect(z2)`",
            "aggregate" => "`let c = r@follow@uniq; var h = H{link: c}`; `r@follow = 5`; `inspect(h)`",
            "stored" => "`let w = s@uniq; w@follow = r` (stores a Reborrow of r in s); `r@follow = 5`; `inspect(s@follow)`",
            _ => "`var rr = slot@uniq; let c = rr@follow@follow@uniq`; `slot@follow = 5`; `inspect(c)`",
        });
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        var probe = "r@follow = 5";
        switch (how)
        {
            case "reborrow":
                c.N("let a = r").Def("a").Borrow("La", "r.*", Uniq, "a");
                c.N("let c = a@follow@uniq").Def("c").Borrow("L2", "a.*", Uniq, "c");
                c.N(probe).A(K.Write, "r.*"); c.N("inspect(c)").Use("c"); break;
            case "copy":
                c.N("let z = r@follow@ref").Def("z").Borrow("Lz", "r.*", Ref, "z");
                c.N("let z2 = z").Def("z2").Flow("z", "z2");
                probe = "let c = r@follow@uniq"; c.N(probe).Def("c").Borrow("L2", "r.*", Uniq, "c");
                c.N("inspect(z2)").Use("z2"); c.N("inspect(c)").Use("c"); break;
            case "aggregate":
                c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
                c.N("var h = H{link: c}").Def("h").Borrow("Lh", "c.*", Uniq, "h.link");
                c.N(probe).A(K.Write, "r.*"); c.N("inspect(h)").Use("h"); break;
            case "stored":
                c.N("var s = y@uniq").Def("s").Borrow("Ly", "y", Uniq, "s");
                c.N("let w = s@uniq").Def("w").Borrow("Lw", "s", Uniq, "w");
                c.N("$t = r (Reborrow)").Def("$t").Borrow("La", "r.*", Uniq, "$t");
                c.N("w@follow = $t").Through("w.*", "$t");
                c.N(probe).A(K.Write, "r.*"); c.N("inspect(s@follow)").A(K.Read, "s.*"); break;
            default:
                c.N("var slot = x@uniq").Def("slot").Borrow("Ls", "x", Uniq, "slot");
                c.N("var rr = slot@uniq").Def("rr").Borrow("Lo", "slot", Uniq, "rr");
                c.N("let c = rr@follow@follow@uniq").Def("c").Borrow("L2", "rr.*.*", Uniq, "c");
                probe = "slot@follow = 5"; c.N(probe).A(K.Write, "slot.*"); c.N("inspect(c)").Use("c"); break;
        }
        return c.Error(probe);
    }

    // The closure needs no feedback: references held by holders that are dead at the conflicting access.
    static Case DeadHolder(string id, bool chain)
    {
        var c = new Case(id, chain ? "`mid = slot@ref; top = mid@ref`; `consume(data@move)`; read through `top` (slot and mid otherwise dead)"
                                   : "`w = slot@uniq`; `consume(data@move)`; `t = w@follow@ref` formed after it; read through `t`");
        c.N("var data = makeData()").Def("data");
        c.N("var slot: ref/Data = data@ref").Def("slot").Borrow("Ld", "data", Ref, "slot");
        if (chain)
        {
            c.N("var mid = slot@ref").Def("mid").Borrow("Ls", "slot", Ref, "mid");
            c.N("var top = mid@ref").Def("top").Borrow("Lm", "mid", Ref, "top");
            c.N("consume(data@move)").A(K.Move, "data");
            c.N("inspect(top@follow@follow@follow)").A(K.Read, "top.*.*.*");
        }
        else
        {
            c.N("var w = slot@uniq").Def("w").Borrow("Lw", "slot", Uniq, "w");
            c.N("consume(data@move)").A(K.Move, "data");
            c.N("let t = w@follow@ref").Def("t").Borrow("Lt", "w.*", Ref, "t");
            c.N("inspect(t@follow@follow)").A(K.Read, "t.*.*");
        }
        return c.Error("consume(data@move)");
    }

    // P1 and a partial Move: struct P2 {left: ref/i32, right: ref/i32}, not Copy.
    static Case PartialMove(string id, bool restore)
    {
        var c = new Case(id, restore ? "`let l = p.left@move; p.left = c@ref; a = 3; inspect(p)`" : "`let l = p.left@move; a = 3; inspect(l)`");
        c.N("var a = 1; var b = 2; var c = 3").Def("a", "b", "c");
        c.N("var p = P2{left: a@ref, right: b@ref}").Def("p").Borrow("La", "a", Ref, "p.left").Borrow("Lb", "b", Ref, "p.right");
        c.N("let l = p.left@move").Def("l").A(K.Move, "p.left").Flow("p.left", "l");
        if (restore) c.N("p.left = c@ref").Assign("p.left").Borrow("Lc", "c", Ref, "p.left");
        c.N("a = 3").Assign("a");
        c.N(restore ? "inspect(p)" : "inspect(l)").Use(restore ? "p" : "l");
        if (restore) { c.ALoss = "P1 (a partial write ends no Loan)"; return c.Valid(); }
        return c.Error("a = 3");
    }

    // A Reborrow stored back into its own base (the in-place traversal); 19b also writes through the parent root.
    static Case InPlace(string id, bool parent)
    {
        var c = new Case(id, "`cursor = cursor.children[0]@uniq` in a loop" + (parent ? "; `root.value = 2`" : "") + "; `cursor.value = 1`");
        c.N("func descend(root: uniq/Tree)").Param("Lroot", "root");
        c.N("var cursor = root").Def("cursor").Borrow("L0", "root.*", Uniq, "cursor");
        c.N("while cursor.children.length > 0").A(K.Read, "cursor.*.children").Goto("  cursor = cursor.children[0]@uniq", parent ? "root.value = 2" : "cursor.value = 1");
        c.N("  cursor = cursor.children[0]@uniq").Assign("cursor").Borrow("Ln", "cursor.*.children.[]", Uniq, "cursor").Goto("while cursor.children.length > 0");
        if (parent) c.N("root.value = 2").A(K.Write, "root.*.value");
        c.N("cursor.value = 1").A(K.Write, "cursor.*.value");
        return parent ? c.Error("root.value = 2") : c.Valid();
    }

    // ---- Round 4: one Loan id covers every iteration of a borrow site ----
    static Case LoopSite(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "uniq" => "loop: `let cur = v@uniq; inspect(prev); prev = cur`",
            "child" => "loop: `let cur = r@follow@uniq; inspect(prev); prev = cur`",
            "reserved" => "loop: `let k2 = f(v@uniq)` (reserved; f retains); `inspect(keep); keep = k2`",
            "window" => "loop: reserved `$t = v@uniq`, then `$n = v + 0` in the window; `keep = f($t, $n)`",
            "refkept" => "loop: `let u = v@uniq; u@follow = 1; inspect(keep); let a = v@ref; keep = a`",
            _ => "loop: `let s = v@ref; inspect(s); inspect(keep); let a = v@uniq; keep = a`",
        });
        c.N("var v = 1; var w = 2; var z = 0").Def("v", "w", "z");
        var head = "loop";
        switch (kind)
        {
            case "uniq": case "child":
                if (kind == "child") c.N("var x = 1; var r = x@uniq").Def("x", "r").Borrow("L1", "x", Uniq, "r");
                c.N("var prev = w@uniq").Def("prev").Borrow("Lw", "w", Uniq, "prev");
                c.N(head);
                var site = kind == "uniq" ? "  let cur = v@uniq" : "  let cur = r@follow@uniq";
                c.N(site).Def("cur").Borrow("Lc", kind == "uniq" ? "v" : "r.*", Uniq, "cur");
                c.N("  inspect(prev)").Use("prev");
                c.N("  prev = cur").Assign("prev").Flow("cur", "prev").Goto(head);
                return c.Error(site);
            case "reserved": case "window":
                c.N("var keep = w@uniq").Def("keep").Borrow("Lw", "w", Uniq, "keep");
                c.N(head);
                c.N("  $t = v@uniq (reserved)").Def("$t").Borrow("Lt", "v", Uniq, "$t", reserve: true);
                if (kind == "window") c.N("  $n = v + 0").Def("$n").A(K.Read, "v");
                c.N("  let k2 = f($t)").Activate("Lt").Def("k2").Flow("$t", "k2");
                c.N("  inspect(keep)").Use("keep");
                c.N("  keep = k2").Assign("keep").Flow("k2", "keep").Goto(head);
                return c.Error();
            case "refkept":
                c.N("var keep: ref/i32 = z@ref").Def("keep").Borrow("Lz", "z", Ref, "keep");
                c.N(head);
                c.N("  let u = v@uniq").Def("u").Borrow("Lu", "v", Uniq, "u");
                c.N("  u@follow = 1").A(K.Write, "u.*");
                c.N("  inspect(keep)").Use("keep");
                c.N("  let a = v@ref").Def("a").Borrow("La", "v", Ref, "a");
                c.N("  keep = a").Assign("keep").Flow("a", "keep").Goto(head);
                return c.Error(); // `let u` and the write through u both conflict with the earlier shared Loan
            default:
                c.N("var keep = w@uniq").Def("keep").Borrow("Lw", "w", Uniq, "keep");
                c.N(head);
                c.N("  let s = v@ref").Def("s").Borrow("Ls", "v", Ref, "s");
                c.N("  inspect(s)").Use("s");
                c.N("  inspect(keep)").Use("keep");
                c.N("  let a = v@uniq").Def("a").Borrow("La", "v", Uniq, "a");
                c.N("  keep = a").Assign("keep").Flow("a", "keep").Goto(head);
                return c.Error("  let s = v@ref");
        }
    }

    // ---- Round 5 (plan fe1a5b34) ----
    // MIR's Temp shape of `cursor = cursor.children[0]@uniq`: the child is issued into _t, then moved into cursor.
    static Case TempStep(string id, bool clear)
    {
        var c = new Case(id, "Temp shape: `_t = Ref(cursor.children[0])`" + (clear ? "; `cursor.children.clear()`" : "") + "; `cursor = move _t`");
        c.N("func descend(root: uniq/Tree)").Param("Lroot", "root");
        c.N("var cursor = root").Def("cursor").Borrow("L0", "root.*", Uniq, "cursor");
        c.N("while cursor.children.length > 0").A(K.Read, "cursor.*.children").Goto("  _t = Ref(cursor.children[0])", "cursor.value = 1");
        c.N("  _t = Ref(cursor.children[0])").Def("_t").Borrow("Ln", "cursor.*.children.[]", Uniq, "_t");
        if (clear) c.N("  cursor.children.clear()").A(K.UBorrow, "cursor.*.children");
        c.N("  cursor = move _t").Assign("cursor").A(K.Move, "_t").Flow("_t", "cursor").Goto("while cursor.children.length > 0");
        c.N("cursor.value = 1").A(K.Write, "cursor.*.value");
        return clear ? c.Error("  cursor.children.clear()") : c.Valid();
    }

    // var arr: [2 of uniq/i32] = [x@uniq, y@uniq]; a child through arr[i], an exchange of arr[j] (§4.1, §15.1.3).
    static Case IndexBan(string id, bool constant)
    {
        var (ci, cj) = constant ? ("0", "1") : ("[]", "[]");
        var (si, sj) = constant ? ("0", "1") : ("i", "j");
        var c = new Case(id, $"`c = arr[{si}]@follow@uniq; old = exchange(arr[{sj}]@uniq, with: z@uniq); old@follow = 7; inspect(c)`");
        c.N("var x = 1; var y = 2; var z = 3; let i: isize = pick(); let j: isize = pick()").Def("x", "y", "z", "i", "j");
        c.N("var arr: [2 of uniq/i32] = [x@uniq, y@uniq]").Def("arr").Borrow("Lx", "x", Uniq, "arr.0").Borrow("Ly", "y", Uniq, "arr.1");
        c.N($"let c = arr[{si}]@follow@uniq").Def("c").Borrow("L2", $"arr.{ci}.*", Uniq, "c");
        c.N($"$t = arr[{sj}]@uniq (reserved)").Def("$t").Borrow("Lt", $"arr.{cj}", Uniq, "$t", reserve: true);
        c.N("$u = z@uniq (reserved)").Def("$u").Borrow("Lz", "z", Uniq, "$u", reserve: true);
        c.N("let old = exchange($t, with: $u)").Activate("Lt").Activate("Lz").Def("old").Flow("$t.*", "old").Through("$t.*", "$u");
        c.N("old@follow = 7").A(K.Write, "old.*");
        c.N("inspect(c)").Use("c");
        return constant ? c.Valid() : c.Error("old@follow = 7").S1(true, "let old = exchange($t, with: $u)");
    }

    // The outer holder is replaced; the child's base is rr.*, which `ended` (no Deref between B and K) never ends.
    static Case OuterThenWrite()
    {
        var c = new Case("23a", "`c = rr@follow@follow@uniq; rr = slot2@uniq; rr@follow@follow = 7; c@follow = 1`");
        c.N("var x = 1; var z = 3").Def("x", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var rr = slot@uniq").Def("rr").Borrow("Lo", "slot", Uniq, "rr");
        c.N("let c = rr@follow@follow@uniq").Def("c").Borrow("L2", "rr.*.*", Uniq, "c");
        c.N("rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr");
        c.N("rr@follow@follow = 7").A(K.Write, "rr.*.*");
        c.N("c@follow = 1").A(K.Write, "c.*");
        return c.Valid().S1(true, "rr@follow@follow = 7"); // listed loss of the plan's kill (round 6 correction)
    }

    // Round 6 correction (plan review): a kill across a Deref ends both children of q. S {var p: uniq/i32}.
    static Case CrossingKill()
    {
        var c = new Case("29a", "(coordinator's 25a) `q = s@uniq; c = q.p@follow@uniq; w = q@follow@ref; q = o@uniq; a = w.p@follow; c@follow = 5; inspect(w.p@follow)`");
        c.N("var t = 1; var u = 2").Def("t", "u");
        c.N("var s = S(p: t@uniq); var o = S(p: u@uniq)").Def("s", "o").Borrow("Lt", "t", Uniq, "s.p").Borrow("Lu", "u", Uniq, "o.p");
        c.N("var q = s@uniq").Def("q").Borrow("Lq", "s", Uniq, "q");
        c.N("let c = q.p@follow@uniq").Def("c").Borrow("Lc", "q.*.p.*", Uniq, "c");
        c.N("let w = q@follow@ref").Def("w").Borrow("Lw", "q.*", Ref, "w");
        c.N("q = o@uniq").Assign("q").Borrow("Lo", "o", Uniq, "q");
        c.N("let a = w.p@follow").Def("a").A(K.Read, "w.*.p.*");
        c.N("c@follow = 5").A(K.Write, "c.*");
        c.N("inspect(w.p@follow)").A(K.Read, "w.*.p.*");
        return c.Error();
    }

    // Transit into an escaped destination: p: uniq/S; S's drop writes through self.r; drop and store at one point.
    // Round 13: as MIR builds it, the Drop terminator (MirTerminatorKind.Drop), then the store at the next location (the second
    // design review's dump fD2: `drop _2 -> bb3; _2 = move _7`); rounds 5-12's one-point encoding is Case.OnePoint (OnePointDrop).
    static Case EscapedTransit() { var c = EscapedTransitCase(false); c.OnePoint = EscapedTransitCase(true); return c; }

    static Case EscapedTransitCase(bool onePoint)
    {
        var c = new Case("29b", "`p: uniq/S`; `p@follow = S(r: p.r@follow@uniq)`, S's drop writes through self.r (" + (onePoint ? "one point)" : "MIR: `drop (*p)`, then `(*p) = move $v`; rounds 6-12: one point)"));
        c.N("func f(p: uniq/S)").Param("Lp", "p");
        c.N("$t = p.r@follow@uniq").Def("$t").Borrow("L2", "p.*.r.*", Uniq, "$t");
        c.N("$v = S(r: $t)").Def("$v").Flow("$t", "$v.r");
        string st;
        if (onePoint) c.N(st = "p@follow = move $v (drops old *p)").Through("p.*", "$v").Drop("p.*").A(K.Write, "p.*.r.*").A(K.Move, "$v");
        else { c.N(st = "drop (*p) (old value)").Drop("p.*").A(K.Write, "p.*.r.*"); c.N("p@follow = move $v").Through("p.*", "$v").A(K.Move, "$v"); }
        return c.Error(st);
    }

    // Linked-list traversal (Temp shape). struct Node {var value: i32; var next: Option<L>}; `.some` is the payload
    // a match selects. ll1: L = obj/Node, whose payload is an owned step (part of the handle's storage, A2 §3), so the
    // place has one borrow Deref and B = node. ll2: L = uniq/Node, so B = node.*.next.some, and `node = …` ends nothing.
    static Case LinkedList(string id, string kind)
    {
        bool obj = kind == "obj"; bool conflict = kind == "conflict";
        var c = new Case(id, (obj ? "ll1 (obj links)" : conflict ? "ll3 (uniq links, write through the old link)" : kind == "peek" ? "ll2 + read through the new node's link" : "ll2 (uniq links)")
            + ": loop `_t = node.next.some@follow@uniq; node = move _t`" + (conflict ? " with `node.next@follow.value = 1` while _t is live" : ""));
        c.N("func last(head: uniq/Node)").Param("Lh", "head");
        c.N("var node = head").Def("node").Borrow("L0", "head.*", Uniq, "node");
        c.N("while node.next is .Some").A(K.Read, "node.*.next").Goto("  _t = node.next.some@follow@uniq", "node.value = 1");
        c.N("  _t = node.next.some@follow@uniq").Def("_t").Borrow("Ln", obj ? "node.*.next.some.payload" : "node.*.next.some.*", Uniq, "_t");
        if (conflict) c.N("  node.next@follow.value = 1").A(K.Write, "node.*.next.some.*.value");
        var step = c.N("  node = move _t").Assign("node").A(K.Move, "_t").Flow("_t", "node");
        if (kind == "peek") c.N("  inspect(node.next@follow.value)").A(K.Read, "node.*.next.some.*.value").Goto("while node.next is .Some");
        else step.Goto("while node.next is .Some");
        c.N("node.value = 1").A(K.Write, "node.*.value");
        if (kind == "peek") c.ALoss = "23a family: the plan's within kill (stale base name through a Deref)";
        return conflict ? c.Error("  node.next@follow.value = 1") : c.Valid();
    }

    // ---- Round 7 (design A3 unit 0b) ----
    // swap with two reference parameters: the caller's activation keeps them disjoint (S15:614). 31b: the caller.
    static Case SwapParams(string id, bool caller)
    {
        var c = new Case(id, caller ? "caller of `f(a, b) => swap(a, b)`: `f(x@uniq, x@uniq)`" : "`func f(a: uniq/T, b: uniq/T) => Kimi.Intrinsics.swap(a, b)`");
        if (caller)
        {
            c.N("var x = make()").Def("x");
            c.N("$1 = x@uniq (reserved)").Def("$1").Borrow("L1", "x", Uniq, "$1", reserve: true);
            c.N("$2 = x@uniq (reserved)").Def("$2").Borrow("L2", "x", Uniq, "$2", reserve: true);
            c.N("f($1, $2)").Activate("L1").Activate("L2").Use("$1", "$2");
            return c.Error("f($1, $2)");
        }
        c.N("func f(a: uniq/T, b: uniq/T)").Param("La", "a").Param("Lb", "b");
        c.N("$1 = a (Reborrow at a uniq parameter, reserved)").Def("$1").Borrow("L1", "a.*", Uniq, "$1", reserve: true);
        c.N("$2 = b (Reborrow at a uniq parameter, reserved)").Def("$2").Borrow("L2", "b.*", Uniq, "$2", reserve: true);
        c.N("swap($1, $2)").Activate("L1").Activate("L2").Use("$1", "$2");
        return c.Valid();
    }

    // A Copy read through a reference stored into a holder (BinaryOp: the value carries no Loan).
    static Case CopyThrough(string id, bool childLive)
    {
        var c = new Case(id, childLive ? "`var h = x@uniq; let c = h@follow@uniq; y = h@follow + 1; c@follow = 2`" : "`let h = x@ref; y = h@follow + 1; x = 3; inspect(y)`");
        c.N("var x = 1; var y = 0").Def("x", "y");
        if (childLive)
        {
            c.N("var h = x@uniq").Def("h").Borrow("Lh", "x", Uniq, "h");
            c.N("let c = h@follow@uniq").Def("c").Borrow("Lc", "h.*", Uniq, "c");
            c.N("$v = h@follow").Def("$v").A(K.Read, "h.*").Flow("h.*", "$v");
            c.N("y = $v + 1").Assign("y").Use("$v");
            c.N("c@follow = 2").A(K.Write, "c.*");
            return c.Error("$v = h@follow");
        }
        c.N("let h = x@ref").Def("h").Borrow("Lh", "x", Ref, "h");
        c.N("$v = h@follow").Def("$v").A(K.Read, "h.*").Flow("h.*", "$v");
        c.N("y = $v + 1").Assign("y").Use("$v");
        c.N("x = 3").Assign("x");
        c.N("inspect(y)").Use("y");
        return c.Valid();
    }

    // §8.11.3 takeTwo (valid) and the plan's takeAcross (error). `take` is abstract and its result descends from the
    // receiver, so it carries Φ_c on `source.*`. 33c: the excuse only at the Call, not at the receiver's reservation.
    static Case PreservesResults(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "across" => "`takeAcross`: `first = source.take(); source@follow = fresh@move; second = source.take()`",
            "callonly" => "`takeTwo`, the excuse stated at the Call only; round 10 derives it at the reservation (A3's erratum)",
            "other" => "`first = source.take(); let n = source.other(); return first` (`other` is another requirement)",
            "inwindow" => "`first = source.take(); second = source.takeWith(do { source@follow = fresh@move; 0 })`",
            _ => "`takeTwo<S: StableTake>`: `first = source.take(); second = source.take()` (§8.11.3)",
        });
        c.N("func f(source: uniq/S, fresh: S)").Param("Ls", "source").Def("fresh");
        c.N("$r1 = source (receiver, reserved)").Def("$r1").Borrow("Lr1", "source.*", Uniq, "$r1", reserve: true);
        c.N("let first = source.take()").Activate("Lr1").Def("first").Phantom("Phi1", "source.*", "first", "Lr1");
        if (kind == "other") // round 10: the derived excuse never covers a call that is not excused (33c's derivation)
        {
            c.N("$r2 = source (receiver of other, reserved)").Def("$r2").Borrow("Lr2", "source.*", Uniq, "$r2", reserve: true);
            c.N("let n = source.other()").Activate("Lr2").Def("n");
            c.N("return first").Flow("first", "$ret");
            return c.Error();
        }
        if (kind == "inwindow") // round 10: a disturbance inside c2's argument; the excuse holds at the formation only
        {
            c.N("$r2 = source (receiver, reserved)").Def("$r2").Borrow("Lr2", "source.*", Uniq, "$r2", reserve: true);
            c.N("do { source@follow = fresh@move; 0 } (argument)").A(K.Move, "fresh").Through("source.*", "fresh").Disturb("Phi1");
            c.N("let second = source.takeWith($r2, …)").Activate("Lr2").Def("second").Phantom("Phi2", "source.*", "second", "Lr2").Excuse("Phi1");
            c.N("return (first@move, second@move)").Flow("first", "$ret").Flow("second", "$ret");
            return c.Error("do { source@follow = fresh@move; 0 } (argument)", "let second = source.takeWith($r2, …)");
        }
        if (kind == "across") c.N("source@follow = fresh@move").A(K.Move, "fresh").Through("source.*", "fresh").Disturb("Phi1");
        var r2 = c.N("$r2 = source (receiver, reserved)").Def("$r2").Borrow("Lr2", "source.*", Uniq, "$r2", reserve: true);
        if (kind != "callonly") r2.Excuse("Phi1");
        c.N("let second = source.take()").Activate("Lr2").Def("second").Phantom("Phi2", "source.*", "second", "Lr2").Excuse("Phi1");
        c.N("return (first@move, second@move)").Flow("first", "$ret").Flow("second", "$ret");
        return kind == "across" ? c.Error() : c.Valid();
    }

    // ---- Round 8 (case 23a proposal review) ----
    // A child through arr[i] (one or two borrow Derefs); arr[0] (34a/34b) or the whole arr (34c) is replaced; a write
    // through arr[i], the old route unless the whole array was replaced.
    static Case IndexKill(string id, bool deref2, bool whole = false)
    {
        var f = deref2 ? "@follow@follow" : "@follow";
        var c = new Case(id, $"`c = arr[i]{f}@uniq; {(whole ? "arr = [z@uniq, w@uniq]" : "arr[0] = …")}; arr[i]{f} = 7; c@follow = 1`");
        c.N("var x = 1; var y = 2; var z = 3; var w = 4; let i: isize = pick()").Def("x", "y", "z", "w", "i");
        if (deref2)
        {
            c.N("var s1 = x@uniq; var s2 = y@uniq; var s3 = z@uniq").Def("s1", "s2", "s3").Borrow("Lx", "x", Uniq, "s1").Borrow("Ly", "y", Uniq, "s2").Borrow("Lz", "z", Uniq, "s3");
            c.N("var arr: [2 of uniq/uniq/i32] = [s1@uniq, s2@uniq]").Def("arr").Borrow("L0", "s1", Uniq, "arr.0").Borrow("L1", "s2", Uniq, "arr.1");
            c.N("let c = arr[i]@follow@follow@uniq").Def("c").Borrow("L2", "arr.[].*.*", Uniq, "c");
            c.N("arr[0] = s3@uniq").Assign("arr.0").Borrow("L3", "s3", Uniq, "arr.0");
        }
        else
        {
            c.N("var arr: [2 of uniq/i32] = [x@uniq, y@uniq]").Def("arr").Borrow("Lx", "x", Uniq, "arr.0").Borrow("Ly", "y", Uniq, "arr.1");
            c.N("let c = arr[i]@follow@uniq").Def("c").Borrow("L2", "arr.[].*", Uniq, "c");
            if (whole) c.N("arr = [z@uniq, w@uniq]").Assign("arr").Borrow("Lz", "z", Uniq, "arr.0").Borrow("Lw", "w", Uniq, "arr.1");
            else c.N("arr[0] = z@uniq").Assign("arr.0").Borrow("Lz", "z", Uniq, "arr.0");
        }
        c.N($"arr[i]{f} = 7").A(K.Write, deref2 ? "arr.[].*.*" : "arr.[].*");
        c.N("c@follow = 1").A(K.Write, "c.*");
        if (!whole) return c.Error($"arr[i]{f} = 7");
        c.ALoss = "the plan's within: no end across a non-path index after K (narrower than S1's text)";
        return c.Valid();
    }

    // 23a split through a named intermediate: t's Loan has base rr, so rr = slot2@uniq ends it under every base rule.
    static Case SplitOuter()
    {
        var c = new Case("37", "23a through a named intermediate: `t = rr@follow@uniq; c = t@follow@uniq; rr = slot2@uniq; rr@follow@follow = 7; c@follow = 1`");
        c.N("var x = 1; var z = 3").Def("x", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var rr = slot@uniq").Def("rr").Borrow("Lo", "slot", Uniq, "rr");
        c.N("let t = rr@follow@uniq").Def("t").Borrow("Lt", "rr.*", Uniq, "t");
        c.N("let c = t@follow@uniq").Def("c").Borrow("L2", "t.*", Uniq, "c");
        c.N("rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr");
        c.N("rr@follow@follow = 7").A(K.Write, "rr.*.*");
        c.N("c@follow = 1").A(K.Write, "c.*");
        return c.Valid();
    }

    // uniq-linked walk (ll2): a Borrow of the new node's Field outlives the next step. 35a is straight-line; 35b keeps
    // each iteration's Borrow in one holder used after the loop.
    static Case Walk(string id, bool loop)
    {
        var c = new Case(id, loop ? "ll2 loop: `keep = node.value@uniq; node = node.next…@uniq`, keep used after the loop"
            : "`node = node.next…@uniq; b = node.value@uniq; node = node.next…@uniq; b@follow = 1`");
        c.N("func f(head: uniq/Node)").Param("Lh", "head");
        c.N("var node = head").Def("node").Borrow("L0", "head.*", Uniq, "node");
        if (loop)
        {
            c.N("var keep = none").Def("keep");
            c.N("while node.next is .Some").A(K.Read, "node.*.next").Goto("  keep = node.value@uniq", "keep@follow = 1");
            c.N("  keep = node.value@uniq").Assign("keep").Borrow("Lb", "node.*.value", Uniq, "keep");
            c.N("  _t = node.next.some@follow@uniq").Def("_t").Borrow("Ln", "node.*.next.some.*", Uniq, "_t");
            c.N("  node = move _t").Assign("node").A(K.Move, "_t").Flow("_t", "node").Goto("while node.next is .Some");
            c.N("keep@follow = 1").A(K.Write, "keep.*");
        }
        else
        {
            c.N("_t = node.next.some@follow@uniq").Def("_t").Borrow("Ln1", "node.*.next.some.*", Uniq, "_t");
            c.N("node = move _t").Assign("node").A(K.Move, "_t").Flow("_t", "node");
            c.N("let b = node.value@uniq").Def("b").Borrow("Lb", "node.*.value", Uniq, "b");
            c.N("_t2 = node.next.some@follow@uniq").Def("_t2").Borrow("Ln2", "node.*.next.some.*", Uniq, "_t2");
            c.N("node = move _t2").Assign("node").A(K.Move, "_t2").Flow("_t2", "node");
            c.N("b@follow = 1").A(K.Write, "b.*");
        }
        c.ALoss = "23a family: the plan's within kill (a Borrow through the new node while a kept value carries the stale Loan)";
        return c.Valid();
    }

    // F6 phantom through a replaced holder. H {var inner: uniq/S}; Φ_c on cur.*.inner.* is carried by first. cur moves to
    // b; then 36a/36b clear a's iterator through cur = a again, 36c/36d through a itself. retain: first also keeps the
    // receiver Loan Lr1 (A3 `out` with Value retention); otherwise first keeps Φ_c and, from round 9, its parent Loans.
    static Case PhantomParents(string id, bool direct, bool retain)
    {
        var c = new Case(id, "F6: `first = cur.inner.take(); cur = b; " + (direct ? "a.inner.clear()" : "cur = a; cur.inner.clear()")
            + "; return first`" + (retain ? ", first retains the receiver" : ""));
        c.N("func f(a: uniq/H, b: uniq/H)").Param("La", "a").Param("Lb", "b");
        c.N("var cur = a").Def("cur").Borrow("L0", "a.*", Uniq, "cur");
        c.N("$r1 = cur.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "cur.*.inner.*", Uniq, "$r1", reserve: true);
        var call = c.N("let first = cur.inner.take()").Activate("Lr1").Def("first");
        if (retain) call.Flow("$r1", "first");
        call.Phantom("Phi1", "cur.*.inner.*", "first", "Lr1");
        c.N("cur = b").Assign("cur").Borrow("Lb2", "b.*", Uniq, "cur").Disturb("Phi1");
        if (!direct) c.N("cur = a").Assign("cur").Borrow("La2", "a.*", Uniq, "cur");
        var recv = direct ? "a.*.inner.*" : "cur.*.inner.*";
        c.N("$r2 = " + (direct ? "a" : "cur") + ".inner (receiver of clear)").Def("$r2").Borrow("Lr2", recv, Uniq, "$r2");
        c.N("clear($r2)").Use("$r2");
        c.N("return first").Flow("first", "$ret");
        return c.Error();
    }

    // Round 9 (G113), from the review. 36e/36h: a `do` argument assigns cur during the receiver's reservation, which
    // SPEC 15.6.7 rejects and A3's shallow write misses (36h, a gap outside G113); 36e then shows that Φ_c's parents
    // come from the receiver's value (a's), not from recv(c) at the call (b's). 36f: the cost P12, a write to another
    // part of the parent referent while the result is live (SPEC and rustc accept). 36g: plain Take; `it = a` ends Φ_c
    // under the plan base, so only the parent Loans reject the reborrow.
    static Case PhantomRoot(string id)
    {
        var c = new Case(id, "F6: " + id switch
        {
            "36e" => "`first = cur.inner.takeWith(do { cur = b; 0 }); a.inner.clear(); return first`",
            "36f" => "`var h = H(inner: src, other: 0); var r = h@uniq; first = r.inner.take(); h.other = 5; return first`",
            "36g" => "`var it = a; first = it.take(); it = a; second = it.take()` (Take only)",
            _ => "`first = cur.inner.takeWith(do { cur = b; 0 }); return first`",
        });
        if (id == "36f")
        {
            c.N("func f(src: uniq/S)").Param("Ls", "src");
            c.N("var h = H(inner: src, other: 0)").Def("h").Borrow("Lsrc", "src.*", Uniq, "h.inner");
            c.N("var r = h@uniq").Def("r").Borrow("Lh", "h", Uniq, "r");
            c.N("$r1 = r.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "r.*.inner.*", Uniq, "$r1", reserve: true);
            c.N("let first = r.inner.take()").Activate("Lr1").Def("first").Phantom("Phi1", "r.*.inner.*", "first", "Lr1");
            c.N("h.other = 5").Assign("h.other");
            c.N("return first").Flow("first", "$ret");
            c.ALoss = "P12 (G113): a descending abstract-call result keeps the receiver's parent Loans";
            return c.Valid();
        }
        if (id == "36g")
        {
            c.N("func f(a: uniq/S)").Param("La", "a");
            c.N("var it = a").Def("it").Borrow("L0", "a.*", Uniq, "it");
            c.N("$r1 = it (receiver, reserved)").Def("$r1").Borrow("Lr1", "it.*", Uniq, "$r1", reserve: true);
            c.N("let first = it.take()").Activate("Lr1").Def("first").Phantom("Phi1", "it.*", "first", "Lr1");
            c.N("it = a").Assign("it").Borrow("L1", "a.*", Uniq, "it").Disturb("Phi1");
            c.N("$r2 = it (receiver, reserved)").Def("$r2").Borrow("Lr2", "it.*", Uniq, "$r2", reserve: true);
            c.N("let second = it.take()").Activate("Lr2").Def("second").Phantom("Phi2", "it.*", "second", "Lr2");
            c.N("return (first, second)").Flow("first", "$ret").Flow("second", "$ret");
            return c.Error();
        }
        c.N("func f(a: uniq/H, b: uniq/H)").Param("La", "a").Param("Lb", "b");
        c.N("var cur = a").Def("cur").Borrow("L0", "a.*", Uniq, "cur");
        c.N("$r1 = cur.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "cur.*.inner.*", Uniq, "$r1", reserve: true);
        c.N("do { cur = b; 0 } (argument)").Assign("cur").Borrow("Lb2", "b.*", Uniq, "cur");
        c.N("let first = takeWith($r1, …)").Activate("Lr1").Def("first").Phantom("Phi1", "cur.*.inner.*", "first", "Lr1");
        if (id == "36e")
        {
            c.N("$r2 = a.inner (receiver of clear)").Def("$r2").Borrow("Lr2", "a.*.inner.*", Uniq, "$r2");
            c.N("clear($r2)").Use("$r2");
        }
        c.N("return first").Flow("first", "$ret");
        return id == "36h" ? c.Error("do { cur = b; 0 } (argument)") : c.Error(); // 36e: SPEC errs first at the write, as in 36h
    }

    // Path-exact transit: the store goes to s.inner, while only s is used later.
    static Case PartTransit()
    {
        var c = new Case("29c", "`s.inner = f(s.inner@ref)` (retains); only `s` used later");
        c.N("var s = S(inner: make(), other: 0)").Def("s");
        c.N("$a = s.inner@ref").Def("$a").Borrow("Li", "s.inner", Ref, "$a");
        c.N("$r = f($a)").Def("$r").Flow("$a", "$r");
        c.N("s.inner = move $r").Assign("s.inner").A(K.Move, "$r").Flow("$r", "s.inner");
        c.N("inspect(s)").Use("s");
        return c.Error("s.inner = move $r");
    }

    // Soundness probe for a kill across a Deref: the outer holder is a reborrow of a parameter's referent.
    static Case ParamOuter()
    {
        var c = new Case("23b", "`q: uniq/(uniq/i32)`; `var q2 = q; c = q2@follow@follow@uniq; q2 = s2@uniq; q@follow@follow = 5; inspect(c)`");
        c.N("func f(q: uniq/(uniq/i32))").Param("Lq", "q");
        c.N("var o = 5; var s2 = o@uniq").Def("o", "s2").Borrow("Ls2", "o", Uniq, "s2");
        c.N("var q2 = q").Def("q2").Borrow("Lq2", "q.*", Uniq, "q2");
        c.N("let c = q2@follow@follow@uniq").Def("c").Borrow("L2", "q2.*.*", Uniq, "c");
        c.N("q2 = s2@uniq").Assign("q2").Borrow("Lq3", "s2", Uniq, "q2");
        c.N("q@follow@follow = 5").A(K.Write, "q.*.*");
        c.N("inspect(c)").Use("c");
        return c.Error("q@follow@follow = 5");
    }

    // struct S {r: uniq/i32}; 24a: S has a `drop` writing through self.r. `s = S(r: s.r@follow@uniq)` in MIR order:
    // the right side into Temps, the old value destroyed (its drop runs), then the store.
    static Case DropBase(string id, bool userDrop)
    {
        var c = new Case(id, "`s = S(r: s.r@follow@uniq)`" + (userDrop ? ", S's drop writes through self.r" : ", S has no drop"));
        c.N("var x = 1").Def("x");
        c.N("var s = S(r: x@uniq)").Def("s").Borrow("L1", "x", Uniq, "s.r");
        c.N("$t = s.r@follow@uniq").Def("$t").Borrow("L2", "s.r.*", Uniq, "$t");
        c.N("$v = S(r: $t)").Def("$v").Flow("$t", "$v.r");
        var d = c.N("destroy old s" + (userDrop ? " (drop writes self.r@follow)" : "")).Def("s");
        if (userDrop) d.Drop("s").A(K.Write, "s.r.*");
        c.N("s = move $v").Assign("s").A(K.Move, "$v").Flow("$v", "s");
        c.N("inspect(s)").Use("s");
        return userDrop ? c.Error("destroy old s (drop writes self.r@follow)") : c.Valid();
    }

    // 24a with the destruction and the store at one point: the new value is in transit from the dying $v.
    // Round 13: as MIR builds it, the Drop terminator (MirTerminatorKind.Drop), then the store at the next location (the second
    // design review's dump fD2: `drop _2 -> bb3; _2 = move _7`); rounds 5-12's one-point encoding is Case.OnePoint (OnePointDrop).
    static Case DropMerged() { var c = DropMergedCase(false); c.OnePoint = DropMergedCase(true); return c; }

    static Case DropMergedCase(bool onePoint)
    {
        var c = new Case("24c", "24a with the old value's drop and the store " + (onePoint ? "at one point" : "as MIR builds them: `drop s`, then `s = move $v` (rounds 5-12: at one point)"));
        c.N("var x = 1").Def("x");
        c.N("var s = S(r: x@uniq)").Def("s").Borrow("L1", "x", Uniq, "s.r");
        c.N("$t = s.r@follow@uniq").Def("$t").Borrow("L2", "s.r.*", Uniq, "$t");
        c.N("$v = S(r: $t)").Def("$v").Flow("$t", "$v.r");
        string st;
        if (onePoint) c.N(st = "s = move $v (drops old s)").Assign("s").Drop("s").A(K.Write, "s.r.*").A(K.Move, "$v").Flow("$v", "s");
        else { c.N(st = "drop s (old value; its drop writes self.r@follow)").Drop("s").A(K.Write, "s.r.*"); c.N("s = move $v").Assign("s").A(K.Move, "$v").Flow("$v", "s"); }
        c.N("inspect(s)").Use("s");
        return c.Error(st);
    }

    // ---- Round 6 (review r2) ----
    // f1: `x = f(x@ref)`, f retains its input in its result; x's Type needs no drop (no Drop statement in MIR).
    static Case SelfStore(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "dead" => "f1 dead store: `x = f(x@ref)`, x unused afterwards",
            "enum" => "f1 enum: `var x: E = .A(n: 42); x = pin(x@ref); match x .B(let r) => inspect(r@follow)`",
            _ => "f1: `x = f(x@ref)` (retains; no drop); `inspect(x)`",
        });
        c.N(kind == "enum" ? "var x: E = .A(n: 42)" : "var x = make()").Def("x");
        c.N("$a = x@ref").Def("$a").Borrow("Lx", "x", Ref, "$a");
        c.N(kind == "enum" ? "$r = pin($a)" : "$r = f($a)").Def("$r").Flow("$a", "$r");
        c.N("x = move $r").Assign("x").A(K.Move, "$r").Flow("$r", "x");
        if (kind == "enum") { c.N("match x .B(let r)").Def("r").A(K.Read, "x").Flow("x", "r"); c.N("inspect(r@follow)").A(K.Read, "r.*"); }
        else if (kind != "dead") c.N("inspect(x)").Use("x");
        return kind == "dead" ? c.Valid() : c.Error("x = move $r");
    }

    // f2: S {r: uniq/Array<i32>} whose drop clears self.r. 26a: `replace(s@uniq, with: …)`; 26b: `arr.clear()` on Array<S>.
    // The destroying call is a destroy access of its target, deep and exclusive (the Type observes), before `ended`.
    // Round 10 final (review r2 F1): the destroying call's destruction is A3's err(destroy) at the call (DestroyAt);
    // AsDrop holds rounds 6-10's encoding, a Drop on CS plus the drop's write through self.r.
    static Case Destroying(string id, bool clear) { var c = DestroyingCall(id, clear, false); c.AsDrop = DestroyingCall(id, clear, true); return c; }

    static Case DestroyingCall(string id, bool clear, bool asDrop)
    {
        var c = new Case(id, clear ? "f2: `c = arr[0].r@follow@uniq; arr.clear()` (Array<S>, S's drop writes self.r); `inspect(c@follow)`"
                                   : "f2: `c = s.r@follow[0]@uniq; replace(s@uniq, with: S(r: y@uniq))` (old s's drop clears self.r); `inspect(c@follow)`");
        c.N("var x = [1, 2]; var y = [3]").Def("x", "y");
        if (clear)
        {
            c.N("var arr: Array<S> = [S(r: x@uniq)]").Def("arr").Borrow("L1", "x", Uniq, "arr");
            c.N("let c = arr[0].r@follow@uniq").Def("c").Borrow("L2", "arr.[].r.*", Uniq, "c");
            c.N("$t = arr (clear receiver, reserved; destroying)").Def("$t").Borrow("Lt", "arr", Uniq, "$t", reserve: true, inPlace: true);
            var clr = c.N("arr.clear()").Activate("Lt");
            if (asDrop) clr.Drop("arr.[]").A(K.Write, "arr.[].r.*"); else clr.DestroyAt("arr.[]", "Lt");
            c.N("inspect(c@follow)").A(K.Read, "c.*");
            return c.Error("arr.clear()");
        }
        c.N("var s = S(r: x@uniq)").Def("s").Borrow("L1", "x", Uniq, "s.r");
        c.N("let c = s.r@follow[0]@uniq").Def("c").Borrow("L2", "s.r.*.[]", Uniq, "c");
        c.N("$t = s@uniq (reserved, replace target)").Def("$t").Borrow("Lt", "s", Uniq, "$t", reserve: true, inPlace: true);
        c.N("$v = S(r: y@uniq)").Def("$v").Borrow("Ly", "y", Uniq, "$v.r");
        var rpl = c.N("replace($t, with: $v)").Activate("Lt").Def("s");
        if (asDrop) rpl.Drop("s").A(K.Write, "s.r.*"); else rpl.DestroyAt("s", "Lt");
        rpl.Flow("$v", "s");
        c.N("inspect(c@follow)").A(K.Read, "c.*");
        return c.Error("replace($t, with: $v)");
    }

    // f3 (valid): S {r: ref/i32} has a drop; a shared reborrow through the shared field outlives s.
    static Case SharedField()
    {
        var c = new Case("27", "f3: `var v = other@ref; do: let s = S(r: x@ref); v = s.r@follow@ref` (S has a drop); `inspect(v)`");
        c.N("var x = 1; var other = 2").Def("x", "other");
        c.N("var v = other@ref").Def("v").Borrow("Lo", "other", Ref, "v");
        c.N("  let s = S(r: x@ref)").Def("s").Borrow("Lx", "x", Ref, "s.r");
        c.N("  v = s.r@follow@ref").Assign("v").Borrow("L2", "s.r.*", Ref, "v", sharedAt: 1);
        c.N("  (end of do: drop s)").Drop("s").Def("s");
        c.N("inspect(v)").Use("v");
        return c.Valid();
    }

    // f9: arguments of one call. An exclusive argument borrow is a reservation (§15.6.7); 28d is a stored borrow.
    static Case Args(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "uniqref" => "f9: `g(a@uniq, a@ref)`",
            "refref" => "f9: `g(a@ref, a@ref)`",
            "fields" => "f9: `g(a.x@uniq, a.y@ref)`",
            _ => "f9: `let r = a@uniq; g(r, a@ref)` (r stored, not a reservation)",
        });
        c.N("var a = A(x: 1, y: 2)").Def("a");
        switch (kind)
        {
            case "uniqref":
                c.N("$1 = a@uniq (reserved)").Def("$1").Borrow("L1", "a", Uniq, "$1", reserve: true);
                c.N("$2 = a@ref").Def("$2").Borrow("L2", "a", Ref, "$2");
                c.N("g($1, $2)").Activate("L1").Use("$1", "$2");
                return c.Error("g($1, $2)");
            case "refref":
                c.N("$1 = a@ref").Def("$1").Borrow("L1", "a", Ref, "$1");
                c.N("$2 = a@ref").Def("$2").Borrow("L2", "a", Ref, "$2");
                c.N("g($1, $2)").Use("$1", "$2");
                return c.Valid();
            case "fields":
                c.N("$1 = a.x@uniq (reserved)").Def("$1").Borrow("L1", "a.x", Uniq, "$1", reserve: true);
                c.N("$2 = a.y@ref").Def("$2").Borrow("L2", "a.y", Ref, "$2");
                c.N("g($1, $2)").Activate("L1").Use("$1", "$2");
                return c.Valid();
            default:
                c.N("let r = a@uniq").Def("r").Borrow("Lr", "a", Uniq, "r");
                c.N("$2 = a@ref").Def("$2").Borrow("L2", "a", Ref, "$2");
                c.N("g(r, $2)").Use("r", "$2");
                return c.Error("$2 = a@ref");
        }
    }

    // ---- 10: call reservations (§15.6.7) ----
    static Case Replace()
    {
        var c = new Case("10a", "`Kimi.Intrinsics.replace(p@uniq, with: p + 1)`");
        c.N("var p: i32 = 1").Def("p");
        c.N("$t = p@uniq (reserved)").Def("$t").Borrow("Lp", "p", Uniq, "$t", reserve: true);
        c.N("$w = p + 1").Def("$w").A(K.Read, "p");
        c.N("replace($t, with: $w)").Activate("Lp").Use("$t").Through("$t.*", "$w");
        return c.Valid();
    }

    static Case AppendRemove()
    {
        var c = new Case("10b", "`tasks.append(tasks.remove(0))`");
        c.N("var tasks: Array<Task> = []").Def("tasks");
        c.N("$ra = tasks (append receiver, reserved)").Def("$ra").Borrow("La", "tasks", Uniq, "$ra", reserve: true);
        c.N("$rr = tasks (remove receiver, reserved)").Def("$rr").Borrow("Lr", "tasks", Uniq, "$rr", reserve: true);
        c.N("$v = remove($rr, 0)").Activate("Lr").Use("$rr").Def("$v");
        c.N("append($ra, $v)").Activate("La").Use("$ra", "$v");
        return c.Error("$v = remove($rr, 0)");
    }

    static Case Insert()
    {
        var c = new Case("10c", "`tasks.insert(tasks.length, Task.init(9))`");
        c.N("var tasks: Array<Task> = []").Def("tasks");
        c.N("$r = tasks (insert receiver, reserved)").Def("$r").Borrow("Li", "tasks", Uniq, "$r", reserve: true);
        c.N("$n = tasks.length").Def("$n").A(K.Read, "tasks");
        c.N("$task = Task.init(9)").Def("$task");
        c.N("insert($r, $n, $task)").Activate("Li").Use("$r", "$n", "$task");
        return c.Valid();
    }

    static Case Swap()
    {
        var c = new Case("10d", "`Kimi.Intrinsics.swap(p@uniq, p@uniq)` (§15.7.1)");
        c.N("var p: i32 = 0").Def("p");
        c.N("$a = p@uniq (reserved)").Def("$a").Borrow("La", "p", Uniq, "$a", reserve: true);
        c.N("$b = p@uniq (reserved)").Def("$b").Borrow("Lb", "p", Uniq, "$b", reserve: true);
        c.N("swap($a, $b)").Activate("La").Activate("Lb").Use("$a", "$b");
        return c.Error("swap($a, $b)");
    }

    // ---- 11: unreachable code (§14.10.3) ----
    static Case Unreachable()
    {
        var c = new Case("11", "Loan conflict after `return` (assumed path)");
        c.N("var x = 1").Def("x");
        c.N("let r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("return 0").Goto("exit").Assume("x = 3");
        c.N("x = 3").Assign("x");
        c.N("inspect(r)").Use("r").Goto("exit");
        c.N("exit").Goto();
        return c.Error("x = 3");
    }

    static Case UnreachableJoin()
    {
        var c = new Case("11b", "two assumed predecessors disagree (union reading; intersection would accept)");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = y@uniq").Def("r").Borrow("Ly", "y", Uniq, "r");
        c.N("if c").Goto("  r = x@uniq", "  r = y@uniq");
        c.N("  r = x@uniq").Assign("r").Borrow("Lx", "x", Uniq, "r");
        c.N("  return 0").Goto("exit").Assume("x = 3");
        c.N("  r = y@uniq").Assign("r").Borrow("Ly2", "y", Uniq, "r");
        c.N("  return 1").Goto("exit").Assume("x = 3");
        c.N("x = 3").Assign("x");
        c.N("inspect(r)").Use("r").Goto("exit");
        c.N("exit").Goto();
        return c.Error("x = 3");
    }
}


// ---- Reviewer (E13/E14 design review): M' encodings of the 132 family and new probes 134-135 ----
// Round 12: the operands of every operation are listed in evaluation order (Node.Input; Node.Operation for a non-Call). A case
// whose reviewer encoding is a form round 12 rejects keeps it as its other form: 134a and 134e the G123 MIR (Always: M′, which
// design A runs), 134d the shape-derived reservation (Shape), 135b A3 §4's intrinsic row (IntrinsicRow).
static class Reviewer
{
    const bool Uniq = true, Ref = false;

    // M': every PairAcquire is secured into its own temp where it is evaluated (a reservation there in the uniq view, a Copy in the
    // ref view); null where the case already has that form (132c-e; round 13: 132k, 132l), and 132j/m use their Secured encoding.
    public static Case PairAlways(string id)
    {
        if (id is "132c" or "132d" or "132e" or "132j" or "132k" or "132l" or "132m") return null; // round 13: 132k, 132l are built so
        bool uniqView = id is not ("132b" or "132i" or "132l" or "132n");
        var c = new Case(id, "M' " + id);
        c.N("func outer<s/T>(p: s/T), " + (uniqView ? "uniq view" : "ref view")).Param("Lp", "p");
        void Acq(string tmp, string place, string loan)
        {
            if (uniqView) c.N($"{tmp} = acquire {place} (secured: a reservation)").Def(tmp).Acquire(loan, place + ".*", tmp);
            else c.N($"{tmp} = acquire {place} (secured: a Copy)").Def(tmp).A(K.Read, place).Flow(place, tmp);
        }
        string call;
        switch (id)
        {
            case "132a": case "132b":
                c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
                c.N("$4 = v@ref").Def("$4").Borrow("L4", "v", Ref, "$4");
                Acq("$5", "v", "L5");
                var na = c.N(call = "inner2(move $4, move $5)").Input(K.Move, "$4").Input(K.Move, "$5"); if (uniqView) na.Activate("L5");
                return uniqView ? c.Error(call) : c.Valid();
            case "132f":
                Acq("$3", "p", "L3");
                Acq("$5", "p", "L5");
                c.N(call = "$4 = bumpS(move $5)").Def("$4").Input(K.Move, "$5").Activate("L5");
                c.N("inner7(move $3, move $4)").Input(K.Move, "$3").Input(K.Move, "$4").Activate("L3");
                return c.Error(call);
            case "132g":
                c.N("$3 = p.n + 1").Def("$3").A(K.Read, "p.*.n");
                Acq("$4", "p", "L4");
                c.N("inner8(move $3, move $4)").Input(K.Move, "$3").Input(K.Move, "$4").Activate("L4");
                return c.Valid();
            case "132h": case "132i":
                c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
                Acq("$3", "v", "L3"); Acq("$4", "v", "L4");
                var nh = c.N(call = "Call inner9(v, v)").Input(K.Move, "$3").Input(K.Move, "$4"); if (uniqView) nh.Activate("L3").Activate("L4");
                return uniqView ? c.Error(call) : c.Valid();
            default: // 132n
                c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
                c.N("let u = v@follow@ref").Def("u").Borrow("Lu", "v.*", Ref, "u", sharedAt: 1);
                Acq("$3", "v", "L3");
                c.N("Call innerR(v, u)").Input(K.Move, "$3").Input(K.Read, "u");
                return c.Valid();
        }
    }

    public static Case Make(string id) => id switch
    {
        "134a" => With(Z1(id, false), always: Z1(id, true)),
        "134b" => Z1(id, true),
        "134c" => X2(id, false),
        "134d" => With(X2(id, false), shape: X2(id, true)),
        "134e" => With(V1(id, false), always: V1(id, true)),
        "134f" => V1(id, true),
        "134g" => W1(id),
        "135b" => With(Clone(id, false), row: Clone(id, true)),
        _ => Clone(id, false),
    };

    static Case With(Case c, Case always = null, Case shape = null, Case row = null) { c.Always = always; c.Shape = shape; c.IntrinsicRow = row; return c; }

    // 134a, 134b: z1, `var v = p; let u = v@follow@ref; _ = (v, u)`, uniq view; $t is dead after (a discard's shallow drop of a
    // non-observing Type is no use, A3 §5 use(H, ℓ), so no node follows). G123 leaves the PairAcquire at the Aggregate:
    // `$t = (acquire v, copy u)`; M′ secures it, unmarked (an aggregate operand): `$1 = acquire v` (a live Uniq), `$t = (move $1, copy u)`.
    static Case Z1(string id, bool m2)
    {
        var c = new Case(id, id == "134b" ? "z1 under M' for every operation: `_ = (v, u)` -> `$1 = acquire v; $t = (move $1, copy u)`"
            : "z1, uniq view: `var v = p; let u = v@follow@ref; _ = (v, u)` (G123's MIR `$t = (acquire v, copy u)`; $t dead; design A builds 134b's M′)");
        c.N("func outer<s/T>(p: s/T), uniq view").Param("Lp", "p");
        c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
        c.N("let u = v@follow@ref").Def("u").Borrow("Lu", "v.*", Ref, "u");
        string at;
        if (m2)
        {
            c.N(at = "$1 = acquire v (secured: live Uniq)").Def("$1").Borrow("L1", "v.*", Uniq, "$1");
            c.N("$t = (move $1, copy u)").Def("$t").Input(K.Move, "$1").Flow("$1", "$t").Input(K.Read, "u").Flow("u", "$t").Operation();
        }
        else c.N(at = "$t = (acquire v, copy u)").Def("$t").AcquireOp("Lt", "v.*", "$t").Input(K.Read, "u").Flow("u", "$t").Operation();
        return c.Error(at);
    }

    // 134c, 134d: x2, `inner11(do => v, peek(v@follow@ref))`, uniq view: `$a = acquire v; $b = &(*v); $c = peek(move $b);
    // inner11(move $a, move $c)`. The builder leaves a control-flow result's PairAcquire unmarked (a live Uniq, SPEC 15.6.7);
    // shape: A5 reads `$a = acquire v` moved into a Call as a reservation (x1's shape, 139a), the rejected option.
    static Case X2(string id, bool shape)
    {
        var c = new Case(id, (id == "134d" ? "x2, reservation derived from the MIR shape (design A: the marker, as 134c; ShapeReservation: the shape): "
            : "x2 (SPEC 15.6.7: a control-flow result is live): ")
            + "`inner11(do => v, peek(v@follow@ref))` (`$a = acquire v; $b = &(*v); $c = peek(move $b); inner11(move $a, move $c)`)");
        c.N("func outer<s/T>(p: s/T), uniq view").Param("Lp", "p");
        c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
        if (shape) c.N("$a = acquire v (do => v; read as a reservation)").Def("$a").Acquire("La", "v.*", "$a");
        else c.N("$a = acquire v (do => v; live Uniq)").Def("$a").Borrow("La", "v.*", Uniq, "$a");
        c.N("$b = &(*v)").Def("$b").Borrow("Lb", "v.*", Ref, "$b");
        c.N("$c = peek(move $b)").Def("$c").Input(K.Move, "$b");
        var n = c.N("inner11(move $a, move $c)").Input(K.Move, "$a").Input(K.Move, "$c"); if (shape) n.Activate("La");
        return c.Error("$b = &(*v)");
    }

    // 134e, 134f: v1, `let t = (v, v@follow); inspect(t)`, uniq view, T Copy; t live. G123: `t = (acquire v, copy (*v))`; M′,
    // unmarked: `$1 = acquire v` (a live Uniq), `t = (move $1, copy (*v))`.
    static Case V1(string id, bool m2)
    {
        var c = new Case(id, id == "134f" ? "v1 under M' for every operation: `$1 = acquire v; t = (move $1, copy (*v))`"
            : "v1, uniq view: `let t = (v, v@follow); inspect(t)` (G123's MIR `t = (acquire v, copy (*v))`; design A builds 134f's M′)");
        c.N("func outer<s/T>(p: s/T), uniq view").Param("Lp", "p");
        c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
        string at;
        if (m2)
        {
            c.N("$1 = acquire v (secured: live Uniq)").Def("$1").Borrow("L1", "v.*", Uniq, "$1");
            c.N(at = "t = (move $1, copy (*v))").Def("t").Input(K.Move, "$1").Flow("$1", "t").Input(K.Read, "v.*").Flow("v.*", "t").Operation();
        }
        else c.N(at = "t = (acquire v, copy (*v))").Def("t").AcquireOp("Lt", "v.*", "t").Input(K.Read, "v.*").Flow("v.*", "t").Operation();
        c.N("inspect(t)").A(K.Read, "t");
        return c.Error(at);
    }

    // 134g: w1, `_ = (x@uniq, x)`: `$1 = &uniq x; $t = (move $1, copy x)`, $t dead (the old path rejects it at the read of x).
    static Case W1(string id)
    {
        var c = new Case(id, "w1: `_ = (x@uniq, x)` (`$1 = &uniq x; $t = (move $1, copy x)`; $t dead)");
        c.N("var x = 1").Def("x");
        c.N("$1 = x@uniq").Def("$1").Borrow("L1", "x", Uniq, "$1");
        c.N("$t = (move $1, copy x)").Def("$t").Input(K.Move, "$1").Flow("$1", "$t").Input(K.Read, "x").Flow("x", "$t").Operation();
        return c.Error("$t = (move $1, copy x)");
    }

    // 135a-c: Kimi.Intrinsics.clone<s/T>(value: ref/(s/T)) -> s/T, s is rc or arc. E14: the declaration retains the payload's
    // Loans (Content); A3 §4's intrinsic row (every operand -> result, Value) is 135b's IntrinsicRow form.
    static Case Clone(string id, bool valueRow)
    {
        var c = new Case(id, id switch
        {
            "135a" => "clone, Content row (its declaration): `let owner = makeRc(S(r: x@ref)); let c = clone(owner@ref); _ = owner@move; inspect(c)`",
            "135b" => "135a with A3 4's intrinsic row (every operand -> result, Value): c carries the borrow of owner (design A: E14's Content row, as 135a; IntrinsicAllOperands: the row)",
            _ => "135a's Content row is sound: `...; let c = clone(owner@ref); x = 5; inspect(c)`",
        });
        c.N("var x = 1").Def("x");
        c.N("let owner = makeRc(S(r: x@ref))").Def("owner").Borrow("Lx", "x", Ref, "owner");
        c.N("$t = owner@ref").Def("$t").Borrow("Lo", "owner", Ref, "$t");
        var cl = c.N("let c = clone(move $t)").Def("c").Input(K.Move, "$t");
        if (valueRow) cl.Flow("$t", "c"); else cl.Flow("$t.*", "c");
        if (id == "135c") { c.N("x = 5").Assign("x"); c.N("inspect(c)").A(K.Read, "c"); return c.Error("x = 5"); }
        c.N("_ = owner@move").A(K.Move, "owner");
        c.N("inspect(c)").A(K.Read, "c");
        return c.Valid();
    }
}

// ---- Round 12 (A3 E13 as the owner chose on 2026-10-18, option R1: A5 handles operand evaluation order) ----
// E13 (1): the operands an operation leaves at ℓ (Node.Input, in evaluation order: SPEC 12.2, a receiver or Value callee first)
// are checked on CS_i(ℓ); Node.Operation marks an operation other than a Call. E13 (3), M′: the builder secures every PairAcquire
// into its own temp; Acquire is a marked one (it produces an invocation input: a reservation in its temp, activated at its
// Call), a Uniq Borrow an unmarked one (a let, a control-flow result, an aggregate operand, a capture). G123: a Place operand is
// secured into a temp when a later operand of the same operation builds code; a Ref, a PairAcquire and a Call build code.
// give2(a: i32, b: i32); give2T<T>(a: T, b: i32); inner11<s/T>(a: s/T, b: i32); peek<T>(x: ref/T) -> i32; run<F>(f: F,
// k: i32); contract Plus { Self is Copy; func plus(self: Self, other: Self) -> i32 }; S3<T> {r: T, n: i32} with T = ref/i32.
static class Round12
{
    const bool Uniq = true, Ref = false;

    public static IEnumerable<Case> All() => [
        .. new[] { "136a", "136b", "136c", "136d", "136e", "136f", "136g", "136h" }.Select(Order),
        .. new[] { "137a", "137b", "137c", "137d", "137e" }.Select(Aggregate),
        .. new[] { "138a", "138b", "138c", "138d" }.Select(c => { var k = Capture(c); k.Built = CaptureBuilt(c); return k; }),
        .. new[] { "139a", "139b", "139c", "139d" }.Select(Marker),
        .. new[] { "140a", "140b", "140c", "140d", "140e", "140f", "140g" }.Select(OwnValue)];

    // 136: evaluation order (the E13 review's o1-o4, both orders; a by-value receiver first).
    static Case Order(string id)
    {
        bool rFirst = id is "136a" or "136c" or "136e" or "136h";
        var (op, dest) = id switch
        {
            "136a" or "136b" => ("give2", (string)null), "136c" or "136d" => ("t = ", "t"), "136e" or "136f" => ("s = Add", "s"), _ => ("k = plus", "k"),
        };
        var args = rFirst ? "copy (*r), copy x" : "copy x, copy (*r)";
        var label = dest == "t" ? $"t = ({args})" : $"{op}({args})";
        var c = new Case(id, id switch
        {
            "136a" => "o1: `let r = x@uniq; give2(r@follow, x)` (SPEC 12.2: r is not used after its operand; the old path accepts)",
            "136b" => "o2: `let r = x@uniq; give2(x, r@follow)` (x is read while r is still to be read; old path: ComparisonLoanConflict_Kd at x)",
            "136c" => "o3: `let r = x@uniq; let t = (r@follow, x); inspect(t)` (t live)",
            "136d" => "o3 reversed: `let t = (x, r@follow); inspect(t)`",
            "136e" => "o4: `let r = x@uniq; let s = r@follow + x`",
            "136f" => "o4 reversed: `let s = x + r@follow`",
            "136g" => "receiver first: `var x = x0; let r = x@uniq; let k = x.plus(r@follow)`, T is Plus (a Copy receiver)",
            _ => "receiver first: `var x = x0; let r = x@uniq; let k = r@follow.plus(x)`",
        } + $" (`{label}`)");
        c.N(id is "136g" or "136h" ? "var x = x0 (T is Plus, Copy)" : "var x = 1").Def("x");
        c.N("let r = x@uniq").Def("r").Borrow("Lr", "x", Uniq, "r");
        var n = c.N(label);
        if (rFirst) n.Input(K.Read, "r.*").Input(K.Read, "x"); else n.Input(K.Read, "x").Input(K.Read, "r.*");
        if (dest != null) n.Def(dest); // an i32 result: plus and give2 retain nothing, an Add carries no Loan
        if (dest is "t" or "s") n.Operation();
        if (dest == "t") { n.Flow("r.*", "t").Flow("x", "t"); c.N("inspect(t)").A(K.Read, "t"); }
        return rFirst ? c.Valid() : c.Error(label);
    }

    // 137: aggregates (the E13 review's w1-w3; 134g is w1 itself).
    static Case Aggregate(string id)
    {
        var c = new Case(id, id switch
        {
            "137a" => "w1's counterpart: `_ = (x, x@uniq)` (G123 secures x: `$1 = copy x; $2 = &uniq x; $t = (move $1, move $2)`; SPEC 12.2: x is read before the Borrow, and its i32 Copy carries no Loan)",
            "137b" => "w1 with the tuple used: `let t = (x@uniq, x); inspect(t)` (`$1 = &uniq x; t = (move $1, copy x)`)",
            "137c" => "w3: `let r = x@uniq; _ = (r, x)` (`$1 = &uniq (*r)`, a bare acquisition live when formed, SPEC 15.6.7; `$t = (move $1, copy x)`)",
            "137d" => "w3's counterpart, a dead aggregate that reads only: `let r = x@ref; _ = (r, x)` (`$t = (copy r, copy x)`)",
            _ => "`let r = x@uniq; _ = (x, r)` (G123 secures x: `$1 = copy x; $2 = &uniq (*r); $t = (move $1, move $2)`)",
        });
        c.N("var x = 1").Def("x");
        string at = null;
        switch (id)
        {
            case "137a":
                c.N("$1 = copy x").Def("$1").A(K.Read, "x").Flow("x", "$1");
                c.N("$2 = &uniq x").Def("$2").Borrow("L2", "x", Uniq, "$2");
                c.N("$t = (move $1, move $2)").Def("$t").Input(K.Move, "$1").Flow("$1", "$t").Input(K.Move, "$2").Flow("$2", "$t").Operation();
                break;
            case "137b":
                c.N("$1 = &uniq x").Def("$1").Borrow("L1", "x", Uniq, "$1");
                c.N(at = "t = (move $1, copy x)").Def("t").Input(K.Move, "$1").Flow("$1", "t").Input(K.Read, "x").Flow("x", "t").Operation();
                c.N("inspect(t)").A(K.Read, "t");
                break;
            case "137c":
                c.N("let r = x@uniq").Def("r").Borrow("Lr", "x", Uniq, "r");
                c.N("$1 = &uniq (*r)").Def("$1").Borrow("L1", "r.*", Uniq, "$1");
                c.N(at = "$t = (move $1, copy x)").Def("$t").Input(K.Move, "$1").Flow("$1", "$t").Input(K.Read, "x").Flow("x", "$t").Operation();
                break;
            case "137d":
                c.N("let r = x@ref").Def("r").Borrow("Lr", "x", Ref, "r");
                c.N("$t = (copy r, copy x)").Def("$t").Input(K.Read, "r").Flow("r", "$t").Input(K.Read, "x").Flow("x", "$t").Operation();
                break;
            default: // 137e
                c.N("let r = x@uniq").Def("r").Borrow("Lr", "x", Uniq, "r");
                c.N(at = "$1 = copy x").Def("$1").A(K.Read, "x").Flow("x", "$1");
                c.N("$2 = &uniq (*r)").Def("$2").Borrow("L2", "r.*", Uniq, "$2");
                c.N("$t = (move $1, move $2)").Def("$t").Input(K.Move, "$1").Flow("$1", "$t").Input(K.Move, "$2").Flow("$2", "$t").Operation();
                break;
        }
        return at == null ? c.Valid() : c.Error(at);
    }

    // 138: Closure captures with a PairAcquire (SPEC 7.7: explicit entries execute left to right; a capture's PairAcquire is
    // unmarked, a live Uniq in the uniq view, SPEC 15.6.7: no reservation passes through captures).
    static Case Capture(string id)
    {
        bool uniqView = id != "138b";
        var c = new Case(id, (uniqView ? "uniq view: " : "ref view: ") + id switch
        {
            "138a" => "z3: `var v = p; let u = v@follow@ref; let c = func [v, u] () => ()` (`$1 = acquire v`, unmarked; `c = Closure(move $1, copy u)`)",
            "138b" => "z3: `var v = p; let u = v@follow@ref; let c = func [v, u] () => ()` (`$1 = acquire v`: a Copy)",
            "138c" => "the other capture order: `let c = func [u, v] () => ()` (G123 secures u: `$1 = copy u; $2 = acquire v; c = Closure(move $1, move $2)`)",
            _ => "a capture inside an invocation input: `run(func [v] () => (), peek(v@follow@ref))` (`$1 = acquire v`, unmarked; `$c = Closure(move $1)`; `$b = &(*v)`; compare 139a)",
        });
        c.N("func outer<s/T>(p: s/T), " + (uniqView ? "uniq view" : "ref view")).Param("Lp", "p");
        c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
        void Acq(string tmp, string loan)
        {
            if (uniqView) c.N($"{tmp} = acquire v (unmarked: a capture; live Uniq)").Def(tmp).Borrow(loan, "v.*", Uniq, tmp);
            else c.N($"{tmp} = acquire v (a Copy)").Def(tmp).A(K.Read, "v").Flow("v", tmp);
        }
        if (id == "138d")
        {
            Acq("$1", "L1");
            c.N("$c = Closure(move $1)").Def("$c").Input(K.Move, "$1").Flow("$1", "$c").Operation();
            c.N("$b = &(*v)").Def("$b").Borrow("Lb", "v.*", Ref, "$b");
            c.N("$r = peek(move $b)").Def("$r").Input(K.Move, "$b");
            c.N("run(move $c, move $r)").Input(K.Move, "$c").Input(K.Move, "$r");
            return c.Error("$b = &(*v)");
        }
        c.N("let u = v@follow@ref").Def("u").Borrow("Lu", "v.*", Ref, "u", sharedAt: uniqView ? 0 : 1);
        if (id == "138c")
        {
            c.N("$1 = copy u").Def("$1").A(K.Read, "u").Flow("u", "$1");
            Acq("$2", "L2");
            c.N("c = Closure(move $1, move $2)").Def("c").Input(K.Move, "$1").Flow("$1", "c").Input(K.Move, "$2").Flow("$2", "c").Operation();
            return c.Error("$2 = acquire v (unmarked: a capture; live Uniq)");
        }
        Acq("$1", "L1");
        c.N("c = Closure(move $1, copy u)").Def("c").Input(K.Move, "$1").Flow("$1", "c").Input(K.Read, "u").Flow("u", "c").Operation();
        return uniqView ? c.Error("$1 = acquire v (unmarked: a capture; live Uniq)") : c.Valid();
    }

    // Round 13: today's builder builds captures without G123 or M′ (the second design review's MIR dump p1/a138: outerA
    // `Closure(acquire b0 _2, copy _3)`, outerC `Closure(copy _3, acquire b0 _2)`): the PairAcquire stays at the Closure as its
    // operand, a live Uniq formed there in the uniq view (AcquireOp), a Copy in the ref view. Case 138's own nodes are E13 (2)/(4)'s.
    static Case CaptureBuilt(string id)
    {
        bool uniqView = id != "138b";
        var c = new Case(id, "today's builder: captures at the Closure");
        c.N("func outer<s/T>(p: s/T), " + (uniqView ? "uniq view" : "ref view")).Param("Lp", "p");
        c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
        Node Acq(Node n, string into, string loan) => uniqView ? n.AcquireOp(loan, "v.*", into) : n.Input(K.Read, "v").Flow("v", into);
        if (id == "138d")
        {
            Acq(c.N("$c = Closure(acquire v)").Def("$c"), "$c", "L1").Operation();
            c.N("$b = &(*v)").Def("$b").Borrow("Lb", "v.*", Ref, "$b");
            c.N("$r = peek(move $b)").Def("$r").Input(K.Move, "$b");
            c.N("run(move $c, move $r)").Input(K.Move, "$c").Input(K.Move, "$r");
            return c.Error("$b = &(*v)");
        }
        c.N("let u = v@follow@ref").Def("u").Borrow("Lu", "v.*", Ref, "u", sharedAt: uniqView ? 0 : 1);
        string at;
        if (id == "138c") Acq(c.N(at = "c = Closure(copy u, acquire v)").Def("c").Input(K.Read, "u").Flow("u", "c"), "c", "L2").Operation();
        else Acq(c.N(at = "c = Closure(acquire v, copy u)").Def("c"), "c", "L1").Input(K.Read, "u").Flow("u", "c").Operation();
        return uniqView ? c.Error(at) : c.Valid();
    }

    // 139: the marker (E13 (3)): x1 (x2 is 134c) and an explicit case adaptation at an input, against its unmarked `let` form.
    static Case Marker(string id)
    {
        bool uniqView = id != "139b";
        var c = new Case(id, (uniqView ? "uniq view: " : "ref view: ") + id switch
        {
            "139a" or "139b" => "x1: `var v = p; inner11(v, peek(v@follow@ref))` (`$a = acquire v`, marked; `$b = &(*v)`; `$c = peek(move $b)`; `inner11(move $a, move $c)`)",
            "139c" => "an explicit case adaptation at an input: `var x = item@move; inner11(x@(s/T), peek(x@ref))` (`$a = x@(s/T)`, marked)",
            _ => "the adaptation stored first: `let a = x@(s/T); let k = peek(x@ref); inner11(a, k)` (`a = x@(s/T)`, unmarked: a let, live when formed)",
        });
        c.N("func outer<s/T>(p: s/T, item: T), " + (uniqView ? "uniq view" : "ref view")).Param("Lp", "p");
        if (id is "139a" or "139b")
        {
            c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
            if (uniqView) c.N("$a = acquire v (marked: a reservation)").Def("$a").Acquire("La", "v.*", "$a");
            else c.N("$a = acquire v (a Copy)").Def("$a").A(K.Read, "v").Flow("v", "$a");
            c.N("$b = &(*v)").Def("$b").Borrow("Lb", "v.*", Ref, "$b", sharedAt: uniqView ? 0 : 1);
            c.N("$c = peek(move $b)").Def("$c").Input(K.Move, "$b");
            var n = c.N("inner11(move $a, move $c)").Input(K.Move, "$a").Input(K.Move, "$c"); if (uniqView) n.Activate("La");
            return c.Valid();
        }
        c.N("var x = item@move").Def("x");
        if (id == "139c")
        {
            c.N("$a = x@(s/T) (marked: a reservation)").Def("$a").Acquire("La", "x", "$a");
            c.N("$b = &x").Def("$b").Borrow("Lb", "x", Ref, "$b");
            c.N("$c = peek(move $b)").Def("$c").Input(K.Move, "$b");
            c.N("inner11(move $a, move $c)").Input(K.Move, "$a").Input(K.Move, "$c").Activate("La");
            return c.Valid();
        }
        c.N("let a = x@(s/T) (unmarked: a let; live Uniq)").Def("a").Borrow("La", "x", Uniq, "a");
        c.N("$b = &x").Def("$b").Borrow("Lb", "x", Ref, "$b");
        c.N("let k = peek(move $b)").Def("k").Input(K.Move, "$b");
        c.N("$a2 = acquire a (marked: a reservation)").Def("$a2").Acquire("La2", "a.*", "$a2");
        c.N("inner11(move $a2, copy k)").Input(K.Move, "$a2").Input(K.Read, "k").Activate("La2");
        return c.Error("$b = &x");
    }

    // 140: the operand's own value (round 12, finding 1). `var z = 0; var s = S3(r: z@ref, n: 1); s.r = s.n@ref` makes s carry
    // its own Loan Ln on s.n; Moving s as an operand moves a value the operation then uses (SPEC 15.6: checked against the Loans
    // of the values used after the Move; rustc E0505). The old path rejects 140a-140c, 140e and 140f at `s@move`. 140e-140g
    // (planning session on finding 2): a single-operand Assign `let t = s@move` uses s's value at ℓ, t dead or not; 140g's s
    // carries a Loan on another root only (`let r2 = y@ref; s.r = r2`).
    static Case OwnValue(string id)
    {
        var c = new Case(id, "own value: `var s = S3(r: z@ref, n: 1); s.r = s.n@ref; " + id switch
        {
            "140a" => "give2T(s@move, 1)` (round 13, the builder's MIR: `$7 = move s; give2T(move $7, const 1)`; give2T retains nothing)",
            "140b" => "_ = (s@move, 1)` (round 13: `$7 = move s; $t = (move $7, const 1)`; $t dead)",
            "140c" => "let t = (s@move, 1); inspect(t)` (round 13: `$7 = move s; t = (move $7, const 1)`, t live)",
            "140d" => "give2(s.n, s.n)` (`give2(copy s.n, copy s.n)`: reads only)",
            "140e" => "let t = s@move` (`t = move s`, a single-operand Use; t dead)",
            "140f" => "let t = s@move; inspect(t)` (t used)",
            _ => "…` with `let r2 = y@ref; s.r = r2; let t = s@move` instead (s carries Ly on another root)",
        });
        c.N("var z = 0").Def("z"); c.N("var s = S3(r: z@ref, n: 1)").Def("s").Borrow("Lz", "z", Ref, "s.r");
        if (id == "140g")
        {
            c.N("var y = 0").Def("y"); c.N("let r2 = y@ref").Def("r2").Borrow("Ly", "y", Ref, "r2");
            c.N("s.r = r2").Assign("s.r").A(K.Read, "r2").Flow("r2", "s.r");
            c.N("let t = s@move").Def("t").A(K.Move, "s").Flow("s", "t");
            return c.Valid();
        }
        c.N("s.r = s.n@ref").Assign("s.r").Borrow("Ln", "s.n", Ref, "s.r");
        string at;
        switch (id)
        {
            // Round 13 (the second design review's MIR dump p2/b140): an explicit `@move` builds code, so the Move is its own Use
            // into a temp the operation reads (round 12 left `move s` at the operation; the review's r13m140a/b are these forms).
            case "140a": c.N(at = "$7 = move s").Def("$7").A(K.Move, "s").Flow("s", "$7"); c.N("give2T(move $7, const 1)").Input(K.Move, "$7"); break;
            case "140b": c.N(at = "$7 = move s").Def("$7").A(K.Move, "s").Flow("s", "$7"); c.N("$t = (move $7, const 1)").Def("$t").Input(K.Move, "$7").Flow("$7", "$t").Operation(); break;
            case "140c": c.N(at = "$7 = move s").Def("$7").A(K.Move, "s").Flow("s", "$7"); c.N("t = (move $7, const 1)").Def("t").Input(K.Move, "$7").Flow("$7", "t").Operation(); c.N("inspect(t)").A(K.Read, "t"); break;
            case "140e": case "140f": c.N(at = "let t = s@move").Def("t").A(K.Move, "s").Flow("s", "t"); if (id == "140f") c.N("inspect(t)").A(K.Read, "t"); break;
            default: c.N("give2(copy s.n, copy s.n)").Input(K.Read, "s.n").Input(K.Read, "s.n"); return c.Valid();
        }
        return c.Error(at);
    }
}


// ---- Round 13 (A3 E13 rewritten after its second design review; the owner's "one rule for every access") ----
// The second design review's cases (its r13 ids, taken as written except n1 and n2, which run E13 (2)'s order with the review's
// MIR as Case.Built; its m140a and m140b are 140a and 140b as corrected below), and this round's d3, s1 and s2. Old-path
// verdicts are the review's (Kimi.dll check, legacy pipeline; d3 from its ledger record). S3<T> {r: T, n: i32}, S4 = S3;
// Q2 {a} {r: uniq/i32 during a, n: i32} whose drop writes `self.r@follow = 0`; S7 {a} {r: uniq/i32, q: ref/i32 during a};
// give(a: uniq/i32, b: i32); give2(a: i32, b: i32); G {level: i32 with set(value: i32)}; S5 {r: ref/i32}; W5 {a} {p: uniq/S5,
// q: uniq/(ref/i32) during a}. b1 and b2 are this round's too (the planning session's E3 bound).
static class R13
{
    const bool Uniq = true, Ref = false;
    public static IEnumerable<Case> All() => [D1(), D2(), D3(), RF1(), RF2(), U1(), U2(), N1(), N2(), A1(), A2(), C1(), Setter("r13s1", false), Setter("r13s2", true), Past("r13b1", false), Past("r13b2", true)];

    // a1, a2: an activation takes the values of the other operands, not their holders (E13 (1); CS_call's live_in(ℓ) rejects a1).
    static Case A(string id, bool used)
    {
        var c = new Case(id, "activation after a shared read through a dead reference: `let r = y@ref; give(y@uniq, r@follow)`" + (used ? "; inspect(r)" : "") + (used ? " (error)" : " (valid; old path valid)"));
        c.N("var y = 1").Def("y");
        c.N("let r = y@ref").Def("r").Borrow("Lr", "y", Ref, "r");
        c.N("$4 = &uniq two-phase y").Def("$4").Borrow("L4", "y", Uniq, "$4", reserve: true);
        c.N("give(move $4, copy (*r))").Input(K.Move, "$4").Input(K.Read, "r.*").Activate("L4");
        if (used) c.N("inspect(r)").A(K.Read, "r");
        return used ? c.Error("give(move $4, copy (*r))") : c.Valid();
    }
    static Case A1() => A("r13a1", false);
    static Case A2() => A("r13a2", true);

    // c1: j = i at a Closure (a capture's own value): today's builder and E13 (2) both build `Closure(move s)`.
    static Case C1()
    {
        var c = new Case("r13c1", "capture Move of a self-ref holder: `s.r = s.n@ref; let c = func [s@move] () => ()` (MIR c = Closure(move s), c dead; old path error)");
        c.N("var z = 0").Def("z"); c.N("var s = S3(r: z@ref, n: 1)").Def("s").Borrow("Lz", "z", Ref, "s.r");
        c.N("s.r = s.n@ref").Assign("s.r").Borrow("Ln", "s.n", Ref, "s.r");
        c.N("c = Closure(move s)").Def("c").Input(K.Move, "s").Flow("s", "c").Operation();
        return c.Error("c = Closure(move s)");
    }

    // d1-d3: an observing destruction of X takes val(X) (SPEC 15.6, 15.6.6).
    static Case D1()
    {
        var c = new Case("r13d1", "self-ref observing drop: `var q = Q2(r: z@uniq); q.r = q.n@uniq` (scope end: drop q writes through q.r); old path: error");
        c.N("var z = 0").Def("z");
        c.N("var q = Q2(r: z@uniq)").Def("q").Borrow("Lz", "z", Uniq, "q.r");
        c.N("q.r = q.n@uniq").Assign("q.r").Borrow("Ln", "q.n", Uniq, "q.r");
        c.N("drop q (scope end)").Drop("q").A(K.Write, "q.r.*");
        return c.Error("drop q (scope end)");
    }
    static Case D2()
    {
        var c = new Case("r13d2", "d1's counterpart: `q.r = y@uniq` (another root), drop q at scope end");
        c.N("var z = 0").Def("z"); c.N("var y = 0").Def("y");
        c.N("var q = Q2(r: z@uniq)").Def("q").Borrow("Lz", "z", Uniq, "q.r");
        c.N("q.r = y@uniq").Assign("q.r").Borrow("Ly", "y", Uniq, "q.r");
        c.N("drop q (scope end)").Drop("q").A(K.Write, "q.r.*");
        return c.Valid();
    }
    // d3 (the review's probe d3; MIR `drop _2; _2 = move _7`): the old value's drop at a whole assignment, q dead in between.
    static Case D3()
    {
        var c = new Case("r13d3", "d1 by reassignment: `q.r = q.n@uniq; q = Q2(r: z2@uniq)` (`drop q; q = move $7`: the old value's drop writes through q.r); old path: valid (wrong)");
        c.N("var z = 0; var z2 = 0").Def("z", "z2");
        c.N("var q = Q2(r: z@uniq)").Def("q").Borrow("Lz", "z", Uniq, "q.r");
        c.N("q.r = q.n@uniq").Assign("q.r").Borrow("Ln", "q.n", Uniq, "q.r");
        c.N("$7 = Q2(r: z2@uniq)").Def("$7").Borrow("L7", "z2", Uniq, "$7");
        c.N("drop q (old value)").Drop("q").A(K.Write, "q.r.*");
        c.N("q = move $7").Assign("q").A(K.Move, "$7").Flow("$7", "q");
        c.N("drop q (scope end)").Drop("q").A(K.Write, "q.r.*");
        return c.Error("drop q (old value)");
    }

    // rf1, rf2: a Ref's borrow of X takes its R2 parents whatever its destination's liveness (supersedes R4-M57's seed).
    static Case RF(string id, bool live)
    {
        var c = new Case(id, "Ref through a self-ref holder: `var s = S7(r: z@uniq, q: w@ref); s.q = s.r@follow@ref; let t = s.r@follow@uniq`" + (live ? "; inspect(t)" : " (t dead)") + "; old path: valid (wrong)");
        c.N("var z = 0").Def("z"); c.N("var w = 0").Def("w");
        c.N("var s = S7(r: z@uniq, q: w@ref)").Def("s").Borrow("Lz", "z", Uniq, "s.r").Borrow("Lw", "w", Ref, "s.q");
        c.N("s.q = s.r@follow@ref").Assign("s.q").Borrow("Lq", "s.r.*", Ref, "s.q");
        c.N("let t = s.r@follow@uniq").Def("t").Borrow("Lt", "s.r.*", Uniq, "t");
        if (live) c.N("inspect(t)").A(K.Read, "t.*");
        return c.Error("let t = s.r@follow@uniq");
    }
    static Case RF1() => RF("r13rf1", false);
    static Case RF2() => RF("r13rf2", true);

    // u1, u2: j = i at a Call and at a single-operand Use (MIR `give2(copy _2.1, const 1)`, `_6 = copy _2.1`).
    static Case U1()
    {
        var c = new Case("r13u1", "Copy operand of a self-ref holder at the Call: `s.r = s.n@uniq; give2(s.n, 1)` (MIR give2(copy s.n, const 1)); old path: valid (wrong)");
        c.N("var z = 0").Def("z"); c.N("var s = S4(r: z@uniq, n: 1)").Def("s").Borrow("Lz", "z", Uniq, "s.r");
        c.N("s.r = s.n@uniq").Assign("s.r").Borrow("Ln", "s.n", Uniq, "s.r");
        c.N("give2(copy s.n, const 1)").Input(K.Read, "s.n");
        return c.Error("give2(copy s.n, const 1)");
    }
    static Case U2()
    {
        var c = new Case("r13u2", "single-operand Copy of a self-ref holder: `s.r = s.n@uniq; let t = s.n` (t dead); old path: valid (wrong)");
        c.N("var z = 0").Def("z"); c.N("var s = S4(r: z@uniq, n: 1)").Def("s").Borrow("Lz", "z", Uniq, "s.r");
        c.N("s.r = s.n@uniq").Assign("s.r").Borrow("Ln", "s.n", Uniq, "s.r");
        c.N("let t = s.n").Def("t").A(K.Read, "s.n").Flow("s.n", "t");
        return c.Error("let t = s.n");
    }

    // n1, n2: named arguments in another order (SPEC 10.1.1). E13 (2): a Place operand evaluated before an operand that precedes it
    // in operand order is secured where it is evaluated. Built: today's builder passes them by slot (`give2(copy x, copy (*r))`).
    static Case N(string id)
    {
        bool n1 = id == "r13n1";
        var c = new Case(id, n1 ? "`let r = x@uniq; give2(b: r@follow, a: x)`: evaluated r.*, then x (valid, old path valid); E13 (2): `$1 = copy (*r); give2(copy x, move $1)`"
            : "`let r = x@uniq; give2(b: x, a: r@follow)`: evaluated x, then r.* (error at x, old path error); E13 (2): `$1 = copy x; give2(copy (*r), move $1)`");
        c.N("var x = 1").Def("x"); c.N("let r = x@uniq").Def("r").Borrow("Lr", "x", Uniq, "r");
        if (n1) { c.N("$1 = copy (*r) (b, secured)").Def("$1").A(K.Read, "r.*").Flow("r.*", "$1"); c.N("give2(copy x, move $1)").Input(K.Read, "x").Input(K.Move, "$1"); c.Valid(); }
        else { c.N("$1 = copy x (b, secured)").Def("$1").A(K.Read, "x").Flow("x", "$1"); c.N("give2(copy (*r), move $1)").Input(K.Read, "r.*").Input(K.Move, "$1"); c.Error("$1 = copy x (b, secured)"); }
        var b = new Case(id, "today's builder, by slot");
        b.N("var x = 1").Def("x"); b.N("let r = x@uniq").Def("r").Borrow("Lr", "x", Uniq, "r");
        if (n1) { b.N("give2(copy x, copy (*r))").Input(K.Read, "x").Input(K.Read, "r.*"); b.Valid(); }
        else { b.N("give2(copy (*r), copy x)").Input(K.Read, "r.*").Input(K.Read, "x"); b.Error("give2(copy (*r), copy x)"); }
        c.Built = b;
        return c;
    }
    static Case N1() => N("r13n1");
    static Case N2() => N("r13n2");

    // b1, b2: a borrow past a shared Deref takes its R2 parents as E3 bounds them (SPEC 15.6.3: it derives only from the
    // reference of its last shared Deref, s.r, and carries its Loans). Reaching s.r reads w.p, but w's value is not used
    // after ℓ in b1, so w's Lq (a uniq borrow of s.r, held in w.q) is not checked; in b2 w is used after ℓ. ParentsEveryLayer
    // takes w's Loans too (layer 0) and rejects b1.
    static Case Past(string id, bool used)
    {
        var c = new Case(id, "Ref past a shared Deref: `var s = S5(r: y@ref); var w = W5(p: s@uniq, q: o@uniq); w.q = w.p@follow.r@uniq; let t = w.p@follow.r@follow@ref; inspect(t)"
            + (used ? "; inspect(w.q)`" : "` (w dead after t)") + "; old path not probed");
        c.N("var y = 1; var o: ref/i32 = y@ref").Def("y", "o").Borrow("Ly0", "y", Ref, "o");
        c.N("var s = S5(r: y@ref)").Def("s").Borrow("Ly", "y", Ref, "s.r");
        c.N("var w = W5(p: s@uniq, q: o@uniq)").Def("w").Borrow("Ls", "s", Uniq, "w.p").Borrow("Lo", "o", Uniq, "w.q");
        c.N("w.q = w.p@follow.r@uniq").Assign("w.q").Borrow("Lq", "w.p.*.r", Uniq, "w.q");
        c.N("let t = w.p@follow.r@follow@ref").Def("t").Borrow("Lt", "w.p.*.r.*", Ref, "t", sharedAt: 2);
        c.N("inspect(t)").Use("t");
        if (!used) return c.Valid();
        c.N("inspect(w.q)").A(K.Read, "w.q");
        return c.Error("let t = w.p@follow.r@follow@ref");
    }

    // s1, s2: a setter's value is evaluated before its receiver (SPEC 13.7.1), so E13 (2) secures a Place value before the
    // receiver's reservation: `$1 = copy (*r).level; $5 = &uniq two-phase g; level.Set(move $5, move $1)`. Today's builder for a
    // Place value is not probed (a Call value is built first: the review's bsetter probe), so there is no Built form.
    static Case Setter(string id, bool used)
    {
        var c = new Case(id, "setter value first: `var g = G(); let r = g@uniq; g.level = r@follow.level`" + (used ? "; inspect(r)" : "") + " (E13 (2): the value secured before the receiver's reservation)");
        c.N("var g = G()").Def("g");
        c.N("let r = g@uniq").Def("r").Borrow("Lr", "g", Uniq, "r");
        c.N("$1 = copy (*r).level (value, secured)").Def("$1").A(K.Read, "r.*.level").Flow("r.*.level", "$1");
        c.N("$5 = &uniq two-phase g (receiver)").Def("$5").Borrow("L5", "g", Uniq, "$5", reserve: true);
        c.N("level.Set(move $5, move $1)").Input(K.Move, "$5").Input(K.Move, "$1").Activate("L5");
        if (!used) return c.Valid();
        c.N("inspect(r)").A(K.Read, "r");
        return c.Error("$5 = &uniq two-phase g (receiver)", "level.Set(move $5, move $1)");
    }
}
