'use strict';

const {
  app,
  BrowserWindow,
  dialog,
  ipcMain,
  nativeTheme,
  safeStorage,
  session,
  shell,
  systemPreferences,
} = require('electron');
const crypto = require('node:crypto');
const fs = require('node:fs');
const fsp = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');

const APP_NAME = 'Rewindle';
const PRODUCT_NAME = 'Rewindle';
const TAGLINE = 'Backup you can verify';
const APP_ID = 'com.rewindle.desktop';
const VERSION = '0.2.0-alpha.1';
const PAGES = new Set(['Protection', 'Activity', 'Restore', 'Settings']);
const THEMES = new Set(['System', 'Midnight', 'Daylight']);
const COMMANDS = new Set([
  'ready', 'refresh', 'navigate', 'setTheme', 'togglePreview',
  'backupNow', 'cancelBackup', 'addSource', 'removeSource', 'editSchedule',
  'changeRepository', 'repairRepository', 'reviewChanges', 'openRestore',
  'checkReadiness', 'selectRun', 'viewRunDetails', 'exportDiagnostics',
  'retrySourceChange', 'dismissSourceChange',
]);
const COMMAND_PAYLOAD_KEYS = {
  navigate: ['page'],
  setTheme: ['theme'],
  removeSource: ['path'],
  selectRun: ['runId'],
  viewRunDetails: ['runId'],
  openRestore: ['snapshotId', 'destination', 'path'],
};
const MAX_PAYLOAD_BYTES = 32 * 1024;

let mainWindow = null;
let service = null;
let serviceStateListener = null;
let isQuitting = false;
let smokeMode = false;
let smokeOutput = null;
let stateHeartbeat = null;
let ipcInstalled = false;
let navigationConfigured = false;
let coreInitialized = false;
let scheduledConsumed = false;
let quitRequested = false;
let presentationTheme = 'System';
const smokeDiagnostics = [];
const activeModals = new Map();
const PREVIEW_LOOP_MS = 16_000;
const PREVIEW_ACTIVE_MS = 13_800;
const PREVIEW_STAGES = [
  { label: 'Reading protected folders', detail: 'Previewing the folders that would be read.' },
  { label: 'Saving encrypted snapshot', detail: 'Previewing the encrypted point-in-time save.' },
  { label: 'Checking repository', detail: 'Previewing the repository integrity check.' },
  { label: 'Verifying restore canary', detail: 'Previewing the independent restore verification.' },
];
let previewStartedAt = 0;
let stateHeartbeatDelay = 0;

app.setName(APP_NAME);
app.setAppUserModelId(APP_ID);

function appRoot() {
  return app.isPackaged ? process.resourcesPath : path.resolve(__dirname, '..');
}

function applicationRoot() {
  return app.isPackaged ? app.getAppPath() : path.resolve(__dirname, '..');
}

function resourcePath(...segments) {
  return path.join(appRoot(), ...segments);
}

function applicationPath(...segments) {
  return path.join(applicationRoot(), ...segments);
}

function userDataPath() {
  return process.env.REWINDLE_USER_DATA || app.getPath('userData');
}

function stateDirectory() {
  return process.env.REWINDLE_DATA_DIR || path.join(userDataPath(), 'state');
}

function resticPath() {
  return process.env.REWINDLE_RESTIC_PATH || resourcePath('restic', process.platform === 'win32' ? 'restic.exe' : 'restic');
}

function executablePath() {
  return process.execPath;
}

function bundlePath() {
  if (!app.isPackaged || process.platform !== 'darwin') return appRoot();
  const executable = process.execPath;
  const marker = '.app/';
  const markerIndex = executable.indexOf(marker);
  return markerIndex >= 0 ? executable.slice(0, markerIndex + 4) : path.dirname(executable);
}

function cloneJson(value) {
  if (value === undefined) return undefined;
  return JSON.parse(JSON.stringify(value));
}

function safeText(value, fallback = '') {
  return typeof value === 'string' ? value : fallback;
}

function normalizeTheme(theme) {
  return THEMES.has(theme) ? theme : 'System';
}

function readPresentation() {
  try {
    const file = path.join(userDataPath(), 'presentation.json');
    const parsed = JSON.parse(fs.readFileSync(file, 'utf8'));
    return { theme: normalizeTheme(parsed.theme) };
  } catch (_error) {
    return { theme: 'System' };
  }
}

function setNativeTheme(theme) {
  nativeTheme.themeSource = theme === 'Midnight' ? 'dark' : theme === 'Daylight' ? 'light' : 'system';
}

async function persistTheme(theme) {
  const normalized = normalizeTheme(theme);
  await fsp.mkdir(userDataPath(), { recursive: true });
  const file = path.join(userDataPath(), 'presentation.json');
  const temporary = `${file}.${process.pid}.tmp`;
  await fsp.writeFile(temporary, `${JSON.stringify({ theme: normalized }, null, 2)}\n`, { mode: 0o600 });
  await fsp.rename(temporary, file);
  presentationTheme = normalized;
  setNativeTheme(normalized);
}

function applyPresentation(state) {
  if (!state || typeof state !== 'object') return state;
  // Core starts with its portable System default.  The host-owned presentation
  // file is the source of truth for the user's saved choice until a live
  // dashboard theme command updates both values; no synthetic core write is
  // needed during boot.
  const coreTheme = normalizeTheme(state.theme);
  const theme = coreTheme === 'System' ? presentationTheme : coreTheme;
  const dark = theme === 'Midnight' || (theme === 'System' && nativeTheme.shouldUseDarkColors);
  let animationSettings = null;
  try {
    animationSettings = typeof systemPreferences.getAnimationSettings === 'function'
      ? systemPreferences.getAnimationSettings()
      : null;
  } catch (_error) {
    animationSettings = null;
  }
  const reducedMotion = Boolean(state.reducedMotion || (animationSettings && animationSettings.shouldRenderRichAnimation === false));
  const presentation = { ...state, theme, dark, reducedMotion };
  if (!state.preview) {
    // The preview clock is presentation-only.  Stopping the toggle returns
    // the unmodified core state, including its real status and history.
    previewStartedAt = 0;
    return presentation;
  }
  if (!previewStartedAt) previewStartedAt = Date.now();
  return { ...presentation, status: animationPreviewStatus(state.status), updated: new Date().toISOString() };
}

