import { copyFile, mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { createRequire } from 'node:module';
import * as path from 'node:path';

const extensionRoot = path.resolve(import.meta.dirname, '..');
const repositoryRoot = path.resolve(extensionRoot, '../..');
await mkdir(path.join(repositoryRoot, 'artifacts', 'packages'), { recursive: true });
const readme = (await readFile(path.join(repositoryRoot, 'README.md'), 'utf8')).replace(/\r\n/g, '\n');
const heading = '## Visual Studio Code\n';
const start = readme.indexOf(heading);
if (start < 0) {
  throw new Error('Root README.md must contain the Visual Studio Code section.');
}
const next = readme.indexOf('\n## ', start + heading.length);
const section = readme.slice(start, next < 0 ? undefined : next)
  .replace(heading, '## Kimi Extension\n')
  .replace(/^(#{2,6}) /gm, (_match, hashes) => `${hashes.slice(1)} `)
  .replace(/\]\((?![a-z]+:|\/)([^)]+)\)/gi, (_match, target) => {
    const repository = 'https://github.com/archi-Doc/Kimigayo';
    const url = target.startsWith('#') ? repository + target
      : `${repository}/${target.endsWith('/') ? 'tree' : 'blob'}/main/${target}`;
    return `](${url})`;
  });

await writeFile(path.join(extensionRoot, 'README.md'), section.trimEnd() + '\n');
await copyFile(path.join(repositoryRoot, 'LICENSE'), path.join(extensionRoot, 'LICENSE'));

// Bundled runtime packages still need their complete copyright and license notices.
const visited = new Set();
const notices = [];
async function collectLicenses(manifestPath) {
  const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
  const require = createRequire(manifestPath);
  for (const name of Object.keys(manifest.dependencies ?? {}).sort()) {
    // Some packages do not export package.json; locate it through Node's search paths.
    const dependencyPath = require.resolve.paths(name)?.map(directory => path.join(directory, name, 'package.json')).find(existsSync);
    if (!dependencyPath) throw new Error(`Cannot locate bundled dependency ${name}`);
    if (visited.has(dependencyPath)) continue;
    visited.add(dependencyPath);
    const dependency = JSON.parse(await readFile(dependencyPath, 'utf8'));
    const directory = path.dirname(dependencyPath);
    const licenseFiles = (await readdir(directory)).filter(file => /^licen[cs]e(?:\.(?:txt|md))?$/i.test(file)).sort();
    if (licenseFiles.length === 0) throw new Error(`Missing license for bundled dependency ${name}`);
    const licenses = await Promise.all(licenseFiles.map(file => readFile(path.join(directory, file), 'utf8')));
    notices.push(`${dependency.name}@${dependency.version}\n\n${licenses.join('\n')}`);
    await collectLicenses(dependencyPath);
  }
}
await collectLicenses(path.join(extensionRoot, 'package.json'));
await mkdir(path.join(extensionRoot, 'out'), { recursive: true });
await writeFile(path.join(extensionRoot, 'out', 'THIRD_PARTY_NOTICES.txt'), notices.join('\n\n---\n\n') + '\n');
// The language client resolves this POSIX process-tree helper relative to the bundle.
const require = createRequire(path.join(extensionRoot, 'package.json'));
await copyFile(
  path.join(path.dirname(require.resolve('vscode-languageclient/node')), 'lib', 'node', 'terminateProcess.sh'),
  path.join(extensionRoot, 'out', 'terminateProcess.sh'),
);
