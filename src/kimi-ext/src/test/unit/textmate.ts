import { readFileSync } from 'node:fs';
import * as path from 'node:path';
import * as oniguruma from 'vscode-oniguruma';
import { IGrammar, INITIAL, parseRawGrammar, Registry } from 'vscode-textmate';

export const extensionRoot = path.resolve(__dirname, '../../..');
export const repositoryRoot = path.resolve(extensionRoot, '../..');

export interface Token {
  line: number;
  start: number;
  end: number;
  text: string;
  scopes: string[];
}

export interface Tokenized {
  tokens: Token[];
  /** Whether every construct opened by the source is closed at its end. */
  closed: boolean;
}

interface GrammarContribution {
  scopeName: string;
  path: string;
  injectTo?: string[];
}

// VS Code supplies the Markdown grammar; an empty host is enough to exercise the injected fenced block.
const markdownHost = JSON.stringify({ scopeName: 'text.html.markdown', patterns: [] });
let registry: Promise<Registry> | undefined;

function createRegistry(): Promise<Registry> {
  const manifest = JSON.parse(readFileSync(path.join(extensionRoot, 'package.json'), 'utf8'));
  const grammars: GrammarContribution[] = manifest.contributes.grammars;
  const wasm = readFileSync(require.resolve('vscode-oniguruma/release/onig.wasm'));
  const onigLib = oniguruma.loadWASM(wasm).then(() => ({
    createOnigScanner: (sources: string[]) => oniguruma.createOnigScanner(sources),
    createOnigString: (text: string) => oniguruma.createOnigString(text)
  }));
  return Promise.resolve(new Registry({
    onigLib,
    loadGrammar: async scopeName => {
      if (scopeName === 'text.html.markdown') {
        return parseRawGrammar(markdownHost, 'markdown.json');
      }
      const contribution = grammars.find(grammar => grammar.scopeName === scopeName);
      return contribution
        ? parseRawGrammar(readFileSync(path.join(extensionRoot, contribution.path), 'utf8'), contribution.path)
        : null;
    },
    getInjections: scopeName => grammars.filter(grammar => grammar.injectTo?.includes(scopeName)).map(grammar => grammar.scopeName)
  }));
}

/** Load a contributed grammar through the extension manifest, as VS Code does. */
export async function loadGrammar(scopeName: string): Promise<IGrammar> {
  registry ??= createRegistry();
  const grammar = await (await registry).loadGrammar(scopeName);
  if (!grammar) {
    throw new Error(`Grammar ${scopeName} is not contributed.`);
  }
  return grammar;
}

export function tokenize(grammar: IGrammar, source: string): Tokenized {
  const tokens: Token[] = [];
  let state = INITIAL;
  source.split(/\r\n|\r|\n/).forEach((text, line) => {
    const result = grammar.tokenizeLine(text, state);
    for (const token of result.tokens) {
      const end = Math.min(token.endIndex, text.length);
      tokens.push({ line, start: token.startIndex, end, text: text.slice(token.startIndex, end), scopes: token.scopes });
    }
    state = result.ruleStack;
  });
  return { tokens, closed: state.equals(grammar.tokenizeLine('', INITIAL).ruleStack) };
}
