'use strict';

/**
 * Rewindle's macOS backup service.
 *
 * The service deliberately owns no UI toolkit.  The Electron preload injects
 * safeStorage wrappers, a small dialog API, and (in production) Electron's
 * child-process launcher.  Keeping those seams explicit makes this module
 * usable from a CLI and makes the security-sensitive code testable without
 * replacing Restic with a fake data model.
 */

const {
  EventEmitter,
} = require('node:events');
const {
  promises: fs,
} = require('node:fs');
const fsSync = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const crypto = require('node:crypto');
const childProcess = require('node:child_process');

const SERVICE_VERSION = 1;
const CONFIG_FILE = 'config.json';
const CREDENTIAL_FILE = 'credential.json';
const HISTORY_FILE = 'history.json';
const STATUS_FILE = 'status.json';
const CANARY_DIR = 'canary';
const RUNTIME_DIR = '.runtime';
const MAX_HISTORY = 100;
const RESTIC_COMMANDS = new Set(['init', 'backup', 'check', 'restore', 'snapshots', 'ls']);
const PASSWORD_ENV_KEYS = ['RESTIC_PASSWORD', 'RESTIC_PASSWORD_FILE', 'RESTIC_PASSWORD_COMMAND'];
const RECOVERY_KEY_NAME = 'Rewindle-RecoveryKey.txt';

const PAGE_NAMES = new Set(['Protection', 'Activity', 'Restore', 'Settings']);
const COMMAND_NAMES = new Set([
  'ready', 'refresh', 'navigate', 'setTheme', 'togglePreview', 'backupNow',
  'cancelBackup', 'addSource', 'removeSource', 'editSchedule',
  'changeRepository', 'repairRepository', 'reviewChanges', 'openRestore',
  'checkReadiness', 'selectRun', 'viewRunDetails', 'exportDiagnostics',
  'retrySourceChange', 'dismissSourceChange',
]);
const THEMES = new Set(['System', 'Midnight', 'Daylight']);
const DAY_NAMES = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

class UserCancelledError extends Error {
  constructor(message = 'The requested dialog was cancelled.') {
    super(message);
    this.name = 'UserCancelledError';
    this.code = 'USER_CANCELLED';
  }
}

class UnsupportedPlatformError extends Error {
  constructor() {
    super('Rewindle macOS backup service requires macOS.');
    this.name = 'UnsupportedPlatformError';
    this.code = 'UNSUPPORTED_PLATFORM';
  }
}

function asString(value) {
  return typeof value === 'string' ? value : '';
}

function finiteNumber(value, fallback = 0) {
  return Number.isFinite(Number(value)) ? Number(value) : fallback;
}

function clamp(value, low, high) {
  return Math.max(low, Math.min(high, value));
}

function isoNow() {
  return new Date().toISOString();
}

function formatBytes(value) {
  const bytes = Math.max(0, finiteNumber(value));
  if (bytes < 1024) return `${Math.round(bytes)} B`;
  const units = ['KiB', 'MiB', 'GiB', 'TiB'];
  let amount = bytes;
  let unit = 'B';
  for (const next of units) {
    amount /= 1024;
    unit = next;
    if (amount < 1024 || next === units.at(-1)) break;
  }
  return `${amount.toFixed(amount >= 10 ? 1 : 2)} ${unit}`;
}

function formatCount(value) {
  return Math.max(0, Math.round(finiteNumber(value))).toLocaleString('en-US');
}

function formatDuration(seconds) {
  const total = Math.max(0, Math.round(finiteNumber(seconds)));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const secs = total % 60;
  if (hours) return `${hours}h ${String(minutes).padStart(2, '0')}m`;
  return `${minutes}m ${String(secs).padStart(2, '0')}s`;
}

function displayDate(value) {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '—';
  return new Intl.DateTimeFormat('en-US', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date);
}

function parseJsonLine(line) {
  const trimmed = String(line || '').trim();
  if (!trimmed || !trimmed.startsWith('{')) return null;
  try {
    return JSON.parse(trimmed);
  } catch {
    return null;
  }
}

function isAbsolutePath(value) {
  return typeof value === 'string' && path.isAbsolute(value);
}

function normalizePath(value) {
  if (!isAbsolutePath(value)) throw new Error('A full macOS path is required.');
  return path.resolve(value);
}

function sameOrChild(candidate, parent) {
  const child = path.resolve(candidate);
  const root = path.resolve(parent);
  return child === root || child.startsWith(`${root}${path.sep}`);
}

function pathsOverlap(left, right) {
  return sameOrChild(left, right) || sameOrChild(right, left);
}

function isSha256(value) {
  return typeof value === 'string' && /^[a-f0-9]{64}$/i.test(value);
}

function normalizeSelection(value, multiple) {
  if (Array.isArray(value)) return value.filter((entry) => typeof entry === 'string' && entry);
  if (typeof value === 'string' && value) return multiple ? [value] : [value];
  if (value && Array.isArray(value.paths)) return value.paths.filter((entry) => typeof entry === 'string' && entry);
  if (value && typeof value.path === 'string' && value.path) return [value.path];
  return [];
}

function normalizeInput(value) {
  if (typeof value === 'string') return value;
  if (value && typeof value.value === 'string') return value.value;
  if (value && typeof value.path === 'string') return value.path;
  return '';
}

function randomId() {
  return crypto.randomBytes(12).toString('hex');
}

function randomPassword() {
  return crypto.randomBytes(32).toString('base64url');
}

function safeDisplayPath(value) {
  if (!value) return '—';
  const resolved = path.resolve(value);
  const home = os.homedir();
  if (resolved === home) return '~';
  if (resolved.startsWith(`${home}${path.sep}`)) return `~${resolved.slice(home.length)}`;
  return resolved;
}

function redactedPath(value) {
  if (!value) return '';
  return path.basename(value) || path.parse(value).root;
}

function normalizeAction(enabled, label, help, visible = true) {
  return { enabled: Boolean(enabled), visible: Boolean(visible), label, help };
}

function defaultActions() {
  return {
    ready: normalizeAction(true, 'Ready', 'The backup service is ready.'),
    refresh: normalizeAction(true, 'Refresh', 'Refresh backup status.'),
    navigate: normalizeAction(true, 'Navigate', 'Open a dashboard page.'),
    setTheme: normalizeAction(true, 'Appearance', 'Change the dashboard appearance.'),
    togglePreview: normalizeAction(true, 'Preview animations', 'Play the presentation animation without running a backup.'),
    backupNow: normalizeAction(true, 'Back up now', 'Start an encrypted Restic backup.'),
    cancelBackup: normalizeAction(false, 'Cancel backup', 'No backup is currently running.'),
    addSource: normalizeAction(false, 'Add folder', 'Choose another folder to protect.'),
    removeSource: normalizeAction(false, 'Remove folder', 'Remove this folder from future backups.'),
    editSchedule: normalizeAction(false, 'Edit schedule', 'Change the daily launchd schedule.'),
    changeRepository: normalizeAction(false, 'Change repository', 'Choose a different Restic repository.'),
    repairRepository: normalizeAction(false, 'Repair repository', 'Advanced repository repair is not available in this release.', false),
    reviewChanges: normalizeAction(false, 'Review changes', 'Destructive repository changes are not automated.', false),
    openRestore: normalizeAction(false, 'Open restore', 'Open the restore workflow.'),
    checkReadiness: normalizeAction(false, 'Check readiness', 'Check repository and recovery readiness.'),
    selectRun: normalizeAction(true, 'Select run', 'Select a backup run.'),
    viewRunDetails: normalizeAction(true, 'View details', 'Open backup run details.'),
    exportDiagnostics: normalizeAction(false, 'Export diagnostics', 'Save redacted diagnostic information.'),
    retrySourceChange: normalizeAction(false, 'Retry', 'Retry the folder change.'),
    dismissSourceChange: normalizeAction(false, 'Dismiss', 'Dismiss the folder change status.'),
  };
}

