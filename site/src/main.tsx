import { StrictMode, useEffect, useRef, useState, type ReactNode } from "react";
import { createRoot } from "react-dom/client";
import {
  AnimatePresence,
  MotionConfig,
  motion,
  useReducedMotion,
  useScroll,
  useTransform,
} from "motion/react";
import {
  Activity,
  ArrowDown,
  ArrowDownToLine,
  ArrowRight,
  ArrowUpRight,
  Check,
  CheckCheck,
  ChevronRight,
  CircleHelp,
  Clock3,
  FileText,
  Folder,
  Github,
  HardDrive,
  History,
  KeyRound,
  LockKeyhole,
  Menu,
  Monitor,
  Pause,
  Play,
  RotateCcw,
  Settings2,
  ShieldCheck,
  Sparkles,
  X,
} from "lucide-react";
import { Button } from "./vendor/beautifului/Button";
import { Shimmer } from "./vendor/beautifului/Shimmer";
import TaskRows, { type TaskRow } from "./vendor/beautifului/TaskRows";
import GlideMenu from "./vendor/beautifului/GlideMenu";
import "./styles.css";

const BASE = import.meta.env.BASE_URL;
const REPO = "https://github.com/50sotero/ResticBackuper";
const RELEASE = `${REPO}/releases/tag/v0.2.0-alpha.1`;
const DOWNLOAD = `${REPO}/releases/download/v0.2.0-alpha.1/`;
const ease = [0.16, 1, 0.3, 1] as const;

function Brand({ light = false }: { light?: boolean }) {
  return (
    <img
      className="brand"
      src={`${BASE}brand/lockup-${light ? "light" : "dark"}.svg`}
      alt="Rewindle"
      width="170"
      height="40"
    />
  );
}

function Reveal({
  children,
  className = "",
  enabled = true,
}: {
  children: ReactNode;
  className?: string;
  enabled?: boolean;
}) {
  return (
    <motion.div
      className={className}
      initial={enabled ? { opacity: 0, y: 30 } : false}
      whileInView={{ opacity: 1, y: 0 }}
      viewport={{ once: true, amount: 0.12 }}
      transition={{ duration: enabled ? 0.8 : 0, ease }}
    >
      {children}
    </motion.div>
  );
}

const flow = [
  {
    label: "Create encrypted snapshot",
    detail: "Save only what changed",
    meta: "Incremental",
    icon: Folder,
  },
  {
    label: "Check the repository",
    detail: "Check snapshot structure",
    meta: "Integrity",
    icon: ShieldCheck,
  },
  {
    label: "Restore a canary file",
    detail: "Compare the restored content hash",
    meta: "Verified",
    icon: CheckCheck,
  },
];

