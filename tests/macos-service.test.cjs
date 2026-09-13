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

test('Restic waits for pipe closure before returning the final snapshot summary', async () => {
  const root = await tempDir('rewindle-pipe-close-');
  const child = new EventEmitter();
  child.stdout = new PassThrough();
  child.stderr = new PassThrough();
  const service = new MacBackupService({ platform: 'darwin', dataDir: root, resticPath: path.join(root, 'restic'), spawnProcess: () => child });
  let resolved = false;
  const result = service._runRestic(['-r', root, 'backup']).then(value => { resolved = true; return value; });
  child.emit('exit', 0, null);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(resolved, false, 'the process exit event must not discard unread stdout');
  child.stdout.end('{"message_type":"summary","snapshot_id":"abc12345"}\n');
  child.stderr.end();
  child.emit('close', 0, null);
  assert.match((await result).stdout, /abc12345/);
  await fs.rm(root, { recursive: true, force: true });
});

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
        const config = JSON.parse(await fs.readFile(path.join(root, 'Rewindle', 'config.json'), 'utf8').catch(() => '{}'));
        const source = config.canaryPath;
        if (target && source) {
          await fs.mkdir(target, { recursive: true });
          const relative = path.relative(path.parse(source).root, source);
          await fs.copyFile(source, path.join(target, relative)).catch(async () => {
            await fs.mkdir(path.join(target, path.dirname(relative)), { recursive: true }).catch(() => undefined);
            await fs.copyFile(source, path.join(target, relative)).catch(() => undefined);
          });
        }
      }
      if (command === 'snapshots') {
        child.stdout.write(`${JSON.stringify([{ id: '1234567890abcdef', short_id: '12345678', time: '2026-09-13T02:00:00Z', hostname: 'test-host', paths: ['/tmp/source'], tags: ['rewindle'] }])}\n`);
        child.stdout.end();
        child.stderr.end();
        child.emit('close', 0, null);
        return;
      }
      if (command === 'ls') {
        child.stdout.write(`${JSON.stringify({ message_type: 'snapshot', struct_type: 'snapshot', id: '1234567890abcdef' })}\n`);
        child.stdout.write(`${JSON.stringify({ message_type: 'node', struct_type: 'node', name: 'hello.txt', type: 'file', path: '/tmp/source/hello.txt', size: 5, mtime: '2026-09-13T02:00:00Z' })}\n`);
        child.stdout.end();
        child.stderr.end();
        child.emit('close', 0, null);
        return;
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
  const dataDir = path.join(root, 'Rewindle');
  await fs.mkdir(dataDir, { recursive: true });
  const canaryPath = path.join(dataDir, 'canary', 'rewindle-canary.txt');
  await fs.mkdir(path.dirname(canaryPath), { recursive: true });
  const canaryContent = 'rewindle-test-canary';
  await fs.writeFile(canaryPath, canaryContent, { mode: 0o600 });
  const canaryHash = crypto.createHash('sha256').update(canaryContent).digest('hex');
  const password = 'unit-test-rewindle-password';
  const encryptString = (value) => Buffer.from(value, 'utf8');
  const decryptString = (value) => Buffer.from(value).toString('utf8');
  await fs.writeFile(path.join(dataDir, 'credential.json'), JSON.stringify({ version: 1, encryptedPassword: Buffer.from(password).toString('base64') }), { mode: 0o600 });
  await fs.writeFile(path.join(dataDir, 'config.json'), JSON.stringify({
    version: 1,
    repository,
    sources: [source],
    canaryPath,
    canaryHash,
    schedule: { enabled: true, time: '02:00' },
  }), { mode: 0o600 });
  const ui = uiFixture(root, { password });
  const service = new MacBackupService({
    platform: 'darwin',
    dataDir,
    resticPath: '/Applications/Rewindle.app/Contents/Resources/restic',
    encryptString,
    decryptString,
    spawnProcess: runner,
    ui,
    enforceUnixPermissions: false,
  });
  await service.initialize();
  return { service, ui, dataDir, source, repository };
}