function animationPreviewStatus(source) {
  const base = isObject(source) ? source : {};
  const elapsed = (Date.now() - previewStartedAt) % PREVIEW_LOOP_MS;
  const clearValues = {
    runId: '',
    files: '—',
    bytes: '—',
    speed: '—',
    elapsed: '—',
    errors: '0',
    etaTitle: 'Preview timeline',
  };
  if (elapsed >= PREVIEW_ACTIVE_MS) {
    return {
      ...base,
      ...clearValues,
      key: 'preview-complete',
      title: 'Animation preview complete',
      detail: 'All protection stages are shown. No backup is running.',
      badge: 'Preview complete',
      active: false,
      success: true,
      failure: false,
      cancelled: false,
      phaseIndex: PREVIEW_STAGES.length - 1,
      phaseLabel: 'Done',
      progress: 1,
      estimated: false,
      eta: '—',
      etaHint: '',
    };
  }
  const stageDuration = PREVIEW_ACTIVE_MS / PREVIEW_STAGES.length;
  const stageIndex = Math.min(PREVIEW_STAGES.length - 1, Math.floor(elapsed / stageDuration));
  const stage = PREVIEW_STAGES[stageIndex];
  const withinStage = elapsed - (stageIndex * stageDuration);
  const progress = ((stageIndex + (withinStage / stageDuration)) / PREVIEW_STAGES.length);
  const remainingSeconds = Math.max(0, Math.ceil((PREVIEW_ACTIVE_MS - elapsed) / 1000));
  return {
    ...base,
    ...clearValues,
    key: 'preview',
    title: 'Animation preview',
    detail: `${stage.detail} No backup is running.`,
    badge: 'Preview',
    active: true,
    success: false,
    failure: false,
    cancelled: false,
    phaseIndex: stageIndex,
    phaseLabel: stage.label,
    progress: Math.max(0, Math.min(1, Number(progress.toFixed(3)))),
    estimated: false,
    elapsed: `${Math.floor(elapsed / 1000)}s`,
    eta: `${remainingSeconds}s`,
    etaHint: 'Presentation only; no Restic process is running.',
  };
}

function encryptString(value) {
  if (!safeStorage.isEncryptionAvailable()) {
    throw new Error('macOS secure storage is unavailable; Rewindle will not persist credentials.');
  }
  return safeStorage.encryptString(String(value)).toString('base64');
}

function decryptString(value) {
  if (!safeStorage.isEncryptionAvailable()) {
    throw new Error('macOS secure storage is unavailable; Rewindle will not decrypt credentials.');
  }
  if (typeof value !== 'string' || !value) throw new Error('Encrypted credential is empty.');
  return safeStorage.decryptString(Buffer.from(value, 'base64'));
}

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function validateCommand(message) {
  if (!isObject(message) || message.type !== 'command') throw new Error('Invalid native message.');
  const id = safeText(message.id);
  const command = safeText(message.command);
  if (!id || id.length > 128 || !COMMANDS.has(command)) throw new Error('Unsupported dashboard command.');
  const payload = message.payload === undefined ? {} : message.payload;
  if (!isObject(payload)) throw new Error('Command payload must be an object.');
  const encoded = Buffer.byteLength(JSON.stringify(payload), 'utf8');
  if (encoded > MAX_PAYLOAD_BYTES) throw new Error('Command payload is too large.');
  const allowed = COMMAND_PAYLOAD_KEYS[command] || [];
  for (const key of Object.keys(payload)) {
    if (!allowed.includes(key)) throw new Error(`Unexpected payload field for ${command}.`);
    const value = payload[key];
    if (typeof value !== 'string' && typeof value !== 'boolean') throw new Error('Invalid command payload value.');
    if (typeof value === 'string' && (value.length > 8192 || value.includes('\u0000'))) throw new Error('Invalid command payload text.');
  }
  if (command === 'navigate' && !PAGES.has(payload.page)) throw new Error('Unknown dashboard page.');
  if (command === 'setTheme' && !THEMES.has(payload.theme)) throw new Error('Unknown dashboard theme.');
  if ((command === 'selectRun' || command === 'viewRunDetails') && !safeText(payload.runId)) throw new Error('Run ID is required.');
  return { id, command, payload: cloneJson(payload) || {} };
}

function findWebRoot() {
  const candidates = [
    process.env.REWINDLE_WEB_ROOT,
    app.isPackaged ? resourcePath('web') : null,
    path.join(appRoot(), 'src', 'dashboard', 'dist', 'web'),
    path.join(appRoot(), 'src', 'dashboard', 'web', 'dist'),
  ].filter(Boolean);
  for (const candidate of candidates) {
    const index = path.join(candidate, 'index.html');
    if (fs.existsSync(index)) return { root: candidate, index };
  }
  throw new Error(`Rewindle UI build not found. Expected src/dashboard/dist/web/index.html or packaged resources/web/index.html.`);
}

function localFilePathAllowed(filePath, root) {
  const resolved = path.resolve(filePath);
  const rootResolved = path.resolve(root);
  return resolved === rootResolved || resolved.startsWith(`${rootResolved}${path.sep}`);
}

function getCoreModulePath() {
  const configured = process.env.REWINDLE_CORE_MODULE;
  return configured || applicationPath('desktop', 'macos', 'service.cjs');
}

function loadCoreClass() {
  const modulePath = getCoreModulePath();
  if (!fs.existsSync(modulePath)) {
    throw new Error(`Rewindle core module is missing: ${modulePath}`);
  }
  // eslint-disable-next-line global-require, import/no-dynamic-require
  const loaded = require(modulePath);
  const Core = loaded.MacBackupService || loaded.default || loaded;
  if (typeof Core !== 'function') throw new Error('desktop/macos/service.cjs must export MacBackupService.');
  return Core;
}

