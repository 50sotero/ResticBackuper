import { useEffect, useMemo, useRef, useState } from 'react';
import { AnimatePresence, motion, MotionConfig } from 'motion/react';
import { Liveline } from 'liveline';
import {
  ShieldCheck, History, RotateCcw, Settings, RefreshCw, ArrowUpRight, ArrowRight,
  FolderPlus, Folder, HardDrive, Clock3, LockKeyhole, Check, X, Play, Pause,
  Search, Sun, Moon, Monitor, ChevronRight, CloudCheck, ArchiveRestore,
  Files, Gauge, Database, CircleAlert, CircleCheck, Activity, ListChecks,
} from 'lucide-react';
import SidebarNav from '@/components/primitives/SidebarNav';
import TaskRows, { type TaskRow } from '@/components/primitives/TaskRows';
import FilterTable, { type TableRow, type FilterTableSelection } from '@/components/primitives/FilterTable';
import LoadingState from '@/components/primitives/LoadingState';
import ContextCards from '@/components/primitives/ContextCards';
import InsightCards, { type InsightPage } from '@/components/primitives/InsightCards';
import { Button } from '@/components/atoms/Button';
import { Shimmer } from '@/components/atoms/Shimmer';
import { useDashboard } from './useDashboard';
import type { CommandName, DashboardPage, DashboardState, RunState, ThemePreference } from './native-types';

type Send = (command: CommandName, payload?: Record<string, string | boolean>) => void;
const navItems = [
  { key: 'Protection', label: 'Protection', icon: <ShieldCheck size={18} /> },
  { key: 'Activity', label: 'Activity', icon: <History size={18} /> },
  { key: 'Restore', label: 'Restore', icon: <ArchiveRestore size={18} /> },
];
const number = new Intl.NumberFormat();
const bytes = (n: number) => n >= 1073741824 ? `${number.format(Math.round(n / 1073741824 * 100) / 100)} GiB` : n >= 1048576 ? `${number.format(Math.round(n / 1048576 * 10) / 10)} MiB` : `${number.format(n)} B`;
const duration = (n: number) => { const seconds = Math.max(0, Math.floor(n)); return seconds >= 3600 ? `${Math.floor(seconds / 3600)}h ${String(Math.floor(seconds % 3600 / 60)).padStart(2, '0')}m` : seconds >= 60 ? `${Math.floor(seconds / 60)}m ${String(seconds % 60).padStart(2, '0')}s` : `${seconds}s`; };
type HistoryView = { query: string; sort: string; filter: FilterTableSelection };
type FolderView = { query: string; unavailable: boolean };

function Action({ state, send, command, children, icon, primary = false, payload, size = 'sm' }: {
  state: DashboardState; send: Send; command: CommandName; children?: React.ReactNode;
  icon?: React.ReactNode; primary?: boolean; payload?: Record<string, string | boolean>; size?: 'sm' | 'md' | 'xs';
}) {
  const action = state.actions[command];
  if (action?.visible === false) return null;
  return <Button size={size} variant={primary ? 'primary' : 'secondary'} disabled={!action?.enabled}
    title={action?.help} onClick={() => send(command, payload)}>{icon}{children ?? action?.label ?? command}</Button>;
}

function Stat({ label, value, icon }: { label: string; value: string; icon?: React.ReactNode }) {
  return <div><span className="stat-label">{icon}{label}</span><div style={{ overflow: 'hidden' }}>
    <motion.span className="stat-number" initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }}
      transition={{ duration: .28, ease: [.16, 1, .3, 1] }}>{value || '—'}</motion.span>
  </div></div>;
}

function Heading({ title, description, children }: { title: string; description: string; children?: React.ReactNode }) {
  return <div className="section-head"><div><h1 className="page-title">{title}</h1><p className="page-description">{description}</p></div>{children && <div className="actions">{children}</div>}</div>;
}

function Repairs({ state, send }: { state: DashboardState; send: Send }) {
  if (!state.recovery.repairNeeded) return null;
  return <motion.div className="attention-note" initial={{ opacity: 0, y: -8 }} animate={{ opacity: 1, y: 0 }}>
    <strong><CircleAlert size={14} className="inline mr-2" />Repository needs attention</strong>
    <p>{state.recovery.repairMessage}<br />{state.recovery.repairState}</p>
    {state.recovery.repairBusy ? <LoadingState label="Repairing repository" variant="Orbit" />
      : <Action state={state} send={send} command="repairRepository" />}
  </motion.div>;
}