test('macOS service rejects accidental use on another platform', () => {
    assert.throws(() => new MacBackupService({ platform: 'win32', dataDir: '/tmp/rewindle' }), (error) => {
    assert.ok(error instanceof UnsupportedPlatformError);
    assert.equal(error.code, 'UNSUPPORTED_PLATFORM');
    return true;
  });
  assert.throws(() => new MacLaunchAgentScheduler({ platform: 'linux', executablePath: '/tmp/rewindle' }), /requires macOS/);
});

test('first-run setup saves an encrypted credential, recovery key, canary, and initializes Restic without a password argument', async () => {
  const root = await tempDir('rewindle-first-run-');
  const source = path.join(root, 'source');
  await fs.mkdir(source, { recursive: true });
  await fs.writeFile(path.join(source, 'hello.txt'), 'hello');
  const calls = [];
  const runner = fakeResticRunner({ root });
  const ui = uiFixture(root, { sources: [source], repository: path.join(root, 'repository') });
  const service = new MacBackupService({
    platform: 'darwin',
    dataDir: path.join(root, 'Rewindle'),
    resticPath: '/Applications/Rewindle.app/Contents/Resources/restic',
    encryptString: (value) => { calls.push(['encrypt', value]); return Buffer.from(`encrypted:${value}`); },
    decryptString: (value) => Buffer.from(value).toString('utf8').replace(/^encrypted:/, ''),
    spawnProcess: runner,
    ui,
    enforceUnixPermissions: false,
  });
  await service.initialize();
  const previousPassword = process.env.RESTIC_PASSWORD;
  const previousPasswordFile = process.env.RESTIC_PASSWORD_FILE;
  const previousPasswordCommand = process.env.RESTIC_PASSWORD_COMMAND;
  process.env.RESTIC_PASSWORD = 'must-not-reach-restic';
  process.env.RESTIC_PASSWORD_FILE = path.join(root, 'inherited-password-file');
  process.env.RESTIC_PASSWORD_COMMAND = 'echo must-not-reach-restic';
  let state;
  try {
    state = await service.execute('backupNow');
  } finally {
    if (previousPassword === undefined) delete process.env.RESTIC_PASSWORD;
    else process.env.RESTIC_PASSWORD = previousPassword;
    if (previousPasswordFile === undefined) delete process.env.RESTIC_PASSWORD_FILE;
    else process.env.RESTIC_PASSWORD_FILE = previousPasswordFile;
    if (previousPasswordCommand === undefined) delete process.env.RESTIC_PASSWORD_COMMAND;
    else process.env.RESTIC_PASSWORD_COMMAND = previousPasswordCommand;
  }
  assert.equal(state.status.success, true);
  assert.equal(state.history.length, 1);
  assert.equal(state.history[0].result, 'Verified');
  assert.ok(calls[0][1]);
  const config = JSON.parse(await fs.readFile(path.join(root, 'Rewindle', 'config.json'), 'utf8'));
  assert.equal(config.sources[0], source);
  assert.ok(config.canaryPath);
  assert.ok(config.canaryHash);
  assert.equal(await fs.access(path.join(root, 'recovery-key.txt')).then(() => true), true);
  const credential = JSON.parse(await fs.readFile(path.join(root, 'Rewindle', 'credential.json'), 'utf8'));
  assert.match(credential.encryptedPassword, /^[A-Za-z0-9+/]+=*$/);
  assert.equal(runner.calls.every((call) => !call.args.includes('encrypted:')), true);
  assert.equal(runner.calls.every((call) => !call.args.some((arg) => arg.includes('unit-test'))), true);
  assert.equal(runner.calls.every((call) => typeof call.options.env.RESTIC_PASSWORD_FILE === 'string'), true);
  assert.equal(runner.calls.every((call) => call.options.env.RESTIC_PASSWORD_FILE !== path.join(root, 'inherited-password-file')), true);
  assert.equal(runner.calls.every((call) => call.options.env.RESTIC_PASSWORD === undefined), true);
  assert.equal(runner.calls.every((call) => call.options.env.RESTIC_PASSWORD_COMMAND === undefined), true);
  const checkCall = runner.calls.find((call) => call.args[2] === 'check');
  assert.ok(checkCall.args.includes('--read-data-subset=5%'));
  assert.deepEqual(runner.calls.map((call) => call.args[2]), ['init', 'backup', 'check', 'restore']);
  const restoreCall = runner.calls.find((call) => call.args[2] === 'restore');
  assert.ok(restoreCall.args.includes('--verify'));
  assert.ok(restoreCall.args.includes('--include'));
  const configuredCanary = config.canaryPath;
  assert.equal(restoreCall.args[restoreCall.args.indexOf('--include') + 1], configuredCanary);
  assert.equal(await fs.access(runner.calls[0].options.env.RESTIC_PASSWORD_FILE).then(() => true).catch(() => false), false);
});