function modalOptions(options, kind) {
  const source = isObject(options) ? options : {};
  const fields = Array.isArray(source.fields) ? source.fields.slice(0, 12).map((field, index) => ({
    name: safeText(field && field.name, `value${index}`).slice(0, 80),
    label: safeText(field && field.label, `Value ${index + 1}`).slice(0, 120),
    type: ['password', 'number', 'select', 'textarea', 'time', 'text'].includes(field && field.type) ? field.type : 'text',
    placeholder: safeText(field && field.placeholder).slice(0, 200),
    value: safeText(field && field.value).slice(0, 4096),
    help: safeText(field && field.help).slice(0, 500),
    required: field && field.required !== false,
    options: Array.isArray(field && field.options) ? field.options.slice(0, 200).map((option) => ({
      value: safeText(option && option.value).slice(0, 512),
      label: safeText(option && option.label, safeText(option && option.value)).slice(0, 200),
    })).filter((option) => option.value || option.label) : undefined,
  })) : undefined;
  return {
    kind,
    title: safeText(source.title, APP_NAME).slice(0, 160),
    message: safeText(source.message || source.prompt).slice(0, 8000),
    label: safeText(source.label, 'Value').slice(0, 120),
    placeholder: safeText(source.placeholder).slice(0, 200),
    type: source.type === 'password' ? 'password' : source.type === 'number' ? 'number' : source.type === 'time' ? 'time' : 'text',
    value: safeText(source.value).slice(0, 4096),
    submitLabel: safeText(source.submitLabel, 'Continue').slice(0, 80),
    cancelLabel: safeText(source.cancelLabel, 'Cancel').slice(0, 80),
    fields,
  };
}

