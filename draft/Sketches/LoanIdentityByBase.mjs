// Prototype for draft/Proposals/2026-10-16 Loan Identity by Base.md. Not part of the compiler.
//
// A small evaluator of the proposed Loan rules on a toy language of exclusive (uniq) and shared (ref) reference holders,
// checked against an exact reference semantics. It tests the hypothesis H1 that ending a child Loan when its base is
// destroyed in place (`=`, a `replace` target or its storage end) is sound, given that the base and its ancestors can be
// neither Moved nor exclusively borrowed (`exchange` and `swap` targets included) while the child is live: after the
// kill, nothing outside the child's descendants holds the base's old reference, and every live exclusive value that
// reaches the child's Place descends from the child or is an ancestor that the parent Loans suspend.
//
//   node draft/Sketches/LoanIdentityByBase.mjs [--random N] [--seed S] [--bound K] [--cap T]
//
// Static rules (`base`, the proposal; a Loan is its Borrow site, so the domain is finite). Every toy Loan follows one
// reference, so its base, the Place before its last borrow dereference, is a holder and the kill never has to cross one.
//   base(L,B)      :- borrow_through(L,B).          // B: the holder whose exclusive reference the Borrow follows
//   ended(L,P)     :- base(L,B), prefix(G,B), destroyed_in_place(G,P).   // `=`, a `replace` target, a storage end
//   carries(X,L,Q) :- carries(X,L,P), edge(P,Q), !replaced(X,P), !ended(L,P).   // also for the values flowing at P
//   error(P)       :- access(P,B,Pl,K), live(L,P), base(L,B), overlaps(Pl,place(L)), conflicts(K,mode(L)).
//   error(P)       :- (moves(P,G) ; lends_exclusively(P,G)), prefix(G,B), live(L,P), base(L,B).   // not a `replace` target
// live(L,P) goes by holder, never by Loan id: the Loans carried on entry to P by the holders used after P (other than
// those P overwrites) and by the values P stores into them, so an earlier execution of P's own site still conflicts.
// Two comparison rules: `brief` ends a child on every complete replacement of its base, `exchange` and `swap` included,
// and forbids only Moving the base itself; `site-authority` reconstructs one Loan per site with self-authority (no base,
// no ending, and an access never conflicts with a Loan its own holder may carry).
// Reference semantics (ground truth): every Borrow creates a fresh tag below the tag of the reference it was formed
// through, at an exact absolute Place. An access through a reference with tag t conflicts with every tag u that is live
// (a value whose tag descends from u is used later) and is not t or an ancestor of t, when the Places overlap and the
// modes conflict. This is the value-identity reading of the current SPEC 15.6.3, with unbounded Places.
//
// Toy language, one statement per line, blocks by indentation. Holders are locals; `s/a` is Field `a` of local `s`, and
// a statement may name `s` to act on all of its Fields.
//   params a:uniq b:ref          locals x:uniq y:ref s/a:uniq
//   x = y.f.g@uniq | x = y.f@ref    Borrow through y (a child Loan when y is uniq; a shared Borrow through ref adds none)
//   x = y                           bare acquisition: Reborrow of a uniq y in x's mode, Copy of a ref y
//   x = y@move | x = fresh          Move (also of a whole `s`); a Borrow of a fresh local root
//   o = exchange x with y           exchange(x@uniq, with: y@move): o receives x's old value
//   swap x y | replace x with y     swap(x@uniq, y@uniq); replace(x@uniq, with: y@move), destroying x's old value
//   y.f = _ | read y.f | use y      write through y (`y = _` writes its whole referent); read through y; use of y
//   kill y                          storage end of y (or of every Field of y)
//   if / else / while               nondeterministic branch and loop

const args = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, all) => {
  if (a.startsWith('--')) acc.push([a.slice(2), all[i + 1]]);
  return acc;
}, []));
const RANDOM = Number(args.random ?? 20000);
const SEED = Number(args.seed ?? 20261016);
const BOUND = Number(args.bound ?? 3);
const CAP = Number(args.cap ?? 4096);

// ---------------------------------------------------------------------------------------------------------------------
// Programs

let siteCounter = 0;
const NAME = '[A-Za-z]\\w*(?:/\\w+)*';

function parse(text) {
  const lines = text.split('\n').map(l => l.replace(/\s+$/, '')).filter(l => l.trim() && !l.trim().startsWith('//'));
  const kinds = {};
  const params = [];
  let i = 0;
  for (; i < lines.length && /^(params|locals)\b/.test(lines[i]); i++) {
    const [head, ...decls] = lines[i].trim().split(/\s+/);
    for (const d of decls) {
      const [name, kind] = d.split(':');
      kinds[name] = kind;
      if (head === 'params') params.push(name);
    }
  }
  const prog = { kinds, params, body: null };
  const indentOf = l => l.length - l.trimStart().length;
  function block(indent) {
    const out = [];
    while (i < lines.length && indentOf(lines[i]) === indent) {
      const t = lines[i].trim();
      i++;
      if (t === 'if') {
        const s = { op: 'if', then: block(indentOf(lines[i])), else: [] };
        if (i < lines.length && indentOf(lines[i]) === indent && lines[i].trim() === 'else') {
          i++;
          s.else = block(indentOf(lines[i]));
        }
        out.push(s);
      } else if (t === 'while') out.push({ op: 'while', body: block(indentOf(lines[i])) });
      else out.push(simple(t, prog));
    }
    return out;
  }
  prog.body = block(indentOf(lines[i]));
  return prog;
}