test('backup fails closed when the configured canary is missing or has the wrong hash', async () => {
  for (const [label, mutate] of [
    ['missing', (config) => { config.canaryPath = ''; }],
    ['wrong hash', (config) => { config.canaryHash = '0'.repeat(64); }],
  ]) {
    const root = await tempDir(`rewindle-canary-${label.replace(/\s+/g, '-')}-`);
    const runner = fakeResticRunner({ root });
    const { service, dataDir } = await makeConfiguredService(root, runner);
    const configPath = path.join(dataDir, 'config.json');
    const config = JSON.parse(await fs.readFile(configPath, 'utf8'));
    mutate(config);
    await fs.writeFile(configPath, `${JSON.stringify(config)}\n`);
    await service.refresh();
    await assert.rejects(() => service.execute('backupNow'), /canary/i, label);
    assert.equal(runner.calls.some((call) => call.args[2] === 'backup'), false, label);
  }
});

test('Restic command execution rejects unknown commands before spawning a child', async () => {
  const root = await tempDir('rewindle-command-guard-');
  const runner = fakeResticRunner({ root });
  const { service, repository } = await makeConfiguredService(root, runner);
  await assert.rejects(
    () => service._runRestic(['-r', repository, 'prune'], {}),
    /Unsupported or unsafe Restic command/,
  );
  assert.equal(runner.calls.length, 0);
});

test('startup cleanup removes only stale owned runtime password files', async () => {
  const root = await tempDir('rewindle-runtime-cleanup-');
  const dataDir = path.join(root, 'Rewindle');
  const runtimeDir = path.join(dataDir, '.runtime');
  await fs.mkdir(runtimeDir, { recursive: true });
  const stale = path.join(runtimeDir, `password-999999999-${'a'.repeat(24)}`);
  const active = path.join(runtimeDir, `password-${process.pid}-${'b'.repeat(24)}`);
  const unrelated = path.join(runtimeDir, 'keep-this-file');
  await Promise.all([
    fs.writeFile(stale, 'stale'),
    fs.writeFile(active, 'active'),
    fs.writeFile(unrelated, 'unrelated'),
  ]);
  const service = new MacBackupService({
    platform: 'darwin',
    dataDir,
    resticPath: path.join(root, 'restic'),
    enforceUnixPermissions: false,
  });
  await service.initialize();
  await assert.rejects(() => fs.access(stale));
  assert.equal(await fs.access(active).then(() => true), true);
  assert.equal(await fs.access(unrelated).then(() => true), true);
});

