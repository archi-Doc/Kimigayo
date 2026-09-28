import * as assert from 'node:assert/strict';
import { chmod, mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import * as path from 'node:path';
import { after, before, test } from 'node:test';
import { resolveServerPath } from '../../serverPath';

let root: string;
let executable: string;
before(async () => {
  root = await mkdtemp(path.join(tmpdir(), 'kimi-lsp-path-'));
  executable = path.join(root, 'Kimi server.exe');
  await writeFile(executable, 'test fixture');
  await chmod(executable, 0o700);
});
after(async () => {
  assert.ok(path.resolve(root).startsWith(path.resolve(tmpdir()) + path.sep));
  await rm(root, { recursive: true, force: true });
});

test('accepts a file path containing spaces', async () => {
  assert.equal(await resolveServerPath(executable), executable);
});
test('accepts the quoted path produced by Windows Copy as path', async () => {
  assert.equal(await resolveServerPath(` "${executable}" `), executable);
});
test('reports the missing file and setting name', async () => {
  const missing = path.join(root, 'missing.exe');
  await assert.rejects(resolveServerPath(missing), error => error instanceof Error && error.message.includes(missing) && error.message.includes('kimi.serverPath'));
});
test('rejects directories even when their name ends with exe', async () => {
  const directory = path.join(root, 'directory.exe');
  await mkdir(directory);
  await assert.rejects(resolveServerPath(directory), /expected an executable file/);
});
test('rejects empty, non-string and malformed values', async () => {
  for (const value of ['', '  ', undefined, null, 42, '""', 'bad\0path', 'bad\npath', '"unterminated']) {
    await assert.rejects(resolveServerPath(value), /kimi.serverPath/);
  }
});
test('rejects relative paths instead of depending on the host working directory', async () => {
  await assert.rejects(resolveServerPath('./Kimi.exe'), /absolute path/);
  await assert.rejects(resolveServerPath('folder\\Kimi.exe'), /absolute path/);
});
test('rejects Windows root-relative paths that depend on the current drive', { skip: process.platform !== 'win32' }, async () => {
  await assert.rejects(resolveServerPath(executable.slice(2)), /absolute path/);
});
test('ignores Windows root-relative PATH directories', { skip: process.platform !== 'win32' }, async () => {
  await assert.rejects(resolveServerPath(path.basename(executable), root.slice(2)), /found on PATH/);
});
test('resolves a command through PATH and skips missing directories', async () => {
  const search = [path.join(root, 'missing'), root].join(path.delimiter);
  assert.equal(await resolveServerPath(path.basename(executable), search), executable);
});
test('handles quoted PATH entries', async () => {
  assert.equal(await resolveServerPath(path.basename(executable), `"${root}"`), executable);
});
test('reports a command missing from PATH', async () => {
  await assert.rejects(resolveServerPath('missing.exe', root), /found on PATH/);
});
test('does not search the current directory for empty or relative PATH entries', async () => {
  await assert.rejects(resolveServerPath('Kimi.exe', ['', '.', 'relative'].join(path.delimiter)), /found on PATH/);
});
test('adds exe to an extensionless Windows command', { skip: process.platform !== 'win32' }, async () => {
  const file = path.join(root, 'Kimi.exe');
  await writeFile(file, 'test fixture');
  assert.equal(await resolveServerPath('Kimi', root), file);
});
test('rejects non-executable files on POSIX', { skip: process.platform === 'win32' }, async () => {
  const file = path.join(root, 'not-executable');
  await writeFile(file, 'test fixture', { mode: 0o600 });
  await assert.rejects(resolveServerPath(file), /cannot be read or executed/);
});