function Protection({ state, send, folders, setFolders }: { state: DashboardState; send: Send; folders: FolderView; setFolders: (value: FolderView) => void }) {
  const s = state.status;
  const names = ['Back up files', 'Save snapshot', 'Check repository', 'Test restore'];
  const rows: TaskRow[] = names.map((label, i) => ({
    key: `backup-${i}`, label, step: i + 1,
    status: s.success ? 'done' : s.active && i < s.phaseIndex ? 'done' : s.active && i === s.phaseIndex ? 'running'
      : s.failure && i === s.phaseIndex ? 'failed' : s.cancelled && i === s.phaseIndex ? 'cancelled' : 'pending',
    amount: s.success ? ['Files protected', 'Snapshot saved', 'Integrity checked', 'Canary verified'][i] : s.active && i === s.phaseIndex ? s.phaseLabel : '',
    details: [
      { label: ['Read your protected folders', 'Save an encrypted point in time', 'Check repository integrity', 'Recover and verify the restore canary'][i], meta: '' },
      { label: i === 0 ? `${s.files} files · ${s.bytes}` : i === 1 ? state.repository.path : i === 2 ? 'Restic repository check' : 'Independent restore verification', meta: '' },
    ],
  }));
  const unavailable = state.sources.filter(source => !source.exists).length;
  const visibleSources = state.sources.filter(source => (!folders.unavailable || !source.exists) && `${source.name} ${source.path}`.toLowerCase().includes(folders.query.trim().toLowerCase()));
  const chunks = visibleSources.map(source => ({
    id: source.path, title: source.name, chars: source.isCanary ? 'Required' : source.exists ? 'Protected' : 'Unavailable',
    body: source.path, source: source.isCanary ? 'Includes the restore canary' : source.exists ? 'Included in future backups' : 'Folder not found on this computer',
    badge: source.isCanary ? 'RC' : 'DIR', tone: source.isCanary ? 'bg-green' : 'bg-accent',
    action: !source.isCanary ? <Button size="xs" variant="quiet" disabled={!source.canRemove} title={`Remove ${source.name} from future backups`} aria-label={`Remove ${source.name}`}
      onClick={() => send('removeSource', { path: source.path })}><X size={13} /></Button> : <LockKeyhole size={12} className="text-ink-3" />,
  }));
  return <>
    <Heading title="Protection" description={`${state.sources.length} protected locations · ${state.schedule.summary}`}>
      <Action state={state} send={send} command="cancelBackup" icon={<X size={13} />} />
      <Action state={state} send={send} command="backupNow" primary size="md" icon={<Play size={13} fill="currentColor" />} />
    </Heading>
    <Repairs state={state} send={send} />
    <section className={`surface surface-pad protection-hero ${s.active ? 'is-active' : ''}`}>
      <div className="hero-grid">
        <div>
          <span className="eyebrow">Protection status</span>
          <div className="status-line">
            <motion.div key={s.key} initial={{ scale: .7, opacity: 0 }} animate={{ scale: 1, opacity: 1 }} transition={{ type: 'spring', stiffness: 310, damping: 21 }}
              className={`status-emblem ${s.active ? 'active' : s.failure ? 'danger' : s.cancelled || !s.success ? 'warning' : ''}`}>
              {s.failure ? <CircleAlert size={22} /> : s.active ? <Activity size={23} /> : s.success ? <ShieldCheck size={24} /> : <Clock3 size={22} />}
            </motion.div>
            <div className="status-title">{s.title}</div>
          </div>
          <div className="status-detail">{s.detail}</div>
          {s.active ? <div className="progress-area">
            <div className="progress-copy"><LoadingState label={s.phaseLabel || 'Backing up'} /><strong>{Math.round(s.progress * 100)}%</strong></div>
            <div className="progress-rail" role="progressbar" aria-label={state.preview ? 'Preview backup progress' : 'Backup progress'} aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(s.progress * 100)}><motion.div className="progress-bar" animate={{ width: `${Math.max(0, Math.min(100, s.progress * 100))}%` }} transition={{ duration: .65, ease: [.16, 1, .3, 1] }} /></div>
            <p className="muted-copy" style={{ marginTop: 9 }} title={s.etaHint}>{s.etaTitle || 'Time remaining'}: {s.eta || 'Calculating…'}{ s.estimated ? ' · estimated' : '' }</p>
          </div> : <div className="status-tags"><span className="quiet-tag"><LockKeyhole size={11} />Restic encryption</span><span className="quiet-tag">{s.success ? <Check size={11} /> : <Clock3 size={11} />}{!s.success && s.badge === 'READY' ? 'Awaiting backup' : s.badge}</span></div>}
          <div className="info-action"><Action state={state} send={send} command="reviewChanges" icon={<ListChecks size={13} />} /></div>
        </div>
        <div><TaskRows rows={rows} className="backup-tasks" labels={{ completed: 'Verified', failed: 'Failed' }} /></div>
      </div>
      <div className="stats-grid">
        <Stat label="Files" value={s.files} icon={<Files size={12} />} />
        <Stat label="Processed" value={s.bytes} icon={<Database size={12} />} />
        <Stat label={s.active ? 'Throughput' : 'Last run'} value={s.active ? s.speed : s.elapsed} icon={<Gauge size={12} />} />
        <Stat label={s.active ? 'Elapsed' : 'Errors'} value={s.active ? s.elapsed : s.errors} icon={<Clock3 size={12} />} />
      </div>
    </section>
    <div className="two-columns backup-destinations">
      <section className="surface surface-pad"><div className="info-heading"><Clock3 size={15} className="text-ink-3" />Next automatic backup</div>
        <div className="info-main">{state.schedule.nextRun || 'No upcoming run'}</div><div className="muted-copy">{state.schedule.summary}</div>
        <div className="info-action"><Action state={state} send={send} command="editSchedule" icon={<ArrowUpRight size={12} />} /></div></section>
      <section className="surface surface-pad"><div className="info-heading"><HardDrive size={15} className="text-ink-3" />Backup destination</div>
        <div className="info-main">{state.repository.path || 'Destination unavailable'}</div><div className="muted-copy">{state.repository.volume}</div>
        <div className="info-action"><Action state={state} send={send} command="changeRepository" icon={<ArrowUpRight size={12} />} /></div></section>
    </div>
    <div className="health-summary">
      <div><ShieldCheck size={16} /><span><strong>{state.freshness.title || 'Backup freshness unavailable'}</strong><span>{state.freshness.detail}</span></span></div>
      <div><CloudCheck size={16} /><span><strong>{state.offsite.title || 'Cloud verification unavailable'}</strong><span>{state.offsite.detail}</span></span></div>
    </div>
    <section>
      <div className="section-toolbar"><div><h2 className="section-title">Protected folders <span className="count-badge">{state.sources.length}</span></h2><p className="muted-copy" style={{ margin: '5px 0 0' }}>Included in each backup. Adding a folder does not start a run.</p></div><Action state={state} send={send} command="addSource" icon={<FolderPlus size={13} />} /></div>
      <div className="folder-toolbar"><label className="search-field"><Search size={14} /><input aria-label="Search protected folders" placeholder="Find a folder or path…" value={folders.query} onChange={event => setFolders({ ...folders, query: event.target.value })} />{folders.query && <button aria-label="Clear folder search" onClick={() => setFolders({ ...folders, query: '' })}><X size={13} /></button>}</label><Button size="xs" aria-pressed={folders.unavailable} onClick={() => setFolders({ ...folders, unavailable: !folders.unavailable })}><CircleAlert size={12} />Unavailable <span>{unavailable}</span></Button><span className="muted-copy">{visibleSources.length} of {state.sources.length} locations</span></div>
      {state.sourceStatus && <p className={/missing|unavailable|cannot|failed/i.test(state.sourceStatus) ? 'source-warning' : 'source-guidance'} role={/missing|unavailable|cannot|failed/i.test(state.sourceStatus) ? 'alert' : undefined}>{state.sourceStatus}</p>}
      {state.sourceOperation.stage !== 'Idle' && state.sourceOperation.stage !== 'Succeeded' && <div className={state.sourceOperation.active ? 'operation-note' : 'attention-note'} style={{ marginBottom: 15 }} role={state.sourceOperation.stage === 'Failed' ? 'alert' : 'status'}>
        <div>{state.sourceOperation.active ? <LoadingState label={state.sourceOperation.message || 'Updating protected folders'} variant="Orbit" /> : <><strong>{state.sourceOperation.stage === 'Cancelled' ? 'Folder change cancelled' : 'Folder change needs attention'}</strong><p>{state.sourceOperation.message}</p></>}
          {state.sourceOperation.path && <p className="muted-copy" style={{ margin: '7px 0 0' }}>{state.sourceOperation.path}</p>}</div>
        <div className="actions"><Action state={state} send={send} command="retrySourceChange" /><Action state={state} send={send} command="dismissSourceChange" /></div>
      </div>}
      {chunks.length > 0 ? <ContextCards chunks={chunks} labels={{ header: 'Backup scope', count: String(chunks.length) }} className="folder-cards" />
        : <div className="surface empty-state"><Folder size={25} /><strong>{state.sources.length ? 'No matching folders' : 'No folders available'}</strong><p>{state.sources.length ? folders.unavailable ? 'All other protected folders are available.' : 'Try a folder name or a different part of its path.' : state.sourceStatus}</p>{state.sources.length > 0 && <Button size="sm" onClick={() => setFolders({ query: '', unavailable: false })}>Show all folders</Button>}</div>}
    </section>
  </>;
}

