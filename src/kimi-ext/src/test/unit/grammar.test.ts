import * as assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import * as path from 'node:path';
import { before, test } from 'node:test';
import { IGrammar } from 'vscode-textmate';
import { loadGrammar, repositoryRoot, Token, tokenize } from './textmate';

let kimi: IGrammar;
let markdown: IGrammar;
before(async () => {
  kimi = await loadGrammar('source.kimi');
  markdown = await loadGrammar('text.html.markdown');
});

function hasScope(token: Token, scope: string): boolean {
  return token.scopes.some(name => name === scope || name.startsWith(scope + '.'));
}

/** Tokens covering the occurrence-th single-line `fragment` of `source`. */
function covering(grammar: IGrammar, source: string, fragment: string, occurrence: number): Token[] {
  let index = -1;
  for (let i = 0; i <= occurrence; i++) {
    index = source.indexOf(fragment, index + 1);
    assert.notEqual(index, -1, `${JSON.stringify(fragment)} occurrence ${occurrence} is not in ${JSON.stringify(source)}`);
  }
  const lines = source.slice(0, index).split(/\r\n|\r|\n/);
  const line = lines.length - 1;
  const start = lines[line].length;
  const end = start + fragment.length;
  const tokens = tokenize(grammar, source).tokens.filter(token => token.line === line && token.start < end && token.end > start);
  assert.ok(tokens.length > 0);
  return tokens;
}

function describe(token: Token): string {
  return `${JSON.stringify(token.text)} has ${token.scopes.join(' ')}`;
}

/** Assert that every token of the fragment has `scope` or one of its subscopes. */
function expectScope(source: string, fragment: string, scope: string, occurrence = 0, grammar = kimi): void {
  for (const token of covering(grammar, source, fragment, occurrence)) {
    assert.ok(hasScope(token, scope), `In ${JSON.stringify(source)}, ${describe(token)}; expected ${scope}`);
  }
}

/** Assert that no token of the fragment has `scope` or one of its subscopes. */
function expectNoScope(source: string, fragment: string, scope: string, occurrence = 0, grammar = kimi): void {
  for (const token of covering(grammar, source, fragment, occurrence)) {
    assert.ok(!hasScope(token, scope), `In ${JSON.stringify(source)}, ${describe(token)}; unexpected ${scope}`);
  }
}

/** The backquoted word spellings in the second column of the table whose header starts with `header`. */
function specTableSpellings(markdownText: string, header: string): string[] {
  const lines = markdownText.split(/\r?\n/);
  const start = lines.findIndex(line => line.startsWith(`| ${header}`));
  assert.notEqual(start, -1, `SPEC table ${header} is missing`);
  const spellings: string[] = [];
  for (const line of lines.slice(start + 2)) {
    if (!line.startsWith('|')) {
      break;
    }
    const cell = line.split('|')[2];
    for (const match of cell.matchAll(/`([\p{L}_][\p{L}\p{N}_]*)`/gu)) {
      spellings.push(match[1]);
    }
  }
  return spellings;
}

test('colors every reserved keyword of SPEC 2.5.1 as a keyword', () => {
  const lexical = readFileSync(path.join(repositoryRoot, 'docs/spec/02-source-and-lexical-structure.md'), 'utf8');
  const reserved = specTableSpellings(lexical, 'Reserved class');
  assert.ok(reserved.length >= 50, `only ${reserved.length} reserved keywords were read from the SPEC table`);
  const keywordScopes = ['keyword', 'storage', 'constant.language', 'variable.language', 'support.type.primitive'];
  for (const word of reserved) {
    for (const token of covering(kimi, word, word, 0)) {
      assert.ok(keywordScopes.some(scope => hasScope(token, scope)), `Reserved ${describe(token)}`);
    }
  }
});