function ProductPreview({ animated }: { animated: boolean }) {
  const [tab, setTab] = useState("Protection");
  const [tick, setTick] = useState(18);
  const [playing, setPlaying] = useState(true);
  const [restoreIndex, setRestoreIndex] = useState(2);
  const [restored, setRestored] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const { scrollYProgress } = useScroll({
    target: ref,
    offset: ["start end", "center center"],
  });
  const rotateX = useTransform(scrollYProgress, [0, 1], [7, 0]);
  useEffect(() => {
    if (!animated || !playing || tab !== "Protection" || tick >= 100) return;
    const timer = window.setTimeout(() => setTick((value) => value + 1), 150);
    return () => clearTimeout(timer);
  }, [animated, playing, tab, tick]);
  const phase = tick < 40 ? 0 : tick < 72 ? 1 : tick < 98 ? 2 : 3;
  const rows: TaskRow[] = flow.map((item, i) => ({
    key: `step-${i}`,
    label: item.label,
    amount: "",
    status: phase > i ? "done" : phase === i ? "running" : "pending",
    step: i + 1,
    details: [{ label: item.detail, meta: item.meta }],
  }));
  const tabs = [
    { title: "Protection", icon: ShieldCheck },
    { title: "Activity", icon: Activity },
    { title: "Restore", icon: History },
    { title: "Settings", icon: Settings2 },
  ];
  return (
    <div className="preview-wrap" id="preview" ref={ref}>
      <div className="preview-caption">
        <span>
          <span className="tiny-dot" /> INTERACTIVE PRODUCT PREVIEW
        </span>
        <span>Sample data. Real interface components.</span>
      </div>
      <motion.div
        className="app-window"
        style={animated ? { rotateX } : undefined}
      >
        <div className="window-bar">
          <div className="window-dots" aria-hidden="true">
            <i />
            <i />
            <i />
          </div>
          <span>Rewindle</span>
          <span className="window-local">
            <LockKeyhole size={12} /> Your workspace
          </span>
        </div>
        <div className="app-layout">
          <aside className="app-sidebar">
            <img
              src={`${BASE}brand/mark.svg`}
              className="sidebar-mark"
              alt=""
              width="34"
              height="34"
            />
            <GlideMenu
              className="app-navigation"
              highlightClassName="inset-x-0 rounded-[10px] bg-hover"
            >
              {tabs.map(({ title, icon: Icon }) => (
                <button
                  key={title}
                  data-menu-row
                  className={`app-nav-item ${tab === title ? "selected" : ""}`}
                  aria-current={tab === title ? "page" : undefined}
                  onClick={() => setTab(title)}
                >
                  <Icon size={17} />
                  <span>{title}</span>
                  {tab === title && (
                    <motion.i
                      layoutId="nav-dot"
                      className="nav-dot"
                      transition={{ duration: animated ? 0.3 : 0 }}
                    />
                  )}
                </button>
              ))}
            </GlideMenu>
            <div className="sidebar-bottom">
              <span className="tiny-dot" /> Desktop, with peace of mind.
              <span>Powered by restic</span>
            </div>
          </aside>
          <div className="app-content">
            <AnimatePresence mode="wait" initial={false}>
              <motion.div
                key={tab}
                initial={animated ? { opacity: 0, y: 9 } : false}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: animated ? -6 : 0 }}
                transition={{ duration: animated ? 0.22 : 0 }}
              >
                <div className="app-page-title">
                  <div>
                    <span className="app-eyebrow">
                      YOUR BACKUP, AT A GLANCE
                    </span>
                    <h3>{tab}</h3>
                  </div>
                  <span className="sample-pill">Sample workspace</span>
                </div>
                {tab === "Protection" && (
                  <>
                    <div
                      className={`protection-card ${phase === 3 ? "complete" : ""}`}
                    >
                      <div className="status-orbit">
                        <svg viewBox="0 0 70 70" aria-hidden="true">
                          <circle
                            cx="35"
                            cy="35"
                            r="30"
                            fill="none"
                            stroke="currentColor"
                            strokeWidth="2"
                            opacity=".13"
                          />
                          <motion.circle
                            cx="35"
                            cy="35"
                            r="30"
                            fill="none"
                            stroke="currentColor"
                            strokeWidth="2.5"
                            strokeLinecap="round"
                            initial={false}
                            animate={{ pathLength: Math.min(tick / 98, 1) }}
                            transition={{ duration: animated ? 0.2 : 0 }}
                            transform="rotate(-90 35 35)"
                          />
                        </svg>
                        {phase === 3 ? (
                          <Check size={25} />
                        ) : (
                          <ShieldCheck size={25} />
                        )}
                      </div>
                      <div>
                        <span className="app-eyebrow">
                          {phase === 3
                            ? "BACKUP VERIFIED"
                            : "A LITTLE PEACE OF MIND, IN PROGRESS"}
                        </span>
                        <h4>
                          {
                            [
                              "Keeping your work safe.",
                              "Checking what matters.",
                              "Putting recovery to the test.",
                              "Ready when you need it.",
                            ][phase]
                          }
                        </h4>
                        <p>
                          {phase === 3
                            ? "Snapshot saved. Canary restored and verified."
                            : "Your files → encrypted snapshot → verified recovery."}
                        </p>
                      </div>
                      <span className="status-chip">
                        {phase === 3 ? "Verified" : "In progress"}
                      </span>
                    </div>
                    <div className="app-columns">
                      <div>
                        <div className="app-section-label">
                          VERIFICATION PIPELINE <span>3 steps</span>
                        </div>
                        <TaskRows rows={rows} className="demo-tasks" />
                      </div>
                      <div className="repository-card">
                        <HardDrive size={24} strokeWidth={1.5} />
                        <span>Backup destination</span>
                        <strong>My backup drive</strong>
                        <div className="repo-path">
                          <Folder size={12} /> /Rewindle backups
                        </div>
                        <div className="repo-rule" />
                        <div className="repo-feature">
                          <LockKeyhole size={14} /> Encrypted with your key
                        </div>
                        <div className="repo-feature">
                          <History size={14} /> Previous snapshots kept
                        </div>
                      </div>
                    </div>
                    <div className="preview-control">
                      <span>
                        {phase < 3 && animated && playing ? (
                          <Shimmer>Preview running</Shimmer>
                        ) : phase === 3 ? (
                          "A successful sample run"
                        ) : (
                          "Preview paused"
                        )}{" "}
                        <span className="control-hint">
                          · Select a step to see what it does
                        </span>
                      </span>
                      <Button
                        size="sm"
                        variant="quiet"
                        onClick={() => {
                          if (phase === 3) {
                            setTick(0);
                            setPlaying(true);
                          } else setPlaying((v) => !v);
                        }}
                        aria-label={
                          phase === 3
                            ? "Replay backup preview"
                            : playing && animated
                              ? "Pause backup preview"
                              : "Play backup preview"
                        }
                        disabled={!animated}
                      >
                        {phase === 3 ? (
                          <RotateCcw size={13} />
                        ) : playing && animated ? (
                          <Pause size={13} />
                        ) : (
                          <Play size={13} />
                        )}
                        {phase === 3
                          ? "Replay"
                          : playing && animated
                            ? "Pause"
                            : "Play"}
                      </Button>
                    </div>
                  </>
                )}
                {tab === "Activity" && (
                  <div className="activity-demo">
                    <div className="app-section-label">
                      RECENT BACKUPS <span>Sample history</span>
                    </div>
                    {["Today, 09:00", "Yesterday, 09:00", "Monday, 09:00"].map(
                      (date, i) => (
                        <div className="history-row" key={date}>
                          <span className="history-check">
                            <Check size={16} />
                          </span>
                          <div>
                            <strong>{date}</strong>
                            <span>
                              {
                                [
                                  "Studio files",
                                  "Studio files",
                                  "First snapshot",
                                ][i]
                              }
                            </span>
                          </div>
                          <span className="history-tag">Canary verified</span>
                          <span>
                            {["Incremental", "Incremental", "Full scan"][i]}
                          </span>
                        </div>
                      ),
                    )}
                    <p className="app-note">
                      <Activity size={15} /> Follow each run, from the first
                      file to the final verification.
                    </p>
                  </div>
                )}
                {tab === "Restore" && (
                  <div className="restore-demo">
                    <p>Choose a snapshot. Recover to a separate folder.</p>
                    <div className="snapshot-options">
                      {["Monday", "Yesterday", "Today"].map((day, i) => (
                        <Button
                          key={day}
                          aria-pressed={restoreIndex === i}
                          variant={restoreIndex === i ? "accent" : "secondary"}
                          onClick={() => {
                            setRestoreIndex(i);
                            setRestored(false);
                          }}
                        >
                          <History size={14} />
                          {day}
                        </Button>
                      ))}
                    </div>
                    <div className="restore-file">
                      <FileText size={24} />
                      <div>
                        <strong>Studio notes.md</strong>
                        <span>
                          {
                            [
                              "The first idea.",
                              "A little more detail.",
                              "Something worth keeping.",
                            ][restoreIndex]
                          }
                        </span>
                      </div>
                      <Check size={16} />
                    </div>
                    <Button variant="accent" onClick={() => setRestored(true)}>
                      <RotateCcw size={15} />
                      {restored
                        ? "Sample copy recovered"
                        : "Try a sample restore"}
                    </Button>
                    <p className="app-note" role="status">
                      {restored
                        ? "Demo complete. In the app, Rewindle restores to a new or empty folder."
                        : "This browser preview uses sample data; it does not access your files."}
                    </p>
                  </div>
                )}
                {tab === "Settings" && (
                  <div className="settings-demo">
                    {[
                      {
                        icon: Folder,
                        title: "Your folders",
                        desc: "You choose what to protect.",
                      },
                      {
                        icon: HardDrive,
                        title: "Your destination",
                        desc: "A local or external drive you control.",
                      },
                      {
                        icon: KeyRound,
                        title: "Your recovery key",
                        desc: "Keep an independent copy somewhere safe.",
                      },
                    ].map(({ icon: Icon, title, desc }) => (
                      <div className="setting-row" key={title}>
                        <Icon size={20} />
                        <div>
                          <strong>{title}</strong>
                          <p>{desc}</p>
                        </div>
                        <Check size={16} />
                      </div>
                    ))}
                    <p className="app-note">
                      The desktop app guides you through the real setup.
                    </p>
                  </div>
                )}
              </motion.div>
            </AnimatePresence>
          </div>
        </div>
      </motion.div>
      <div className="preview-bottom">
        <span>
          <Monitor size={15} /> Made for your desktop
        </span>
        <span>Windows · macOS</span>
        <a href="#how-it-works">
          A closer look <ArrowDown size={15} />
        </a>
      </div>
    </div>
  );
}