function simple(t, prog) {
  const path = p => (p ? p.split('.').filter(Boolean) : []);
  const re = s => new RegExp(`^${s.replace(/N/g, `(${NAME})`)}$`);
  let m;
  if ((m = t.match(re('N = fresh')))) return { op: 'fresh', dst: m[1] };
  if ((m = t.match(re('N = N@move')))) return { op: 'move', dst: m[1], src: m[2] };
  if ((m = t.match(re('N = exchange N with N')))) return { op: 'exchange', dst: m[1], target: m[2], src: m[3] };
  if ((m = t.match(re('swap N N')))) return { op: 'swap', target: m[1], other: m[2] };
  if ((m = t.match(re('replace N with N')))) return { op: 'replace', target: m[1], src: m[2] };
  if ((m = t.match(re('N = N((?:\\.\\w+)*)@(uniq|ref)'))))
    return { op: 'borrow', site: `s${++siteCounter}`, dst: m[1], src: m[2], path: path(m[3]), mode: m[4] };
  if ((m = t.match(re('N = N')))) {
    if (prog.kinds[m[2]] === 'ref') return { op: 'copy', dst: m[1], src: m[2] };
    return { op: 'borrow', site: `s${++siteCounter}`, dst: m[1], src: m[2], path: [], mode: prog.kinds[m[1]] };
  }
  if ((m = t.match(re('N((?:\\.\\w+)*) = _')))) return { op: 'write', holder: m[1], path: path(m[2]) };
  if ((m = t.match(re('read N((?:\\.\\w+)*)')))) return { op: 'read', holder: m[1], path: path(m[2]) };
  if ((m = t.match(re('use N')))) return { op: 'use', holder: m[1], path: [] };
  if ((m = t.match(re('kill N')))) return { op: 'kill', holder: m[1] };
  throw new Error(`cannot parse: ${t}`);
}

function show(s) {
  const p = s.path?.length ? '.' + s.path.join('.') : '';
  switch (s.op) {
    case 'borrow': return `${s.dst} = ${s.src}${p}@${s.mode}`;
    case 'move': return `${s.dst} = ${s.src}@move`;
    case 'copy': return `${s.dst} = ${s.src}`;
    case 'fresh': return `${s.dst} = fresh`;
    case 'exchange': return `${s.dst} = exchange ${s.target} with ${s.src}`;
    case 'swap': return `swap ${s.target} ${s.other}`;
    case 'replace': return `replace ${s.target} with ${s.src}`;
    case 'write': return `${s.holder}${p} = _`;
    case 'read': return `read ${s.holder}${p}`;
    case 'use': return `use ${s.holder}`;
    case 'kill': return `kill ${s.holder}`;
    default: return s.op;
  }
}

function print(prog) {
  const out = [];
  const ps = prog.params.map(p => `${p}:${prog.kinds[p]}`).join(' ');
  const ls = Object.keys(prog.kinds).filter(h => !prog.params.includes(h)).map(h => `${h}:${prog.kinds[h]}`).join(' ');
  out.push(`params ${ps}`, `locals ${ls}`);
  (function walk(stmts, ind) {
    for (const s of stmts) {
      if (s.op === 'if') {
        out.push(`${ind}if`);
        walk(s.then, ind + '  ');
        if (s.else.length) { out.push(`${ind}else`); walk(s.else, ind + '  '); }
      } else if (s.op === 'while') { out.push(`${ind}while`); walk(s.body, ind + '  '); }
      else out.push(ind + show(s));
    }
  })(prog.body, '');
  return out.join('\n');
}

// The holders a name stands for: itself, or every Field of a struct local; Fields pair up by their suffix.
function fields(prog, name) {
  if (prog.kinds[name]) return [name];
  return Object.keys(prog.kinds).filter(h => h.startsWith(name + '/')).sort();
}
function pairs(prog, a, b) { return fields(prog, a).map(f => [f, b + f.slice(a.length)]); }

