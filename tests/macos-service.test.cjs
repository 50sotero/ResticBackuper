'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { EventEmitter } = require('node:events');
const { PassThrough } = require('node:stream');
const { promises: fs } = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const crypto = require('node:crypto');

const {
  MacBackupService,
  UnsupportedPlatformError,
} = require('../desktop/macos/service.cjs');
const {
  MacLaunchAgentScheduler,
  buildLaunchAgentPlist,
  validateScheduleTime,
} = require('../desktop/macos/scheduler.cjs');

async function tempDir(prefix) {
  return fs.mkdtemp(path.join(os.tmpdir(), prefix));
}

function uiFixture(root, answers = {}) {
  const calls = [];
  return {
    calls,
    async chooseDirectories(options) {
      calls.push(['chooseDirectories', options]);
      return options.multiple ? (answers.sources || [path.join(root, 'source')]) : (answers.repository || path.join(root, 'repository'));
    },
    async confirm(options) {
      calls.push(['confirm', options]);
      return true;
    },
    async input(options) {
      calls.push(['input', options]);
      return answers.password || '';
    },
    async saveText(options) {
      calls.push(['saveText', options]);
      if (options.defaultPath.includes('RecoveryKey')) {
        const target = path.join(root, 'recovery-key.txt');
        await fs.writeFile(target, options.text, { mode: 0o600 });
        return target;
      }
      const target = path.join(root, 'diagnostics.json');
      await fs.writeFile(target, options.text, { mode: 0o600 });
      return target;
    },
  };
}

function fakeResticRunner({ root, keepBackupOpen = false } = {}) {
  const calls = [];
  let backupChild = null;
  let snapshotNumber = 0;
  const runner = (executable, args, options) => {
    const child = new EventEmitter();
    child.stdout = new PassThrough();
    child.stderr = new PassThrough();
    calls.push({ executable, args: [...args], options, child });
    child.kill = () => {
      if (child !== backupChild) return false;
      child.stdout.end();
      child.stderr.end();
      queueMicrotask(() => child.emit('close', null, 'SIGINT'));
      return true;
    };
    queueMicrotask(async () => {
      const command = args[2];
      if (command === 'backup') {
        backupChild = child;
        snapshotNumber += 1;
        const snapshot = `snapshot-${String(snapshotNumber).padStart(8, '0')}`;
        child.stdout.write(`${JSON.stringify({ message_type: 'status', percent_done: 0.5, total_files_processed: 1, total_bytes_processed: 5 })}\n`);
        child.stdout.write(`${JSON.stringify({ message_type: 'summary', snapshot_id: snapshot, total_files_processed: 1, total_bytes_processed: 5, data_added: 5, duration: 0.25 })}\n`);
        if (keepBackupOpen) return;
        child.stdout.end();
        child.stderr.end();
        child.emit('close', 0, null);
        return;
      }
      if (command === 'restore') {
        const target = args[args.indexOf('--target') + 1];
        const config = JSON.parse(await fs.readFile(path.join(root, 'Proofhold', 'config.json'), 'utf8').catch(() => '{}'));
        const source = config.canaryPath;
        if (target && source) {
          await fs.mkdir(target, { recursive: true });
          await fs.copyFile(source, path.join(target, path.basename(source))).catch(() => undefined);
        }
      }
      child.stdout.write(`${JSON.stringify({ message_type: 'summary' })}\n`);
      child.stdout.end();
      child.stderr.end();
      child.emit('close', 0, null);
    });
    return child;
  };
  runner.calls = calls;
  return runner;
}

async function makeConfiguredService(root, runner) {
  const source = path.join(root, 'source');
  const repository = path.join(root, 'repository');
  await fs.mkdir(source, { recursive: true });
  await fs.mkdir(repository, { recursive: true });
  const dataDir = path.join(root, 'Proofhold');
  await fs.mkdir(dataDir, { recursive: true });
  const password = 'unit-test-proofhold-password';
  const encryptString = (value) => Buffer.from(value, 'utf8');
  const decryptString = (value) => Buffer.from(value).toString('utf8');
  await fs.writeFile(path.join(dataDir, 'credential.json'), JSON.stringify({ version: 1, encryptedPassword: Buffer.from(password).toString('base64') }), { mode: 0o600 });
  await fs.writeFile(path.join(dataDir, 'config.json'), JSON.stringify({
    version: 1,
    repository,
    sources: [source],
    canaryPath: '',
    canaryHash: '',
    schedule: { enabled: true, time: '02:00' },
  }), { mode: 0o600 });
  const ui = uiFixture(root, { password });
  const service = new MacBackupService({
    platform: 'darwin',
    dataDir,
    resticPath: '/Applications/Proofhold.app/Contents/Resources/restic',
    encryptString,
    decryptString,
    spawnProcess: runner,
    ui,
  });
  await service.initialize();
  return { service, ui, dataDir, source, repository };
}

test('macOS service rejects accidental use on another platform', () => {
  assert.throws(() => new MacBackupService({ platform: 'win32', dataDir: '/tmp/proofhold' }), (error) => {
    assert.ok(error instanceof UnsupportedPlatformError);
    assert.equal(error.code, 'UNSUPPORTED_PLATFORM');
    return true;
  });
  assert.throws(() => new MacLaunchAgentScheduler({ platform: 'linux', executablePath: '/tmp/proofhold' }), /requires macOS/);
});

