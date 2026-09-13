'use strict';

/** User LaunchAgent scheduling for Rewindle's daily backup. */

const { promises: fs, constants: fsConstants } = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const childProcess = require('node:child_process');
const crypto = require('node:crypto');

const LABEL = 'com.rewindle.backup';

function validateScheduleTime(value) {
  const text = String(value || '').trim();
  if (!/^([01]\d|2[0-3]):[0-5]\d$/.test(text)) {
    throw new Error('Schedule time must use 24-hour HH:mm format.');
  }
  return text;
}

function launchAgentPath(homeDirectory = os.homedir()) {
  return path.join(homeDirectory, 'Library', 'LaunchAgents', `${LABEL}.plist`);
}

function plistEscape(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&apos;');
}

function plistUnescape(value) {
  return String(value)
    .replaceAll('&quot;', '"')
    .replaceAll('&apos;', "'")
    .replaceAll('&gt;', '>')
    .replaceAll('&lt;', '<')
    .replaceAll('&amp;', '&');
}

function buildLaunchAgentPlist({ executablePath, time, label = LABEL, workingDirectory = '' }) {
  if (!executablePath || !path.isAbsolute(executablePath)) throw new Error('A full Rewindle executable path is required.');
  const normalizedTime = validateScheduleTime(time);
  const [hour, minute] = normalizedTime.split(':').map(Number);
  const entries = [
    `    <string>${plistEscape(executablePath)}</string>`,
    '    <string>--scheduled-backup</string>',
  ];
  const working = workingDirectory && path.isAbsolute(workingDirectory)
    ? `\n  <key>WorkingDirectory</key>\n  <string>${plistEscape(workingDirectory)}</string>`
    : '';
  return `<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>
  <string>${plistEscape(label)}</string>
  <key>ProgramArguments</key>
  <array>
${entries.join('\n')}
  </array>
  <key>RunAtLoad</key>
  <false/>
  <key>ProcessType</key>
  <string>Background</string>
  <key>LimitLoadToSessionType</key>
  <string>Aqua</string>
  <key>StartCalendarInterval</key>
  <dict>
    <key>Hour</key>
    <integer>${hour}</integer>
    <key>Minute</key>
    <integer>${minute}</integer>
  </dict>${working}
</dict>
</plist>
`;
}

function userDomainTarget(uid = typeof process.getuid === 'function' ? process.getuid() : null) {
  if (!Number.isInteger(uid) || uid < 1) throw new Error('Could not determine the macOS user session.');
  return `gui/${uid}`;
}

function spawnDefault(spawnProcess, executable, args, options) {
  return spawnProcess(executable, args, {
    ...options,
    shell: false,
    stdio: ['ignore', 'pipe', 'pipe'],
  });
}

function waitForChild(child) {
  let stderr = '';
  let stdout = '';
  if (child.stdout?.on) child.stdout.on('data', (chunk) => {
    stdout += String(chunk || '');
    if (stdout.length > 20000) stdout = stdout.slice(-20000);
  });
  if (child.stderr?.on) child.stderr.on('data', (chunk) => {
    stderr += String(chunk || '');
    if (stderr.length > 10000) stderr = stderr.slice(-10000);
  });
  return new Promise((resolve, reject) => {
    let settled = false;
    const done = (result) => {
      if (settled) return;
      settled = true;
      resolve({ ...result, stdout, stderr });
    };
    child.once?.('error', (error) => {
      if (settled) return;
      settled = true;
      reject(error);
    });
    child.once?.('close', (code, signal) => done({ code: Number.isInteger(code) ? code : 1, signal }));
  });
}

class MacLaunchAgentScheduler {
  constructor(options = {}) {
    this.platform = options.platform || process.platform;
    if (this.platform !== 'darwin') {
      const error = new Error('Rewindle launchd scheduling requires macOS.');
      error.code = 'UNSUPPORTED_PLATFORM';
      throw error;
    }
    this.homeDirectory = options.homeDirectory || os.homedir();
    this.executablePath = options.executablePath || options.appExecutablePath || options.launchExecutable;
    this.dataDir = options.dataDir || '';
    this.label = options.label || LABEL;
    this.uid = options.uid === undefined ? (typeof process.getuid === 'function' ? process.getuid() : null) : options.uid;
    this.spawnProcess = options.spawnProcess || childProcess.spawn;
  }

