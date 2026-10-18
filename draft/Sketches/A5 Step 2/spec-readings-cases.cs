// Unit 2x-S1 (A5 step 2 plan, PD23): prototype cases for the SPEC readings in spec-readings.md. Not a round of the tracked
// prototype (PD3): it runs on a scratch copy of `draft/Sketches/A5 Step 0/A5RulesPrototype.cs` and changes no rule.
// Run, in an empty folder outside the repository, with both files copied there:
//   sed '/^var verbose = /a if (args.Contains("-s2x1")) { S2x1.Run(); return 0; }' A5RulesPrototype.cs > proto.cs
//   cat spec-readings-cases.cs >> proto.cs
//   dotnet run proto.cs -- -s2x1
// Every case runs under the design (design A with E8's ending, round 13: Mode.A, prefixKill). A case's forms are MIR-fact
// encodings of one program under the representations the reading compares; "SPEC" is the reading's verdict, never a form's.
static class S2x1
{
    const bool Uniq = true;

    sealed record Item(string Group, string Id, string Program, bool Err, string[] At, (string Form, Case Case)[] Forms);

    public static void Run()
    {
        foreach (var group in Items().GroupBy(x => x.Group))
        {
            var forms = group.First().Forms.Select(f => f.Form).ToArray();
            Console.WriteLine($"\n{group.Key}\n| Case | Program | SPEC | {string.Join(" | ", forms)} |\n|{string.Concat(Enumerable.Repeat(" --- |", forms.Length + 3))}");
            foreach (var item in group)
            {
                var spec = item.Err ? "error" + (item.At.Length > 0 ? " at `" + string.Join("`, `", item.At) + "`" : "") : "valid";
                Console.WriteLine($"| {item.Id} | {item.Program} | {spec} | {string.Join(" | ", item.Forms.Select(f => Verdict(item, f.Case)))} |");
            }
        }

        Console.WriteLine("\nError facts (Loan, point, access) under each form:");
        foreach (var item in Items())
        {
            foreach (var (form, c) in item.Forms)
            {
                var a = new Analysis(c, Mode.A, Abl.None, prefixKill: true);
                Console.WriteLine($"{item.Id,-4} {form,-22}: " + (a.Errors.Count == 0 ? "no errors" : string.Join("; ", a.Errors.Select(e => $"{e.L} live at `{a.LabelAt(e.P)}` vs {e.What}"))));
            }
        }
    }

    static string Verdict(Item item, Case c)
    {
        var a = new Analysis(c, Mode.A, Abl.None, prefixKill: true);
        var nodes = a.ErrorNodes().ToList();
        var v = nodes.Count == 0 ? "valid" : "error at `" + string.Join("`, `", nodes) + "`";
        if (nodes.Count > 0 == item.Err)
        {
            return v + (item.Err && item.At.Length > 0 && !nodes.ToHashSet().SetEquals(item.At) ? " (moved)" : string.Empty);
        }

        return v + (item.Err ? " **UNSOUND**" : " (over-strict)");
    }