const snapshots = [
  {
    date: "MON, 09:00",
    title: "An idea takes shape.",
    version: "First draft",
    text: "There is something about starting again. A blank page. An open window. The beginning of a story.",
    accent: "#b5c9e5",
    blocks: [83, 94, 70, 60, 38],
  },
  {
    date: "TUE, 09:00",
    title: "A little more you.",
    version: "Second draft",
    text: "There is something about starting again. A blank page. An open window. And this time, a clearer sense of where the story goes.",
    accent: "#e5c18f",
    blocks: [91, 78, 87, 62, 53],
  },
  {
    date: "WED, 09:00",
    title: "The version you love.",
    version: "Final draft",
    text: "There is something about starting again. A blank page. An open window. The quiet confidence that nothing good has been lost.",
    accent: "#76d6c8",
    blocks: [96, 83, 91, 75, 64],
  },
];

function RewindExperience({ animated }: { animated: boolean }) {
  const [selected, setSelected] = useState(2);
  const [recovered, setRecovered] = useState(false);
  const snapshot = snapshots[selected];
  const choose = (value: number) => {
    setSelected(value);
    setRecovered(false);
  };
  return (
    <section className="rewind-section" id="restore">
      <div className="section-container rewind-layout">
        <Reveal enabled={animated} className="rewind-copy">
          <span className="eyebrow mint">
            <History size={16} /> A WAY BACK, BUILT IN
          </span>
          <h2>
            A deleted file.
            <br />A wrong turn.
            <br />
            <span>Not the end.</span>
          </h2>
          <p>
            Some things deserve a second chance. Browse your snapshots, find the
            version you need, and restore it to a separate folder.
          </p>
          <p className="quiet-copy">
            Your originals stay right where they are.
          </p>
          <a href="#downloads" className="text-link light-link">
            Give your files a way back <ArrowUpRight size={18} />
          </a>
        </Reveal>
        <Reveal enabled={animated} className="rewind-interactive">
          <div className="rewind-demo-label">
            <span className="tiny-dot" /> TRY REWINDING A FILE{" "}
            <span>Sample snapshots</span>
          </div>
          <div className="document-stage">
            <div className="document-shadow shadow-back" aria-hidden="true" />
            <div className="document-shadow shadow-front" aria-hidden="true" />
            <AnimatePresence mode="wait" initial={false}>
              <motion.div
                className="document"
                key={selected}
                initial={animated ? { opacity: 0, y: 18, rotate: -3 } : false}
                animate={{ opacity: 1, y: 0, rotate: 0 }}
                exit={{
                  opacity: 0,
                  y: animated ? -12 : 0,
                  rotate: animated ? 2 : 0,
                }}
                transition={{ duration: animated ? 0.3 : 0, ease }}
              >
                <div className="document-top">
                  <span>
                    <FileText size={17} /> Studio notes.md
                  </span>
                  <span>{snapshot.version}</span>
                </div>
                <div
                  className="document-color"
                  style={{ backgroundColor: snapshot.accent }}
                >
                  <span>NOTES TO SELF</span>
                  <Sparkles size={22} strokeWidth={1.2} />
                </div>
                <h3>{snapshot.title}</h3>
                <p>{snapshot.text}</p>
                <div className="document-lines" aria-hidden="true">
                  {snapshot.blocks.map((width, i) => (
                    <i key={i} style={{ width: `${width}%` }} />
                  ))}
                </div>
                <div className="document-bottom">
                  <span>{snapshot.date}</span>
                  <span>Snapshot {String(selected + 1).padStart(2, "0")}</span>
                </div>
              </motion.div>
            </AnimatePresence>
            <AnimatePresence>
              {recovered && (
                <motion.div
                  role="status"
                  className="recovered-toast"
                  initial={
                    animated ? { opacity: 0, y: 12, scale: 0.95 } : false
                  }
                  animate={{ opacity: 1, y: 0, scale: 1 }}
                  exit={{ opacity: 0 }}
                >
                  <CheckCheck size={19} />
                  <div>
                    <strong>Sample copy recovered</strong>
                    <span>Your original stays untouched.</span>
                  </div>
                </motion.div>
              )}
            </AnimatePresence>
          </div>
          <div className="timeline-control">
            <label htmlFor="snapshot-range">
              <span>
                <RotateCcw size={15} /> Drag to go back in time
              </span>
              <strong>{snapshot.date.split(",")[0]}</strong>
            </label>
            <input
              id="snapshot-range"
              type="range"
              min="0"
              max="2"
              step="1"
              value={selected}
              onChange={(event) => choose(Number(event.target.value))}
              aria-valuetext={`${snapshots[selected].date}, ${snapshots[selected].version}`}
            />
            <div className="timeline-labels">
              {snapshots.map((s, i) => (
                <button
                  key={s.date}
                  aria-pressed={selected === i}
                  onClick={() => choose(i)}
                >
                  {s.date.split(",")[0]}
                  <span>
                    {i === 2
                      ? "Latest"
                      : `${2 - i} day${i === 0 ? "s" : ""} ago`}
                  </span>
                </button>
              ))}
            </div>
          </div>
          <button className="restore-action" onClick={() => setRecovered(true)}>
            <RotateCcw size={17} />
            {recovered ? "Sample restored" : "Restore this sample"}
            <ArrowRight size={17} />
          </button>
          <p className="demo-disclaimer">
            An interactive illustration. No access to your device or files.
          </p>
        </Reveal>
      </div>
    </section>
  );
}