test('first-run setup refuses a non-empty repository before saving credentials or initializing Restic', async () => {
  const root = await tempDir('rewindle-non-empty-repository-');
  const source = path.join(root, 'source');
  const repository = path.join(root, 'repository');
  await fs.mkdir(source, { recursive: true });
  await fs.mkdir(repository, { recursive: true });
  await fs.writeFile(path.join(repository, 'existing-data'), 'keep');
  const runner = fakeResticRunner({ root });
  const ui = uiFixture(root, { sources: [source], repository });
  const service = new MacBackupService({
    platform: 'darwin',
    dataDir: path.join(root, 'Rewindle'),
    resticPath: path.join(root, 'restic'),
    encryptString: (value) => Buffer.from(value),
    decryptString: (value) => Buffer.from(value).toString('utf8'),
    spawnProcess: runner,
    ui,
    enforceUnixPermissions: false,
  });
  await service.initialize();
  await assert.rejects(() => service.execute('backupNow'), /empty repository/);
  assert.equal(runner.calls.length, 0);
  assert.equal(ui.calls.some(([name]) => name === 'saveText'), false);
  await assert.rejects(() => fs.access(path.join(path.join(root, 'Rewindle'), 'credential.json')));
});

test('first-run setup leaves an unsafe recovery key for explicit user cleanup and does not initialize', async () => {
  const root = await tempDir('rewindle-recovery-placement-');
  const source = path.join(root, 'source');
  const dataDir = path.join(root, 'Rewindle');
  await fs.mkdir(source, { recursive: true });
  await fs.mkdir(dataDir, { recursive: true });
  const unsafePath = path.join(dataDir, 'unsafe-recovery-key.txt');
  const runner = fakeResticRunner({ root });
  const ui = uiFixture(root, { sources: [source], repository: path.join(root, 'repository') });
  ui.saveText = async (options) => {
    ui.calls.push(['saveText', options]);
    await fs.writeFile(unsafePath, options.text, { mode: 0o600 });
    return unsafePath;
  };
  const service = new MacBackupService({
    platform: 'darwin',
    dataDir,
    resticPath: path.join(root, 'restic'),
    encryptString: (value) => Buffer.from(value),
    decryptString: (value) => Buffer.from(value).toString('utf8'),
    spawnProcess: runner,
    ui,
    enforceUnixPermissions: false,
  });
  await service.initialize();
  await assert.rejects(() => service.execute('backupNow'), /unsafe location/);
  assert.equal(await fs.access(unsafePath).then(() => true), true);
  assert.equal(runner.calls.length, 0);
  assert.ok(ui.calls.find(([name, options]) => name === 'saveText' && options.blockedPaths.includes(dataDir)));
  await assert.rejects(() => fs.access(path.join(dataDir, 'credential.json')));
});

test('first-run setup removes an unverified LaunchAgent instead of persisting a misleading daily schedule', async () => {
  const root = await tempDir('rewindle-unverified-schedule-');
  const source = path.join(root, 'source');
  const dataDir = path.join(root, 'Rewindle');
  await fs.mkdir(source, { recursive: true });
  const runner = fakeResticRunner({ root });
  const ui = uiFixture(root, { sources: [source], repository: path.join(root, 'repository') });
  const schedulerCalls = [];
  const scheduler = {
    async installDailyLaunchAgent(options) { schedulerCalls.push(['install', options]); },
    async readDailyLaunchAgent() {
      schedulerCalls.push(['read']);
      return { enabled: false, loaded: false, verified: false, time: '02:00', detail: 'LaunchAgent is not loaded.' };
    },
    async removeDailyLaunchAgent() { schedulerCalls.push(['remove']); },
  };
  const service = new MacBackupService({
    platform: 'darwin',
    dataDir,
    resticPath: path.join(root, 'restic'),
    encryptString: (value) => Buffer.from(value),
    decryptString: (value) => Buffer.from(value).toString('utf8'),
    spawnProcess: runner,
    scheduler,
    ui,
    enforceUnixPermissions: false,
  });
  await service.initialize();
  await service._ensureConfigured();
  assert.deepEqual(schedulerCalls.map(([name]) => name), ['install', 'read', 'remove']);
  const config = JSON.parse(await fs.readFile(path.join(dataDir, 'config.json'), 'utf8'));
  assert.equal(config.schedule.enabled, false);
});