    static IEnumerable<Item> Items()
    {
        // G147 (8ii): A3 = the implemented facts (a call result takes ret(i, Value) = val ∪ content(val); Φ_E is carried per
        // holder, P1); narrow = a result tied to an argument does not derive from a read through it (no content flow);
        // per part = derivation per Move Path (a local aggregate's parts as separate holders; a call result stays one value,
        // SPEC 15.6.4 "per argument, never per Origin").
        foreach (var (id, kind, err, at) in new[] { ("g1", "field", true, "v@follow = x"), ("g2", "box", true, "v@follow = x"), ("g3", "counter", true, "v@follow = x"),
            ("g4", "direct", false, ""), ("g5", "tupleLocal", false, ""), ("g6", "tupleParam", true, "v@follow = 9") })
        {
            yield return new("G147 (8ii)", id, HolderTitle(kind), err, at.Length > 0 ? [at] : [],
                [("A3", Holder(id, kind, "A3")), ("narrow", Holder(id, kind, "narrow")), ("per part", Holder(id, kind, "perPart"))]);
        }

        // af43dcd8ffce: insertOrReplace's published retention (SPEC 4.9.6, LIBRARY): result ← receiver Content, value Value;
        // store ← value Value.
        yield return new("af43dcd8ffce", "a1", "`run(first, second)`: `entries = [1: first@move]; item ← entries.insertOrReplace(1, second@move); item@follow = 42; require entries.length == 1` (the ledger source)", false, [], [("design", Replace("a1", locals: false, writeX: false))]);
        yield return new("af43dcd8ffce", "a2", "local twin: `entries.insertOrReplace(1, x@uniq); item ← entries.insertOrReplace(1, y@uniq); item@follow = 42; x = 5; require entries.length == 1`", true, ["x = 5"], [("design", Replace("a2", locals: true, writeX: true))]);
        yield return new("af43dcd8ffce", "a3", "local twin without `x = 5`", false, [], [("design", Replace("a3", locals: true, writeX: false))]);

        // 47f0722c876b: A3 = Φ_c only on a descending result (A3 §5; MIR today); carrier = rule 1's reach as a phantom on the
        // carrier S that the result's Type names, excused at a later call through the same callable value and disturbed by a
        // write of it (A3's `disturbed`, SPEC 8.11.3 "replacing v", 8.11.6 "same callable value").
        foreach (var (id, kind, err, at) in new[] { ("c1", "same", false, ""), ("c2", "replaced", true, "let second = f(source)"), ("c3", "other", true, "let second = g(source)") })
        {
            yield return new("47f0722c876b", id, CallableTitle(kind), err, at.Length > 0 ? [at] : [], [("A3", Callable(id, kind, carrier: false)), ("carrier", Callable(id, kind, carrier: true))]);
        }

        // 59e2289ce4f4: today = no desc(c) (MIR at the base); desc = A3's desc(c), Φ_c on the receiver's referent (2f-3's rule I).
        foreach (var (id, kind, err) in new[] { ("h1", "helper", true), ("h4", "other", true), ("h5", "same", false) })
        {
            yield return new("59e2289ce4f4", id, HelperTitle(kind), err, [], [("today", Helper(id, kind, desc: false)), ("desc", Helper(id, kind, desc: true))]);
        }

        // PD16's stored Φ_c: none = the call's store rows carry no phantom (A3 round 9); result id = they carry the result's Φ_c;
        // own id + self = a distinct phantom Φ_c^s, never excused, also through the receiver's `0 Content store 0` row; own id =
        // Φ_c^s through store rows other than the receiver's own referent.
        foreach (var (id, kind, err) in new[] { ("p1", "transfer", false), ("p2", "transferCall", false), ("p3", "ownStore", true), ("p3b", "ownStoreTakeOnly", true), ("p4", "takeLate", false), ("p5", "takeTwo", false) })
        {
            yield return new("PD16 stored Φ_c", id, StoredTitle(kind), err, [],
                [("none", Stored(id, kind, "none")), ("result id", Stored(id, kind, "resultId")), ("own id + self", Stored(id, kind, "ownIdSelf")), ("own id", Stored(id, kind, "ownId"))]);
        }
    }

    static string HolderTitle(string kind) => kind switch
    {
        "field" => "A4-field-ok, uniq case: `cell = Cell<s/T>.init(link: slot@uniq); child = v; cell.link@follow = child@move; _ = slot@follow; v@follow = x`",
        "box" => "A4-box-ok, uniq case: `box = Box<uniq/(s/T)>.init(item: slot@uniq); …; box.item@follow = child@move; …`",
        "counter" => "the same caller facts, `other: uniq/(uniq/T during q)`: `cell = Wrap.make(slot@uniq)`, whose link is `link@follow@follow@uniq`, i.e. `*other` (caller storage)",
        "direct" => "direct twin: `var link = slot@uniq; link@follow = child@move; _ = slot@follow; v@follow = x`",
        "tupleLocal" => "no call: `var t = (local@uniq, w2@move); t.0@follow = child@move; _ = local@follow; v@follow = 9` (w2 a parameter reference)",
        _ => "no call: the same with `t.1@follow = child@move` (writes `*w2`)",
    };