test('colors comments and documentation', () => {
  const documentation = '    /// Returns `values[0]` when present.';
  expectScope(documentation, '///', 'comment.line.documentation');
  expectScope(documentation, 'Returns', 'comment.line.documentation');
  expectScope(documentation, '`values[0]`', 'markup.inline.raw');
  expectScope('//// Not documentation', 'Not', 'comment.line.double-slash');
  expectNoScope('//// Not documentation', 'Not', 'comment.line.documentation');
  expectScope('let x = 1 /// trailing', 'trailing', 'comment.line.double-slash');
  expectNoScope('let x = 1 /// trailing', 'trailing', 'comment.line.documentation');
  expectScope('let total = 1 /* inline */ + 2', 'inline', 'comment.block');
  expectNoScope('let total = 1 /* inline */ + 2', '2', 'comment');
  // Block comments end at the first terminator and do not nest.
  expectScope('/* first\n/* second */ code', 'second', 'comment.block');
  expectNoScope('/* first\n/* second */ code', 'code', 'comment');
});

test('colors strings, interpolation and escapes', () => {
  const interpolated = '"Total: \\(price * (quantity + 1)) units\\n"';
  expectScope(interpolated, 'Total', 'string.quoted.double');
  expectScope(interpolated, '\\(', 'punctuation.section.embedded.begin');
  expectScope(interpolated, 'price', 'meta.embedded.line');
  expectScope(interpolated, '1', 'constant.numeric');
  expectScope(interpolated, ')', 'punctuation.section.parens.end');
  expectScope(interpolated, ')', 'punctuation.section.embedded.end', 1);
  expectScope(interpolated, 'units', 'string.quoted.double');
  expectNoScope(interpolated, 'units', 'meta.embedded');
  expectScope(interpolated, '\\n', 'constant.character.escape');
  // A string nested in an interpolation does not end the outer string.
  const nested = '"a \\("b)" + c) d" + e';
  expectScope(nested, 'b)', 'string.quoted.double', 0);
  expectScope(nested, 'c', 'meta.embedded.line');
  expectScope(nested, 'd', 'string.quoted.double');
  expectNoScope(nested, 'e', 'string');
  expectScope('"\\u(1f600)\\e"', '\\u(1f600)', 'constant.character.escape');
  expectScope('"\\u(0x41)"', '\\u(0x41)', 'invalid.illegal.escape');
  expectScope('"\\u()"', '\\u()', 'invalid.illegal.escape');
  expectScope('"\\q"', '\\q', 'invalid.illegal.escape');
  expectNoScope('"" + empty', 'empty', 'string');
  expectScope('"\nfirst line\n" + x', 'first line', 'string.quoted.double');
  expectNoScope('"\nfirst line\n" + x', 'x', 'string');
});

test('colors raw strings by their delimiter length', () => {
  expectScope('"""C:\\Users\\name"""', '\\Users', 'string.quoted.raw');
  expectNoScope('"""C:\\Users\\name"""', '\\Users', 'constant.character.escape');
  expectScope('""""The token """ appears here."""" + rest', 'appears', 'string.quoted.raw');
  expectNoScope('""""The token """ appears here."""" + rest', 'rest', 'string');
  // A longer terminating run keeps its first quotes as content.
  expectScope('"""text"""" + rest', 'text"', 'string.quoted.raw');
  expectNoScope('"""text"""" + rest', 'rest', 'string');
  expectScope('"""\n\\(not interpolation)\n""" + rest', 'not interpolation', 'string.quoted.raw');
  expectNoScope('"""\n\\(not interpolation)\n""" + rest', 'not interpolation', 'meta.embedded');
  expectNoScope('"""\n\\(not interpolation)\n""" + rest', 'rest', 'string');
});

test('colors character literals', () => {
  expectScope("let quote = '\\'' + next", "'\\''", 'string.quoted.single');
  expectScope("let quote = '\\'' + next", "\\'", 'constant.character.escape');
  expectNoScope("let quote = '\\'' + next", 'next', 'string');
  expectScope("let euro = '€'", '€', 'string.quoted.single');
});