function validateTime(value) {
  const text = String(value || '').trim();
  if (!/^([01]\d|2[0-3]):[0-5]\d$/.test(text)) {
    throw new Error('Schedule time must use 24-hour HH:mm format.');
  }
  return text;
}

function nextDailyRun(time, now = new Date()) {
  const [hours, minutes] = validateTime(time).split(':').map(Number);
  const next = new Date(now);
  next.setHours(hours, minutes, 0, 0);
  if (next <= now) next.setDate(next.getDate() + 1);
  return next.toISOString();
}

function normalizeConfig(raw) {
  if (!raw || typeof raw !== 'object') return null;
  const repository = typeof raw.repository === 'string' ? normalizePath(raw.repository) : '';
  const sources = Array.isArray(raw.sources) ? raw.sources.filter((source) => typeof source === 'string' && isAbsolutePath(source)).map(normalizePath) : [];
  if (!repository || !sources.length) return null;
  const schedule = raw.schedule && typeof raw.schedule === 'object' ? raw.schedule : {};
  const time = schedule.time ? validateTime(schedule.time) : '02:00';
  const canaryPath = typeof raw.canaryPath === 'string' && isAbsolutePath(raw.canaryPath) ? normalizePath(raw.canaryPath) : '';
  return {
    version: SERVICE_VERSION,
    repository,
    sources: [...new Set(sources)],
    canaryPath,
    canaryHash: typeof raw.canaryHash === 'string' ? raw.canaryHash : '',
    schedule: {
      enabled: schedule.enabled !== false,
      time,
    },
    recoveryKeyPath: typeof raw.recoveryKeyPath === 'string' ? normalizePath(raw.recoveryKeyPath) : '',
    created: typeof raw.created === 'string' ? raw.created : isoNow(),
    updated: typeof raw.updated === 'string' ? raw.updated : isoNow(),
  };
}

class MacBackupService extends EventEmitter {
  constructor(options = {}) {
    super();
    this.platform = options.platform || process.platform;
    if (this.platform !== 'darwin') throw new UnsupportedPlatformError();

    this.dataDir = normalizePath(options.dataDir || path.join(os.homedir(), 'Library', 'Application Support', 'Rewindle'));
    this.resticPath = normalizePath(options.resticPath || path.join(options.appBundlePath || '', 'Contents', 'Resources', 'restic'));
    this.appBundlePath = options.appBundlePath || '';
    this.appExecutablePath = options.appExecutablePath || options.launchExecutable || '';
    this.encryptString = typeof options.encryptString === 'function' ? options.encryptString : null;
    this.decryptString = typeof options.decryptString === 'function' ? options.decryptString : null;
    this.enforceUnixPermissions = options.enforceUnixPermissions === undefined ? process.platform === 'darwin' : Boolean(options.enforceUnixPermissions);
    this.spawnProcess = options.spawnProcess || childProcess.spawn;
    this.ui = options.ui || {};
    this.scheduler = options.scheduler || null;
    if (!this.scheduler && this.appExecutablePath) {
      try {
        // Keep the dependency optional for unit tests and CLI consumers that
        // do not want to touch launchd.  The packaged macOS app gets a real
        // user LaunchAgent by default.
        const { MacLaunchAgentScheduler } = require('./scheduler.cjs');
        this.scheduler = new MacLaunchAgentScheduler({
          platform: this.platform,
          homeDirectory: options.homeDirectory || os.homedir(),
          executablePath: this.appExecutablePath,
          dataDir: this.dataDir,
          uid: options.uid,
          spawnProcess: this.spawnProcess,
        });
      } catch {
        this.scheduler = null;
      }
    }
    this._config = null;
    this._history = [];
    this._state = null;
    this._child = null;
    this._cancelRequested = false;
    this._operation = Promise.resolve();
    this._writeQueue = Promise.resolve();
    this._scheduleStatus = null;
    this._initialized = false;
  }

  async initialize() {
    await this._ensureDataDir();
    this._config = await this._readConfig();
    this._history = await this._readHistory();
    this._scheduleStatus = await this._readScheduleStatus();
    const persisted = await this._readJson(STATUS_FILE, null);
    this._state = this._buildState(persisted && persisted.page ? persisted.page : 'Protection');
    this._initialized = true;
    this._emitState();
    return this.getState();
  }

  getState() {
    if (!this._state) this._state = this._buildState('Protection');
    return structuredClone(this._state);
  }

  async execute(command, payload = {}) {
    if (!COMMAND_NAMES.has(command)) throw new Error(`Unsupported dashboard command: ${command}`);
    if (!this._initialized) await this.initialize();

    switch (command) {
      case 'ready': return this.getState();
      case 'refresh': return this.refresh();
      case 'navigate': return this._navigate(payload);
      case 'setTheme': return this._setTheme(payload);
      case 'togglePreview': return this._togglePreview();
      case 'backupNow': return this._queue('backupNow', () => this.backupNow(payload));
      case 'cancelBackup': return this.cancelBackup();
      case 'addSource': return this._queue('addSource', () => this.addSource());
      case 'removeSource': return this._queue('removeSource', () => this.removeSource(payload));
      case 'editSchedule': return this._queue('editSchedule', () => this.editSchedule(payload));
      case 'changeRepository': return this._unsupported('Changing repositories is not available in this release. Choose a new plan only after exporting your recovery key.');
      case 'repairRepository': return this._unsupported('Repository repair is intentionally unavailable in this release.');
      case 'reviewChanges': return this._unsupported('Destructive repository changes are intentionally unavailable in this release.');
      case 'openRestore': return payload.destination
        ? this._queue('restore', () => this.openRestore(payload))
        : this._openRestore(payload);
      case 'checkReadiness': return this._queue('checkReadiness', () => this.checkReadiness());
      case 'selectRun': return this._selectRun(payload);
      case 'viewRunDetails': return this._viewRunDetails(payload);
      case 'exportDiagnostics': return this._queue('exportDiagnostics', () => this.exportDiagnostics());
      case 'retrySourceChange': return this._retrySourceChange();
      case 'dismissSourceChange': return this._dismissSourceChange();
      default: throw new Error(`Unsupported dashboard command: ${command}`);
    }
  }

  async refresh() {
    if (!this._initialized) await this.initialize();
    this._config = await this._readConfig();
    this._history = await this._readHistory();
    this._scheduleStatus = await this._readScheduleStatus();
    this._state = this._buildState(this._state?.page || 'Protection');
    this._state.dataError = undefined;
    this._emitState();
    return this.getState();
  }