    // value, other: uniq/T in the uniq case (s = uniq); v and slot carry their parameters' entry Loans (Φ_E).
    static Case Holder(string id, string kind, string form)
    {
        var c = new Case(id, HolderTitle(kind));
        bool call = kind is "field" or "box" or "counter";
        if (kind.StartsWith("tuple"))
        {
            c.N("entry: var v = value@move; var local = w@move; w2").Param("Lv", "v").Param("Lw", "local").Param("Lw2", "w2");
            var (t0, t1) = form == "perPart" ? ("t0", "t1") : ("t.0", "t.1"); // per part: the parts as separate holders
            c.N("var t = (local@uniq, w2@move)").Def(t0.Split('.')[0], t1.Split('.')[0]).Borrow("Ll", "local", Uniq, t0).A(K.Move, "w2").Flow("w2", t1);
            c.N("let child = v (a Reborrow)").Def("child").Borrow("Lc", "v.*", Uniq, "child");
            var part = kind == "tupleLocal" ? t0 : t1;
            c.N((kind == "tupleLocal" ? "t.0" : "t.1") + "@follow = child@move").A(K.Move, "child").Through(part + ".*", "child");
            c.N("_ = local@follow").A(K.Read, "local.*");
            c.N("v@follow = 9").A(K.Write, "v.*");
            return kind == "tupleLocal" ? c.Valid() : c.Error("v@follow = 9");
        }

        c.N("entry: var v = value@move; var slot = other@move").Param("Lv", "v").Param("Lo", "slot");
        var holder = kind == "box" ? "box" : "cell";
        if (call)
        {
            c.N("$t = &uniq two-phase slot").Def("$t").Borrow("Ls", "slot", Uniq, "$t", reserve: true);
            var callee = kind switch { "field" => "Cell.init(link: move $t)", "box" => "Box.init(item: move $t)", _ => "Wrap.make(move $t)" };
            var n = c.N($"{holder} = {callee} [0 Value result]").Input(K.Move, "$t").Activate("Ls").Def(holder).Flow("$t", holder);
            if (form != "narrow") n.Flow("$t.*", holder); // ret(0, Value) = val($t) ∪ content(val($t)): slot's Φ_E
        }
        else
        {
            c.N("var link = slot@uniq").Def("link").Borrow("Ls", "slot", Uniq, "link");
        }

        c.N("let child = v (a Reborrow)").Def("child").Borrow("Lc", "v.*", Uniq, "child");
        var place = kind == "box" ? "box.item" : call ? "cell.link" : "link";
        c.N(place + "@follow = child@move").A(K.Move, "child").Through(place + ".*", "child");
        c.N("_ = slot@follow").A(K.Read, "slot.*");
        c.N("v@follow = x").A(K.Write, "v.*");
        return kind == "direct" ? c.Valid() : c.Error("v@follow = x");
    }

    static Case Replace(string id, bool locals, bool writeX)
    {
        var c = new Case(id, "insertOrReplace");
        var (first, second) = locals ? ("$x", "$y") : ("first", "second");
        if (locals)
        {
            c.N("var x = 1; var y = 2; var entries = [:]").Def("x", "y", "entries");
            c.N("$r0 = &uniq two-phase entries; $x = &uniq two-phase x").Def("$r0", "$x").Borrow("Lr0", "entries", Uniq, "$r0", reserve: true).Borrow("Lx", "x", Uniq, "$x", reserve: true);
            c.N("_ = insertOrReplace(move $r0, 1, move $x)").Input(K.Move, "$r0").Input(K.Move, "$x").Activate("Lr0").Activate("Lx").Def("$m0")
                .Flow("$r0.*", "$m0").Flow("$x", "$m0").Through("$r0.*", "$x");
            c.N("$y = &uniq two-phase y").Def("$y").Borrow("Ly", "y", Uniq, "$y", reserve: true);
        }
        else
        {
            c.N("func run(first: uniq/i32, second: uniq/i32)").Param("Lf", "first").Param("Lg", "second");
            c.N("var entries = [1: first@move]").Def("entries").A(K.Move, "first").Flow("first", "entries");
        }

        c.N("$r = &uniq two-phase entries").Def("$r").Borrow("Lr", "entries", Uniq, "$r", reserve: true);
        var call = c.N($"$m = insertOrReplace(move $r, 1, move {second}) [0 Content result, 2 Value result, 2 Value store 0]").Input(K.Move, "$r").Input(K.Move, second);
        if (locals) call.Activate("Ly");
        call.Activate("Lr").Def("$m").Flow("$r.*", "$m").Flow(second, "$m").Through("$r.*", second);
        c.N(".Some(let item)").Def("item").Flow("$m", "item");
        c.N("item@follow = 42").A(K.Write, "item.*");
        if (writeX) c.N("x = 5").Assign("x");
        c.N("require entries.length == 1").A(K.Read, "entries");
        return writeX ? c.Error("x = 5") : c.Valid();
    }