// One plan per operation, shared by the static rules and the reference semantics:
//   uses / defs         holders whose values are read / overwritten or cleared (liveness and initialization)
//   flows [dst, src]    dst receives src's pre-state value ('fresh' for a fresh root); a borrow also forms a child
//   destroyed           holders whose old value is destroyed in place (`=`, `replace`, storage end): the kills
//   cleared             holders left Uninitialized
//   transferred, lent   holders whose value leaves without being destroyed; holders lent exclusively as slots
//   access              {holder, path, kind}: an access through the reference in holder
const FRESH = Symbol('fresh');
function plan(prog, s) {
  const p = { uses: [], defs: [], flows: [], destroyed: [], cleared: [], transferred: [], lent: [], access: null, borrow: null };
  switch (s.op) {
    case 'borrow':
      Object.assign(p, { uses: [s.src], defs: [s.dst], flows: [[s.dst, s.src]], destroyed: [s.dst], borrow: s });
      p.access = { holder: s.src, path: s.path, kind: s.mode === 'uniq' ? 'write' : 'read' };
      break;
    case 'copy': Object.assign(p, { uses: [s.src], defs: [s.dst], flows: [[s.dst, s.src]], destroyed: [s.dst] }); break;
    case 'fresh': Object.assign(p, { defs: [s.dst], flows: [[s.dst, FRESH]], destroyed: [s.dst] }); break;
    case 'move':
      for (const [src, dst] of pairs(prog, s.src, s.dst)) {
        p.uses.push(src); p.defs.push(dst, src); p.flows.push([dst, src]);
        p.destroyed.push(dst); p.cleared.push(src); p.transferred.push(src);
      }
      break;
    case 'exchange':
      for (const [target, dst] of pairs(prog, s.target, s.dst)) {
        const src = s.src + target.slice(s.target.length);
        p.uses.push(target, src); p.defs.push(dst, target, src);
        p.flows.push([dst, target], [target, src]);
        p.destroyed.push(dst); p.cleared.push(src); p.transferred.push(target, src); p.lent.push(target);
      }
      break;
    case 'swap':
      for (const [target, other] of pairs(prog, s.target, s.other)) {
        p.uses.push(target, other); p.defs.push(target, other);
        p.flows.push([target, other], [other, target]);
        p.transferred.push(target, other); p.lent.push(target, other);
      }
      break;
    case 'replace':
      // The target is destroyed in place, so it is exempt from the exclusive-Borrow ban and ends children like `=`
      // (owner decision 2026-10-17, A5 design A3 §9 item 4).
      for (const [target, src] of pairs(prog, s.target, s.src)) {
        p.uses.push(target, src); p.defs.push(target, src); p.flows.push([target, src]);
        p.destroyed.push(target); p.cleared.push(src); p.transferred.push(src);
      }
      break;
    case 'kill':
      for (const f of fields(prog, s.holder)) { p.defs.push(f); p.destroyed.push(f); p.cleared.push(f); }
      break;
    case 'write': p.uses = [s.holder]; p.access = { holder: s.holder, path: s.path, kind: 'write' }; break;
    case 'read': case 'use': p.uses = [s.holder]; p.access = { holder: s.holder, path: s.path, kind: 'read' }; break;
  }
  return p;
}

const conflicts = (kind, mode) => kind === 'write' || mode === 'uniq';
const prefix = (a, b) => a.length <= b.length && a.every((x, i) => x === b[i]);
const overlap = (a, b) => prefix(a, b) || prefix(b, a);

// ---------------------------------------------------------------------------------------------------------------------
// Control flow

function buildCfg(prog) {
  const nodes = [];
  const node = stmt => { const n = { id: nodes.length, stmt, plan: stmt && plan(prog, stmt), succ: [], pred: [] }; nodes.push(n); return n; };
  const link = (from, to) => { for (const f of from) if (!f.succ.includes(to)) { f.succ.push(to); to.pred.push(f); } };
  const entry = node(null);
  function block(stmts, preds) {
    let cur = preds;
    for (const s of stmts) {
      if (s.op === 'if') {
        const join = node(null);
        link(block(s.then, cur), join);
        link(block(s.else, cur), join);
        cur = [join];
      } else if (s.op === 'while') {
        const head = node(null);
        link(cur, head);
        link(block(s.body, [head]), head);
        cur = [head];
      } else {
        const n = node(s);
        link(cur, n);
        cur = [n];
      }
    }
    return cur;
  }
  const exit = node(null);
  link(block(prog.body, [entry]), exit);
  return { nodes, entry, exit };
}

// ---------------------------------------------------------------------------------------------------------------------
// Static analysis. rule = 'base' (the proposal), 'brief' (ending on every complete replacement) or 'site-authority'.