test('colors number literals and Tuple indices', () => {
  for (const literal of ['0x_FF', '0b__101__', '0o17', '1_000', '1.5e-3', '123_']) {
    expectScope(`let n = ${literal}`, literal, 'constant.numeric');
  }
  expectScope('pair.0.1', '0', 'constant.numeric.integer.tuple-index');
  expectScope('pair.0.1', '.', 'punctuation.accessor', 1);
  expectScope('pair.0.1', '1', 'constant.numeric.integer.tuple-index');
  expectScope('let x = 1.', '.', 'punctuation.accessor');
  expectScope('0..5', '..', 'keyword.operator.range');
  expectScope('a..1.5', '1.5', 'constant.numeric.decimal');
  expectNoScope('let point2 = 1', 'point2', 'constant.numeric');
});

test('colors declarations, Types and calls', () => {
  const header = 'public func insert<P>(self: uniq/Self, index: P, value: T) -> Option<ref/T during self>';
  expectScope(header, 'public', 'storage.modifier.access');
  expectScope(header, 'func', 'storage.type.function');
  expectScope(header, 'insert', 'entity.name.function');
  expectScope(header, 'P', 'entity.name.type');
  expectScope(header, 'self', 'variable.language.self');
  expectScope(header, 'uniq', 'storage.modifier.semantics');
  expectScope(header, '/', 'punctuation.separator.semantics');
  expectScope(header, 'Self', 'variable.language.self');
  expectScope(header, 'Option', 'entity.name.type');
  expectScope(header, 'during', 'keyword.other.origin');
  expectNoScope(header, 'index', 'entity.name');
  expectScope('public struct Array<T>', 'Array', 'entity.name.type');
  expectScope('public struct Array<T>', 'struct', 'storage.type.declaration');
  expectScope('rootgroup Program', 'Program', 'entity.name.type');
  expectScope('public computed length: isize', 'length', 'variable.other.property');
  expectScope('public computed length: isize', 'isize', 'support.type.primitive');
  expectScope('alias Output => ::Kimi.Console', 'Output', 'entity.name.type.alias');
  expectScope('alias Kimi.Windows', 'alias', 'storage.type.alias');
  expectScope('self.insertAt(position, value@move)', 'insertAt', 'entity.name.function.call');
  expectScope('self.insertAt(position, value@move)', 'move', 'keyword.operator.expression.explicit');
  expectScope('let pair = make<Option<T>>(x)', 'make', 'entity.name.function.call');
  expectScope('let total = 合計(values)', '合計', 'entity.name.function.call');
  expectScope('let value = Ärger.create()', 'Ärger', 'entity.name.type');
  expectNoScope('let value = 合計', '合計', 'entity.name');
  expectScope('ArrayIterator<T>.init(storage)', 'init', 'keyword.other.construction');
  expectScope('Self is Copy when T is Copy', 'when', 'keyword.other.when');
  expectScope('Self is Copy when T is Copy', 'is', 'keyword.operator.expression');
});

test('colors enum Case references', () => {
  expectScope('    .Some(let position) => position', 'Some', 'variable.other.enummember');
  expectScope('    .Some(let position) => position', 'let', 'storage.type.binding');
  expectScope('return .None', 'None', 'variable.other.enummember');
  expectScope('let none = Option<i32>.None', 'None', 'entity.name.type');
  expectNoScope('let none = Option<i32>.None', 'None', 'variable.other.enummember');
});

