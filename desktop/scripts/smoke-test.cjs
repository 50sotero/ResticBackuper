'use strict';

const { spawn } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { mkdtempSync, rmSync } = fs;

const root = path.resolve(__dirname, '../..');
const outputFlag = process.argv.indexOf('--output');
const requestedOutput = outputFlag >= 0 ? process.argv[outputFlag + 1] : process.argv[2];
const output = path.resolve(root, requestedOutput && !requestedOutput.startsWith('--') ? requestedOutput : 'desktop/dist/smoke-test.json');
const appFlag = process.argv.indexOf('--app');
const requestedApp = appFlag >= 0 ? process.argv[appFlag + 1] : process.env.REWINDLE_SMOKE_APP;
const smokeRoot = process.env.REWINDLE_SMOKE_DATA_DIR || mkdtempSync(path.join(os.tmpdir(), 'rewindle-smoke-'));
const userData = process.env.REWINDLE_USER_DATA || path.join(smokeRoot, 'user-data');
const dataDir = process.env.REWINDLE_DATA_DIR || path.join(smokeRoot, 'state');
fs.mkdirSync(path.dirname(output), { recursive: true });

function packagedExecutable(value) {
  if (!value) return null;
  const resolved = path.resolve(root, value);
  if (process.platform === 'darwin' && resolved.endsWith('.app')) return path.join(resolved, 'Contents', 'MacOS', 'Rewindle');
  return resolved;
}

function detectPackagedApp() {
  if (process.platform !== 'darwin') return null;
  const architecture = process.arch === 'arm64' ? 'arm64' : 'x64';
  const candidates = [
    path.join(root, 'desktop', 'dist', `mac-${architecture}-unpacked`, 'Rewindle.app'),
    path.join(root, 'desktop', 'dist', 'mac-unpacked', 'Rewindle.app'),
  ];
  const match = candidates.find((candidate) => fs.existsSync(path.join(candidate, 'Contents', 'MacOS', 'Rewindle')));
  return match || null;
}

// CI can call `npm run smoke-test` after the DMG build.  Prefer the unpacked
// app emitted by electron-builder so the check exercises packaged resources;
// the development Electron fallback remains useful before a build exists.
const appExecutable = packagedExecutable(requestedApp || detectPackagedApp());
const binary = appExecutable || require('electron');
const child = spawn(binary, appExecutable ? ['--smoke-test', output] : ['.', '--smoke-test', output], {
  cwd: root,
  env: { ...process.env, ELECTRON_ENABLE_LOGGING: '1', REWINDLE_USER_DATA: userData, REWINDLE_DATA_DIR: dataDir },
  stdio: 'inherit',
});
child.on('error', (error) => {
  console.error(`Rewindle smoke test could not launch Electron: ${error.message}`);
  process.exitCode = 1;
});
child.on('exit', (code, signal) => {
  if (!process.env.REWINDLE_SMOKE_DATA_DIR) rmSync(smokeRoot, { recursive: true, force: true });
  if (signal) {
    console.error(`Rewindle smoke test exited with signal ${signal}.`);
    process.exitCode = 1;
    return;
  }
  process.exitCode = code === 0 ? 0 : code || 1;
});