function analyze(prog, rule) {
  const { nodes, entry } = buildCfg(prog);
  const holders = Object.keys(prog.kinds);
  const loans = new Map();
  for (const n of nodes) {
    const s = n.stmt;
    if (s?.op === 'borrow' && prog.kinds[s.src] === 'uniq') loans.set(s.site, { site: s.site, base: s.src, path: s.path, mode: s.mode, text: show(s) });
  }

  // Holder liveness: backward may-analysis.
  const liveIn = nodes.map(() => new Set());
  const liveOut = nodes.map(() => new Set());
  for (let changed = true; changed;) {
    changed = false;
    for (let k = nodes.length - 1; k >= 0; k--) {
      const n = nodes[k];
      const out = new Set();
      for (const s of n.succ) for (const h of liveIn[s.id]) out.add(h);
      const inn = new Set(out);
      if (n.plan) { for (const d of n.plan.defs) inn.delete(d); for (const u of n.plan.uses) inn.add(u); }
      if (out.size !== liveOut[k].size || inn.size !== liveIn[k].size) changed = true;
      liveOut[k] = out;
      liveIn[k] = inn;
    }
  }

  // Definite initialization: forward must-analysis.
  const initIn = nodes.map(() => null);
  initIn[entry.id] = new Set(prog.params);
  const initOut = n => {
    const s = new Set(initIn[n.id]);
    if (n.plan) { for (const c of n.plan.cleared) s.delete(c); for (const [d] of n.plan.flows) s.add(d); }
    return s;
  };
  for (let changed = true; changed;) {
    changed = false;
    for (const n of nodes) {
      if (n === entry || !n.pred.length) continue;
      let acc = null;
      for (const p of n.pred) {
        if (!initIn[p.id]) continue;
        const o = initOut(p);
        acc = acc ? new Set([...acc].filter(h => o.has(h))) : o;
      }
      if (acc && (!initIn[n.id] || acc.size !== initIn[n.id].size)) { initIn[n.id] = acc; changed = true; }
    }
  }

  // The kills: `base` ends a child where its base's old value is destroyed in place; `brief` wherever the base's value is
  // completely replaced, transferred or not.
  const kills = p => (rule === 'base' ? p.destroyed : rule === 'brief' ? [...new Set([...p.destroyed, ...p.flows.map(([d]) => d)])] : []);
  // The pre-state values that an operation's destinations receive (a borrow's own new Loan excluded).
  const incoming = (p, C) => p.flows.map(([d, src]) => [d, src === FRESH ? new Set(rule === 'site-authority' ? [`root:${d}`] : []) : new Set(C.get(src))]);

  // Carried Loans: forward may-analysis (union at joins, least fixed point over loops).
  const carriesIn = nodes.map(() => null);
  carriesIn[entry.id] = new Map(holders.map(h => [h, new Set()]));
  if (rule === 'site-authority') for (const p of prog.params) carriesIn[entry.id].get(p).add(`root:${p}`);
  const transfer = (p, C) => {
    const out = new Map([...C].map(([h, set]) => [h, new Set(set)]));
    if (!p) return out;
    const values = incoming(p, C);
    if (p.borrow && loans.has(p.borrow.site)) values[0][1].add(p.borrow.site);
    for (const c of p.cleared) out.set(c, new Set());
    for (const k of kills(p)) {
      const ended = [...loans.values()].filter(l => l.base === k).map(l => l.site);
      for (const set of [...out.values(), ...values.map(([, v]) => v)]) for (const l of ended) set.delete(l);
    }
    for (const [d, v] of values) out.set(d, v);
    return out;
  };
  const work = [entry];
  while (work.length) {
    const n = work.shift();
    const out = transfer(n.plan, carriesIn[n.id]);
    for (const s of n.succ) {
      let grew = false;
      if (!carriesIn[s.id]) { carriesIn[s.id] = new Map([...out].map(([h, set]) => [h, new Set(set)])); grew = true; }
      else for (const [h, set] of out) for (const l of set) if (!carriesIn[s.id].get(h).has(l)) { carriesIn[s.id].get(h).add(l); grew = true; }
      if (grew && !work.includes(s)) work.push(s);
    }
  }

  // site-authority only: the Loans a site's parent may carry.
  const ancestry = new Map();
  for (const n of nodes) {
    const s = n.stmt;
    if (carriesIn[n.id] && s?.op === 'borrow' && loans.has(s.site)) {
      const a = ancestry.get(s.site) ?? new Set();
      for (const l of carriesIn[n.id].get(s.src)) a.add(l);
      ancestry.set(s.site, a);
    }
  }

  const errors = [];
  const invariant = [];
  let endedLive = 0;
  for (const n of nodes) {
    const s = n.stmt;
    const p = n.plan;
    const C = carriesIn[n.id];
    if (!s || !C) continue;
    for (const u of p.uses) if (!initIn[n.id]?.has(u)) errors.push({ kind: 'uninit', at: show(s) });
    // Loans live after this operation: carried by holders used later, except values it overwrites; a destination's new
    // value carries its source's Loans (a borrow's own new Loan never conflicts with its access).
    const live = new Set();
    const D = new Set(p.defs);
    for (const z of liveOut[n.id]) if (!D.has(z)) for (const l of C.get(z)) live.add(l);
    for (const [d, v] of incoming(p, C)) if (liveOut[n.id].has(d)) for (const l of v) live.add(l);
    const a = p.access;
    if (a) {
      for (const id of live) {
        const l = loans.get(id);
        if (!l || !overlap(l.path, a.path) || !conflicts(a.kind, l.mode)) continue;
        if (rule !== 'site-authority' && l.base === a.holder) errors.push({ kind: 'conflict', at: show(s), loan: l.text });
        if (rule === 'site-authority') {
          const authority = C.get(a.holder);
          if (!authority.has(id) && [...(ancestry.get(id) ?? [])].some(x => authority.has(x)))
            errors.push({ kind: 'conflict', at: show(s), loan: l.text });
        }
      }
    }
    if (rule === 'base') {
      for (const id of live) {
        const l = loans.get(id);
        if (!l) continue;
        if (p.lent.includes(l.base)) errors.push({ kind: 'lent-base', at: show(s), loan: l.text });
        else if (p.transferred.includes(l.base)) errors.push({ kind: 'moved-base', at: show(s), loan: l.text });
      }
    }
    if (rule === 'brief' && s.op === 'move' && prog.kinds[s.src])
      for (const id of live) if (loans.get(id)?.base === s.src) errors.push({ kind: 'moved-base', at: show(s), loan: loans.get(id).text });
    if (rule === 'base') {
      // Invariant of the finite domain: no holder used later carries a Loan whose base is itself.
      const out = transfer(p, C);
      for (const [h, set] of out) if (liveOut[n.id].has(h)) for (const id of set) if (loans.get(id)?.base === h) invariant.push(`${h} carries ${loans.get(id).text} after ${show(s)}`);
      // Where ending decides: a kill ends a Loan that a value used later still carried.
      for (const k of kills(p)) for (const id of live) if (loans.get(id)?.base === k) endedLive++;
    }
  }
  return { errors, invariant, endedLive, loans: loans.size };
}

