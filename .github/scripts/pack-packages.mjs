import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { cp, mkdir, readdir, readFile, rm, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const packagesRoot = join(repositoryRoot, 'Packages');
const outputDirectory = join(repositoryRoot, 'dist', 'packages');
const sitePackageDirectory = join(repositoryRoot, 'Website~', 'build', 'packages');
const siteBaseUrl = process.env.DOCUSAURUS_BASE_URL || '/';

await rm(outputDirectory, { recursive: true, force: true });
await mkdir(outputDirectory, { recursive: true });

const entries = await readdir(packagesRoot, { withFileTypes: true });
const packageDirectories = entries
  .filter((entry) => entry.isDirectory() && entry.name.startsWith('com.kibalab.tsmp'))
  .map((entry) => entry.name)
  .filter((directoryName) => existsSync(join(packagesRoot, directoryName, 'package.json')))
  .sort((a, b) => a.localeCompare(b));

if (packageDirectories.length === 0) {
  throw new Error('No TSMP package directories were found.');
}

const packages = [];

for (const directoryName of packageDirectories) {
  const packageDirectory = join(packagesRoot, directoryName);
  const manifestPath = join(packageDirectory, 'package.json');
  const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));

  const packResult = spawnSync(
    'npm',
    ['pack', packageDirectory, '--pack-destination', outputDirectory],
    {
      cwd: repositoryRoot,
      encoding: 'utf8',
      shell: process.platform === 'win32',
      stdio: ['ignore', 'pipe', 'pipe'],
    },
  );

  if (packResult.status !== 0) {
    const detail = packResult.stderr || packResult.stdout || 'npm pack failed without output.';
    throw new Error(`Failed to pack ${manifest.name}: ${detail}`);
  }

  const tarball = packResult.stdout.trim().split(/\r?\n/).filter(Boolean).pop();
  if (!tarball) {
    throw new Error(`npm pack did not report a tarball for ${manifest.name}.`);
  }

  packages.push({
    name: manifest.name,
    displayName: manifest.displayName || manifest.name,
    version: manifest.version,
    description: manifest.description || '',
    author: manifest.author || null,
    file: tarball,
    url: `${siteBaseUrl.replace(/\/?$/, '/')}packages/${tarball}`,
  });
}

const index = {
  generatedAt: new Date().toISOString(),
  packages,
};

await writeFile(join(outputDirectory, 'index.json'), `${JSON.stringify(index, null, 2)}\n`, 'utf8');
await rm(sitePackageDirectory, { recursive: true, force: true });
await mkdir(sitePackageDirectory, { recursive: true });
await cp(outputDirectory, sitePackageDirectory, { recursive: true });

for (const item of packages) {
  console.log(`${item.name}@${item.version} -> ${item.file}`);
}
