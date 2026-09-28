import * as assert from 'node:assert/strict';
import { test } from 'node:test';
import { toProtocolUri } from '../../documentUri';

test('Windows drive colon is preserved while filename characters remain escaped', { skip: process.platform !== 'win32' }, () => {
  const result = toProtocolUri({ scheme: 'file', fsPath: 'C:\\Project Files\\日本語 #100%.kimi', toString: () => 'unused' });
  assert.equal(result, 'file:///C:/Project%20Files/%E6%97%A5%E6%9C%AC%E8%AA%9E%20%23100%25.kimi');
  const parsed = new URL(result);
  assert.equal(parsed.hash, '');
  assert.equal(parsed.search, '');
});
test('preserves UNC server and share', { skip: process.platform !== 'win32' }, () => {
  assert.equal(toProtocolUri({ scheme: 'file', fsPath: '\\\\server\\share\\Hello World.kimi', toString: () => 'unused' }), 'file://server/share/Hello%20World.kimi');
});
test('POSIX file URI remains escaped', { skip: process.platform === 'win32' }, () => {
  assert.equal(toProtocolUri({ scheme: 'file', fsPath: '/tmp/Test #1.kimi', toString: () => 'unused' }), 'file:///tmp/Test%20%231.kimi');
});
test('preserves non-file URIs', () => {
  assert.equal(toProtocolUri({ scheme: 'untitled', fsPath: '', toString: () => 'untitled:Untitled-1' }), 'untitled:Untitled-1');
});