// ---------------------------------------------------------------------------------------------------------------------
// Reference semantics over concrete paths (loops unrolled up to BOUND times).

function traces(stmts, bound = BOUND) {
  let truncated = false;
  const product = (a, b) => {
    const out = [];
    for (const x of a) for (const y of b) { if (out.length >= CAP) { truncated = true; return out; } out.push([...x, ...y]); }
    return out;
  };
  const expand = list => {
    let acc = [[]];
    for (const s of list) {
      let opts;
      if (s.op === 'if') opts = [...expand(s.then), ...expand(s.else)];
      else if (s.op === 'while') {
        const body = expand(s.body);
        opts = [[]];
        let layer = [[]];
        for (let k = 1; k <= bound; k++) { layer = product(layer, body); opts.push(...layer); }
      } else opts = [[s]];
      acc = product(acc, opts);
    }
    return acc;
  };
  const all = expand(stmts);
  return { all, truncated };
}

function isAncestorOrSelf(anc, t) { for (; t; t = t.parent) if (t === anc) return true; return false; }

function run(prog, trace) {
  const tags = [];
  const tag = (parent, place, mode, via) => { const t = { id: tags.length, parent, place, mode, via }; tags.push(t); return t; };
  const value = new Map(Object.keys(prog.kinds).map(h => [h, null]));
  let roots = 0;
  for (const p of prog.params) { const place = [`R${roots++}`]; value.set(p, { place, tag: tag(null, place, prog.kinds[p], null) }); }
  const plans = trace.map(s => plan(prog, s));
  const result = { violation: null, checks: 0, failures: [] };
  for (let i = 0; i < trace.length; i++) {
    const s = trace[i];
    const p = plans[i];
    for (const u of p.uses) if (!value.get(u)) { result.violation = { kind: 'uninit', at: show(s), step: i }; return result; }
    const usedLater = z => {
      for (let j = i + 1; j < trace.length; j++) { if (plans[j].uses.includes(z)) return true; if (plans[j].defs.includes(z)) return false; }
      return false;
    };
    const chain = (set, t) => { for (; t; t = t.parent) set.add(t); };
    const D = new Set(p.defs);
    const live = new Set();
    for (const [z, v] of value) if (v && !D.has(z) && usedLater(z)) chain(live, v.tag);
    for (const [d, src] of p.flows) if (src !== FRESH && usedLater(d)) chain(live, value.get(src).tag);
    const a = p.access;
    if (a) {
      const v = value.get(a.holder);
      const place = [...v.place, ...a.path];
      for (const u of live)
        if (!isAncestorOrSelf(u, v.tag) && overlap(u.place, place) && conflicts(a.kind, u.mode)) {
          result.violation = { kind: 'conflict', at: show(s), step: i };
          return result;
        }
    }
    const old = new Map(p.destroyed.map(h => [h, value.get(h)]));
    const incoming = p.flows.map(([d, src]) => {
      if (src === FRESH) { const place = [`F${roots++}`]; return [d, { place, tag: tag(null, place, prog.kinds[d], null) }]; }
      const v = value.get(src);
      if (p.borrow) {
        // Only a Borrow through an exclusive holder forms a child with a base; a shared one is a Copy of authority.
        const place = [...v.place, ...p.borrow.path];
        return [d, { place, tag: tag(v.tag, place, p.borrow.mode, prog.kinds[src] === 'uniq' ? src : null) }];
      }
      return [d, v];
    });
    for (const c of p.cleared) value.set(c, null);
    for (const [d, v] of incoming) value.set(d, v);
    // The hypothesis H1, checked where the proposed rule ends children: after a uniq base's old value is destroyed in
    // place, no holder holds that old reference, and every live exclusive value that reaches a live exclusive child
    // formed through it descends from the child or is an ancestor of the old reference, which the parent Loans suspend.
    // (Shared children may overlap one another, so shared values are left to the differential test.)
    for (const [h, before] of old) {
      if (!before || prog.kinds[h] !== 'uniq') continue;
      const after = new Set();
      for (const [z, v] of value) if (v && usedLater(z)) chain(after, v.tag);
      result.checks++;
      for (const [z, v] of value) if (v && v.tag === before.tag) result.failures.push(`${show(s)}: ${z} still holds ${h}'s old reference`);
      for (const child of after) {
        if (child.parent !== before.tag || child.via !== h || child.mode !== 'uniq') continue;
        for (const [z, v] of value) {
          if (!v || v.tag.mode !== 'uniq' || !usedLater(z) || !overlap(v.place, child.place)) continue;
          result.checks++;
          if (!isAncestorOrSelf(child, v.tag) && !(v.tag !== before.tag && isAncestorOrSelf(v.tag, before.tag)))
            result.failures.push(`${show(s)}: ${z} reaches ${child.place.join('.')} outside the child's descendants`);
        }
      }
    }
  }
  return result;
}

