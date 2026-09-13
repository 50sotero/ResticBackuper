'use strict';

const { contextBridge, ipcRenderer } = require('electron');

// The web client was originally hosted by WebView2.  Keep that small bridge
// deliberately compatible so the same Beautiful UI bundle can run in the
// Windows and macOS hosts without gaining access to Node or Electron.
const nativeListeners = new Set();
const modalListeners = new Set();

function safeListener(listener) {
  return typeof listener === 'function' ? listener : null;
}

ipcRenderer.on('native:message', (_event, message) => {
  for (const listener of nativeListeners) {
    try {
      listener({ data: message });
    } catch (_error) {
      // A renderer listener must never be able to break the bridge dispatch.
    }
  }
});

ipcRenderer.on('modal:init', (_event, options) => {
  for (const listener of modalListeners) {
    try {
      listener(options);
    } catch (_error) {
      // Modal rendering is isolated from the main dashboard bridge.
    }
  }
});

const webview = Object.freeze({
  postMessage(message) {
    // The main process performs the authoritative schema and command checks.
    // Returning the promise is useful for smoke tests, while the existing
    // React client intentionally ignores it just like WebView2 does.
    return ipcRenderer.invoke('native:command', message);
  },
  addEventListener(type, listener) {
    if (type !== 'message') return;
    const safe = safeListener(listener);
    if (safe) nativeListeners.add(safe);
  },
  removeEventListener(type, listener) {
    if (type !== 'message') return;
    nativeListeners.delete(listener);
  },
});

// Chromium owns window.chrome. Expose our narrow API under a product-owned
// property instead of replacing that built-in namespace.
contextBridge.exposeInMainWorld('rewindleNative', webview);

contextBridge.exposeInMainWorld('rewindleModal', Object.freeze({
  onInit(listener) {
    const safe = safeListener(listener);
    if (safe) modalListeners.add(safe);
    return () => {
      if (safe) modalListeners.delete(safe);
    };
  },
  submit(nonce, values) {
    ipcRenderer.send('modal:submit', { nonce, values });
  },
  cancel(nonce) {
    ipcRenderer.send('modal:cancel', { nonce });
  },
}));

// Local-file bundles do not inherit a server response header.  This policy is
// injected before the page's scripts run and keeps the packaged UI local-only.
try {
  const meta = document.createElement('meta');
  meta.httpEquiv = 'Content-Security-Policy';
  meta.content = [
    "default-src 'self'",
    "script-src 'self'",
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: blob:",
    "font-src 'self' data:",
    "connect-src 'none'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'none'",
  ].join('; ');
  document.documentElement.appendChild(meta);
} catch (_error) {
  // CSP is defense in depth; the BrowserWindow still has nodeIntegration off,
  // sandbox on, and the main process denies non-local navigation.
}