test('colors contextual keywords only in their contexts', () => {
  expectScope('for (key, value) in pairs', 'in', 'keyword.control.in');
  expectScope('for (key, value) in pairs', 'for', 'keyword.control');
  expectScope('let saved = label work: do', 'label', 'keyword.control.label');
  expectScope('let saved = label work: do', 'work', 'entity.name.label');
  expectScope('let saved = label work: do', 'do', 'keyword.control');
  expectScope('exit to work value', 'to', 'keyword.control.to');
  expectScope('exit to work value', 'work', 'entity.name.label');
  expectScope('let words: [2 of string] = ["a", "b"]', 'of', 'keyword.other.of');
  expectScope('let words: [2 of string] = ["a", "b"]', '2', 'constant.numeric');
  expectScope('let filled = [N of _]', 'of', 'keyword.other.of');
  expectScope('let filled = [N of _]', '_', 'variable.language.wildcard');
  expectScope('func sum<length N>(values: [N of i32]) -> i32', 'length', 'storage.modifier.length');
  expectScope('func index(self: ref/Self) -> place ref/V during self', 'place', 'storage.modifier.place');
  expectScope('    origin r.source == self.source', 'origin', 'keyword.other.origin');
  expectScope('func f() -> ref/i32 during static', 'static', 'keyword.other.origin.static');
  expectScope('    specialize func tryResolve<isize>(self: isize) -> bool', 'specialize', 'storage.modifier.specialize');
  expectScope('    specialize func tryResolve<isize>(self: isize) -> bool', 'tryResolve', 'entity.name.function');
  expectScope('static let limit: i32 = 1', 'static', 'storage.modifier.static');
  expectScope('unsafe func release()', 'unsafe', 'storage.modifier.unsafe');
  expectScope('    unsafe', 'unsafe', 'storage.modifier.unsafe');
  expectScope('defer => unsafe => releaseRaw(pointer)', 'unsafe', 'storage.modifier.unsafe');
  expectScope('    get', 'get', 'storage.type.accessor');
  expectScope('    private set', 'set', 'storage.type.accessor');
  expectScope('    private set', 'private', 'storage.modifier.access');
  expectScope('        get(self: ref/Self) -> i32 => self.storedValue', 'get', 'storage.type.accessor');
  expectScope('    get() -> f64', 'get', 'storage.type.accessor');
  expectScope('    associate Iterable.IteratorType(source) is ArrayIterator<T> during source', 'associate', 'storage.type.associate');
  expectScope('    T is owning', 'owning', 'storage.modifier.semantics.category');
  // Elsewhere the same words are ordinary Names.
  const names = 'let during = origin + when + place + length + of + static + unsafe';
  for (const word of ['during', 'origin', 'when', 'place', 'length', 'of', 'static', 'unsafe']) {
    expectNoScope(names, word, 'keyword');
    expectNoScope(names, word, 'storage');
  }
  expectScope('    get(index)', 'get', 'entity.name.function.call');
  expectNoScope('let owner = ref + raw', 'owner', 'storage');
  expectNoScope('let owner = ref + raw', 'ref', 'storage');
  expectNoScope('let total = value', 'value', 'storage');
});

test('colors directives, Attributes, explicit operations and Composition Root operations', () => {
  expectScope('#if windows', '#if', 'keyword.control.directive');
  expectScope('    #case _', '#case', 'keyword.control.directive');
  expectScope('    #case _', '_', 'variable.language.wildcard');
  expectScope('#switch', '#switch', 'keyword.control.directive');
  expectScope('#Test', 'Test', 'entity.other.attribute-name');
  expectScope('#LibraryImport("kernel32", "QueryPerformanceCounter")', 'LibraryImport', 'entity.other.attribute-name');
  expectScope('#LibraryImport("kernel32", "QueryPerformanceCounter")', 'kernel32', 'string.quoted.double');
  expectScope('$abort("unexpected")', '$abort', 'support.function.builtin');
  expectScope('hash ^ b@wrap<Wrapping<u32>>', 'wrap', 'keyword.operator.expression.explicit');
  expectScope('hash ^ b@wrap<Wrapping<u32>>', 'Wrapping', 'entity.name.type');
  expectScope('return values[0]@ref', 'ref', 'storage.modifier.semantics');
  expectScope('let pointer = value@raw', 'raw', 'keyword.operator.expression.explicit');
  expectScope('let pointer = address@raw/u8', 'raw', 'storage.modifier.semantics');
  expectScope('let wide = self@u128', 'u128', 'support.type.primitive');
  expectScope('let wide = self@u128', '@', 'keyword.operator.explicit');
});