test('setup rejects repository and source selections that overlap Rewindle data', async () => {
  const root = await tempDir('rewindle-overlap-');
  const dataDir = path.join(root, 'Rewindle');
  const source = path.join(root, 'source');
  await fs.mkdir(dataDir, { recursive: true });
  await fs.mkdir(source, { recursive: true });
  const runner = fakeResticRunner({ root });
  const ui = uiFixture(root, { sources: [dataDir], repository: path.join(root, 'repository') });
  const service = new MacBackupService({
    platform: 'darwin',
    dataDir,
    resticPath: path.join(root, 'restic'),
    encryptString: (value) => Buffer.from(value),
    decryptString: (value) => Buffer.from(value).toString('utf8'),
    spawnProcess: runner,
    ui,
    enforceUnixPermissions: false,
  });
  await service.initialize();
  await assert.rejects(() => service.execute('backupNow'), /application data/);

  const repositoryUi = uiFixture(root, { sources: [source], repository: dataDir });
  const repositoryService = new MacBackupService({
    platform: 'darwin',
    dataDir,
    resticPath: path.join(root, 'restic'),
    encryptString: (value) => Buffer.from(value),
    decryptString: (value) => Buffer.from(value).toString('utf8'),
    spawnProcess: runner,
    ui: repositoryUi,
    enforceUnixPermissions: false,
  });
  await repositoryService.initialize();
  await assert.rejects(() => repositoryService.execute('backupNow'), /application data/);
  assert.equal(runner.calls.length, 0);
});

test('setup rejects a protected folder that is a symbolic link', async () => {
  const root = await tempDir('rewindle-source-link-');
  const realSource = path.join(root, 'real-source');
  const linkedSource = path.join(root, 'linked-source');
  await fs.mkdir(realSource, { recursive: true });
  const linked = await fs.symlink(realSource, linkedSource, 'dir').then(() => true).catch(() => false);
  if (!linked) return;
  const runner = fakeResticRunner({ root });
  const ui = uiFixture(root, { sources: [linkedSource], repository: path.join(root, 'repository') });
  const service = new MacBackupService({
    platform: 'darwin',
    dataDir: path.join(root, 'Rewindle'),
    resticPath: path.join(root, 'restic'),
    encryptString: (value) => Buffer.from(value),
    decryptString: (value) => Buffer.from(value).toString('utf8'),
    spawnProcess: runner,
    ui,
    enforceUnixPermissions: false,
  });
  await service.initialize();
  await assert.rejects(() => service.execute('backupNow'), /symbolic link/);
  assert.equal(runner.calls.length, 0);
});

test('readiness reports snapshot listing and explicitly limits the read-only check', async () => {
  const root = await tempDir('rewindle-readiness-');
  const runner = fakeResticRunner({ root });
  const { service } = await makeConfiguredService(root, runner);
  const state = await service.execute('checkReadiness');
  assert.equal(state.recovery.title, 'Readiness checks passed');
  assert.match(state.recovery.detail, /snapshot listing passed/);
  assert.match(state.recovery.detail, /does not run a restore drill/);
  assert.equal(runner.calls.filter((call) => call.args[2] === 'snapshots').length, 1);
  assert.equal(runner.calls.some((call) => call.args[2] === 'restore'), false);
});

test('backup cancellation sends SIGINT to the active Restic child and records a cancelled run', async () => {
  const root = await tempDir('rewindle-cancel-');
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
  const root = await tempDir('rewindle-restore-');
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
    await assert.rejects(() => service.execute('openRestore', { snapshotId: '12345678', destination: linkedDestination }), /overlap|symbolic link/);
  }
  assert.equal(runner.calls.some((call) => call.args[2] === 'restore'), false);
});

