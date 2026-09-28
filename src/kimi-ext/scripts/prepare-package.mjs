import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
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