  async backupNow() {
    if (this._state.status.active) throw new Error('A backup is already running.');
    await this._ensureConfigured();
    if (!this._config || !this._config.sources.length) throw new Error('Choose at least one folder before starting a backup.');
    await this._validateConfiguredPaths();
    await this._validateCanary();
    const password = await this._loadPassword();
    const started = Date.now();
    const runId = randomId();
    this._cancelRequested = false;
    this._setState({
      status: this._status('active', 'Backup in progress', 'Restic is encrypting the selected folders.', {
        runId, phaseIndex: 1, phaseLabel: 'Backing up', progress: 0,
      }),
      sourceOperation: { active: false, stage: '', message: '', path: '' },
    });
    let result = null;
    let passwordFile = null;
    try {
      passwordFile = await this._createPasswordFile(password);
      const args = ['-r', this._config.repository, 'backup', '--json', '--tag', 'rewindle', ...this._config.sources];
      if (this._config.canaryPath) args.push(this._config.canaryPath);
      const events = [];
      result = await this._runRestic(args, {
        passwordFile,
        onJson: (event) => {
          events.push(event);
          this._consumeBackupEvent(event, runId);
        },
      });
      if (this._cancelRequested || result.cancelled) throw new CancelledOperationError();
      if (result.code !== 0) throw new Error(result.stderr.trim() || 'Restic backup failed.');
      const summary = [...events].reverse().find((event) => event.message_type === 'summary') || {};
      const snapshotId = asString(summary.snapshot_id);
      if (!snapshotId) throw new Error('Restic completed without returning a snapshot ID.');

      this._setState({
        status: this._status('active', 'Verifying backup', 'Checking repository structure, a 5% data subset, and restoring the canary file.', {
          runId, phaseIndex: 2, phaseLabel: 'Verifying', progress: 0.75,
        }),
      });
      let checkSummary = null;
      const checkResult = await this._runRestic(['-r', this._config.repository, 'check', '--json', '--read-data-subset=5%'], {
        passwordFile,
        onJson: (event) => {
          if (event.message_type === 'summary') checkSummary = event;
        },
      });
      if (checkResult.code !== 0 || finiteNumber(checkSummary?.num_errors) > 0) {
        throw new Error(checkResult.stderr.trim() || 'Repository verification reported errors.');
      }
      await this._verifyCanary(snapshotId, passwordFile);

      const durationSeconds = finiteNumber(summary.total_duration || summary.duration, (Date.now() - started) / 1000);
      const historyItem = this._makeHistoryItem({
        id: snapshotId,
        started: new Date(started).toISOString(),
        type: 'Backup',
        result: 'Verified',
        success: true,
        durationSeconds,
        files: finiteNumber(summary.total_files_processed),
        processedBytes: finiteNumber(summary.total_bytes_processed),
        storedBytes: finiteNumber(summary.data_added),
        snapshot: snapshotId,
      });
      await this._recordHistory(historyItem);
      this._setState({
        status: this._status('success', 'Backup verified', `${historyItem.filesDisplay} protected files are safely stored.`, {
          runId: snapshotId, phaseIndex: 3, phaseLabel: 'Verified', progress: 1,
          files: historyItem.filesDisplay, bytes: historyItem.processedDisplay,
        }),
      });
      return this.getState();
    } catch (error) {
      const cancelled = error instanceof CancelledOperationError || this._cancelRequested;
      const historyItem = this._makeHistoryItem({
        id: runId,
        started: new Date(started).toISOString(),
        type: 'Backup',
        result: cancelled ? 'Cancelled' : 'Failed',
        success: false,
        durationSeconds: (Date.now() - started) / 1000,
        files: 0,
        processedBytes: 0,
        storedBytes: 0,
        snapshot: '',
      });
      await this._recordHistory(historyItem);
      this._setState({
        status: this._status(cancelled ? 'cancelled' : 'failure', cancelled ? 'Backup cancelled' : 'Backup failed', cancelled ? 'The running Restic process was asked to stop.' : (error.message || 'Restic reported a backup error.'), {
          runId, phaseIndex: cancelled ? 0 : 1, phaseLabel: cancelled ? 'Cancelled' : 'Needs attention', progress: 0,
        }),
      });
      return this.getState();
    } finally {
      if (passwordFile) await this._removeRuntimeFile(passwordFile);
      this._child = null;
      this._cancelRequested = false;
    }
  }

  async cancelBackup() {
    if (!this._child) return this.getState();
    this._cancelRequested = true;
    try {
      if (typeof this._child.kill === 'function') this._child.kill('SIGINT');
    } catch {
      // The process may already have exited.  The close handler records the result.
    }
    this._emitState();
    return this.getState();
  }

  async addSource() {
    await this._ensureConfigured(false);
    const selected = normalizeSelection(await this._callUi('chooseDirectories', {
      title: 'Choose folders to protect',
      multiple: true,
    }), true);
    if (!selected.length) throw new UserCancelledError();
    const paths = await this._validateSourceSelection(selected);
    const sources = [...new Set([...this._config.sources, ...paths])];
    this._config = { ...this._config, sources, updated: isoNow() };
    await this._writeConfig(this._config);
    this._state = this._buildState(this._state.page);
    this._emitState();
    return this.getState();
  }

  async removeSource(payload = {}) {
    await this._ensureConfigured(false);
    const target = normalizePath(asString(payload.path || payload.source));
    if (!this._config.sources.includes(target)) throw new Error('That folder is not in the current backup plan.');
    if (this._config.sources.length <= 1) throw new Error('Keep at least one protected folder.');
    const confirmed = await this._callUi('confirm', {
      title: 'Remove protected folder?',
      message: `Remove ${safeDisplayPath(target)} from future backups?`,
      detail: 'Existing Restic snapshots remain untouched.',
    });
    if (!confirmed) throw new UserCancelledError();
    this._config = { ...this._config, sources: this._config.sources.filter((source) => source !== target), updated: isoNow() };
    await this._writeConfig(this._config);
    this._state = this._buildState(this._state.page);
    this._emitState();
    return this.getState();
  }

  async editSchedule(payload = {}) {
    await this._ensureConfigured(false);
    if (!this.scheduler || typeof this.scheduler.installDailyLaunchAgent !== 'function') {
      throw new Error('Automatic scheduling is unavailable on this macOS host.');
    }
    let time = payload.time ? validateTime(payload.time) : '';
    if (!time) {
      time = validateTime(normalizeInput(await this._callUi('input', {
        title: 'Daily backup schedule',
        label: 'Time (24-hour HH:mm)',
        type: 'time',
        value: this._config.schedule.time,
      })));
    }
    const previousConfig = this._config;
    try {
      await this.scheduler.installDailyLaunchAgent({ time });
      this._scheduleStatus = typeof this.scheduler.readDailyLaunchAgent === 'function'
        ? await this.scheduler.readDailyLaunchAgent()
        : null;
      if (!this._scheduleStatus?.enabled || !this._scheduleStatus.verified) {
        throw new Error('The daily LaunchAgent could not be verified after installation.');
      }
      this._config = { ...previousConfig, schedule: { enabled: true, time }, updated: isoNow() };
      await this._writeConfig(this._config);
    } catch (error) {
      this._config = previousConfig;
      try {
        if (previousConfig.schedule.enabled) {
          await this.scheduler.installDailyLaunchAgent({ time: previousConfig.schedule.time });
        } else if (typeof this.scheduler.removeDailyLaunchAgent === 'function') {
          await this.scheduler.removeDailyLaunchAgent();
        }
      } catch {
        // Keep the old in-memory configuration and surface the write failure.
      }
      this._scheduleStatus = await this._readScheduleStatus();
      throw error;
    }
    this._state = this._buildState(this._state.page);
    this._emitState();
    return this.getState();
  }

  async changeRepository() {
    return this._unsupported('Changing repositories is not available in this release. Choose a new plan only after exporting your recovery key.');
  }

  async checkReadiness() {
    const checks = [];
    checks.push({ label: 'Repository configured', ok: Boolean(this._config?.repository) });
    const credential = await this._readCredential();
    checks.push({ label: 'Encrypted credential available', ok: Boolean(credential) });
    let canaryOkay = false;
    try {
      await this._validateCanary();
      canaryOkay = true;
    } catch {
      canaryOkay = false;
    }
    checks.push({ label: 'Canary file and hash valid', ok: canaryOkay });
    const repositoryStat = this._config?.repository ? await fs.stat(this._config.repository).catch(() => null) : null;
    const repositoryLink = this._config?.repository ? await fs.lstat(this._config.repository).catch(() => null) : null;
    if (credential && repositoryStat?.isDirectory() && !repositoryLink?.isSymbolicLink()) {
      try {
        const password = await this._loadPassword(false);
        const passwordFile = await this._createPasswordFile(password);
        try {
          const result = await this._runRestic(['-r', this._config.repository, 'snapshots', '--json'], { passwordFile });
          checks.push({ label: 'Repository responds to snapshot listing', ok: result.code === 0 });
        } finally {
          await this._removeRuntimeFile(passwordFile);
        }
      } catch {
        checks.push({ label: 'Repository responds to snapshot listing', ok: false });
      }
    } else {
      checks.push({ label: 'Repository responds to snapshot listing', ok: false });
    }
    const ready = checks.every((check) => check.ok);
    this._state = this._buildState('Restore');
    this._state.recovery = {
      title: ready ? 'Readiness checks passed' : 'Recovery needs attention',
      detail: ready ? 'Encrypted credential, canary hash, and repository snapshot listing passed. This read-only check does not run a restore drill.' : checks.filter((check) => !check.ok).map((check) => check.label).join(' · '),
      repairNeeded: !ready,
      repairMessage: ready ? 'Run an independent restore drill before relying on this repository for recovery.' : 'Resolve the checks above before relying on this repository for recovery.',
      repairState: ready ? 'ready' : 'attention',
      repairBusy: false,
      repairPercent: null,
    };
    this._emitState();
    return this.getState();
  }

