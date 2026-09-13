export type DashboardPage = 'Protection' | 'Activity' | 'Restore' | 'Settings';
export type ThemePreference = 'System' | 'Midnight' | 'Daylight';
export type CommandName = 'ready' | 'refresh' | 'navigate' | 'setTheme' | 'togglePreview'
  | 'backupNow' | 'cancelBackup' | 'addSource' | 'removeSource' | 'editSchedule'
  | 'changeRepository' | 'repairRepository' | 'reviewChanges' | 'openRestore'
  | 'checkReadiness' | 'selectRun' | 'viewRunDetails' | 'exportDiagnostics'
  | 'retrySourceChange' | 'dismissSourceChange';
export interface ActionState { enabled: boolean; visible: boolean; label: string; help: string }
export interface SourceState { path: string; name: string; isCanary: boolean; exists: boolean; canRemove: boolean; detail: string }
export interface RunState {
  id: string; started: string; startedDisplay: string; type: string; result: string;
  success: boolean; durationSeconds: number; durationDisplay: string; files: number;
  filesDisplay: string; processedBytes: number; processedDisplay: string;
  storedBytes: number; storedDisplay: string; snapshot: string;
}
export interface DashboardState {
  page: DashboardPage; demo: boolean; preview: boolean; theme: ThemePreference;
  dark: boolean; reducedMotion: boolean; highContrast: boolean; updated: string; subtitle: string; dataError?: string;
  status: {
    key: string; title: string; detail: string; badge: string; active: boolean;
    success: boolean; failure: boolean; cancelled: boolean; phaseIndex: number;
    phaseLabel: string; progress: number; estimated: boolean; runId: string;
    files: string; bytes: string; speed: string; elapsed: string; errors: string;
    etaTitle: string; eta: string; etaHint: string;
  };
  sources: SourceState[]; sourceStatus: string;
  sourceOperation: { active: boolean; stage: string; message: string; path: string };
  history: RunState[]; selectedRunId: string | null;
  schedule: { summary: string; nextRun: string; detail: string };
  repository: { path: string; volume: string };
  freshness: { title: string; detail: string };
  offsite: { title: string; detail: string; evidence: string };
  recovery: { title: string; detail: string; repairNeeded: boolean; repairMessage: string; repairState: string; repairBusy: boolean; repairPercent: number | null };
  actions: Partial<Record<CommandName, ActionState>>;
}
export interface NativeCommand { type: 'command'; id: string; command: CommandName; payload?: Record<string, string | boolean> }
export type NativeMessage = { type: 'state'; state: DashboardState } | { type: 'result'; id: string; ok: boolean; message?: string };
export interface NativeWebView {
  postMessage(message: NativeCommand): void;
  addEventListener(type: 'message', listener: (event: MessageEvent<NativeMessage>) => void): void;
  removeEventListener(type: 'message', listener: (event: MessageEvent<NativeMessage>) => void): void;
}
declare global { interface Window { chrome?: { webview?: NativeWebView } } }
