import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const web = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const output = path.resolve(web, '../dist/licenses');
const lock = JSON.parse(await fs.readFile(path.join(web, 'package-lock.json'), 'utf8'));
await fs.mkdir(output, { recursive: true });
let count = 0;
for (const [relative, metadata] of Object.entries(lock.packages)) {
  if (!relative.startsWith('node_modules/') || metadata.dev) continue;
  const directory = path.join(web, relative);
  const manifest = JSON.parse(await fs.readFile(path.join(directory, 'package.json'), 'utf8'));
  const names = (await fs.readdir(directory)).filter(name => /^(licen[sc]e|notice|copying|ofl)([.-]|$)/i.test(name));
  if (!names.length) throw new Error(`Missing license text for bundled dependency ${manifest.name}`);
  for (const name of names) {
    const source = path.join(directory, name);
    if (!(await fs.stat(source)).isFile()) continue;
    const target = `${manifest.name.replace(/[^a-z0-9._-]/gi, '_')}-${name.replace(/[^a-z0-9._-]/gi, '_')}.txt`;
    await fs.copyFile(source, path.join(output, target));
    count++;
  }
}
await fs.copyFile(path.join(web, 'vendor/BEAUTIFULUI-MIT-LICENSE.txt'), path.join(output, 'BEAUTIFULUI-MIT-LICENSE.txt'));
console.log(`Collected ${count} runtime dependency license notices and the Beautiful UI license.`);