function RunChart({ history, metric, dark }: { history: RunState[]; metric: 'duration' | 'processed'; dark: boolean }) {
  const runs = useMemo(() => [...history].sort((a, b) => Date.parse(a.started) - Date.parse(b.started)).slice(-30), [history]);
  // Liveline's frozen viewport is anchored to mount time. Shift the historical
  // axis together and reverse the shift in labels; values and intervals stay exact.
  const timeOffset = useMemo(() => runs.length ? Math.floor(Date.now() / 1000) - Date.parse(runs[runs.length - 1].started) / 1000 : 0, [runs]);
  const points = useMemo(() => runs.map(run => ({ time: Date.parse(run.started) / 1000 + timeOffset, value: metric === 'duration' ? run.durationSeconds : run.processedBytes })), [runs, metric, timeOffset]);
  const last = points.at(-1);
  const first = points[0];
  if (!last || !first || points.length < 2) return <div className="surface first-run-summary"><History size={22} /><div><strong>{last ? 'Your first backup is recorded' : 'No completed backups yet'}</strong><p>{last ? `${runs[0].startedDisplay} · ${metric === 'duration' ? runs[0].durationDisplay : runs[0].processedDisplay}. A trend appears after the next backup.` : 'Run history and trends will appear after your first backup.'}</p></div></div>;
  return <div className="surface chart-card">
    <div className="chart-heading"><div><span className="eyebrow">{metric === 'duration' ? 'Latest run duration' : 'Latest data processed'}</span><div className="chart-value">{metric === 'duration' ? duration(last.value) : bytes(last.value)}</div></div><span className="quiet-tag">{runs.length} runs</span></div>
    <div className="chart-area"><Liveline data={points} value={last.value} theme={dark ? 'dark' : 'light'} color={metric === 'duration' ? '#5b91ed' : '#33af8b'}
      grid fill scrub paused pulse={false} momentum={false} window={Math.max(60, last.time - first.time)}
      formatValue={metric === 'duration' ? duration : bytes} formatTime={time => new Date((time - timeOffset) * 1000).toLocaleDateString(undefined, { day: 'numeric', month: 'short' })}
      padding={{ top: 12, right: 68, bottom: 28, left: 8 }} lineWidth={2.25} /></div>
  </div>;
}