test('colors operators and marks unavailable tokens', () => {
  expectScope('let a = b; c', ';', 'invalid.illegal.unavailable');
  expectScope('a && b || c', '&&', 'invalid.illegal.unavailable');
  expectScope('a && b || c', '||', 'invalid.illegal.unavailable');
  expectScope('func f() -> i32 => 1', '->', 'keyword.operator.arrow');
  expectScope('func f() -> i32 => 1', '=>', 'keyword.operator.arrow');
  expectScope('x <<= 2', '<<=', 'keyword.operator.assignment.compound');
  expectScope('a != b', '!=', 'keyword.operator.comparison');
  expectScope('0..=5', '..=', 'keyword.operator.range');
  expectScope('a and not b or c', 'not', 'keyword.operator.expression.logical');
  expectScope('let x: i32? = null', '?', 'keyword.operator.optional');
  expectScope('let x: i32? = null', 'null', 'constant.language.null');
});

test('colors Kimi fenced code blocks in Markdown', () => {
  const document = 'Use `let` here.\n\n```kimi\nlet total = "a \\(b)"\n```\n\nAfter let.\n\n~~~ Kimi title\nfunc f()\n~~~\n';
  expectScope(document, 'let', 'meta.embedded.block.kimi', 1, markdown);
  expectScope(document, 'let', 'storage.type.binding', 1, markdown);
  expectScope(document, 'b', 'meta.embedded.line', 0, markdown);
  expectNoScope(document, 'let', 'storage', 0, markdown);
  expectNoScope(document, 'let', 'storage', 2, markdown);
  expectScope(document, 'func', 'storage.type.function', 0, markdown);
  expectScope(document, 'kimi', 'fenced_code.block.language', 0, markdown);
});

function kimiSources(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true, recursive: true })
    .filter(entry => entry.isFile() && entry.name.endsWith('.kimi'))
    .map(entry => path.join(entry.parentPath, entry.name))
    .sort();
}

test('detects constructs left open at the end of the source', () => {
  for (const source of ['"open', '"""\nopen""', '/* open', '"\\(open"', 'let total = 1']) {
    assert.equal(tokenize(kimi, source).closed, source === 'let total = 1', JSON.stringify(source));
  }
});

test('closes every construct and finds no unavailable token in valid sources', () => {
  const sources = ['src/Kimi/Library', 'docs/examples', 'tests/milestones', 'src/Benchmark/Kimi']
    .flatMap(directory => kimiSources(path.join(repositoryRoot, directory)));
  assert.ok(sources.length >= 70, `only ${sources.length} sources were found`);
  for (const source of sources) {
    const result = tokenize(kimi, readFileSync(source, 'utf8'));
    assert.ok(result.closed, `${source} ends inside a string or comment`);
    const invalid = result.tokens.find(token => hasScope(token, 'invalid'));
    assert.equal(invalid, undefined, invalid && `${source}:${invalid.line + 1}: ${describe(invalid)}`);
  }
});

test('closes every construct of the SPEC Kimi examples', () => {
  const blocks: { file: string; line: number; text: string }[] = [];
  for (const directory of ['docs/spec', 'docs/impl']) {
    for (const entry of readdirSync(path.join(repositoryRoot, directory), { withFileTypes: true, recursive: true })) {
      if (!entry.isFile() || !entry.name.endsWith('.md')) {
        continue;
      }
      const file = path.join(entry.parentPath, entry.name);
      const lines = readFileSync(file, 'utf8').split(/\r?\n/);
      for (let i = 0; i < lines.length; i++) {
        const fence = /^(\s*)```kimi\s*$/.exec(lines[i]);
        if (!fence) {
          continue;
        }
        const end = lines.findIndex((line, j) => j > i && line.trim() === '```');
        assert.notEqual(end, -1, `${file}:${i + 1}: unterminated fence`);
        blocks.push({ file, line: i + 2, text: lines.slice(i + 1, end).map(line => line.slice(fence[1].length)).join('\n') });
        i = end;
      }
    }
  }
  assert.ok(blocks.length >= 300, `only ${blocks.length} Kimi blocks were found`);
  for (const block of blocks) {
    assert.ok(tokenize(kimi, block.text).closed, `${block.file}:${block.line}: the example ends inside a string or comment`);
  }
});