function createModal(options, kind) {
  if (!mainWindow || mainWindow.isDestroyed()) return Promise.resolve(null);
  const nonce = crypto.randomBytes(18).toString('hex');
  const modal = new BrowserWindow({
    width: 520,
    height: Math.min(680, Math.max(330, 350 + ((options && options.fields && options.fields.length) || 1) * 58)),
    minWidth: 420,
    minHeight: 300,
    maxWidth: 720,
    maxHeight: 780,
    parent: mainWindow,
    modal: true,
    show: false,
    resizable: false,
    title: PRODUCT_NAME,
    webPreferences: {
      preload: path.join(__dirname, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      webSecurity: true,
    },
  });
  const sanitized = { ...modalOptions(options, kind), nonce };
  return new Promise((resolve) => {
    activeModals.set(nonce, { modal, resolve, kind });
    const finish = (value) => {
      if (!activeModals.has(nonce)) return;
      activeModals.delete(nonce);
      if (!modal.isDestroyed()) modal.close();
      resolve(value);
    };
    modal.once('closed', () => finish(null));
    modal.webContents.once('did-finish-load', () => {
      modal.webContents.send('modal:init', sanitized);
      modal.show();
    });
    modal.loadFile(path.join(__dirname, 'renderer', 'modal.html')).catch(() => finish(null));
    modal.__rewindleFinish = finish;
  });
}

function sanitizeModalValues(values) {
  if (typeof values === 'string') return values.slice(0, 16384);
  if (!isObject(values)) return null;
  const result = {};
  for (const [key, value] of Object.entries(values).slice(0, 24)) {
    if (!/^[A-Za-z0-9_.-]{1,80}$/.test(key)) continue;
    if (typeof value !== 'string' && typeof value !== 'boolean' && typeof value !== 'number') continue;
    result[key] = typeof value === 'string' ? value.slice(0, 16384) : value;
  }
  return result;
}

async function chooseDirectories(options = {}) {
  if (!mainWindow || mainWindow.isDestroyed()) return null;
  const result = await dialog.showOpenDialog(mainWindow, {
    title: safeText(options.title, 'Choose a folder').slice(0, 160),
    message: safeText(options.message).slice(0, 1000),
    properties: ['openDirectory', 'createDirectory', ...(options.multiple ? ['multiSelections'] : [])],
  });
  return result.canceled ? null : (options.multiple ? result.filePaths : result.filePaths[0] || null);
}

async function openPath(options = {}) {
  const requested = safeText(options.path);
  if (!requested || requested.includes('\u0000')) return false;
  const resolved = path.resolve(requested);
  if (!fs.existsSync(resolved)) return false;
  // Only open a user-selected local path.  No shell command or URL is passed
  // through this API.
  if (resolved.startsWith('\\\\') || resolved.startsWith('//')) return false;
  await shell.openPath(resolved);
  return true;
}

async function saveText(options = {}) {
  if (!mainWindow || mainWindow.isDestroyed()) return null;
  const blockedPaths = [
    ...(Array.isArray(options.blockedPaths) ? options.blockedPaths : []),
    ...(typeof options.blockedPath === 'string' ? [options.blockedPath] : []),
  ].filter((candidate) => typeof candidate === 'string' && candidate && !candidate.includes('\u0000'));

  // Resolve the existing part of a path before comparing it with a protected
  // location.  This catches a recovery key saved through a symlink into the
  // repository or Rewindle's app data, while still allowing a new file in an
  // otherwise safe folder.
  const comparablePath = (candidate) => {
    const resolved = path.resolve(candidate);
    let cursor = resolved;
    const suffix = [];
    while (!fs.existsSync(cursor)) {
      const parent = path.dirname(cursor);
      if (parent === cursor) break;
      suffix.unshift(path.basename(cursor));
      cursor = parent;
    }
    let realBase = cursor;
    try { realBase = fs.realpathSync.native(cursor); } catch (_error) { /* keep the resolved parent */ }
    const compared = path.join(realBase, ...suffix);
    return process.platform === 'darwin' ? compared.toLowerCase() : compared;
  };
  const isInside = (candidate, root) => {
    const child = comparablePath(candidate);
    const parent = comparablePath(root);
    return child === parent || child.startsWith(`${parent}${path.sep}`);
  };

  const validateDestination = async (candidate) => {
    if (blockedPaths.some((blocked) => isInside(candidate, blocked))) {
      return 'Choose a location outside the repository, protected folders, and Rewindle app data.';
    }
    if (typeof options.validatePath === 'function') {
      try {
        const validation = await options.validatePath(candidate);
        if (validation === false) return 'Choose a different location for this file.';
        if (typeof validation === 'string' && validation) return validation.slice(0, 500);
        if (isObject(validation) && validation.ok === false) {
          return safeText(validation.message, 'Choose a different location for this file.').slice(0, 500);
        }
      } catch (_error) {
        return 'Rewindle could not validate that location. Choose a different location.';
      }
    }
    return null;
  };

  const saveOptions = {
    title: safeText(options.title, 'Save Rewindle report').slice(0, 160),
    defaultPath: safeText(options.defaultPath, 'rewindle-report.txt').slice(0, 240),
    filters: [{ name: 'Text', extensions: ['txt', 'log', 'json'] }],
  };
  for (let attempt = 0; attempt < 4; attempt += 1) {
    const result = await dialog.showSaveDialog(mainWindow, saveOptions);
    if (result.canceled || !result.filePath) return null;
    const destination = path.resolve(result.filePath);
    const validationMessage = await validateDestination(destination);
    if (validationMessage) {
      await showText({
        title: 'Choose a safer location',
        message: validationMessage,
        detail: 'The file was not written. Choose another folder to continue.',
        type: 'warning',
      });
      continue;
    }
    const content = safeText(options.text || options.content);
    await fsp.writeFile(destination, content, { encoding: 'utf8', mode: 0o600 });
    // The mode option is ignored when a file already exists.  Recovery keys
    // and exported diagnostics therefore get an explicit restrictive mode in
    // both the new-file and overwrite cases.
    await fsp.chmod(destination, 0o600);
    return destination;
  }
  return null;
}

async function showText(options = {}) {
  if (!mainWindow || mainWindow.isDestroyed()) return { response: 0 };
  return dialog.showMessageBox(mainWindow, {
    type: options.type === 'error' ? 'error' : options.type === 'warning' ? 'warning' : 'info',
    title: safeText(options.title, APP_NAME).slice(0, 160),
    message: safeText(options.message || options.text).slice(0, 8000),
    detail: safeText(options.detail).slice(0, 8000),
    buttons: ['OK'],
    noLink: true,
  });
}

async function confirm(options = {}) {
  if (!mainWindow || mainWindow.isDestroyed()) return false;
  const result = await dialog.showMessageBox(mainWindow, {
    type: options.type === 'warning' ? 'warning' : 'question',
    title: safeText(options.title, APP_NAME).slice(0, 160),
    message: safeText(options.message || options.prompt).slice(0, 8000),
    detail: safeText(options.detail).slice(0, 8000),
    buttons: [safeText(options.cancelLabel, 'Cancel'), safeText(options.confirmLabel, 'Continue')],
    defaultId: 1,
    cancelId: 0,
    noLink: true,
  });
  return result.response === 1;
}

function runDetailValue(run, displayKey, rawKey) {
  const display = run && run[displayKey];
  if (typeof display === 'string' && display.trim()) return display.trim();
  const raw = run && run[rawKey];
  if (raw === undefined || raw === null || raw === '') return '';
  return String(raw);
}

function formatRunDetails(run) {
  const lines = [];
  const fields = [
    ['Date', runDetailValue(run, 'startedDisplay', 'started')],
    ['Result', runDetailValue(run, 'result', 'result')],
    ['Files', runDetailValue(run, 'filesDisplay', 'files')],
    ['Duration', runDetailValue(run, 'durationDisplay', 'durationSeconds')],
    ['Processed', runDetailValue(run, 'processedDisplay', 'processedBytes')],
    ['Stored', runDetailValue(run, 'storedDisplay', 'storedBytes')],
    ['Snapshot', runDetailValue(run, 'snapshot', 'snapshot') || 'Unavailable'],
  ];
  for (const [label, value] of fields) {
    if (value) lines.push(`${label}: ${value}`);
  }
  return lines.join('\n');
}

async function showRunDetails(runId) {
  const state = await currentState();
  const history = Array.isArray(state.history) ? state.history : [];
  const run = history.find((entry) => isObject(entry) && entry.id === runId);
  if (!run) throw new Error('Unknown backup run.');

  // Keep selection behavior in core, then show the fields already exposed in
  // the redacted dashboard history.  No raw logs or synthetic run values are
  // introduced by the native host.
  const result = await service.execute('viewRunDetails', { runId });
  const resultState = result && result.status ? result : result && result.state && result.state.status ? result.state : null;
  const nextState = applyPresentation(resultState || await currentState());
  postNativeMessage({ type: 'state', state: nextState });
  await showText({
    title: 'Backup run details',
    message: `${safeText(run.type, 'Backup run')} · ${safeText(run.result, 'Result unavailable')}`,
    detail: formatRunDetails(run),
  });
  return { ok: true, state: nextState, message: 'Run details opened.' };
}

function normalizeRestoreSnapshotList(value) {
  const entries = Array.isArray(value) ? value : value && Array.isArray(value.snapshots) ? value.snapshots : [];
  return entries.map((entry) => {
    if (typeof entry === 'string') return { id: entry, label: entry };
    if (!isObject(entry)) return null;
    const id = safeText(entry.id || entry.snapshotId || entry.snapshot);
    if (!id || !/^[a-z0-9]{8,64}$/i.test(id)) return null;
    const display = safeText(entry.startedDisplay || entry.display || entry.time || entry.started);
    const result = safeText(entry.result, 'Verified');
    const files = safeText(entry.filesDisplay || entry.files);
    const label = [display || id, result, files ? `${files} files` : ''].filter(Boolean).join(' · ');
    return { id, label: label.slice(0, 260) };
  }).filter(Boolean).filter((entry, index, all) => all.findIndex((candidate) => candidate.id === entry.id) === index).slice(0, 200);
}

async function restoreSnapshots(state) {
  const provider = service.listRestoreSnapshots || service.listSnapshots;
  if (typeof provider === 'function') {
    // A provider supplied by core must return redacted metadata only.  Do not
    // silently replace a failed repository listing with demo data.
    return normalizeRestoreSnapshotList(await provider.call(service));
  }
  // Older core builds expose verified snapshots in dashboard history.  This is
  // still real repository history and keeps the chooser useful while the core
  // gains a full snapshots endpoint.
  return normalizeRestoreSnapshotList(state && state.history ? state.history.filter((entry) => entry.success && entry.snapshot) : []);
}

async function restorePaths(snapshotId) {
  const provider = service.listRestoreEntries || service.listRestorePaths || service.listSnapshotPaths;
  if (typeof provider !== 'function') return null;
  const value = service.listRestoreEntries
    ? await provider.call(service, snapshotId, '')
    : await provider.call(service, snapshotId);
  const entries = Array.isArray(value) ? value : value && Array.isArray(value.paths) ? value.paths : [];
  return entries.map((entry) => {
    if (typeof entry === 'string') return { value: entry, label: entry };
    if (!isObject(entry)) return null;
    const item = safeText(entry.path || entry.value);
    const kind = /^(dir|directory)$/i.test(safeText(entry.type)) ? 'Folder' : 'File';
    const label = safeText(entry.label, safeText(entry.name, item));
    return item ? { value: item, label: `${kind} · ${label}`.slice(0, 260) } : null;
  }).filter(Boolean).slice(0, 500);
}

async function openRestoreFlow() {
  // Open the Restore page immediately so the web client reflects the native
  // workflow while the repository chooser is being prepared.
  const pageState = await service.execute('openRestore', {});
  postNativeMessage({ type: 'state', state: applyPresentation(pageState && pageState.status ? pageState : await currentState()) });
  const state = await currentState();
  const snapshots = await restoreSnapshots(state);
  if (!snapshots.length) {
    await showText({
      title: 'No verified snapshots yet',
      message: 'Rewindle needs a completed, verified snapshot before it can restore files.',
      detail: 'Run a backup, then return to Restore Center to choose a snapshot and destination.',
      type: 'info',
    });
    return { ok: false, message: 'No verified snapshots are available yet.' };
  }

  const snapshotForm = await createModal({
    title: 'Choose a snapshot',
    message: 'Select the verified point in time to browse and restore.',
    submitLabel: 'Choose snapshot',
    fields: [{ name: 'snapshotId', label: 'Verified snapshot', type: 'select', options: snapshots }],
  }, 'setup');
  const snapshotId = safeText(snapshotForm && (snapshotForm.snapshotId || snapshotForm.value));
  if (!snapshotId) return { ok: true, message: 'Restore cancelled.' };

  let pathValue = '';
  const paths = await restorePaths(snapshotId);
  if (paths) {
    const pathForm = await createModal({
      title: 'Choose files to restore',
      message: 'Restore the full snapshot or select a path inside it.',
      submitLabel: 'Choose destination',
      fields: [{
        name: 'path', label: 'Snapshot path', type: 'select', required: false,
        options: [{ value: '', label: 'Entire snapshot' }, ...paths],
      }],
    }, 'setup');
    pathValue = safeText(pathForm && pathForm.path);
    if (pathForm === null) return { ok: true, message: 'Restore cancelled.' };
  } else {
    const pathForm = await createModal({
      title: 'Choose files to restore',
      message: 'Leave the path blank to restore the full snapshot, or enter a path from the snapshot.',
      submitLabel: 'Choose destination',
      fields: [{ name: 'path', label: 'Snapshot path (optional)', type: 'text', required: false, placeholder: 'e.g. Documents/notes.md' }],
    }, 'setup');
    pathValue = safeText(pathForm && pathForm.path);
    if (pathForm === null) return { ok: true, message: 'Restore cancelled.' };
  }

  const destination = await chooseDirectories({
    title: 'Choose an empty restore folder',
    message: 'Rewindle will never overwrite the original source folder.',
    multiple: false,
  });
  if (!destination) return { ok: true, message: 'Restore cancelled.' };
  const confirmed = await confirm({
    title: 'Restore these files?',
    message: `Snapshot ${snapshotId.slice(0, 12)}… will be restored to ${destination}.`,
    detail: pathValue ? `Selected path: ${pathValue}` : 'The full verified snapshot will be restored.',
    confirmLabel: 'Restore and verify',
  });
  if (!confirmed) return { ok: true, message: 'Restore cancelled.' };
  const result = await service.execute('openRestore', { snapshotId, destination, path: pathValue });
  const resultState = result && result.status ? result : result && result.state && result.state.status ? result.state : null;
  const nextState = resultState || await currentState();
  postNativeMessage({ type: 'state', state: applyPresentation(nextState) });
  const status = nextState && nextState.status ? nextState.status : {};
  const resultRejected = result && result.ok === false;
  if (resultRejected || status.failure) {
    return { ok: false, state: nextState, message: safeText(result && result.message, status.detail || 'Restore failed.') };
  }
  if (status.cancelled) {
    return { ok: false, state: nextState, message: status.detail || 'Restore cancelled.' };
  }
  if (status.success) {
    return { ok: true, state: nextState, message: 'Restore complete and verified.' };
  }
  return { ok: true, state: nextState, message: 'Restore started.' };
}

function uiBridge() {
  return {
    input: (options) => createModal(options, 'input').then((value) => typeof value === 'string' ? value : null),
    setup: (options) => createModal(options, 'setup'),
    showText,
    saveText,
    openPath,
    chooseDirectories,
    confirm,
    // The aliases keep the core portable while the renderer receives only the
    // small named capabilities above.
    chooseDirectory: chooseDirectories,
    saveFile: (options) => dialog.showSaveDialog(mainWindow, options || {}).then((result) => result.canceled ? null : result.filePath || null),
    showMessage: showText,
  };
}

function coreOptions() {
  return {
    dataDir: stateDirectory(),
    resticPath: resticPath(),
    encryptString,
    decryptString,
    launchExecutable: executablePath(),
    appBundlePath: bundlePath(),
    ui: uiBridge(),
  };
}

async function createService() {
  const Core = loadCoreClass();
  const instance = new Core(coreOptions());
  if (!instance || typeof instance.getState !== 'function' || typeof instance.execute !== 'function') {
    throw new Error('MacBackupService must implement getState() and execute(command, payload).');
  }
  service = instance;
  serviceStateListener = (nextState) => {
    const state = applyPresentation(nextState);
    postNativeMessage({ type: 'state', state });
    adjustStateHeartbeat(state);
  };
  if (typeof service.on === 'function') service.on('state', serviceStateListener);
  if (typeof service.initialize === 'function') await service.initialize();
  return service;
}

async function currentState() {
  if (!service) throw new Error('Rewindle core is not initialized.');
  return applyPresentation(await service.getState());
}

function postNativeMessage(message) {
  if (!mainWindow || mainWindow.isDestroyed()) return;
  mainWindow.webContents.send('native:message', cloneJson(message));
}

async function sendState() {
  const state = await currentState();
  postNativeMessage({ type: 'state', state });
  adjustStateHeartbeat(state);
  return state;
}

async function executeCommand(command, payload) {
  if (command === 'openRestore' && !payload.destination) return openRestoreFlow();
  if (command === 'viewRunDetails') return showRunDetails(payload.runId);
  if (command === 'togglePreview') {
    const rawState = await service.getState();
    if (!rawState.preview && rawState.status && rawState.status.active) {
      throw new Error('Stop the active backup before previewing animations.');
    }
  }
  const result = await service.execute(command, payload);
  if (command === 'setTheme' && (!result || result.ok !== false)) {
    await persistTheme(payload.theme);
  }
  let nextState = result && result.state ? result.state : null;
  if (!nextState) nextState = await currentState();
  nextState = applyPresentation(nextState);
  postNativeMessage({ type: 'state', state: nextState });
  adjustStateHeartbeat(nextState);
  return { ok: result && result.ok === false ? false : true, message: result && result.message, state: nextState };
}

async function handleNativeCommand(event, message) {
  if (!mainWindow || event.sender !== mainWindow.webContents || event.senderFrame !== mainWindow.webContents.mainFrame) {
    throw new Error('Native commands must originate from the dashboard frame.');
  }
  const parsed = validateCommand(message);
  if (parsed.command === 'ready' || parsed.command === 'refresh') {
    await sendState();
    return { ok: true, id: parsed.id };
  }
  const result = await executeCommand(parsed.command, parsed.payload);
  return { ...result, id: parsed.id };
}

async function runScheduledBackup() {
  if (!service) return;
  try {
    const result = await service.execute('backupNow', { scheduled: true });
    if (result && result.ok === false) console.error(`[${APP_NAME}] scheduled backup rejected: ${result.message || 'unknown reason'}`);
  } catch (error) {
    console.error(`[${APP_NAME}] scheduled backup failed: ${error.message}`);
  }
}

function installIpc() {
  if (ipcInstalled) return;
  ipcInstalled = true;
  ipcMain.handle('native:command', async (event, message) => {
    try {
      const result = await handleNativeCommand(event, message);
      if (result && result.message) postNativeMessage({ type: 'result', id: result.id, ok: result.ok, message: result.message });
      return { ok: true, id: result && result.id };
    } catch (error) {
      const id = isObject(message) ? safeText(message.id) : '';
      const text = error instanceof Error ? error.message : 'Rewindle command failed.';
      postNativeMessage({ type: 'result', id, ok: false, message: text });
      return { ok: false, id, message: text };
    }
  });

  ipcMain.on('modal:submit', (event, request) => {
    if (!isObject(request) || typeof request.nonce !== 'string') return;
    const active = activeModals.get(request.nonce);
    if (!active || event.sender !== active.modal.webContents) return;
    active.modal.__rewindleFinish(sanitizeModalValues(request.values));
  });
  ipcMain.on('modal:cancel', (event, request) => {
    if (!isObject(request) || typeof request.nonce !== 'string') return;
    const active = activeModals.get(request.nonce);
    if (!active || event.sender !== active.modal.webContents) return;
    active.modal.__rewindleFinish(null);
  });
}

function configureNavigation() {
  if (navigationConfigured) return;
  navigationConfigured = true;
  const filter = { urls: ['file://*/*', 'http://*/*', 'https://*/*'] };
  session.defaultSession.webRequest.onBeforeRequest(filter, (details, callback) => {
    if (!details.url.startsWith('file://')) return callback({ cancel: true });
    let filePath = '';
    try { filePath = decodeURIComponent(new URL(details.url).pathname); } catch (_error) { return callback({ cancel: true }); }
    const allowed = localFilePathAllowed(filePath, applicationRoot()) || localFilePathAllowed(filePath, appRoot());
    return callback({ cancel: !allowed });
  });
}

async function createMainWindow() {
  const web = findWebRoot();
  const icon = resourcePath('brand', 'app-icon.png');
  mainWindow = new BrowserWindow({
    width: 1280,
    height: 820,
    minWidth: 960,
    minHeight: 640,
    title: `${PRODUCT_NAME} — ${TAGLINE}`,
    icon: fs.existsSync(icon) ? icon : undefined,
    backgroundColor: nativeTheme.shouldUseDarkColors ? '#141821' : '#f7f8fb',
    show: false,
    webPreferences: {
      preload: path.join(__dirname, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      webSecurity: true,
      spellcheck: false,
    },
  });
  mainWindow.on('closed', () => {
    if (stateHeartbeat) {
      clearInterval(stateHeartbeat);
      stateHeartbeat = null;
    }
    stateHeartbeatDelay = 0;
    mainWindow = null;
  });
  if (smokeMode) {
    mainWindow.webContents.on('console-message', (_event, level, message) => {
      if (level >= 3) smokeDiagnostics.push(`console: ${message}`);
    });
    mainWindow.webContents.on('render-process-gone', (_event, details) => {
      smokeDiagnostics.push(`renderer: ${details.reason || 'gone'}`);
    });
    mainWindow.webContents.on('did-fail-load', (_event, code, description) => {
      smokeDiagnostics.push(`load: ${code} ${description}`);
    });
  }
  mainWindow.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  mainWindow.webContents.on('will-navigate', (event, url) => {
    if (!url.startsWith('file://') || !localFilePathAllowed(decodeURIComponent(new URL(url).pathname), web.root)) event.preventDefault();
  });
  await mainWindow.loadFile(web.index);
  mainWindow.show();
  startStateHeartbeat(5000);
  return mainWindow;
}

function adjustStateHeartbeat(state) {
  if (!mainWindow || mainWindow.isDestroyed() || !stateHeartbeat) return;
  const nextDelay = state && state.preview ? 500 : 5000;
  if (nextDelay !== stateHeartbeatDelay) startStateHeartbeat(nextDelay);
}

function startStateHeartbeat(delay = 5000) {
  if (stateHeartbeat) clearInterval(stateHeartbeat);
  stateHeartbeatDelay = delay;
  stateHeartbeat = setInterval(() => {
    if (!mainWindow || mainWindow.isDestroyed()) return;
    void currentState().then((state) => {
      postNativeMessage({ type: 'state', state });
      adjustStateHeartbeat(state);
    }).catch(() => undefined);
  }, delay);
  stateHeartbeat.unref?.();
}

async function runSmokeTest() {
  const checks = [];
  let smokePreviewPath = null;
  try {
    if (!mainWindow || mainWindow.isDestroyed()) throw new Error('Smoke window was not created.');
    const frontendLoaded = await mainWindow.webContents.executeJavaScript(`new Promise((resolve) => {
      const started = Date.now();
      const check = () => {
        const heading = document.querySelector('.page-title');
        const ready = document.readyState === 'complete' && !!document.querySelector('#root') && !!document.querySelector('.app-shell') && heading && heading.textContent.trim() === 'Protection';
        if (ready || Date.now() - started > 5000) resolve(Boolean(ready));
        else setTimeout(check, 50);
      };
      check();
    })`, true);
    checks.push({ name: 'frontend-render', ok: frontendLoaded === true });
    const state = await currentState();
    checks.push({ name: 'core-get-state', ok: isObject(state) && typeof state.status === 'object' });
    const expectedTheme = safeText(process.env.REWINDLE_SMOKE_EXPECT_THEME);
    checks.push({
      name: 'presentation-theme',
      ok: !expectedTheme || state.theme === expectedTheme,
      details: { expected: expectedTheme || 'any persisted theme', actual: state.theme },
    });
    const ipcResult = await mainWindow.webContents.executeJavaScript(`new Promise((resolve) => {
      let done = false;
      const finish = (value) => { if (!done) { done = true; resolve(value); } };
      const onMessage = (event) => { if (event && event.data && event.data.type === 'state') finish(true); };
      window.chrome.webview.addEventListener('message', onMessage);
      window.chrome.webview.postMessage({ type: 'command', id: 'smoke-ipc', command: 'ready' });
      setTimeout(() => finish(false), 4000);
    })`, true);
    checks.push({ name: 'preload-ipc', ok: ipcResult === true });
    const navigation = await mainWindow.webContents.executeJavaScript(`new Promise((resolve) => {
      const expected = ['Activity', 'Settings', 'Protection'];
      let index = 0;
      const clickNext = () => {
        const button = [...document.querySelectorAll('button')].find((candidate) => candidate.getAttribute('aria-label') === expected[index] || candidate.textContent.trim() === expected[index]);
        if (!button) return resolve(false);
        button.click();
        const started = Date.now();
        const waitForHeading = () => {
          const heading = document.querySelector('.page-title');
          if (heading && heading.textContent.trim() === expected[index]) {
            index += 1;
            if (index === expected.length) return resolve(true);
            return setTimeout(clickNext, 30);
          }
          if (Date.now() - started > 4000) return resolve(false);
          setTimeout(waitForHeading, 50);
        };
        waitForHeading();
      };
      clickNext();
    })`, true);
    checks.push({ name: 'frontend-navigation', ok: navigation === true });

    const preview = await mainWindow.webContents.executeJavaScript(`new Promise((resolve) => {
      let done = false;
      let sawPreview = false;
      let first = null;
      let startedAt = 0;
      const finish = (value) => {
        if (done) return;
        done = true;
        window.chrome.webview.removeEventListener('message', onMessage);
        resolve(value);
      };
      const onMessage = (event) => {
        const next = event && event.data && event.data.state;
        if (!next || !next.preview || !next.status) return;
        const sample = {
          title: String(next.status.title || ''),
          detail: String(next.status.detail || ''),
          phase: String(next.status.phaseLabel || ''),
          progress: Number(next.status.progress || 0),
        };
        if (!sawPreview) {
          sawPreview = true;
          first = sample;
          startedAt = Date.now();
          return;
        }
        const changed = sample.phase !== first.phase || sample.progress !== first.progress;
        if (changed && Date.now() - startedAt >= 900) {
          finish({ ok: true, sawPreview, first, latest: sample, elapsedMs: Date.now() - startedAt });
        }
      };
      window.chrome.webview.addEventListener('message', onMessage);
      window.chrome.webview.postMessage({ type: 'command', id: 'smoke-preview-start', command: 'togglePreview' });
      setTimeout(() => finish({ ok: false, sawPreview, message: 'Animation preview did not advance within 6 seconds.' }), 6000);
    })`, true);
    const previewLabelled = Boolean(preview && preview.first && preview.first.title === 'Animation preview'
      && preview.first.detail.includes('No backup is running'));
    const previewAdvanced = Boolean(preview && preview.ok && preview.latest
      && (preview.latest.phase !== preview.first.phase || preview.latest.progress !== preview.first.progress));
    checks.push({
      name: 'animation-preview',
      ok: previewLabelled && previewAdvanced,
      details: preview,
    });

    if (preview && preview.ok) {
      try {
        // Let the React frame paint the live preview state before capturing it.
        await new Promise((resolve) => setTimeout(resolve, 100));
        smokePreviewPath = await captureSmokePreview();
      } catch (error) {
        checks.push({ name: 'preview-capture', ok: false, details: error instanceof Error ? error.message : String(error) });
      }
    }
    if (smokePreviewPath) {
      checks.push({ name: 'preview-capture', ok: true, details: path.basename(smokePreviewPath) });
    }

    let stopped = { ok: false, message: 'Animation preview did not start.' };
    if (preview && preview.sawPreview) {
      stopped = await mainWindow.webContents.executeJavaScript(`new Promise((resolve) => {
        let done = false;
        let sawPreview = false;
        const finish = (value) => {
          if (done) return;
          done = true;
          window.chrome.webview.removeEventListener('message', onMessage);
          resolve(value);
        };
        const onMessage = (event) => {
          const next = event && event.data && event.data.state;
          if (!next) return;
          if (next.preview) {
            sawPreview = true;
            return;
          }
          if (sawPreview) {
            finish({
              ok: true,
              preview: false,
              title: String(next.status && next.status.title || ''),
              active: Boolean(next.status && next.status.active),
              history: Array.isArray(next.history) ? next.history : [],
            });
          }
        };
        window.chrome.webview.addEventListener('message', onMessage);
        window.chrome.webview.postMessage({ type: 'command', id: 'smoke-preview-stop', command: 'togglePreview' });
        setTimeout(() => finish({ ok: false, message: 'Animation preview did not stop within 4 seconds.' }), 4000);
      })`, true);
    }
    const historyRestored = Boolean(stopped && JSON.stringify(stopped.history || []) === JSON.stringify(state.history || []));
    const normalStateRestored = Boolean(stopped && stopped.ok && stopped.preview === false
      && !stopped.title.includes('Animation preview') && stopped.active === false && historyRestored);
    checks.push({ name: 'animation-preview-stop', ok: normalStateRestored, details: { ...stopped, historyRestored } });
    checks.push({ name: 'renderer-errors', ok: smokeDiagnostics.length === 0, details: smokeDiagnostics.slice(0, 10) });
    const result = {
      ok: checks.every((check) => check.ok),
      appId: APP_ID,
      version: VERSION,
      platform: process.platform,
      arch: process.arch,
      smokePreview: smokePreviewPath ? path.basename(smokePreviewPath) : null,
      checks,
    };
    await writeSmokeResult(result);
    if (!result.ok) throw new Error(`Smoke checks failed: ${checks.filter((check) => !check.ok).map((check) => check.name).join(', ')}`);
    await closeForSmoke(0);
  } catch (error) {
    const result = {
      ok: false,
      appId: APP_ID,
      version: VERSION,
      platform: process.platform,
      arch: process.arch,
      smokePreview: smokePreviewPath ? path.basename(smokePreviewPath) : null,
      checks,
      error: error instanceof Error ? error.message : String(error),
    };
    await writeSmokeResult(result);
    await closeForSmoke(1);
  }
}

async function captureSmokePreview() {
  if (!smokeOutput || !mainWindow || mainWindow.isDestroyed()) throw new Error('Smoke window is unavailable for capture.');
  const target = path.join(path.dirname(path.resolve(smokeOutput)), 'smoke-preview.png');
  const image = await mainWindow.webContents.capturePage();
  const png = image.toPNG();
  if (!png || png.length < 64) throw new Error('Electron returned an empty frontend capture.');
  await fsp.writeFile(target, png, { mode: 0o600 });
  await fsp.chmod(target, 0o600).catch(() => undefined);
  return target;
}

async function writeSmokeResult(result) {
  if (!smokeOutput) return;
  await fsp.mkdir(path.dirname(path.resolve(smokeOutput)), { recursive: true });
  await fsp.writeFile(path.resolve(smokeOutput), `${JSON.stringify(result, null, 2)}\n`, { encoding: 'utf8', mode: 0o600 });
}

async function closeForSmoke(code) {
  isQuitting = true;
  if (mainWindow && !mainWindow.isDestroyed()) mainWindow.close();
  app.exit(code);
}

async function boot() {
  if (!coreInitialized) {
    const presentation = readPresentation();
    presentationTheme = presentation.theme;
    setNativeTheme(presentation.theme);
    installIpc();
    configureNavigation();
    await createService();
    coreInitialized = true;
  }
  if (!mainWindow) await createMainWindow();
  await sendState();
  if (process.argv.includes('--scheduled-backup') && !scheduledConsumed) {
    scheduledConsumed = true;
    await runScheduledBackup();
  }
  if (smokeMode && !isQuitting) {
    smokeMode = false;
    await runSmokeTest();
  }
}

app.on('before-quit', (event) => {
  if (isQuitting || quitRequested || !service) {
    isQuitting = true;
    return;
  }
  const active = service.getState && service.getState().status && service.getState().status.active;
  if (!active || typeof service.execute !== 'function') {
    isQuitting = true;
    return;
  }
  event.preventDefault();
  quitRequested = true;
  void service.execute('cancelBackup').catch(() => undefined).finally(() => {
    isQuitting = true;
    app.quit();
  });
});
nativeTheme.on('updated', () => {
  const presentation = readPresentation();
  presentationTheme = presentation.theme;
  if (!mainWindow || mainWindow.isDestroyed() || presentation.theme !== 'System') return;
  void sendState().catch(() => undefined);
});
app.on('window-all-closed', () => { if (process.platform !== 'darwin' && !isQuitting) app.quit(); });
app.on('activate', () => { if (!mainWindow) boot().catch((error) => showFatal(error)); });
app.on('second-instance', (_event, argv) => {
  if (mainWindow && !mainWindow.isDestroyed()) {
    if (mainWindow.isMinimized()) mainWindow.restore();
    mainWindow.focus();
  }
  if (argv.includes('--scheduled-backup')) runScheduledBackup().catch((error) => console.error(error));
});

function showFatal(error) {
  const message = error instanceof Error ? error.message : String(error);
  console.error(`[${APP_NAME}] ${message}`);
  if (app.isReady()) dialog.showErrorBox(APP_NAME, `${TAGLINE}\n\n${message}`);
  app.exit(1);
}

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  const smokeIndex = process.argv.indexOf('--smoke-test');
  smokeMode = smokeIndex !== -1;
  smokeOutput = smokeMode ? process.argv[smokeIndex + 1] : null;
  if (smokeMode && (!smokeOutput || smokeOutput.startsWith('--'))) {
    console.error('--smoke-test requires an output JSON path.');
    app.exit(2);
  } else {
    app.whenReady().then(() => boot()).catch(showFatal);
  }
}

module.exports = {
  APP_ID,
  COMMANDS,
  normalizeTheme,
  validateCommand,
};