function ActivityPage({ state, send, view, setView }: { state: DashboardState; send: Send; view: HistoryView; setView: (value: HistoryView) => void }) {
  const { query, sort, filter } = view;
  const historySignature = JSON.stringify(state.history);
  const history = useMemo(() => [...state.history].sort((a, b) => Date.parse(b.started) - Date.parse(a.started)), [historySignature]);
  const backupRuns = useMemo(() => history.filter(run => run.type === 'Backup'), [history]);
  const success = backupRuns.filter(run => run.success);
  const average = success.length ? success.reduce((sum, run) => sum + run.durationSeconds, 0) / success.length : 0;
  const selected = state.history.find(run => run.id === state.selectedRunId);
  const selectedVisible = !!selected && [selected.id, selected.startedDisplay, selected.result, selected.snapshot, selected.type].join(' ').toLowerCase().includes(query.trim().toLowerCase()) && (filter === 'all' || filter === (selected.success ? 'done' : /^(cancelled|canceled)$/i.test(selected.result) ? 'progress' : 'todo'));
  const rows: TableRow[] = useMemo(() => {
    const filtered = history.filter(run => [run.id, run.startedDisplay, run.result, run.snapshot, run.type].join(' ').toLowerCase().includes(query.trim().toLowerCase()));
    filtered.sort((a, b) => sort === 'duration' ? b.durationSeconds - a.durationSeconds : sort === 'size' ? b.processedBytes - a.processedBytes : sort === 'oldest' ? Date.parse(a.started) - Date.parse(b.started) : Date.parse(b.started) - Date.parse(a.started));
    return filtered.map(run => ({ id: run.id, task: `${run.startedDisplay}${run.type !== 'Backup' ? ` · ${run.type}` : ''}`, date: run.durationDisplay, status: run.success ? 'done' : /^(cancelled|canceled)$/i.test(run.result) ? 'progress' : 'todo', statusLabel: run.result, owner: `${run.processedDisplay} · ${run.filesDisplay} files` }));
  }, [history, query, sort]);
  const pages: InsightPage[] = useMemo(() => [
    { key: 'duration', prose: backupRuns.length > 1 ? 'How long your backups take. Scrub the chart to inspect past runs.' : 'Compare backup duration as your run history grows.', Card: () => <RunChart history={backupRuns} metric="duration" dark={state.dark} />, pill: 'Open selected run details' },
    { key: 'processed', prose: 'The amount of data processed by each backup run.', Card: () => <RunChart history={backupRuns} metric="processed" dark={state.dark} />, pill: 'Open selected run details' },
  ], [backupRuns, state.dark]);
  return <>
    <Heading title="Activity" description="Search your runs, inspect a snapshot, or review what needs attention.">
      <Action state={state} send={send} command="exportDiagnostics" icon={<ArrowUpRight size={13} />} />
    </Heading>
    <div className="activity-metrics">
      <div className="surface"><Stat label="Verified backups" value={`${success.length} / ${backupRuns.length}`} icon={<ShieldCheck size={13} />} /></div>
      <div className="surface"><Stat label="Average duration" value={success.length ? duration(average) : '—'} icon={<Clock3 size={13} />} /></div>
      <div className="surface"><Stat label="Latest stored" value={success.length ? bytes(success[0].storedBytes) : '—'} icon={<Database size={13} />} /></div>
    </div>
    <section>
      <div className="section-toolbar"><h2 className="section-title">Run history <span className="text-ink-3 font-normal ml-2">{history.length}</span></h2><div className="history-tools">
        <label className="search-field"><Search size={13} /><input aria-label="Search backup history" placeholder="Date, result, or snapshot…" value={query} onChange={event => setView({ ...view, query: event.target.value })} />{query && <button aria-label="Clear history search" onClick={() => setView({ ...view, query: '' })}><X size={13} /></button>}</label>
        <select className="sort-field" aria-label="Sort backup history" value={sort} onChange={event => setView({ ...view, sort: event.target.value })}><option value="newest">Newest first</option><option value="oldest">Oldest first</option><option value="duration">Longest first</option><option value="size">Largest first</option></select>
      </div></div>
      <FilterTable className="history-table" rows={rows} labels={{ columns: { task: 'When', date: 'Duration', status: 'Result', owner: 'Processed · files' }, filters: { all: 'All runs', done: 'Completed', todo: 'Needs attention', progress: 'Cancelled' } }}
        selectedRowId={state.selectedRunId} filter={filter} onFilterChange={next => setView({ ...view, filter: next })} tableLabel="Backup run history"
        onRowClick={row => send('selectRun', { runId: row.id ?? '' })} onRowOpen={row => send('viewRunDetails', { runId: row.id ?? '' })}
        emptyContent={<div className="empty-state"><Search size={22} /><strong>{history.length ? 'No runs match these filters' : 'No backup history yet'}</strong><p>{history.length ? 'Try another search or show all results.' : 'Your completed backup runs will appear here.'}</p>{history.length > 0 && <Button size="sm" onClick={() => setView({ query: '', sort: 'newest', filter: 'all' })}>Clear filters</Button>}</div>} />
      <div className="selection-bar"><div><strong>{selected ? `${selected.type} · ${selected.startedDisplay}` : 'Select a run to see its details'}</strong><span>{selected ? `${selected.result} · Snapshot ${selected.snapshot || 'unavailable'}${selectedVisible ? '' : ' · Hidden by current filters'}` : 'Double-click a row or press Enter to open it.'}</span></div><Action state={state} send={send} command="viewRunDetails" payload={selected ? { runId: selected.id } : undefined} icon={<ArrowRight size={13} />} /></div>
    </section>
    <div className="insights-wide"><InsightCards pages={pages} labels={{ title: 'Backup trends' }} actionDisabled={!selected || !state.actions.viewRunDetails?.enabled} onAction={() => selected && send('viewRunDetails', { runId: selected.id })} /></div>
  </>;
}