  /**
   * Read-only restore browser data for the Electron Restore Center.
   * Values are projected to the fields the UI needs; Restic's raw JSON is not
   * forwarded wholesale because it can contain host and username metadata.
   */
  async listRestoreSnapshots() {
    await this._ensureConfigured(false);
    const password = await this._loadPassword(false);
    const passwordFile = await this._createPasswordFile(password);
    try {
      const result = await this._runRestic(['-r', this._config.repository, 'snapshots', '--json'], { passwordFile });
      if (result.code !== 0) throw new Error(result.stderr.trim() || 'Restic could not list snapshots.');
      let parsed;
      try {
        parsed = JSON.parse(result.stdout.trim() || '[]');
      } catch {
        parsed = result.stdout.split(/\r?\n/).map(parseJsonLine).filter(Boolean);
      }
      const snapshots = Array.isArray(parsed) ? parsed : [];
      return snapshots.filter((snapshot) => typeof snapshot?.id === 'string').map((snapshot) => ({
        id: snapshot.id,
        shortId: typeof snapshot.short_id === 'string' ? snapshot.short_id : snapshot.id.slice(0, 8),
        time: typeof snapshot.time === 'string' ? snapshot.time : '',
        hostname: typeof snapshot.hostname === 'string' ? snapshot.hostname : '',
        paths: Array.isArray(snapshot.paths) ? snapshot.paths.filter((entry) => typeof entry === 'string') : [],
        tags: Array.isArray(snapshot.tags) ? snapshot.tags.filter((entry) => typeof entry === 'string') : [],
      }));
    } finally {
      await this._removeRuntimeFile(passwordFile);
    }
  }

  async listRestoreEntries(snapshotId, entryPath = '') {
    await this._ensureConfigured(false);
    const id = asString(snapshotId);
    if (!/^(latest|[a-z0-9]{8,64})$/i.test(id)) throw new Error('Invalid snapshot ID.');
    const requestedPath = asString(entryPath);
    if (requestedPath && (!requestedPath.startsWith('/') || requestedPath.includes('\u0000'))) {
      throw new Error('Restore browser paths must be absolute macOS paths.');
    }
    const password = await this._loadPassword(false);
    const passwordFile = await this._createPasswordFile(password);
    try {
      const args = ['-r', this._config.repository, 'ls', id, '--json', '--recursive'];
      if (requestedPath) args.push(requestedPath);
      const result = await this._runRestic(args, { passwordFile });
      if (result.code !== 0) throw new Error(result.stderr.trim() || 'Restic could not list snapshot contents.');
      const entries = result.stdout.split(/\r?\n/).map(parseJsonLine).filter((entry) => entry && (entry.message_type === 'node' || entry.struct_type === 'node'));
      return entries.map((entry) => ({
        path: typeof entry.path === 'string' ? entry.path : '',
        name: typeof entry.name === 'string' ? entry.name : '',
        type: typeof entry.type === 'string' ? entry.type : 'unknown',
        size: finiteNumber(entry.size),
        mtime: typeof entry.mtime === 'string' ? entry.mtime : '',
        permissions: typeof entry.permissions === 'string' ? entry.permissions : '',
      })).filter((entry) => entry.path);
    } finally {
      await this._removeRuntimeFile(passwordFile);
    }
  }

  async openRestore(payload = {}) {
    await this._ensureConfigured(false);
    if (!payload.destination) {
      this._setState({ page: 'Restore' });
      return this.getState();
    }
    const snapshotId = asString(payload.snapshotId);
    if (snapshotId && !/^[a-z0-9]{8,64}$/i.test(snapshotId)) throw new Error('Invalid snapshot ID.');
    const requestedPath = asString(payload.path);
    if (requestedPath && (!requestedPath.startsWith('/') || requestedPath.includes('\u0000'))) {
      throw new Error('Restore paths must be absolute macOS paths.');
    }
    const destination = normalizePath(asString(payload.destination));
    await this._validateRestoreDestination(destination);
    const password = await this._loadPassword();
    const passwordFile = await this._createPasswordFile(password);
    this._setState({
      status: this._status('active', 'Restoring snapshot', 'Restic is restoring selected data to the chosen empty folder.', {
        phaseIndex: 1, phaseLabel: 'Restoring', progress: 0,
      }),
    });
    try {
      await this._validateRestoreDestination(destination);
      const args = ['-r', this._config.repository, 'restore', snapshotId || 'latest', '--target', destination, '--json', '--verify'];
      if (requestedPath) args.push('--include', requestedPath);
      const result = await this._runRestic(args, { passwordFile });
      if (result.code !== 0) throw new Error(result.stderr.trim() || 'Restic restore failed.');
      this._setState({ status: this._status('success', 'Restore complete', `Restored data to ${safeDisplayPath(destination)}.`, { phaseIndex: 2, phaseLabel: 'Complete', progress: 1 }) });
      return this.getState();
    } catch (error) {
      this._setState({ status: this._status('failure', 'Restore failed', error.message || 'Restic reported a restore error.', { phaseIndex: 1, phaseLabel: 'Needs attention', progress: 0 }) });
      return this.getState();
    } finally {
      await this._removeRuntimeFile(passwordFile);
      this._child = null;
    }
  }

  async exportDiagnostics() {
    const diagnostics = {
      product: 'Rewindle',
      version: '0.2.0-alpha.1',
      platform: 'macOS',
      serviceVersion: SERVICE_VERSION,
      generated: isoNow(),
      configuration: {
        repository: redactedPath(this._config?.repository),
        sourceCount: this._config?.sources?.length || 0,
        schedule: this._config?.schedule || null,
        recoveryKeySaved: Boolean(this._config?.recoveryKeyPath),
      },
      status: {
        title: this._state.status.title,
        badge: this._state.status.badge,
        active: this._state.status.active,
        success: this._state.status.success,
        failure: this._state.status.failure,
      },
      history: this._history.slice(0, 20).map((run) => ({
        id: run.id,
        started: run.started,
        result: run.result,
        success: run.success,
        durationSeconds: run.durationSeconds,
        files: run.files,
        processedBytes: run.processedBytes,
      })),
    };
    const text = `${JSON.stringify(diagnostics, null, 2)}\n`;
    const saved = await this._callUi('saveText', {
      title: 'Save Rewindle diagnostics',
      defaultPath: path.join(os.homedir(), 'Desktop', 'Rewindle-diagnostics.json'),
      text,
    });
    if (!saved) throw new UserCancelledError();
    return this.getState();
  }