  get plistPath() {
    return launchAgentPath(this.homeDirectory).replace(LABEL, this.label);
  }

  async installDailyLaunchAgent({ time }) {
    const normalizedTime = validateScheduleTime(time);
    if (!this.executablePath || !path.isAbsolute(this.executablePath)) throw new Error('Rewindle executable path is not configured.');
    try {
      await fs.access(this.executablePath, fsConstants.X_OK);
    } catch {
      throw new Error('Rewindle executable is missing or not executable.');
    }
    const plist = buildLaunchAgentPlist({
      executablePath: this.executablePath,
      time: normalizedTime,
      label: this.label,
      workingDirectory: this.dataDir,
    });
    const previousPlist = await fs.readFile(this.plistPath, 'utf8').catch(() => null);
    await fs.mkdir(path.dirname(this.plistPath), { recursive: true, mode: 0o700 });
    const tempPath = `${this.plistPath}.${process.pid}.${crypto.randomBytes(6).toString('hex')}.tmp`;
    await fs.writeFile(tempPath, plist, { encoding: 'utf8', mode: 0o600 });
    await fs.rename(tempPath, this.plistPath);
    await this._launchctl(['bootout', `${userDomainTarget(this.uid)}/${this.label}`], true);
    const result = await this._launchctl(['bootstrap', userDomainTarget(this.uid), this.plistPath], false);
    if (result.code !== 0) {
      if (previousPlist !== null) {
        await fs.writeFile(this.plistPath, previousPlist, { encoding: 'utf8', mode: 0o600 });
        await this._launchctl(['bootstrap', userDomainTarget(this.uid), this.plistPath], true);
      } else {
        await fs.rm(this.plistPath, { force: true });
      }
      throw new Error(result.stderr.trim() || 'launchd could not install the Rewindle schedule.');
    }
    return { path: this.plistPath, label: this.label, time: normalizedTime };
  }

  async removeDailyLaunchAgent() {
    await this._launchctl(['bootout', `${userDomainTarget(this.uid)}/${this.label}`], true);
    await fs.rm(this.plistPath, { force: true });
    return { removed: true, path: this.plistPath };
  }

  async readDailyLaunchAgent() {
    let text;
    try {
      text = await fs.readFile(this.plistPath, 'utf8');
    } catch {
      return { enabled: false, loaded: false, verified: false, time: null, path: this.plistPath, detail: 'LaunchAgent plist is missing.' };
    }
    const hour = text.match(/<key>Hour<\/key>\s*<integer>(\d+)<\/integer>/)?.[1];
    const minute = text.match(/<key>Minute<\/key>\s*<integer>(\d+)<\/integer>/)?.[1];
    const executable = text.match(/<key>ProgramArguments<\/key>\s*<array>\s*<string>([\s\S]*?)<\/string>/)?.[1];
    const time = hour === undefined || minute === undefined ? null : `${String(Number(hour)).padStart(2, '0')}:${String(Number(minute)).padStart(2, '0')}`;
    const plistExecutable = executable ? plistUnescape(executable) : '';
    let executableOk = false;
    try {
      await fs.access(this.executablePath, fsConstants.X_OK);
      executableOk = true;
    } catch {
      executableOk = false;
    }
    const printed = await this._launchctl(['print', `${userDomainTarget(this.uid)}/${this.label}`], true);
    const loaded = printed.code === 0;
    const verified = Boolean(loaded && executableOk && plistExecutable === this.executablePath && time);
    return {
      enabled: verified,
      loaded,
      verified,
      executableOk,
      time,
      path: this.plistPath,
      detail: verified ? 'User LaunchAgent is loaded and points to the executable.' : 'LaunchAgent is not loaded or its executable could not be verified.',
    };
  }

  async _launchctl(args, ignoreFailure) {
    const child = spawnDefault(this.spawnProcess, '/bin/launchctl', args, { cwd: this.homeDirectory });
    try {
      const result = await waitForChild(child);
      if (ignoreFailure && result.code !== 0) return { ...result, ignored: true };
      return result;
    } catch (error) {
      if (ignoreFailure) return { code: 1, error, ignored: true };
      throw error;
    }
  }
}

module.exports = {
  LABEL,
  MacLaunchAgentScheduler,
  buildLaunchAgentPlist,
  launchAgentPath,
  validateScheduleTime,
  userDomainTarget,
};