function RestorePage({ state, send }: { state: DashboardState; send: Send }) {
  const steps: TaskRow[] = [
    { key: 'choose', label: 'Choose a snapshot', amount: '', step: 1, status: 'guide', details: [{ label: 'Browse saved points in time in Restore Center.', meta: '' }] },
    { key: 'find', label: 'Find your files', amount: '', step: 2, status: 'guide', details: [{ label: 'Explore the original folder structure and select what you need.', meta: '' }] },
    { key: 'destination', label: 'Choose an empty folder', amount: '', step: 3, status: 'guide', details: [{ label: 'Restore into a new or empty location. Your original files stay in place.', meta: '' }] },
    { key: 'verify', label: 'Restore and verify', amount: '', step: 4, status: 'guide', details: [{ label: 'Check the recovered files before using them.', meta: '' }] },
  ];
  return <><Heading title="Restore" description="Choose a saved snapshot and recover files to a separate folder." />
    <Repairs state={state} send={send} />
    <div className="restore-layout"><section className="surface"><span className="eyebrow">Restore Center</span><div className={`status-emblem ${state.recovery.repairNeeded ? 'warning' : ''}`} style={{ marginTop: 22 }}><ArchiveRestore size={24} /></div>
      <h2>{state.recovery.title}</h2><p className="muted-copy">{state.recovery.detail}</p>
      <div className="actions" style={{ marginTop: 22 }}><Action state={state} send={send} command="openRestore" primary size="md" icon={<ArrowUpRight size={14} />} /><Action state={state} send={send} command="checkReadiness" /></div>
    </section><div><h2 className="section-title" style={{ marginBottom: 16 }}>How recovery works</h2><TaskRows rows={steps} className="restore-steps" variant="List" /></div></div>
    <div className="surface surface-pad"><div className="info-heading"><HardDrive size={15} />Your recovery source</div><div className="info-main">{state.repository.path}</div><p className="muted-copy">Browse a snapshot, select files, and review the destination before restoring. Recovered files are verified after the operation.</p></div>
  </>;
}