  async _ensureConfigured(interactive = true) {
    if (this._config) return this._config;
    if (!interactive) throw new Error('Complete Rewindle setup before using this action.');
    const repoSelection = normalizeSelection(await this._callUi('chooseDirectories', {
      title: 'Choose the Restic repository folder',
      multiple: false,
    }), false);
    if (!repoSelection.length) throw new UserCancelledError();
    const repository = normalizePath(repoSelection[0]);
    const sourceSelection = normalizeSelection(await this._callUi('chooseDirectories', {
      title: 'Choose folders to protect',
      multiple: true,
    }), true);
    if (!sourceSelection.length) throw new UserCancelledError('Choose at least one folder to protect.');
    const sources = await this._validateSourceSelection(sourceSelection, repository);
    await this._validateRepositorySelection(repository, sources);
    const password = randomPassword();
    const recoveryText = [
      'Rewindle recovery key',
      '=======================',
      '',
      'Keep this key offline. It unlocks the encrypted Restic repository.',
      'Do not send it in diagnostics or commit it to source control.',
      '',
      password,
      '',
      `Created: ${isoNow()}`,
    ].join('\n');
    const blockedRecoveryPaths = [repository, this.dataDir, ...sources];
    const recoveryPath = await this._callUi('saveText', {
      title: 'Save your Rewindle recovery key',
      defaultPath: path.join(os.homedir(), 'Desktop', RECOVERY_KEY_NAME),
      text: `${recoveryText}\n`,
      blockedPaths: blockedRecoveryPaths,
      validatePath: (candidate) => this._validateRecoveryKeyPath(candidate, {
        requireExisting: false,
        repository,
        sources,
      }),
    });
    if (!recoveryPath) throw new UserCancelledError('Save the recovery key before initializing the repository.');
    const validatedRecoveryPath = await this._validateRecoveryKeyPath(recoveryPath, { repository, sources });
    if (!validatedRecoveryPath.ok) {
      throw new Error('The recovery key was saved at an unsafe location. Remove that file manually and run setup again; Rewindle did not delete it.');
    }
    if (!this.encryptString) throw new Error('Electron safeStorage encryption is unavailable.');
    const encrypted = await this.encryptString(password);
    const canaryPath = path.join(this.dataDir, CANARY_DIR, `rewindle-${randomId()}.txt`);
    await fs.mkdir(path.dirname(canaryPath), { recursive: true, mode: 0o700 });
    const canaryContent = crypto.randomBytes(32).toString('hex');
    await fs.writeFile(canaryPath, canaryContent, { mode: 0o600 });
    const canaryHash = crypto.createHash('sha256').update(canaryContent).digest('hex');
    const config = {
      version: SERVICE_VERSION,
      repository,
      sources,
      canaryPath,
      canaryHash,
      schedule: { enabled: false, time: '02:00' },
      recoveryKeyPath: validatedRecoveryPath.path,
      created: isoNow(),
      updated: isoNow(),
    };
    await this._writeCredential(encrypted);
    try {
      const passwordFile = await this._createPasswordFile(password);
      try {
        await fs.mkdir(repository, { recursive: true, mode: 0o700 });
        const result = await this._runRestic(['-r', repository, 'init', '--json'], { passwordFile });
        if (result.code !== 0) throw new Error(result.stderr.trim() || 'Restic could not initialize the repository.');
      } finally {
        await this._removeRuntimeFile(passwordFile);
      }
    } catch (error) {
      await this._removeCredential();
      throw error;
    }
    if (this.scheduler && typeof this.scheduler.installDailyLaunchAgent === 'function') {
      try {
        await this.scheduler.installDailyLaunchAgent({ time: config.schedule.time });
        this._scheduleStatus = typeof this.scheduler.readDailyLaunchAgent === 'function'
          ? await this.scheduler.readDailyLaunchAgent()
          : null;
        config.schedule.enabled = Boolean(this._scheduleStatus?.enabled && this._scheduleStatus.verified);
      } catch {
        // Keep the completed repository setup usable while accurately showing
        // that launchd could not be installed on this host.
        config.schedule.enabled = false;
        this._scheduleStatus = { enabled: false, loaded: false, verified: false, time: config.schedule.time, detail: 'Daily schedule could not be verified.' };
      }
    }
    this._config = config;
    await this._writeConfig(config);
    this._state = this._buildState(this._state?.page || 'Protection');
    this._emitState();
    return config;
  }

  async _validateSourceSelection(values, repository = this._config?.repository) {
    const sources = [...new Set(values.map(normalizePath))];
    const canonicalSources = new Map();
    for (const source of sources) canonicalSources.set(source, await this._canonicalPath(source));
    const canonicalRepository = repository ? await this._canonicalPath(repository) : '';
    const canonicalDataDir = await this._canonicalPath(this.dataDir);
    for (const source of sources) {
      const stat = await fs.stat(source).catch(() => null);
      if (!stat || !stat.isDirectory()) throw new Error(`Protected folder does not exist or is not a directory: ${safeDisplayPath(source)}`);
      const link = await fs.lstat(source).catch(() => null);
      if (link?.isSymbolicLink()) throw new Error(`Protected folder is a symbolic link. Choose its real folder: ${safeDisplayPath(source)}`);
      if (repository && pathsOverlap(canonicalSources.get(source), canonicalRepository)) throw new Error('A protected folder cannot contain the Restic repository.');
      if (pathsOverlap(canonicalSources.get(source), canonicalDataDir)) throw new Error('Protected folders cannot overlap Rewindle application data.');
    }
    for (let i = 0; i < sources.length; i += 1) {
      for (let j = i + 1; j < sources.length; j += 1) {
        if (pathsOverlap(canonicalSources.get(sources[i]), canonicalSources.get(sources[j]))) throw new Error('Protected folders cannot overlap each other.');
      }
    }
    return sources;
  }

  async _validateRepositorySelection(repository, sources = this._config?.sources || []) {
    const stat = await fs.stat(repository).catch(() => null);
    if (stat && !stat.isDirectory()) throw new Error('The repository path must be a folder.');
    const link = await fs.lstat(repository).catch(() => null);
    if (link?.isSymbolicLink()) throw new Error('The repository path is a symbolic link. Choose its real folder.');
    const canonicalRepository = await this._canonicalPath(repository);
    if (pathsOverlap(canonicalRepository, await this._canonicalPath(this.dataDir))) {
      throw new Error('The Restic repository cannot overlap Rewindle application data.');
    }
    for (const source of sources) {
      if (pathsOverlap(canonicalRepository, await this._canonicalPath(source))) throw new Error('The Restic repository cannot overlap a protected folder.');
    }
    if (stat) {
      const entries = await fs.readdir(repository).catch(() => []);
      if (entries.length) throw new Error('New Rewindle setup requires an empty repository folder. Choose an empty folder or create a new one.');
    }
  }

  async _validateConfiguredPaths() {
    await this._validateSourceSelection(this._config.sources, this._config.repository);
    const repositoryStat = await fs.stat(this._config.repository).catch(() => null);
    if (!repositoryStat || !repositoryStat.isDirectory()) throw new Error('The configured Restic repository no longer exists.');
    const repositoryLink = await fs.lstat(this._config.repository).catch(() => null);
    if (repositoryLink?.isSymbolicLink()) throw new Error('The configured Restic repository is a symbolic link. Choose its real folder.');
    if (pathsOverlap(await this._canonicalPath(this._config.repository), await this._canonicalPath(this.dataDir))) {
      throw new Error('The configured Restic repository cannot overlap Rewindle application data.');
    }
  }

  async _validateCanary() {
    const canaryPath = this._config?.canaryPath;
    if (!isAbsolutePath(canaryPath) || !isSha256(this._config?.canaryHash)) {
      throw new Error('The backup canary is missing or invalid. Run setup again before starting a backup.');
    }
    const canaryLink = await fs.lstat(canaryPath).catch(() => null);
    const canaryStat = await fs.stat(canaryPath).catch(() => null);
    if (!canaryStat || !canaryStat.isFile() || canaryLink?.isSymbolicLink()) {
      throw new Error('The backup canary file is missing or is not a regular file.');
    }
    const canonicalCanary = await this._canonicalPath(canaryPath);
    if (!sameOrChild(canonicalCanary, await this._canonicalPath(path.join(this.dataDir, CANARY_DIR)))) {
      throw new Error('The backup canary must remain inside Rewindle application data.');
    }
    const actualHash = await this._sha256(canaryPath);
    if (actualHash.toLowerCase() !== this._config.canaryHash.toLowerCase()) {
      throw new Error('The backup canary hash has changed. Restore the original canary or run setup again.');
    }
    return true;
  }