test('first-run setup saves an encrypted credential, recovery key, canary, and initializes Restic without a password argument', async () => {
  const root = await tempDir('proofhold-first-run-');
  const source = path.join(root, 'source');
  await fs.mkdir(source, { recursive: true });
  await fs.writeFile(path.join(source, 'hello.txt'), 'hello');
  const calls = [];
  const runner = fakeResticRunner({ root });
  const ui = uiFixture(root, { sources: [source], repository: path.join(root, 'repository') });
  const service = new MacBackupService({
    platform: 'darwin',
    dataDir: path.join(root, 'Proofhold'),
    resticPath: '/Applications/Proofhold.app/Contents/Resources/restic',
    encryptString: (value) => { calls.push(['encrypt', value]); return Buffer.from(`encrypted:${value}`); },
    decryptString: (value) => Buffer.from(value).toString('utf8').replace(/^encrypted:/, ''),
    spawnProcess: runner,
    ui,
  });
  await service.initialize();
  const state = await service.execute('backupNow');
  assert.equal(state.status.success, true);
  assert.equal(state.history.length, 1);
  assert.equal(state.history[0].result, 'Verified');
  assert.ok(calls[0][1]);
  const config = JSON.parse(await fs.readFile(path.join(root, 'Proofhold', 'config.json'), 'utf8'));
  assert.equal(config.sources[0], source);
  assert.ok(config.canaryPath);
  assert.ok(config.canaryHash);
  assert.equal(await fs.access(path.join(root, 'recovery-key.txt')).then(() => true), true);
  const credential = JSON.parse(await fs.readFile(path.join(root, 'Proofhold', 'credential.json'), 'utf8'));
  assert.match(credential.encryptedPassword, /^[A-Za-z0-9+/]+=*$/);
  assert.equal(runner.calls.every((call) => !call.args.includes('encrypted:')), true);
  assert.equal(runner.calls.every((call) => !call.args.some((arg) => arg.includes('unit-test'))), true);
  assert.equal(runner.calls.every((call) => typeof call.options.env.RESTIC_PASSWORD_FILE === 'string'), true);
  assert.equal(await fs.access(runner.calls[0].options.env.RESTIC_PASSWORD_FILE).then(() => true).catch(() => false), false);
});

test('backup cancellation sends SIGINT to the active Restic child and records a cancelled run', async () => {
  const root = await tempDir('proofhold-cancel-');
  const runner = fakeResticRunner({ root, keepBackupOpen: true });
  const { service } = await makeConfiguredService(root, runner);
  const running = service.execute('backupNow');
  for (let i = 0; i < 30 && !runner.calls.some((call) => call.args[2] === 'backup'); i += 1) await new Promise((resolve) => setTimeout(resolve, 5));
  assert.ok(runner.calls.some((call) => call.args[2] === 'backup'));
  await service.execute('cancelBackup');
  const state = await running;
  assert.equal(state.status.cancelled, true);
  assert.equal(state.history[0].result, 'Cancelled');
});

test('restore rejects non-empty and overlapping destinations before invoking Restic', async () => {
  const root = await tempDir('proofhold-restore-');
  const runner = fakeResticRunner({ root });
  const { service, source } = await makeConfiguredService(root, runner);
  const nonEmpty = path.join(root, 'non-empty');
  await fs.mkdir(nonEmpty, { recursive: true });
  await fs.writeFile(path.join(nonEmpty, 'existing.txt'), 'keep');
  await assert.rejects(() => service.execute('openRestore', { snapshotId: '12345678', destination: nonEmpty }), /must be empty/);
  await assert.rejects(() => service.execute('openRestore', { snapshotId: '12345678', destination: source }), /overlap/);
  const linkedDestination = path.join(root, 'source-link');
  const linked = await fs.symlink(source, linkedDestination, 'dir').then(() => true).catch(() => false);
  if (linked) {
    await assert.rejects(() => service.execute('openRestore', { snapshotId: '12345678', destination: linkedDestination }), /overlap/);
  }
  assert.equal(runner.calls.some((call) => call.args[2] === 'restore'), false);
});

test('scheduler writes an escaped user LaunchAgent with exact HH:mm and safe argument arrays', async () => {
  assert.equal(validateScheduleTime('02:05'), '02:05');
  assert.throws(() => validateScheduleTime('2:05'), /HH:mm/);
  const plist = buildLaunchAgentPlist({ executablePath: '/Applications/Proofhold.app/Contents/MacOS/Proofhold', time: '23:45', workingDirectory: '/Users/alice/Proofhold & data' });
  assert.match(plist, /<integer>23<\/integer>/);
  assert.match(plist, /<integer>45<\/integer>/);
  assert.match(plist, /Proofhold &amp; data/);
  assert.match(plist, /--scheduled-backup/);
  assert.doesNotMatch(plist, /RESTIC_PASSWORD/);

  const root = await tempDir('proofhold-launchagent-');
  const launcherCalls = [];
  const fakeSpawn = (executable, args) => {
    launcherCalls.push({ executable, args });
    const child = new EventEmitter();
    child.stdout = new PassThrough();
    child.stderr = new PassThrough();
    queueMicrotask(() => child.emit('close', 0, null));
    return child;
  };
  const scheduler = new MacLaunchAgentScheduler({
    platform: 'darwin',
    homeDirectory: root,
    executablePath: '/Applications/Proofhold.app/Contents/MacOS/Proofhold',
    dataDir: path.join(root, 'data'),
    uid: 501,
    spawnProcess: fakeSpawn,
  });
  const result = await scheduler.installDailyLaunchAgent({ time: '23:45' });
  assert.ok(result.path.endsWith('com.proofhold.backup.plist'));
  assert.deepEqual(launcherCalls.at(-1).args, ['bootstrap', 'gui/501', result.path]);
  const installed = await scheduler.readDailyLaunchAgent();
  assert.deepEqual(installed.time, '23:45');
  await scheduler.removeDailyLaunchAgent();
  await assert.rejects(() => fs.access(result.path));
});