    static string CallableTitle(string kind) => "`twice<S, F>` (F is Callable<uniq, (uniq/S) -> S.Item> with preserves results): `var f = one@move; first = f(source); "
        + kind switch { "same" => "second = f(source)` (936f8571825e)", "replaced" => "f = two@move; second = f(source)` (47f0722c876b)", _ => "var g = two@move; second = g(source)`" };

    static Case Callable(string id, string kind, bool carrier)
    {
        var c = new Case(id, CallableTitle(kind));
        c.N("entry: source; var f = one@move").Param("Lsrc", "source").Def("f", "two");
        c.N("$f1 = &uniq two-phase f; $s1 = &uniq two-phase source.*").Def("$f1", "$s1").Borrow("Lf1", "f", Uniq, "$f1", reserve: true).Borrow("Ls1", "source.*", Uniq, "$s1", reserve: true);
        var first = c.N("let first = f(source)").Input(K.Move, "$f1").Input(K.Move, "$s1").Activate("Lf1").Activate("Ls1").Def("first");
        if (carrier) first.Phantom("Phi1", "S", "first", "Lf1"); // rule 1: Loans S's Origins may denote, kept by first
        var callee = "f";
        if (kind == "replaced")
        {
            var w = c.N("f = two@move").Assign("f").A(K.Move, "two").Flow("two", "f");
            if (carrier) w.Disturb("Phi1"); // another callable value from here on
        }
        else if (kind == "other")
        {
            c.N("var g = two@move").Def("g").A(K.Move, "two").Flow("two", "g");
            callee = "g";
        }

        c.N($"$f2 = &uniq two-phase {callee}; $s2 = &uniq two-phase source.*").Def("$f2", "$s2").Borrow("Lf2", callee, Uniq, "$f2", reserve: true).Borrow("Ls2", "source.*", Uniq, "$s2", reserve: true);
        var second = c.N($"let second = {callee}(source)").Input(K.Move, "$f2").Input(K.Move, "$s2").Activate("Lf2").Activate("Ls2").Def("second");
        if (carrier)
        {
            second.A(K.UBorrow, "S"); // rule 1 through the exclusive input `uniq/S`
            if (callee == "f") second.Excuse("Phi1"); // SPEC 8.11.6: the same callable value (a disturbed Φ_c is not excused)
        }

        c.N("return (first@move, second@move)").Flow("first", "$ret").Flow("second", "$ret");
        return kind == "same" ? c.Valid() : c.Error($"let second = {callee}(source)");
    }

    static string HelperTitle(string kind) => "`through<S: StableSource>`: `first = source.take(); " + kind switch
    {
        "helper" => "second = Helpers.again(source)` (59e2289ce4f4; `again<T: Source>` calls `value.take()`; the same caller facts for a helper calling `value.reset()` or one with `T is StableSource`)",
        "other" => "source.reset(); second = source.take()` (another requirement)",
        _ => "second = source.take()` (SPEC 8.11.1's takeTwo)",
    };

    static Case Helper(string id, string kind, bool desc)
    {
        var c = new Case(id, HelperTitle(kind));
        c.N("entry: source").Param("Lsrc", "source");
        c.N("$r1 = &uniq two-phase source.* (receiver)").Def("$r1").Borrow("Lr1", "source.*", Uniq, "$r1", reserve: true);
        var first = c.N("let first = source.take()").Input(K.Move, "$r1").Activate("Lr1").Def("first");
        if (desc) first.Phantom("Phi1", "source.*", "first", "Lr1");
        if (kind == "helper")
        {
            c.N("$a = &uniq two-phase source.* (argument of again)").Def("$a").Borrow("La", "source.*", Uniq, "$a", reserve: true);
            c.N("let second = Helpers.again(move $a) [0 Content result]").Input(K.Move, "$a").Activate("La").Def("second").Flow("$a.*", "second");
        }
        else
        {
            if (kind == "other")
            {
                c.N("$q = &uniq two-phase source.* (receiver of reset)").Def("$q").Borrow("Lq", "source.*", Uniq, "$q", reserve: true);
                c.N("source.reset()").Input(K.Move, "$q").Activate("Lq");
            }

            c.N("$r2 = &uniq two-phase source.* (receiver)").Def("$r2").Borrow("Lr2", "source.*", Uniq, "$r2", reserve: true);
            var second = c.N("let second = source.take()").Input(K.Move, "$r2").Activate("Lr2").Def("second");
            if (desc) second.Phantom("Phi2", "source.*", "second", "Lr2").Excuse("Phi1");
        }

        c.N("return (first@move, second@move)").Flow("first", "$ret").Flow("second", "$ret");
        return kind == "same" ? c.Valid() : c.Error();
    }