function dynamic(prog, bound = BOUND) {
  const { all, truncated } = traces(prog.body, bound);
  let checks = 0;
  const failures = [];
  for (const t of all) {
    const r = run(prog, t);
    if (r.violation) return { clean: false, violation: r.violation, trace: t, traces: all.length, truncated, checks, failures };
    checks += r.checks;
    failures.push(...r.failures);
  }
  return { clean: true, traces: all.length, truncated, checks, failures };
}

// ---------------------------------------------------------------------------------------------------------------------
// Named cases from the proposal.

const cases = [
  ['deepest: valid loop', `
params root:uniq
locals cursor:uniq next:uniq
cursor = root
read cursor.kids
while
  next = cursor.kids.0@uniq
  cursor = next
  kill next
  read cursor.kids
use cursor`],
  ['deepest: clear() before cursor = next (invalid)', `
params root:uniq
locals cursor:uniq next:uniq
cursor = root
read cursor.kids
while
  next = cursor.kids.0@uniq
  cursor.kids = _
  cursor = next
  kill next
  read cursor.kids
use cursor`],
  ['deepest: cursor = next@move', `
params root:uniq
locals cursor:uniq next:uniq
cursor = root
read cursor.kids
while
  next = cursor.kids.0@uniq
  cursor = next@move
  read cursor.kids
use cursor`],
  ['moved base, child live (lost)', `
params p:uniq
locals c:uniq q:uniq
c = p.x@uniq
q = p@move
use c
use q`],
  ['moved base, then written through (invalid)', `
params p:uniq
locals c:uniq q:uniq
c = p.x@uniq
q = p@move
q.x = _
use c`],
  ['exchange of the base, old value written (invalid)', `
params x:uniq y:uniq
locals r:uniq c:uniq old:uniq
r = x
c = r
old = exchange r with y
old = _
c = _`],
  ['swap of the base (invalid)', `
params x:uniq y:uniq
locals r:uniq c:uniq
r = x
c = r
swap r y
y = _
c = _`],
  ['Move of the base\'s struct (invalid)', `
params x:uniq
locals s/a:uniq t/a:uniq c:uniq
s/a = x
c = s/a
t = s@move
t/a = _
c = _`],
  ['replace of the base, child live (exempt: destroyed in place)', `
params x:uniq y:uniq
locals r:uniq c:uniq
r = x
c = r
replace r with y
c = _`],
  ['replace of the base, then the parent written (invalid)', `
params x:uniq y:uniq
locals r:uniq c:uniq
r = x
c = r
replace r with y
x = _
c = _`],
  ['assignment of the base, child live', `
params x:uniq y:uniq
locals r:uniq c:uniq
r = x
c = r
r = y@move
c = _`],
  ['join: base replaced on one path (invalid)', `
params a:uniq other:uniq
locals b:uniq
b = a.x@uniq
if
  a = other@move
a.x = _
use b`],
  ['join: base replaced on both paths', `
params a:uniq other:uniq
locals b:uniq
b = a.x@uniq
if
  a = other@move
else
  a = fresh
a.x = _
use b`],
  ['shared child, base replaced, parent written (invalid)', `
params root:uniq
locals h:uniq r:ref
h = root
r = h.x@ref
h = fresh
root.x = _
use r`],
];

