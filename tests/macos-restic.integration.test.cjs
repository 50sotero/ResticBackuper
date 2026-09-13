'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { promises: fs } = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { MacBackupService } = require('../desktop/macos/service.cjs');

const resticPath = process.env.REWINDLE_RESTIC_PATH || process.env.RESTIC_BINARY || '';
const runReal = process.env.REWINDLE_REAL_RESTIC === '1' && process.platform === 'darwin' && Boolean(resticPath);

test('real Restic can initialize, back up, verify a canary, and restore into an independent empty folder', {
  skip: !runReal ? 'Set REWINDLE_REAL_RESTIC=1 and REWINDLE_RESTIC_PATH on macOS to run the integration check.' : false,
}, async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'rewindle-restic-integration-'));
  const source = path.join(root, 'source');
  const repository = path.join(root, 'repository');
  const dataDir = path.join(root, 'state');
  const destination = path.join(root, 'independent-restore');
  await fs.mkdir(source, { recursive: true });
  await fs.writeFile(path.join(source, 'fixture.txt'), 'Rewindle real Restic integration\n');
  const recoveryKey = path.join(root, 'recovery-key.txt');
  const ui = {
    chooseDirectories: async (options) => options.multiple ? [source] : repository,
    confirm: async () => true,
    input: async () => '',
    saveText: async (options) => {
      const target = options.defaultPath.includes('RecoveryKey') ? recoveryKey : path.join(root, 'diagnostics.json');
      await fs.writeFile(target, options.text || options.content || '', { mode: 0o600 });
      return target;
    },
  };
  const encryptString = (value) => Buffer.from(value, 'utf8');
  const decryptString = (value) => Buffer.from(value).toString('utf8');
  const service = new MacBackupService({
    platform: 'darwin',
    dataDir,
    resticPath,
    encryptString,
    decryptString,
    ui,
  });
  await service.initialize();
  const state = await service.execute('backupNow');
  assert.equal(state.status.success, true);
  assert.equal(state.history[0].result, 'Verified');
  assert.ok(state.history[0].snapshot);
  const restored = await service.execute('openRestore', {
    snapshotId: state.history[0].snapshot,
    destination,
  });
  assert.equal(restored.status.success, true);
  async function findFixture(directory) {
    for (const entry of await fs.readdir(directory, { withFileTypes: true })) {
      const candidate = path.join(directory, entry.name);
      if (entry.isFile() && entry.name === 'fixture.txt') return candidate;
      if (entry.isDirectory()) {
        const nested = await findFixture(candidate);
        if (nested) return nested;
      }
    }
    return null;
  }
  const restoredFile = await findFixture(destination);
  assert.ok(restoredFile);
  assert.equal(await fs.readFile(restoredFile, 'utf8'), 'Rewindle real Restic integration\n');
});
