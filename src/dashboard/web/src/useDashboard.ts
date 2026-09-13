import { useCallback, useEffect, useRef, useState } from 'react';
import type { CommandName, DashboardState, NativeMessage } from './native-types';

export function useDashboard() {
  const [state, setState] = useState<DashboardState | null>(null);
  const [notice, setNotice] = useState<{ text: string; error: boolean } | null>(null);
  const [connected, setConnected] = useState(false);
  const lastMessage = useRef(0);
  const sequence = useRef(0);
  const send = useCallback((command: CommandName, payload?: Record<string, string | boolean>) => {
    const native = window.rewindleNative ?? window.chrome?.webview;
    if (!native) { setNotice({ text: 'Open the desktop app to use backup controls.', error: true }); return; }
    if (command !== 'ready' && command !== 'refresh' && lastMessage.current && Date.now() - lastMessage.current > 15000) {
      setNotice({ text: 'The dashboard has lost its connection. Refresh before changing your backup plan.', error: true });
      return;
    }
    native.postMessage({ type: 'command', id: String(++sequence.current), command, payload });
  }, []);
  useEffect(() => {
    const native = window.rewindleNative ?? window.chrome?.webview;
    if (!native) return;
    const receive = (event: MessageEvent<NativeMessage>) => {
      const message = event.data;
      if (message.type === 'state') {
        lastMessage.current = Date.now();
        setConnected(true);
        setState(message.state);
      } else if (message.type === 'result' && message.message) {
        setNotice({ text: message.message, error: !message.ok });
      }
    };
    native.addEventListener('message', receive);
    send('ready');
    const heartbeat = window.setInterval(() => {
      if (lastMessage.current && Date.now() - lastMessage.current > 15000) setConnected(false);
    }, 3000);
    return () => { native.removeEventListener('message', receive); clearInterval(heartbeat); };
  }, [send]);
  useEffect(() => {
    if (!notice || notice.error) return;
    const timeout = setTimeout(() => setNotice(null), 6000);
    return () => clearTimeout(timeout);
  }, [notice]);
  const dismissNotice = useCallback(() => setNotice(null), []);
  return { state, connected, send, notice, dismissNotice };
}