function verdict(a) { return a.errors.length ? `reject (${[...new Set(a.errors.map(e => `${e.kind} at \`${e.at}\``))].join('; ')})` : 'accept'; }

console.log(`Loan identity by base: prototype (loop bound ${BOUND}, trace cap ${CAP})\n`);
console.log('| Case | Proposed rule | Brief rule | Site self-authority | Reference semantics |');
console.log('| --- | --- | --- | --- | --- |');
let namedFailures = 0;
for (const [name, text] of cases) {
  const prog = parse(text);
  const base = analyze(prog, 'base');
  const brief = analyze(prog, 'brief');
  const old = analyze(prog, 'site-authority');
  const d = dynamic(prog);
  const dyn = d.clean ? `no violation (${d.traces} paths)` : `violation at \`${d.violation.at}\``;
  console.log(`| ${name} | ${verdict(base)} | ${verdict(brief)} | ${verdict(old)} | ${dyn} |`);
  if (base.errors.length === 0 && !d.clean) namedFailures++;
  if (base.invariant.length || d.failures.length) { namedFailures++; console.log(`  invariant: ${[...base.invariant, ...d.failures].join('; ')}`); }
}

// ---------------------------------------------------------------------------------------------------------------------
// Random differential test: a program the proposed rule accepts must have no violation on any enumerated path.