function SettingsPage({ state, send, replay }: { state: DashboardState; send: Send; replay: () => void }) {
  const themes: { name: ThemePreference; label: string; Icon: typeof Sun }[] = [{ name: 'System', label: 'System', Icon: Monitor }, { name: 'Midnight', label: 'Dark', Icon: Moon }, { name: 'Daylight', label: 'Light', Icon: Sun }];
  return <><Heading title="Settings" description="Manage your backup plan and make the app comfortable to use." />
    <Repairs state={state} send={send} />
    <section className="settings-section"><h2 className="section-title">Your workspace</h2><div className="settings-grid">
      <section className="surface setting-row"><div><h3 className="info-heading"><Sun size={15} />Appearance</h3><p className="muted-copy">Follow your system, or choose a light or dark workspace.</p></div><div className="theme-picker" role="group" aria-label="Appearance">
        {themes.map(({ name, label, Icon }) => <button key={name} className="theme-choice" aria-pressed={state.theme === name} onClick={() => send('setTheme', { theme: name })}>
          {state.theme === name && <motion.div className="theme-highlight" layoutId="theme-thumb" transition={{ type: 'spring', stiffness: 440, damping: 34 }} />}<span><Icon size={13} />{label}</span></button>)}
      </div></section>
      <section className="surface setting-row"><div><h3 className="info-heading"><Activity size={15} />Motion</h3><p className="muted-copy">{state.reducedMotion ? 'Reduced motion follows your system preference.' : 'Preview the animated backup stages without starting a backup.'}</p></div><div className="actions"><Button size="sm" onClick={replay}><RotateCcw size={12} />Replay entrance</Button><Action state={state} send={send} command="togglePreview" icon={state.preview ? <Pause size={12} /> : <Play size={12} />}>{state.preview ? 'Stop preview' : 'Preview animations'}</Action></div></section>
    </div></section>
    <section className="settings-section"><h2 className="section-title">Backup plan</h2><div className="settings-grid">
      <section className="surface setting-row"><div><h3 className="info-heading"><HardDrive size={15} />Storage location</h3><p className="muted-copy path-copy">{state.repository.path}</p><p className="muted-copy">{state.repository.volume}</p></div><Action state={state} send={send} command="changeRepository" /></section>
      <section className="surface setting-row"><div><h3 className="info-heading"><Clock3 size={15} />Automatic backups</h3><p className="info-main">{state.schedule.summary}</p><p className="muted-copy">Next: {state.schedule.nextRun}</p><details className="inline-details"><summary>When this computer is unavailable</summary><p className="muted-copy">{state.schedule.detail}</p></details></div><Action state={state} send={send} command="editSchedule" /></section>
    </div></section>
    <section className="settings-section"><h2 className="section-title">Verification & support</h2><div className="settings-grid">
      <section className="surface setting-row"><div><h3 className="info-heading"><ShieldCheck size={15} />Backup freshness</h3><p className="setting-status">{state.freshness.title}</p><p className="muted-copy">{state.freshness.detail}</p></div></section>
      <section className="surface setting-row"><div><h3 className="info-heading"><CloudCheck size={15} />Off-site verification</h3><p className="setting-status">{state.offsite.title}</p><p className="muted-copy">{state.offsite.detail}</p><details className="inline-details"><summary>Verification evidence</summary><p className="muted-copy">{state.offsite.evidence || 'No verified evidence is available yet.'}</p></details></div></section>
      <section className="surface setting-row support-row"><div><h3 className="info-heading"><ListChecks size={15} />Diagnostics</h3><p className="muted-copy">Create a redacted support bundle to investigate a problem.</p></div><Action state={state} send={send} command="exportDiagnostics" icon={<ArrowUpRight size={13} />} /></section>
    </div></section>
  </>;
}