  async _validateRecoveryKeyPath(candidate, { requireExisting = true, repository, sources } = {}) {
    if (!isAbsolutePath(candidate)) return { ok: false, message: 'Choose an absolute recovery-key path.' };
    const target = normalizePath(candidate);
    const blocked = [repository || this._config?.repository, this.dataDir, ...(sources || this._config?.sources || [])].filter(Boolean);
    const canonicalTarget = await this._canonicalPath(target);
    for (const blockedPath of blocked) {
      if (pathsOverlap(canonicalTarget, await this._canonicalPath(blockedPath))) {
        return { ok: false, message: 'Choose a recovery-key location outside the repository, Rewindle data, and protected folders.' };
      }
    }
    const link = await fs.lstat(target).catch(() => null);
    if (requireExisting) {
      const stat = await fs.stat(target).catch(() => null);
      if (!stat || !stat.isFile() || link?.isSymbolicLink()) return { ok: false, message: 'The recovery key must be an ordinary file.' };
      if (this.enforceUnixPermissions && (stat.mode & 0o777) !== 0o600) return { ok: false, message: 'The recovery key must have owner-only permissions (mode 600).' };
    } else if (link?.isSymbolicLink()) {
      return { ok: false, message: 'Choose a recovery-key path that is not a symbolic link.' };
    }
    return { ok: true, path: target };
  }

  async _validateRestoreDestination(destination) {
    if (!isAbsolutePath(destination)) throw new Error('Restore destination must be a full macOS path.');
    if (path.resolve(destination) === path.parse(destination).root) throw new Error('Restore destination cannot be the filesystem root.');
    const canonicalDestination = await this._canonicalPath(destination);
    if (this._config?.repository && pathsOverlap(canonicalDestination, await this._canonicalPath(this._config.repository))) throw new Error('Restore destination cannot overlap the Restic repository.');
    for (const source of this._config?.sources || []) {
      if (pathsOverlap(canonicalDestination, await this._canonicalPath(source))) throw new Error('Restore destination cannot overlap a protected folder.');
    }
    const stat = await fs.stat(destination).catch(() => null);
    if (stat && !stat.isDirectory()) throw new Error('Restore destination must be a folder.');
    if (stat && (await fs.readdir(destination)).length) throw new Error('Restore destination must be empty.');
    if (!stat) await fs.mkdir(destination, { recursive: true, mode: 0o700 });
    // Re-read after creating a new target so a race that populated the folder
    // between validation and creation cannot turn into an implicit merge.
    if ((await fs.readdir(destination)).length) throw new Error('Restore destination must be empty.');
  }

  async _verifyCanary(snapshotId, passwordFile) {
    await this._validateCanary();
    const restoreRoot = await fs.mkdtemp(path.join(this.dataDir, 'canary-verify-'));
    try {
      const result = await this._runRestic([
        '-r', this._config.repository,
        'restore',
        snapshotId,
        '--target',
        restoreRoot,
        '--json',
        '--verify',
        '--include',
        this._config.canaryPath,
      ], { passwordFile });
      if (result.code !== 0) throw new Error(result.stderr.trim() || 'Restic could not restore the canary file.');
      const relativeCanaryPath = path.relative(path.parse(this._config.canaryPath).root, this._config.canaryPath);
      const candidate = path.join(restoreRoot, relativeCanaryPath);
      const candidateStat = await fs.stat(candidate).catch(() => null);
      if (!candidateStat || !candidateStat.isFile()) throw new Error('The exact canary file was not present in the verified snapshot.');
      const candidateLink = await fs.lstat(candidate).catch(() => null);
      if (candidateLink?.isSymbolicLink()) throw new Error('The restored canary path was a symbolic link.');
      const digest = await this._sha256(candidate);
      if (digest !== this._config.canaryHash) throw new Error('The restored canary hash did not match.');
    } finally {
      await fs.rm(restoreRoot, { recursive: true, force: true });
    }
  }

  async _findFile(root, name) {
    const entries = await fs.readdir(root, { withFileTypes: true }).catch(() => []);
    for (const entry of entries) {
      const candidate = path.join(root, entry.name);
      if (entry.isFile() && entry.name === name) return candidate;
      if (entry.isDirectory()) {
        const nested = await this._findFile(candidate, name);
        if (nested) return nested;
      }
    }
    return null;
  }

  async _sha256(file) {
    const hash = crypto.createHash('sha256');
    const stream = fsSync.createReadStream(file);
    for await (const chunk of stream) hash.update(chunk);
    return hash.digest('hex');
  }

  async _loadPassword(interactive = true) {
    const credential = await this._readCredential();
    if (credential && this.decryptString) {
      try {
        const encoded = credential.encryptedPassword;
        const result = await this.decryptString(Buffer.from(encoded, 'base64'));
        if (result) return result;
      } catch {
        try {
          const result = await this.decryptString(credential.encryptedPassword);
          if (result) return result;
        } catch {
          // Fall through to the explicit recovery password prompt.
        }
      }
    }
    if (!interactive) throw new Error('The encrypted repository credential is unavailable.');
    const value = normalizeInput(await this._callUi('input', {
      title: 'Unlock your Restic repository',
      label: 'Repository password',
      type: 'password',
      value: '',
    }));
    if (!value) throw new UserCancelledError('A repository password is required.');
    return value;
  }