test('restore browser returns projected snapshot and entry data, and restore requests content verification', async () => {
  const root = await tempDir('rewindle-browser-');
  const runner = fakeResticRunner({ root });
  const { service } = await makeConfiguredService(root, runner);
  const snapshots = await service.listRestoreSnapshots();
  assert.deepEqual(snapshots[0], {
    id: '1234567890abcdef',
    shortId: '12345678',
    time: '2026-09-13T02:00:00Z',
    hostname: 'test-host',
    paths: ['/tmp/source'],
    tags: ['rewindle'],
  });
  const entries = await service.listRestoreEntries('1234567890abcdef');
  assert.equal(entries[0].path, '/tmp/source/hello.txt');
  assert.equal(entries[0].size, 5);
  const destination = path.join(root, 'restore');
  const restored = await service.execute('openRestore', { snapshotId: '1234567890abcdef', destination });
  assert.equal(restored.status.success, true);
  const restoreCall = runner.calls.find((call) => call.args[2] === 'restore');
  assert.ok(restoreCall.args.includes('--verify'));
});

test('scheduler writes an escaped user LaunchAgent with exact HH:mm and safe argument arrays', async () => {
  assert.equal(validateScheduleTime('02:05'), '02:05');
  assert.throws(() => validateScheduleTime('2:05'), /HH:mm/);
  const plist = buildLaunchAgentPlist({ executablePath: '/Applications/Rewindle.app/Contents/MacOS/Rewindle', time: '23:45', workingDirectory: '/Users/alice/Rewindle & data' });
  assert.match(plist, /<integer>23<\/integer>/);
  assert.match(plist, /<integer>45<\/integer>/);
  assert.match(plist, /Rewindle &amp; data/);
  assert.match(plist, /--scheduled-backup/);
  assert.doesNotMatch(plist, /RESTIC_PASSWORD/);

  const root = await tempDir('rewindle-launchagent-');
  const executable = path.join(root, 'Rewindle');
  await fs.writeFile(executable, '#!/bin/sh\n', { mode: 0o700 });
  await fs.chmod(executable, 0o700).catch(() => undefined);
  const launcherCalls = [];
  let launchAgentLoaded = true;
  const fakeSpawn = (executable, args) => {
    launcherCalls.push({ executable, args });
    const child = new EventEmitter();
    child.stdout = new PassThrough();
    child.stderr = new PassThrough();
    queueMicrotask(() => child.emit('close', args[0] === 'print' && !launchAgentLoaded ? 1 : 0, null));
    return child;
  };
  const scheduler = new MacLaunchAgentScheduler({
    platform: 'darwin',
    homeDirectory: root,
    executablePath: executable,
    dataDir: path.join(root, 'data'),
    uid: 501,
    spawnProcess: fakeSpawn,
  });
  const result = await scheduler.installDailyLaunchAgent({ time: '23:45' });
  assert.ok(result.path.endsWith('com.rewindle.backup.plist'));
  assert.deepEqual(launcherCalls.at(-1).args, ['bootstrap', 'gui/501', result.path]);
  const installed = await scheduler.readDailyLaunchAgent();
  assert.deepEqual(installed.time, '23:45');
  assert.equal(installed.loaded, true);
  assert.equal(installed.verified, true);
  launchAgentLoaded = false;
  const notLoaded = await scheduler.readDailyLaunchAgent();
  assert.equal(notLoaded.loaded, false);
  assert.equal(notLoaded.enabled, false);
  assert.equal(notLoaded.verified, false);
  await scheduler.removeDailyLaunchAgent();
  assert.deepEqual(launcherCalls.at(-1).args, ['bootout', 'gui/501/com.rewindle.backup']);
  await assert.rejects(() => fs.access(result.path));
});