export default function App() {
  const { state: rawState, connected, send, notice, dismissNotice } = useDashboard();
  const state = useMemo(() => {
    if (!rawState) return null;
    const blocked = !connected || !!rawState.dataError;
    const protectedCommands = new Set(['backupNow', 'cancelBackup', 'addSource', 'removeSource', 'editSchedule', 'changeRepository', 'repairRepository', 'reviewChanges', 'openRestore', 'checkReadiness', 'retrySourceChange']);
    if (!blocked && !rawState.preview) return rawState;
    return { ...rawState, sources: rawState.sources.map(source => ({ ...source, canRemove: false })), actions: Object.fromEntries(Object.entries(rawState.actions).map(([key, value]) => [key, value && (blocked || protectedCommands.has(key)) ? { ...value, enabled: false, help: blocked ? 'Refresh to load current backup status before using this action.' : 'Stop the animation preview before using this action.' } : value])) };
  }, [rawState, connected]);
  const [page, setPage] = useState<DashboardPage>('Protection');
  const [replay, setReplay] = useState(0);
  const [historyView, setHistoryView] = useState<HistoryView>({ query: '', sort: 'newest', filter: 'all' });
  const [folderView, setFolderView] = useState<FolderView>({ query: '', unavailable: false });
  const scrollRef = useRef<HTMLDivElement>(null);
  const pageRef = useRef<HTMLElement>(null);
  const previousPage = useRef<DashboardPage>('Protection');
  const scrollPositions = useRef<Partial<Record<DashboardPage, number>>>({});
  const focusPage = useRef(false);
  useEffect(() => {
    if (!state) return;
    document.documentElement.classList.toggle('dark', state.dark);
    document.documentElement.style.colorScheme = state.dark ? 'dark' : 'light';
    document.documentElement.dataset.highContrast = String(state.highContrast);
    document.documentElement.dataset.reducedMotion = String(state.reducedMotion);
  }, [state?.dark, state?.highContrast, state?.reducedMotion]);
  useEffect(() => { if (state?.page) setPage(state.page); }, [state?.page]);
  useEffect(() => {
    if (previousPage.current !== page) {
      scrollPositions.current[previousPage.current] = scrollRef.current?.scrollTop ?? 0;
      previousPage.current = page;
      focusPage.current = true;
    }
  }, [page]);
  const navigate = (next: DashboardPage) => { setPage(next); send('navigate', { page: next }); };
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const target = event.target;
      if (target instanceof HTMLElement && (target.matches('input, textarea, select') || target.isContentEditable)) return;
      if ((event.ctrlKey || event.metaKey) && !event.altKey && ['1', '2', '3', '4'].includes(event.key)) {
        event.preventDefault(); navigate((['Protection', 'Activity', 'Restore', 'Settings'] as DashboardPage[])[Number(event.key) - 1]);
      } else if (event.key === '/' && (page === 'Activity' || page === 'Protection')) {
        event.preventDefault(); pageRef.current?.querySelector<HTMLInputElement>('.search-field input')?.focus();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [page, send]);
  if (!state) return <div className="connection-screen"><img src="./rewindle-icon.svg" alt="" width="48" height="48" /><LoadingState label="Opening Rewindle" variant="Orbit" /><p>{(window.rewindleNative ?? window.chrome?.webview) ? 'Loading your protected folders, backup history, and repository status.' : 'Open the desktop app to connect to your backup state.'}</p></div>;
  const recents = [...state.history].sort((a, b) => Date.parse(b.started) - Date.parse(a.started)).slice(0, 8).map(run => ({ id: run.id, label: `${run.startedDisplay} · ${run.result}` }));
  return <MotionConfig reducedMotion={state.reducedMotion ? 'always' : 'user'}><div className="app-shell">
    <a className="skip-link" href="#main-content" onClick={event => { event.preventDefault(); pageRef.current?.focus(); }}>Skip to content</a>
    <SidebarNav className="app-sidebar" fill activeNav={page} onNavigate={key => navigate(key as DashboardPage)} navItems={navItems}
      primaryActionLabel={state.actions.backupNow?.label || 'Back up now'} primaryActionIcon={<Play size={16} />}
      primaryActionDisabled={!state.actions.backupNow?.enabled} primaryActionHelp={state.actions.backupNow?.help} onPrimaryAction={() => send('backupNow')}
      workspace={{ key: 'rewindle', name: 'rewindle', monogram: 'r', logo: <img className="brand-icon" src="./rewindle-icon.svg" alt="" /> }} onWorkspaceSettings={() => navigate('Settings')} recents={recents} recentLabel="Recent backups" recentSearchLabel="Search recent backups"
      activeTitle={recents.find(run => run.id === state.selectedRunId)?.label ?? null}
      onPick={id => { setHistoryView({ query: '', sort: 'newest', filter: 'all' }); scrollPositions.current.Activity = 0; send('selectRun', { runId: id }); navigate('Activity'); }} footerLabel="Settings" footerActive={page === 'Settings'} footerIcon={<Settings size={14} />} onFooterClick={() => navigate('Settings')} />
    <div className="workspace"><header className="topbar"><div className="breadcrumb"><span>Personal workspace</span><ChevronRight size={11} /><strong>{page}</strong></div><div className="toolbar-actions">
      {(state.demo || state.preview) && <span className="demo-label">{state.demo ? 'Sample data' : 'Animation preview'}</span>}
      <span className="connection"><span className={`connection-dot ${connected ? '' : 'offline'}`} />{connected ? 'Connected' : 'Reconnecting'}</span>
      {(state.demo || state.preview) && <Button size="sm" disabled={!state.actions.togglePreview?.enabled} title={state.actions.togglePreview?.help} onClick={() => send('togglePreview')}>{state.preview ? <X size={12} /> : <Play size={12} />}{state.preview ? 'Stop preview' : 'Play animation'}</Button>}
      <Button size="xs" variant="quiet" aria-label="Refresh dashboard" title="Refresh dashboard (F5)" onClick={() => send('refresh')}><RefreshCw size={14} /></Button>
      <Button size="xs" variant="quiet" aria-label={state.dark ? 'Use light theme' : 'Use dark theme'} title={state.dark ? 'Use light theme' : 'Use dark theme'} onClick={() => send('setTheme', { theme: state.dark ? 'Daylight' : 'Midnight' })}>{state.dark ? <Sun size={15} /> : <Moon size={15} />}</Button>
    </div></header>
    {!connected && <div className="connection-warning" role="status"><CircleAlert size={14} /><span>Connection interrupted. Displayed status may be out of date.</span><Button size="xs" onClick={() => send('refresh')}>Refresh</Button></div>}
    {state.dataError && <div className="connection-warning" role="alert"><CircleAlert size={14} /><span>Backup status could not be read. History shows the last available data. {state.dataError}</span><Button size="xs" onClick={() => send('refresh')}>Retry</Button></div>}
    <div className="page-scroll" ref={scrollRef}><AnimatePresence mode="wait" initial={false}><motion.main id="main-content" tabIndex={-1} ref={pageRef} className="page" key={`${page}-${replay}`}
      initial={{ opacity: 0, y: state.reducedMotion ? 0 : 7 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, transition: { duration: state.reducedMotion ? 0 : .08 } }}
      onAnimationStart={target => { if (typeof target === 'object' && !Array.isArray(target) && target.opacity === 1 && focusPage.current) scrollRef.current?.scrollTo({ top: state.preview && page === 'Protection' ? 0 : scrollPositions.current[page] ?? 0 }); }}
      onAnimationComplete={target => { if (typeof target === 'object' && !Array.isArray(target) && target.opacity === 1 && focusPage.current) { pageRef.current?.focus({ preventScroll: true }); focusPage.current = false; } }}
      transition={{ duration: state.reducedMotion ? 0 : .22, ease: [.16, 1, .3, 1] }}>
      {page === 'Protection' ? <Protection state={state} send={send} folders={folderView} setFolders={setFolderView} /> : page === 'Activity' ? <ActivityPage state={state} send={send} view={historyView} setView={setHistoryView} /> : page === 'Restore' ? <RestorePage state={state} send={send} /> : <SettingsPage state={state} send={send} replay={() => setReplay(value => value + 1)} />}
      <footer className="app-footnote"><span><LockKeyhole size={10} />{state.preview ? 'Animation preview · no backup is running' : 'Restic · encrypted local backup'}</span><span title="Ctrl/Cmd+1–4 changes pages. / focuses search.">{state.status.active ? <Shimmer>{state.preview ? 'Preview running' : 'Backup is running'}</Shimmer> : state.updated}</span></footer>
    </motion.main></AnimatePresence></div></div>
    <AnimatePresence>{notice && <motion.div role={notice.error ? 'alert' : 'status'} className={`toast ${notice.error ? 'error' : ''}`} initial={{ opacity: 0, y: 16, scale: .98 }} animate={{ opacity: 1, y: 0, scale: 1 }} exit={{ opacity: 0, y: 8 }}><span>{notice.text}</span><Button size="xs" variant="quiet" aria-label="Dismiss notification" onClick={dismissNotice}><X size={14} /></Button></motion.div>}</AnimatePresence>
  </div></MotionConfig>;
}