  async _runRestic(args, options = {}) {
    if (!Array.isArray(args) || args.some((arg) => typeof arg !== 'string')) throw new TypeError('Restic arguments must be an array of strings.');
    if (args[0] !== '-r' || !isAbsolutePath(args[1]) || !RESTIC_COMMANDS.has(args[2])) {
      throw new Error('Unsupported or unsafe Restic command.');
    }
    const env = { ...process.env };
    for (const key of PASSWORD_ENV_KEYS) delete env[key];
    if (options.passwordFile) env.RESTIC_PASSWORD_FILE = options.passwordFile;
    const child = this.spawnProcess(this.resticPath, args, {
      cwd: this.dataDir,
      env,
      shell: false,
      windowsHide: true,
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    this._child = child;
    let stdout = '';
    let stdoutBuffer = '';
    let stderr = '';
    const onJson = typeof options.onJson === 'function' ? options.onJson : null;
    const handleStdout = (chunk) => {
      const text = String(chunk || '');
      stdout += text;
      stdoutBuffer += text;
      const lines = stdoutBuffer.split(/\r?\n/);
      stdoutBuffer = lines.pop() || '';
      for (const line of lines) {
        const event = parseJsonLine(line);
        if (event && onJson) onJson(event);
      }
    };
    const handleStderr = (chunk) => {
      stderr += String(chunk || '');
      if (stderr.length > 20000) stderr = stderr.slice(-20000);
    };
    if (child.stdout?.on) child.stdout.on('data', handleStdout);
    if (child.stderr?.on) child.stderr.on('data', handleStderr);
    return new Promise((resolve, reject) => {
      let settled = false;
      const finish = (result) => {
        if (settled) return;
        settled = true;
        const trailing = parseJsonLine(stdoutBuffer);
        if (trailing && onJson) onJson(trailing);
        this._child = null;
        resolve({ ...result, stdout, stderr, cancelled: this._cancelRequested });
      };
      const fail = (error) => {
        if (settled) return;
        settled = true;
        this._child = null;
        reject(error);
      };
      child.once?.('error', fail);
      child.once?.('close', (code, signal) => finish({ code: Number.isInteger(code) ? code : 1, signal }));
      // Wait for close: exit can arrive before stdout has delivered the final
      // JSON summary, including the immutable snapshot ID.
    });
  }

  _consumeBackupEvent(event, runId) {
    if (event.message_type !== 'status' && event.message_type !== 'summary') return;
    const percent = finiteNumber(event.percent_done, 0);
    const files = finiteNumber(event.files_done || event.total_files_processed, 0);
    const bytes = finiteNumber(event.bytes_done || event.total_bytes_processed, 0);
    this._setState({
      status: this._status('active', 'Backup in progress', 'Restic is encrypting the selected folders.', {
        runId,
        phaseIndex: 1,
        phaseLabel: 'Backing up',
        progress: clamp(percent, 0, 1),
        files: formatCount(files),
        bytes: formatBytes(bytes),
        elapsed: formatDuration(finiteNumber(event.seconds_elapsed)),
        eta: event.seconds_remaining === undefined ? '—' : formatDuration(event.seconds_remaining),
        estimated: event.seconds_remaining !== undefined,
      }),
    });
  }

  _makeHistoryItem(values) {
    const files = finiteNumber(values.files);
    const processedBytes = finiteNumber(values.processedBytes);
    const storedBytes = finiteNumber(values.storedBytes);
    const durationSeconds = finiteNumber(values.durationSeconds);
    return {
      id: values.id || randomId(),
      started: values.started || isoNow(),
      startedDisplay: displayDate(values.started || isoNow()),
      type: values.type || 'Backup',
      result: values.result || 'Failed',
      success: Boolean(values.success),
      durationSeconds,
      durationDisplay: formatDuration(durationSeconds),
      files,
      filesDisplay: formatCount(files),
      processedBytes,
      processedDisplay: formatBytes(processedBytes),
      storedBytes,
      storedDisplay: formatBytes(storedBytes),
      snapshot: values.snapshot || '',
    };
  }

  async _recordHistory(item) {
    this._history = [item, ...this._history.filter((existing) => existing.id !== item.id)].slice(0, MAX_HISTORY);
    await this._writeJson(HISTORY_FILE, this._history);
    this._state = this._buildState(this._state?.page || 'Protection');
  }

  _buildState(page) {
    const config = this._config;
    const latest = this._history[0] || null;
    const active = this._state?.status?.active && this._state.status.key === 'active';
    const schedule = config?.schedule;
    const scheduleStatus = this._scheduleStatus;
    const scheduleVerified = Boolean(schedule?.enabled && scheduleStatus?.enabled && scheduleStatus?.verified);
    const sources = (config?.sources || []).map((source) => ({
      path: source,
      name: path.basename(source) || source,
      isCanary: false,
      exists: this._pathExistsSync(source),
      canRemove: (config?.sources || []).length > 1,
      detail: this._pathExistsSync(source) ? 'Included in the next backup' : 'Folder not found',
    }));
    const status = this._state?.status || this._status(
      latest?.success ? 'success' : (latest?.result === 'Cancelled' ? 'cancelled' : 'idle'),
      latest?.success ? 'Backup verified' : (latest ? `Backup ${String(latest.result).toLowerCase()}` : 'Ready to protect your data'),
      latest?.success ? `${latest.filesDisplay} files are safely stored.` : (latest ? 'Review the latest run for details.' : 'Choose a repository and folders to start.'),
      {
        runId: latest?.id || '',
        phaseLabel: latest?.success ? 'Verified' : 'Ready',
        progress: latest?.success ? 1 : 0,
        files: latest?.filesDisplay || '—',
        bytes: latest?.processedDisplay || '—',
      },
    );
    const configured = Boolean(config);
    const actions = defaultActions();
    actions.backupNow = normalizeAction(!active, configured ? 'Back up now' : 'Set up backup', configured ? 'Start an encrypted Restic backup.' : 'Choose a repository and folders to protect.');
    actions.cancelBackup = normalizeAction(Boolean(active), 'Cancel backup', active ? 'Ask Restic to stop the active backup.' : 'No backup is currently running.');
    actions.addSource = normalizeAction(configured && !active, 'Add folder', 'Choose another folder to protect.');
    actions.removeSource = normalizeAction(configured && !active, 'Remove folder', 'Remove this folder from future backups.');
    actions.editSchedule = normalizeAction(configured && !active, 'Edit schedule', 'Change the daily launchd schedule.');
    actions.changeRepository = normalizeAction(configured && !active, 'Change repository', 'Choose a different Restic repository.');
    actions.changeRepository = normalizeAction(false, 'Change repository', 'Changing repositories is not available in this release.', false);
    actions.openRestore = normalizeAction(configured && !active, 'Open restore', 'Open the restore workflow.');
    actions.checkReadiness = normalizeAction(configured && !active, 'Check readiness', 'Check repository and recovery readiness.');
    actions.exportDiagnostics = normalizeAction(true, 'Export diagnostics', 'Save redacted diagnostic information.');
    const state = {
      page: PAGE_NAMES.has(page) ? page : 'Protection',
      demo: false,
      preview: Boolean(this._state?.preview),
      theme: THEMES.has(this._state?.theme) ? this._state.theme : 'System',
      dark: process.env.REWINDLE_DARK_MODE === '1' || process.env.AppleInterfaceStyle === 'Dark',
      reducedMotion: process.env.REWINDLE_REDUCED_MOTION === '1',
      highContrast: false,
      updated: isoNow(),
      subtitle: configured ? 'Encrypted daily protection for the folders you choose.' : 'Set up encrypted, verified backup in a few steps.',
      status,
      sources,
      sourceStatus: configured ? `${sources.length} protected folder${sources.length === 1 ? '' : 's'}` : 'No backup plan configured',
      sourceOperation: { active: false, stage: '', message: '', path: '' },
      history: this._history.map((item) => ({ ...item })),
      selectedRunId: this._state?.selectedRunId || latest?.id || null,
      schedule: {
        summary: scheduleVerified ? `Daily at ${scheduleStatus.time}` : schedule?.enabled ? 'Schedule needs attention' : 'Schedule off',
        nextRun: scheduleVerified ? nextDailyRun(scheduleStatus.time) : '—',
        detail: scheduleVerified ? scheduleStatus.detail : scheduleStatus?.detail || 'Automatic backup is disabled',
      },
      repository: {
        path: config?.repository || 'Not configured',
        volume: config?.repository ? path.parse(config.repository).root : '—',
      },
      freshness: latest?.success ? { title: 'Freshly verified', detail: `${latest.startedDisplay} · ${latest.durationDisplay}` } : { title: 'No verified backup yet', detail: 'Run a backup to establish a recovery point.' },
      offsite: { title: 'Local repository', detail: 'Choose an external or cloud-synced repository if you need offsite coverage.', evidence: '' },
      recovery: {
        title: configured ? 'Readiness not checked' : 'Finish setup first',
        detail: configured ? 'Run Recovery readiness before relying on this plan.' : 'A separate recovery key is saved during setup.',
        repairNeeded: configured,
        repairMessage: configured ? 'Repository readiness requires an explicit read-only check.' : '',
        repairState: configured ? 'unknown' : 'setup',
        repairBusy: false,
        repairPercent: null,
      },
      actions,
    };
    return state;
  }

  _status(key, title, detail, values = {}) {
    return {
      key,
      title,
      detail,
      badge: key === 'success' ? 'Verified' : key === 'failure' ? 'Needs attention' : key === 'cancelled' ? 'Cancelled' : key === 'active' ? 'In progress' : 'Ready',
      active: key === 'active',
      success: key === 'success',
      failure: key === 'failure',
      cancelled: key === 'cancelled',
      phaseIndex: finiteNumber(values.phaseIndex),
      phaseLabel: values.phaseLabel || 'Ready',
      progress: clamp(finiteNumber(values.progress), 0, 1),
      estimated: Boolean(values.estimated),
      runId: values.runId || '',
      files: values.files || '—',
      bytes: values.bytes || '—',
      speed: values.speed || '—',
      elapsed: values.elapsed || '—',
      errors: values.errors || '0',
      etaTitle: 'Estimated time remaining',
      eta: values.eta || '—',
      etaHint: values.etaHint || '',
    };
  }

  _setState(patch) {
    this._state = { ...this._state, ...patch, updated: isoNow() };
    this._emitState();
  }

  _emitState() {
    if (!this._state) return;
    const snapshot = this.getState();
    this.emit('state', snapshot);
    void this._writeJson(STATUS_FILE, {
      page: snapshot.page,
      selectedRunId: snapshot.selectedRunId,
      preview: snapshot.preview,
      theme: snapshot.theme,
      updated: snapshot.updated,
    });
  }

  _navigate(payload) {
    const page = asString(payload.page || payload.value);
    if (!PAGE_NAMES.has(page)) throw new Error('Unknown dashboard page.');
    this._setState({ page });
    return this.getState();
  }

  _setTheme(payload) {
    const theme = asString(payload.theme || payload.value);
    if (!THEMES.has(theme)) throw new Error('Unknown dashboard theme.');
    this._setState({ theme });
    return this.getState();
  }

  _togglePreview() {
    this._setState({ preview: !this._state.preview });
    return this.getState();
  }

  _openRestore() {
    this._setState({ page: 'Restore' });
    return this.getState();
  }

  _selectRun(payload) {
    const id = asString(payload.id || payload.runId);
    if (id && !this._history.some((run) => run.id === id)) throw new Error('Unknown backup run.');
    this._setState({ selectedRunId: id || null });
    return this.getState();
  }

  _viewRunDetails(payload) {
    return this._selectRun(payload);
  }

  _retrySourceChange() {
    this._state = this._buildState(this._state.page);
    this._emitState();
    return this.getState();
  }

  _dismissSourceChange() {
    this._setState({ sourceOperation: { active: false, stage: '', message: '', path: '' } });
    return this.getState();
  }

  _unsupported(message) {
    this._setState({ dataError: message });
    return this.getState();
  }

  _queue(label, work) {
    const run = this._operation.then(work, work);
    this._operation = run.catch(() => undefined);
    return run;
  }

  async _callUi(name, payload) {
    const fn = this.ui && this.ui[name];
    if (typeof fn !== 'function') throw new Error(`Rewindle needs the ${name} dialog bridge.`);
    return fn(payload);
  }

  async _ensureDataDir() {
    await fs.mkdir(this.dataDir, { recursive: true, mode: 0o700 });
    await fs.chmod(this.dataDir, 0o700).catch(() => undefined);
    await fs.mkdir(path.join(this.dataDir, RUNTIME_DIR), { recursive: true, mode: 0o700 });
    await fs.chmod(path.join(this.dataDir, RUNTIME_DIR), 0o700).catch(() => undefined);
    await this._cleanupRuntimeFiles();
  }

  async _cleanupRuntimeFiles() {
    const runtime = path.join(this.dataDir, RUNTIME_DIR);
    const entries = await fs.readdir(runtime, { withFileTypes: true }).catch(() => []);
    for (const entry of entries) {
      if (!entry.isFile() || !/^password-\d+-[a-f0-9]{24}$/.test(entry.name)) continue;
      const match = entry.name.match(/^password-(\d+)-/);
      const ownerPid = Number(match?.[1]);
      if (!Number.isSafeInteger(ownerPid) || ownerPid <= 0 || ownerPid === process.pid) continue;
      let active = true;
      try {
        process.kill(ownerPid, 0);
      } catch (error) {
        if (error && error.code === 'ESRCH') active = false;
      }
      if (!active) await fs.rm(path.join(runtime, entry.name), { force: true }).catch(() => undefined);
    }
  }

  async _readScheduleStatus() {
    const configuredTime = this._config?.schedule?.time || '02:00';
    if (!this._config?.schedule?.enabled || !this.scheduler || typeof this.scheduler.readDailyLaunchAgent !== 'function') {
      return { enabled: false, loaded: false, verified: false, time: configuredTime, detail: 'Automatic backup is not installed.' };
    }
    try {
      const actual = await this.scheduler.readDailyLaunchAgent();
      if (!actual || actual.enabled !== true) {
        return { enabled: false, loaded: false, verified: false, time: configuredTime, detail: 'Daily schedule is configured but its LaunchAgent is not loaded.' };
      }
      return { enabled: true, loaded: Boolean(actual.loaded), verified: Boolean(actual.verified), time: actual.time || configuredTime, detail: actual.detail || 'User LaunchAgent · runs when you are signed in.' };
    } catch {
      return { enabled: false, loaded: false, verified: false, time: configuredTime, detail: 'Daily schedule could not be verified.' };
    }
  }

  async _readConfig() {
    const raw = await this._readJson(CONFIG_FILE, null);
    try {
      return normalizeConfig(raw);
    } catch {
      return null;
    }
  }

  async _readHistory() {
    const raw = await this._readJson(HISTORY_FILE, []);
    if (!Array.isArray(raw)) return [];
    return raw.filter((entry) => entry && typeof entry.id === 'string').slice(0, MAX_HISTORY).map((entry) => this._makeHistoryItem(entry));
  }

  async _readCredential() {
    const raw = await this._readJson(CREDENTIAL_FILE, null);
    if (!raw || typeof raw.encryptedPassword !== 'string' || !raw.encryptedPassword) return null;
    return raw;
  }

  async _writeCredential(encrypted) {
    const encoded = Buffer.isBuffer(encrypted) ? encrypted.toString('base64') : String(encrypted);
    await this._writeJson(CREDENTIAL_FILE, { version: SERVICE_VERSION, encryptedPassword: encoded });
    await fs.chmod(path.join(this.dataDir, CREDENTIAL_FILE), 0o600).catch(() => undefined);
  }

  async _removeCredential() {
    await fs.rm(path.join(this.dataDir, CREDENTIAL_FILE), { force: true }).catch(() => undefined);
  }

  async _readJson(fileName, fallback) {
    try {
      const text = await fs.readFile(path.join(this.dataDir, fileName), 'utf8');
      return JSON.parse(text);
    } catch {
      return fallback;
    }
  }

  async _writeConfig(config) {
    await this._writeJson(CONFIG_FILE, config);
  }

  async _writeJson(fileName, value) {
    const write = this._writeQueue.then(async () => {
      const target = path.join(this.dataDir, fileName);
      const temporary = `${target}.${process.pid}.${randomId()}.tmp`;
      const data = `${JSON.stringify(value, null, 2)}\n`;
      await fs.writeFile(temporary, data, { encoding: 'utf8', mode: 0o600 });
      await fs.chmod(temporary, 0o600).catch(() => undefined);
      await fs.rename(temporary, target);
      await fs.chmod(target, 0o600).catch(() => undefined);
    });
    this._writeQueue = write.catch(() => undefined);
    return write;
  }

  async _createPasswordFile(password) {
    const file = path.join(this.dataDir, RUNTIME_DIR, `password-${process.pid}-${randomId()}`);
    await fs.writeFile(file, `${password}\n`, { encoding: 'utf8', mode: 0o600 });
    await fs.chmod(file, 0o600).catch(() => undefined);
    return file;
  }

  async _removeRuntimeFile(file) {
    if (file) await fs.rm(file, { force: true }).catch(() => undefined);
  }

  async _pathExists(value) {
    try {
      await fs.access(value);
      return true;
    } catch {
      return false;
    }
  }

  async _canonicalPath(value) {
    try {
      return await fs.realpath(value);
    } catch {
      const parent = path.dirname(value);
      try {
        return path.join(await fs.realpath(parent), path.basename(value));
      } catch {
        return path.resolve(value);
      }
    }
  }

  _pathExistsSync(value) {
    try {
      return fsSync.existsSync(value);
    } catch {
      return false;
    }
  }
}

class CancelledOperationError extends Error {
  constructor() {
    super('The backup was cancelled.');
    this.name = 'CancelledOperationError';
    this.code = 'CANCELLED';
  }
}

module.exports = {
  MacBackupService,
  CancelledOperationError,
  UnsupportedPlatformError,
  UserCancelledError,
  formatBytes,
  formatDuration,
  nextDailyRun,
  pathsOverlap,
  validateTime,
};