const platforms = [
  {
    key: "windows",
    title: "Windows",
    subtitle: "Windows 10 / 11 · x64",
    icon: "windows",
    type: ".exe installer",
    file: "Rewindle-v0.2.0-alpha.1-windows-x64-setup.exe",
    zip: "Rewindle-v0.2.0-alpha.1-windows-x64.zip",
  },
  {
    key: "silicon",
    title: "Mac",
    subtitle: "Apple Silicon · M-series",
    icon: "apple",
    type: ".dmg disk image",
    file: "Rewindle-0.2.0-alpha.1-arm64.dmg",
    zip: "Rewindle-0.2.0-alpha.1-arm64.zip",
  },
  {
    key: "intel",
    title: "Mac",
    subtitle: "Intel processor",
    icon: "apple",
    type: ".dmg disk image",
    file: "Rewindle-0.2.0-alpha.1-x64.dmg",
    zip: "Rewindle-0.2.0-alpha.1-x64.zip",
  },
];

function PlatformIcon({ type }: { type: string }) {
  return type === "windows" ? (
    <svg
      width="28"
      height="28"
      viewBox="0 0 24 24"
      fill="currentColor"
      aria-hidden="true"
    >
      <path d="M2 3.8 11 2.5V11H2V3.8Zm10.2-1.5L22 1v10h-9.8V2.3ZM2 12h9v8.5l-9-1.3V12Zm10.2 0H22v10l-9.8-1.3V12Z" />
    </svg>
  ) : (
    <svg
      width="28"
      height="28"
      viewBox="0 0 24 24"
      fill="currentColor"
      aria-hidden="true"
    >
      <path d="M16.9 12.8c0-2.5 2.1-3.8 2.2-3.9-1.2-1.7-3-2-3.7-2-1.6-.2-3 1-3.8 1-.8 0-2-1-3.2-1-1.7 0-3.3 1-4.1 2.5-1.8 3.1-.5 7.8 1.2 10.3.8 1.2 1.8 2.5 3 2.4 1.2 0 1.7-.8 3.3-.8 1.5 0 2 .8 3.3.8 1.3 0 2.1-1.2 3-2.4 1-1.4 1.4-2.8 1.4-2.9-.1 0-2.6-1-2.6-4ZM14.4 5.3c.7-.9 1.3-2.1 1.1-3.3-1.1.1-2.4.8-3.2 1.7-.7.8-1.3 2-1.1 3.2 1.2.1 2.4-.7 3.2-1.6Z" />
    </svg>
  );
}