    static string StoredTitle(string kind) => kind switch
    {
        "transfer" => "`S is StableSource`: `first = source.take(); held = first@move; second = source.take(); return (held, second)`",
        "transferCall" => "the same, the transfer through another call's store row: `Stash.put(held@uniq, first@move)` [1 Value store 0]",
        "ownStore" => "`first = source.takeInto(spare1@uniq)` [0 Content store 1]; `_ = first@move; second = source.takeInto(spare2@uniq); return (spare1, second)`",
        "ownStoreTakeOnly" => "the same with `S is Source` (no bound)",
        "takeLate" => "`S is Source`: `first = source.take(); _ = first@move; second = source.take()` (EffectBoundCallerTest's takeLate, valid)",
        _ => "`S is StableSource`: `first = source.take(); second = source.take()` (SPEC 8.11.1's takeTwo)",
    };

    // Every take/takeInto call has the receiver's `0 Content store 0` row (RetentionTest.ExclusiveReceiversAreStores): its
    // store into the receiver's referent reaches E (source is a parameter).
    static Case Stored(string id, string kind, string form)
    {
        var c = new Case(id, StoredTitle(kind));
        bool into = kind.StartsWith("ownStore"), bounded = kind is not ("ownStoreTakeOnly" or "takeLate");
        c.N("entry: source; var held = .None; var spare1 = .None; var spare2 = .None").Param("Lsrc", "source").Def("held", "spare1", "spare2");
        void Call(int k, string result, bool excuse)
        {
            var recv = $"$r{k}"; var loan = $"Lr{k}";
            var prep = c.N($"{recv} = &uniq two-phase source.*" + (into ? $"; $p{k} = &uniq two-phase spare{k}" : "")).Def(recv).Borrow(loan, "source.*", Uniq, recv, reserve: true);
            if (into) prep.Def($"$p{k}").Borrow($"Lp{k}", $"spare{k}", Uniq, $"$p{k}", reserve: true);
            var n = c.N($"let {result} = source.{(into ? $"takeInto(spare{k}@uniq)" : "take()")}").Input(K.Move, recv).Activate(loan).Def(result).Phantom($"Phi{k}", "source.*", result, loan);
            if (into) n.Input(K.Move, $"$p{k}").Activate($"Lp{k}").Flow(recv + ".*", $"spare{k}");
            var phantom = form == "resultId" ? $"Phi{k}" : $"Phi{k}s";
            if (form != "none" && into) n.Phantom(phantom, "source.*", $"spare{k}", loan);            // the call's own store into spare_k
            if (form is "resultId" or "ownIdSelf") n.Phantom(phantom, "source.*", "^source", loan);   // the receiver's self row (into E)
            if (excuse) n.Excuse("Phi1");
        }

        Call(1, "first", excuse: false);
        if (kind == "transfer") c.N("held = first@move").Assign("held").A(K.Move, "first").Flow("first", "held");
        if (kind == "transferCall")
        {
            c.N("$h = &uniq two-phase held").Def("$h").Borrow("Lh", "held", Uniq, "$h", reserve: true);
            c.N("Stash.put(move $h, move first) [1 Value store 0]").Input(K.Move, "$h").Input(K.Move, "first").Activate("Lh").Through("$h.*", "first");
        }

        if (into || kind == "takeLate") c.N("_ = first@move").A(K.Move, "first");
        Call(2, "second", excuse: bounded);
        var ret = c.N("return").Flow("second", "$ret");
        if (kind.StartsWith("transfer")) ret.Flow("held", "$ret");
        else if (into) ret.Flow("spare1", "$ret");
        else if (kind == "takeTwo") ret.Flow("first", "$ret");
        return kind.StartsWith("ownStore") ? c.Error() : c.Valid();
    }
}