function rng(seed) {
  let a = seed >>> 0;
  return () => { a = (a + 0x6d2b79f5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}

function randomProgram(next) {
  const pick = arr => arr[Math.floor(next() * arr.length)];
  const kinds = { p0: 'uniq', p1: next() < 0.5 ? 'uniq' : 'ref' };
  const locals = ['a', 'b', 'c', 'd'];
  for (const l of locals) kinds[l] = next() < 0.65 ? 'uniq' : 'ref';
  // Two struct locals with the same Field kinds, so whole-struct Moves and swaps pair up.
  const structs = ['s', 't'];
  const fieldKinds = { a: 'uniq', b: next() < 0.5 ? 'uniq' : 'ref' };
  for (const st of structs) for (const [f, k] of Object.entries(fieldKinds)) kinds[`${st}/${f}`] = k;
  const holders = Object.keys(kinds);
  const own = [...locals, ...holders.filter(h => h.includes('/'))];
  const path = () => Array.from({ length: Math.floor(next() * 3) }, () => pick(['x', 'y']));
  const prog = { kinds, params: ['p0', 'p1'], body: null };
  const borrow = (dst, src) => ({ op: 'borrow', site: `s${++siteCounter}`, dst, src, path: path(), mode: kinds[dst] });
  const two = () => { const x = pick(holders), y = pick(holders); return x !== y && kinds[x] === kinds[y] ? [x, y] : null; };
  // Weighted toward the shapes the hypothesis is about: Reborrows through a holder followed by its replacement
  // (`x = x.f@uniq`, `n = x.f@uniq; x = n`), transfers and exclusive lending of holders, storage ends and fresh values.
  function simpleStmt() {
    for (;;) {
      const r = next();
      if (r < 0.12) { const h = pick(holders); if (kinds[h] !== 'uniq') continue; return borrow(h, h); }
      if (r < 0.34) { const dst = pick(holders), src = pick(holders); if (kinds[src] === 'ref' && kinds[dst] === 'uniq') continue; return borrow(dst, src); }
      if (r < 0.39) { const xy = two(); if (!xy) continue; return { op: 'move', dst: xy[0], src: xy[1] }; }
      if (r < 0.42) { const xy = two(); if (!xy || kinds[xy[0]] !== 'ref') continue; return { op: 'copy', dst: xy[0], src: xy[1] }; }
      if (r < 0.45) { const xy = two(), o = pick(holders); if (!xy || o === xy[0] || o === xy[1] || kinds[o] !== kinds[xy[0]]) continue; return { op: 'exchange', dst: o, target: xy[0], src: xy[1] }; }
      if (r < 0.48) { const xy = two(); if (!xy) continue; return { op: 'swap', target: xy[0], other: xy[1] }; }
      if (r < 0.50) { const xy = two(); if (!xy) continue; return { op: 'replace', target: xy[0], src: xy[1] }; }
      if (r < 0.53) { const [x, y] = next() < 0.5 ? structs : [...structs].reverse(); return pick([{ op: 'move', dst: x, src: y }, { op: 'swap', target: x, other: y }, { op: 'kill', holder: x }]); }
      if (r < 0.63) { const h = pick(holders); if (kinds[h] !== 'uniq') continue; return { op: 'write', holder: h, path: path() }; }
      if (r < 0.71) return { op: 'read', holder: pick(holders), path: path() };
      if (r < 0.82) return { op: 'use', holder: pick(holders), path: [] };
      if (r < 0.88) return { op: 'kill', holder: pick(own) };
      return { op: 'fresh', dst: pick(own) };
    }
  }
  function block(depth, min, max) {
    const out = [];
    const n = min + Math.floor(next() * (max - min + 1));
    for (let i = 0; i < n; i++) {
      const r = next();
      if (depth < 2 && r < 0.15) out.push({ op: 'if', then: block(depth + 1, 1, 2), else: next() < 0.5 ? block(depth + 1, 1, 2) : [] });
      else if (depth < 2 && r < 0.28) out.push({ op: 'while', body: block(depth + 1, 1, 3) });
      else out.push(simpleStmt());
    }
    return out;
  }
  const prefixStmts = [];
  for (const l of own) prefixStmts.push(next() < 0.3 ? { op: 'fresh', dst: l } : borrow(l, kinds[l] === 'uniq' ? 'p0' : pick(prog.params)));
  prog.body = [...prefixStmts, ...block(0, 2, 6)];
  return prog;
}

if (RANDOM > 0) {
  const next = rng(SEED);
  const stats = { accepted: 0, complete: 0, endedLive: 0, rejected: 0, spurious: 0, truncated: 0, unsound: 0, checks: 0, failures: 0, invariant: 0 };
  const others = { brief: { accepted: 0, unsound: 0, first: null }, 'site-authority': { accepted: 0, unsound: 0, first: null } };
  const spuriousKinds = {};
  const rejectedKinds = {};
  let firstUnsound = null;
  let firstInvariant = null;
  let firstUnexplained = null;
  for (let k = 0; k < RANDOM; k++) {
    const prog = randomProgram(next);
    const base = analyze(prog, 'base');
    const d = dynamic(prog);
    if (d.truncated) stats.truncated++;
    stats.checks += d.checks;
    stats.failures += d.failures.length;
    if (base.errors.length === 0) {
      // The finite-domain invariant matters where the rule accepts; a rejected program may break it after its error.
      stats.invariant += base.invariant.length;
      if (base.invariant.length) firstInvariant ??= { prog, invariant: base.invariant };
      stats.accepted++;
      if (!d.truncated) stats.complete++;
      if (base.endedLive) stats.endedLive++;
      if (!d.clean) { stats.unsound++; firstUnsound ??= { prog, d }; }
    } else {
      stats.rejected++;
      for (const kind of new Set(base.errors.map(e => e.kind))) rejectedKinds[kind] = (rejectedKinds[kind] ?? 0) + 1;
      // A truncated path set may miss the violation, so only fully explored programs count as rejected without one. Each
      // is classified: a lost form (a Move or exclusive Borrow of a base, with any follow-on errors), a violation beyond
      // the loop bound (found again with two more iterations), or unexplained, the only class that would be a further loss.
      if (d.clean && !d.truncated) {
        stats.spurious++;
        const kinds = new Set(base.errors.map(e => e.kind));
        let why;
        if (kinds.has('moved-base') || kinds.has('lent-base')) why = kinds.has('lent-base') ? 'lent base' : 'moved base';
        else {
          const deeper = dynamic(prog, BOUND + 2);
          why = !deeper.clean ? 'violation beyond the loop bound' : deeper.truncated ? 'unexplained, deeper paths truncated' : 'unexplained';
          if (why.startsWith('unexplained')) firstUnexplained ??= { prog, errors: base.errors };
        }
        spuriousKinds[why] = (spuriousKinds[why] ?? 0) + 1;
      }
    }
    for (const [rule, o] of Object.entries(others)) {
      if (analyze(prog, rule).errors.length) continue;
      o.accepted++;
      if (!d.clean) { o.unsound++; o.first ??= { prog, d }; }
    }
  }
  const tally = t => Object.entries(t).map(([k, v]) => `${k} ${v}`).join(', ');
  console.log(`\nRandom differential test (seed ${SEED}, ${RANDOM} programs, loop bound ${BOUND}):`);
  console.log(`  proposed rule: ${stats.accepted} accepted (${stats.complete} with every path up to the bound explored; ${stats.endedLive} end a Loan that a later use still carries), ${stats.unsound} of them with a violation`);
  console.log(`  proposed rule: ${stats.rejected} rejected (${tally(rejectedKinds)}), ${stats.spurious} of them fully explored without a violation (${tally(spuriousKinds)})`);
  for (const [rule, o] of Object.entries(others)) console.log(`  ${rule} rule: ${o.accepted} accepted, ${o.unsound} of them with a violation`);
  console.log(`  hypothesis checks at kills: ${stats.checks}, failures ${stats.failures}; finite-domain invariant failures ${stats.invariant}; path sets truncated ${stats.truncated}`);
  const report = (title, x) => x && console.log(`\n${title}:\n${print(x.prog)}\n  violation at ${x.d.violation.at} on path: ${x.d.trace.map(show).join('; ')}`);
  report('First unsound acceptance by the proposed rule', firstUnsound);
  if (firstInvariant) console.log(`\nFirst accepted program breaking the finite-domain invariant:\n${print(firstInvariant.prog)}\n  ${firstInvariant.invariant[0]}`);
  if (firstUnexplained) console.log(`\nFirst unexplained rejection without a violation:\n${print(firstUnexplained.prog)}\n  ${firstUnexplained.errors.map(e => `${e.kind} at ${e.at}${e.loan ? ` with ${e.loan}` : ''}`).join('; ')}`);
  for (const [rule, o] of Object.entries(others)) report(`First unsound acceptance by the ${rule} rule`, o.first);
  process.exitCode = stats.unsound || stats.failures || stats.invariant || namedFailures ? 1 : 0;
} else process.exitCode = namedFailures ? 1 : 0;