function App() {
  const reducedMotion = useReducedMotion();
  const [paused, setPaused] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);
  const headerRef = useRef<HTMLElement>(null);
  const menuButtonRef = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (!menuOpen) return;
    const key = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setMenuOpen(false);
        menuButtonRef.current?.focus();
      }
    };
    const outside = (event: PointerEvent) => {
      if (!headerRef.current?.contains(event.target as Node))
        setMenuOpen(false);
    };
    document.addEventListener("keydown", key);
    document.addEventListener("pointerdown", outside);
    return () => {
      document.removeEventListener("keydown", key);
      document.removeEventListener("pointerdown", outside);
    };
  }, [menuOpen]);
  const animated = !reducedMotion && !paused;
  const { scrollYProgress } = useScroll();
  useEffect(() => {
    document.documentElement.dataset.motion = animated ? "on" : "off";
  }, [animated]);
  return (
    <MotionConfig
      reducedMotion={animated ? "user" : "always"}
      transition={{ ease }}
    >
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      {animated && (
        <motion.div
          className="scroll-progress"
          style={{ scaleX: scrollYProgress }}
        />
      )}
      <header className="site-header" ref={headerRef}>
        <a className="brand-link" href="#" aria-label="Rewindle home">
          <Brand />
        </a>
        <nav
          aria-label="Main navigation"
          className={menuOpen ? "site-nav is-open" : "site-nav"}
        >
          <a href="#how-it-works" onClick={() => setMenuOpen(false)}>
            Why Rewindle
          </a>
          <a href="#restore" onClick={() => setMenuOpen(false)}>
            Find your way back
          </a>
          <a href={REPO + "/tree/v0.2.0-alpha.1"}>
            <Github size={15} /> Open source <ArrowUpRight size={13} />
          </a>
        </nav>
        <div className="header-actions">
          <button
            className="motion-toggle"
            aria-label={animated ? "Pause animations" : "Enable animations"}
            aria-pressed={!animated}
            onClick={() => setPaused((v) => !v)}
            disabled={!!reducedMotion}
            title={
              reducedMotion
                ? "Reduced motion follows your device preference"
                : animated
                  ? "Pause animations"
                  : "Enable animations"
            }
          >
            {animated ? <Pause size={16} /> : <Play size={16} />}
          </button>
          <a className="header-download" href="#downloads">
            Get Rewindle <ArrowDownToLine size={15} />
          </a>
          <button
            ref={menuButtonRef}
            className="mobile-menu"
            aria-label={menuOpen ? "Close menu" : "Open menu"}
            aria-expanded={menuOpen}
            onClick={() => setMenuOpen((v) => !v)}
          >
            {menuOpen ? <X size={22} /> : <Menu size={22} />}
          </button>
        </div>
      </header>
      <main id="main">
        <section className="hero">
          <div className="hero-copy">
            <Reveal enabled={animated}>
              <a className="release-pill" href={RELEASE}>
                <span className="tiny-dot" />
                <span>MEET REWINDLE</span>
                <span className="release-version">v0.2 alpha</span>
                <ChevronRight size={14} />
              </a>
            </Reveal>
            <h1>
              <motion.span
                initial={animated ? { y: 45, opacity: 0 } : false}
                animate={{ y: 0, opacity: 1 }}
                transition={{ duration: 0.85, delay: 0.08, ease }}
              >
                Life moves forward.
              </motion.span>
              <motion.span
                initial={animated ? { y: 45, opacity: 0 } : false}
                animate={{ y: 0, opacity: 1 }}
                transition={{ duration: 0.85, delay: 0.19, ease }}
              >
                Your files can{" "}
                <em>
                  rewind
                  <motion.svg
                    viewBox="0 0 330 16"
                    preserveAspectRatio="none"
                    aria-hidden="true"
                  >
                    <motion.path
                      d="M3 12 Q160 -3 327 9"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="3"
                      strokeLinecap="round"
                      initial={animated ? { pathLength: 0 } : false}
                      animate={{ pathLength: 1 }}
                      transition={{ duration: 1.1, delay: 0.8, ease }}
                    />
                  </motion.svg>
                </em>
                .
              </motion.span>
            </h1>
            <Reveal enabled={animated}>
              <p className="hero-description">
                A calmer home for your backups. Protect what you’re making,
                <br className="desktop-break" /> verify it’s recoverable, and
                get back to what matters.
              </p>
              <div className="hero-actions">
                <a className="primary-link" href="#downloads">
                  Download Rewindle <ArrowDownToLine size={19} />
                </a>
                <a className="watch-link" href="#preview">
                  <span>
                    <Play size={13} fill="currentColor" />
                  </span>
                  Take it for a spin
                </a>
              </div>
              <div className="hero-facts">
                <span>Free & open source</span>
                <i />
                <span>Windows + Mac</span>
                <i />
                <span>Your storage. Your keys.</span>
              </div>
            </Reveal>
          </div>
          <ProductPreview animated={animated} />
          <div className="trust-line">
            <span>BUILT ON A SOLID FOUNDATION</span>
            <strong>
              restic<span>powered</span>
            </strong>
            <span className="trust-divider" />
            <span>
              <LockKeyhole size={17} /> Encrypted by default
            </span>
            <span>
              <CheckCheck size={18} /> Recovery, put to the test
            </span>
            <span>
              <Github size={17} /> Open by design
            </span>
          </div>
        </section>
        <section className="why-section section-container" id="how-it-works">
          <Reveal enabled={animated} className="section-intro">
            <span className="eyebrow">
              LESS BACKUP ANXIETY. MORE MAKING THINGS.
            </span>
            <h2>
              A backup should leave you
              <br />
              <span>with fewer questions.</span>
            </h2>
            <p>
              Did it run? Is it encrypted? Can I get it back?
              <br />
              Rewindle makes the answers part of the experience.
            </p>
          </Reveal>
          <div className="feature-columns">
            <Reveal enabled={animated} className="feature">
              <div className="feature-number">
                01 <span>PROTECT</span>
              </div>
              <div className="feature-visual folder-visual">
                <span className="source-folder">
                  <Folder size={23} strokeWidth={1.5} /> Your files
                </span>
                <div className="transfer-line">
                  <motion.i
                    animate={
                      animated
                        ? { x: [-20, 72], opacity: [0, 1, 0] }
                        : { x: 30, opacity: 1 }
                    }
                    transition={{
                      duration: 2.8,
                      repeat: animated ? Infinity : 0,
                      ease: "linear",
                    }}
                  />
                </div>
                <span className="destination-drive">
                  <HardDrive size={25} strokeWidth={1.5} />
                  <LockKeyhole size={13} />
                </span>
              </div>
              <h3>
                Your work.
                <br />
                Your own safekeeping.
              </h3>
              <p>
                Choose the folders that matter and a backup drive you control.
                Encrypted snapshots save what changed, without copying
                everything again.
              </p>
              <span className="feature-footnote">
                <LockKeyhole size={14} /> Encrypted · Incremental · Deduplicated
              </span>
            </Reveal>
            <Reveal enabled={animated} className="feature">
              <div className="feature-number">
                02 <span>VERIFY</span>
              </div>
              <div className="feature-visual verify-visual">
                <div className="verify-label">
                  <span className="tiny-dot" /> Snapshot saved
                </div>
                <div className="verify-label">
                  <Check size={14} /> Repository checked
                </div>
                <div className="verify-label emphasis">
                  <CheckCheck size={14} /> Canary restored & verified
                </div>
              </div>
              <h3>
                “Done” should
                <br />
                mean something.
              </h3>
              <p>
                A successful run includes a repository check and a real canary
                restore. Rewindle checks the restored file’s hash before calling
                it a success.
              </p>
              <span className="feature-footnote">
                <ShieldCheck size={14} /> Verification is part of the workflow
              </span>
            </Reveal>
            <Reveal enabled={animated} className="feature">
              <div className="feature-number">
                03 <span>RECOVER</span>
              </div>
              <div className="feature-visual recover-visual">
                <div className="mini-snapshot previous">
                  <History size={16} />
                  <span>Yesterday</span>
                </div>
                <div className="mini-snapshot current">
                  <FileText size={18} />
                  <span>Your missing file</span>
                  <span className="recovery-check">
                    <Check size={12} />
                  </span>
                </div>
              </div>
              <h3>
                Good work deserves
                <br />a way back.
              </h3>
              <p>
                Browse earlier snapshots and recover the files you need to a new
                or empty folder. Keep the version you have and the version you
                missed.
              </p>
              <span className="feature-footnote">
                <History size={14} /> Previous snapshots stay available
              </span>
            </Reveal>
          </div>
        </section>
        <RewindExperience animated={animated} />
        <section className="principles-section section-container">
          <Reveal enabled={animated} className="principles-heading">
            <span className="eyebrow">QUIET SOFTWARE. CLEAR PRINCIPLES.</span>
            <h2>
              It’s your backup.
              <br />
              <span>It should feel that way.</span>
            </h2>
          </Reveal>
          <div className="principles-list">
            {[
              {
                icon: HardDrive,
                title: "Bring your own storage.",
                text: "Use a local or external backup drive. You choose where your repository lives.",
              },
              {
                icon: KeyRound,
                title: "Keep your own recovery key.",
                text: "Your key unlocks your backup. Keep an independent copy somewhere safe.",
              },
              {
                icon: Github,
                title: "See what’s under the hood.",
                text: "Open source under the MIT license. Built on restic, with code you can inspect.",
              },
            ].map(({ icon: Icon, title, text }) => (
              <Reveal enabled={animated} className="principle" key={title}>
                <Icon size={22} strokeWidth={1.5} />
                <div>
                  <h3>{title}</h3>
                  <p>{text}</p>
                </div>
                <ArrowUpRight size={18} aria-hidden="true" />
              </Reveal>
            ))}
          </div>
        </section>
        <section className="download-section" id="downloads">
          <div className="section-container">
            <Reveal enabled={animated} className="download-heading">
              <div>
                <span className="eyebrow">
                  <span className="tiny-dot" /> YOUR NEXT GOOD HABIT
                </span>
                <h2>
                  Keep creating.
                  <br />
                  <span>Come back anytime.</span>
                </h2>
              </div>
              <div className="download-intro">
                <p>
                  A little more confidence.
                  <br />A lot less “what if.”
                </p>
                <span className="version-tag">
                  v0.2.0-alpha.1 <span>Public alpha</span>
                </span>
              </div>
            </Reveal>
            <div className="platform-grid">
              {platforms.map((platform) => (
                <Reveal
                  enabled={animated}
                  className="platform"
                  key={platform.key}
                >
                  <PlatformIcon type={platform.icon} />
                  <h3>{platform.title}</h3>
                  <p>{platform.subtitle}</p>
                  <a
                    className="platform-download"
                    href={DOWNLOAD + platform.file}
                    aria-label={`Download Rewindle for ${platform.title} ${platform.key === "silicon" ? "Apple Silicon" : platform.key === "intel" ? "Intel" : "x64"}`}
                  >
                    Download <ArrowDownToLine size={17} />
                  </a>
                  <div className="platform-alternatives">
                    <span>{platform.type}</span>
                    <a href={DOWNLOAD + platform.zip}>
                      Prefer a ZIP? <ArrowUpRight size={12} />
                    </a>
                  </div>
                </Reveal>
              ))}
            </div>
            <div className="alpha-note">
              <CircleHelp size={19} />
              <div>
                <strong>A first chapter, ready to explore.</strong>
                <p>
                  This is an early alpha. Windows installers are unsigned; Mac
                  builds are not notarized. Your operating system may show a
                  security warning. Keep your recovery key safe and test
                  restoring your own files.
                </p>
              </div>
              <a href={RELEASE}>
                Release notes <ArrowUpRight size={14} />
              </a>
            </div>
            <div className="download-utilities">
              <a href={DOWNLOAD + "SHA256SUMS.txt"}>
                <ShieldCheck size={15} /> Verify download checksums
              </a>
              <span>
                On a Mac? Apple menu → About This Mac shows your chip.
              </span>
            </div>
            <div className="download-faq">
              <details>
                <summary>
                  What do I need to get started?<span>+</span>
                </summary>
                <p>
                  Choose your source folders and a local or external backup
                  drive. Rewindle generates and stores the repository password,
                  then provides a separate recovery key for you to keep offline.
                  Windows x64 requires Windows 10 or 11, PowerShell 5.1, and
                  .NET Framework 4.8. Choose the Apple Silicon or Intel build
                  for your Mac. This alpha supports local folder sources and
                  repositories; network sources are outside its supported scope.
                </p>
              </details>
              <details>
                <summary>
                  What does “verified” mean?<span>+</span>
                </summary>
                <p>
                  A successful backup includes checks on the snapshot and
                  repository, plus a real restore of a small known canary file
                  whose content hash is compared. This confirms that the canary
                  can be located, decrypted, and recovered; it does not prove
                  every user file is healthy. Make your own restore tests part
                  of your backup routine.
                </p>
              </details>
              <details>
                <summary>
                  Are the Windows and Mac versions identical?<span>+</span>
                </summary>
                <p>
                  Both provide backup, verification, snapshot browsing, restore,
                  and daily scheduling while the installing user is signed in.
                  Windows also has optional VSS capture and Windows-specific
                  management tools. The Mac alpha focuses on local folder
                  backups and uses a schedule for the logged-in user. See the
                  release notes for platform details.
                </p>
              </details>
            </div>
          </div>
        </section>
      </main>
      <footer className="site-footer">
        <div className="footer-top">
          <a href="#" aria-label="Rewindle home">
            <Brand light />
          </a>
          <p>Backup you can verify.</p>
          <a href="#main" className="back-top">
            Back to top <ArrowUpRight size={16} />
          </a>
        </div>
        <div className="footer-bottom">
          <span>© 2026 Rewindle · Open source, by design.</span>
          <div>
            <a href={REPO + "/tree/v0.2.0-alpha.1"}>
              GitHub <ArrowUpRight size={12} />
            </a>
            <a href={RELEASE}>Releases</a>
            <a href={DOWNLOAD + "Rewindle-v0.2.0-alpha.1-brand-kit.zip"}>
              Brand kit
            </a>
            <a href={`${BASE}licenses/`}>Licenses</a>
          </div>
        </div>
        <div className="footer-word" aria-hidden="true">
          rewindle<span>↶</span>
        </div>
      </footer>
    </MotionConfig>
  );
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
