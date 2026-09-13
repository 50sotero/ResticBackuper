"use client";

import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from "react";
import { createPortal } from "react-dom";
import {
  Activity as IconActivity,
  Check as IconCheckmark1Small,
  ChevronDown as IconChevronDownSmall,
  PanelLeftClose as IconSidebarLeftArrow,
  Pencil as IconEditBig,
  Plus as IconPlusMedium,
  RotateCcw as IconRestore,
  Search as IconMagnifyingGlass,
  Settings as IconSettingsGear1,
  ShieldCheck as IconShieldCheck,
  X as IconCrossSmall,
} from "lucide-react";
import GlideMenu from "@/components/primitives/GlideMenu";

/* ─────────────────────────────────────────────────────────
 * SIDEBAR NAV
 * Shared by the design-system preview and the harness shell:
 * compact workspace switcher, primary navigation, searchable
 * chat history, and a collapse that preserves icon alignment.
 * ───────────────────────────────────────────────────────── */

export type SidebarWorkspace = {
  key: string;
  name: string;
  monogram: string;
  logo?: ReactNode;
};

export type SidebarNavItem = {
  key: string;
  label: string;
  icon: ReactNode;
  count?: string | number;
};

const DEFAULT_WORKSPACE: SidebarWorkspace = { key: "restic", name: "Restic workspace", monogram: "R" };

const DEFAULT_NAV_ITEMS: SidebarNavItem[] = [
  { key: "protection", label: "Protection", icon: <IconShieldCheck size={18} /> },
  { key: "activity", label: "Activity", icon: <IconActivity size={18} /> },
  { key: "restore", label: "Restore", icon: <IconRestore size={18} /> },
  { key: "settings", label: "Settings", icon: <IconSettingsGear1 size={18} /> },
];

export type SidebarRecent = {
  id: string;
  label: string;
  prompt?: string;
};

const DEFAULT_RECENTS: SidebarRecent[] = [
  { id: "latest", label: "Latest backup" },
  { id: "weekly", label: "Weekly verification" },
  { id: "documents", label: "Documents snapshot" },
  { id: "photos", label: "Photos snapshot" },
];

export type SidebarNavProps = {
  workspace?: SidebarWorkspace;
  navItems?: SidebarNavItem[];
  activeTitle?: string | null;
  className?: string;
  fill?: boolean;
  primaryActionLabel?: string;
  primaryActionIcon?: ReactNode;
  onPrimaryAction?: () => void;
  primaryActionDisabled?: boolean;
  primaryActionHelp?: string;
  onNewChat?: () => void;
  onPick?: (id: string, label: string, prompt?: string) => void;
  /** controlled primary-nav selection (e.g. "protection" | "activity") */
  activeNav?: string;
  onNavigate?: (key: string) => void;
  onWorkspaceSettings?: () => void;
  footerLabel?: string | null;
  footerIcon?: ReactNode;
  onFooterClick?: () => void;
  footerDisabled?: boolean;
  footerActive?: boolean;
  recents?: SidebarRecent[];
  recentLabel?: string;
  recentSearchLabel?: string;
  variant?: string;
};

const SIDEBAR_MOTION = {
  expandedWidth: 224,
  collapsedWidth: 52,
  duration: 280,
  copyDuration: 180,
  copyOffset: 8,
  easing: "cubic-bezier(0.16, 1, 0.3, 1)",
};

/* ─────────────────────────────────────────────────────────
 * CHAT SEARCH STORYBOARD
 *
 *   0ms   search is triggered; Chats label begins fading
 *   0ms   field grows right → left from the search control
 * 180ms   field fills the row; cursor is focused and ready
 * ───────────────────────────────────────────────────────── */
const CHAT_SEARCH_MOTION = {
  duration: 180,
  closedWidth: 28,
  easing: "cubic-bezier(0.16, 1, 0.3, 1)",
};

function GlideGroup({ children }: { children: ReactNode }) {
  return (
    <GlideMenu
      rowSelector="[data-row]"
      highlightClassName="sidebar-glide-highlight rounded-[7px] bg-hover-2"
      className="group/glide flex flex-col gap-px"
    >
      {children}
    </GlideMenu>
  );
}

function RailButton({
  icon,
  label,
  active = false,
  count,
  onClick,
  disabled = false,
  help,
}: {
  icon: ReactNode;
  label: string;
  active?: boolean;
  count?: string;
  onClick?: () => void;
  disabled?: boolean;
  help?: string;
}) {
  return (
    <button
      data-row
      type="button"
      disabled={disabled}
      aria-label={label}
      title={help || label}
      aria-description={help}
      aria-current={active ? 'page' : undefined}
      onClick={onClick}
      className={`sidebar-row relative z-10 mx-2 flex h-8 items-center rounded-[8px] px-2 text-left
        transition-[width,background-color,color,transform] duration-150 active:scale-[0.98] disabled:pointer-events-none disabled:opacity-50
        ${active ? "bg-hover-2 group-hover/glide:bg-transparent" : ""}`}
    >
      <span className={`flex size-5 shrink-0 items-center justify-center ${active ? "text-ink" : "text-ink-2"}`}>
        {icon}
      </span>
      <span className={`sidebar-copy ml-1.5 min-w-0 flex-1 truncate text-[14px] font-medium ${active ? "text-ink" : "text-ink-2"}`}>
        {label}
      </span>
      {count && (
        <span className="sidebar-copy mr-2 shrink-0 text-[12px] font-medium tabular-nums text-ink-3">
          {count}
        </span>
      )}
    </button>
  );
}

function WorkspaceMenu({
  position,
  workspace,
  onClose,
  onSettings,
}: {
  position: { top: number; left: number };
  workspace: SidebarWorkspace;
  onClose: () => void;
  onSettings?: () => void;
}) {
  return createPortal(
    <div
      data-workspace-menu
      role="menu"
      aria-label="Workspace"
      className="fixed z-50 w-64 rounded-[14px] bg-surface p-1.5 shadow-overlay"
      style={{
        top: position.top,
        left: position.left,
        animation: "pop-in 180ms cubic-bezier(0.23,1,0.32,1) both",
        transformOrigin: "top left",
      }}
    >
      <GlideMenu className="flex flex-col gap-px" highlightClassName="inset-x-0 rounded-[8px] bg-hover-2">
        <button
          data-menu-row
          role="menuitem"
          type="button"
          onClick={onClose}
          className="relative z-10 flex h-10 w-full items-center gap-1.5 rounded-[8px] px-2 text-left"
        >
          <span className="flex size-6 shrink-0 items-center justify-center rounded-[7px] bg-ink text-[11px] font-semibold text-surface">
            {workspace.logo ?? workspace.monogram}
          </span>
          <span className="min-w-0 flex-1 truncate text-[13.5px] font-medium text-ink">{workspace.name}</span>
          <span className="shrink-0 text-ink"><IconCheckmark1Small size={18} /></span>
        </button>
        <div className="my-1 h-px bg-line" />
        {[
          { label: "Workspace settings", icon: <IconSettingsGear1 size={16} /> },
        ].map((item) => (
          <button
            key={item.label}
            data-menu-row
            role="menuitem"
            type="button"
            onClick={() => { onClose(); onSettings?.(); }}
            className="relative z-10 flex h-9 w-full items-center gap-1.5 rounded-[8px] px-2 text-left"
          >
            <span className="flex size-5 shrink-0 items-center justify-center text-ink-2">{item.icon}</span>
            <span className="min-w-0 flex-1 truncate text-[13.5px] text-ink">{item.label}</span>
          </button>
        ))}
        <div className="my-1 h-px bg-line" />
        <button
          data-menu-row
          role="menuitem"
          type="button"
          onClick={onClose}
          className="relative z-10 flex h-9 w-full items-center gap-1.5 rounded-[8px] px-2 text-left"
        >
          <span className="flex size-5 shrink-0 items-center justify-center text-ink-2"><IconCrossSmall size={16} /></span>
          <span className="min-w-0 flex-1 truncate text-[13.5px] text-ink">Close</span>
        </button>
      </GlideMenu>
    </div>,
    document.body,
  );
}

export default function SidebarNav({
  workspace = DEFAULT_WORKSPACE,
  navItems = DEFAULT_NAV_ITEMS,
  activeTitle,
  className = "",
  fill = false,
  primaryActionLabel = "Run backup",
  primaryActionIcon = <IconPlusMedium size={18} />,
  onPrimaryAction,
  primaryActionDisabled = false,
  primaryActionHelp,
  onNewChat,
  onPick,
  activeNav,
  onNavigate,
  onWorkspaceSettings,
  footerLabel = null,
  footerIcon,
  onFooterClick,
  footerDisabled = false,
  footerActive = false,
  recents = DEFAULT_RECENTS,
  recentLabel = "Backup history",
  recentSearchLabel = "Search backup history",
}: SidebarNavProps) {
  const [collapsed, setCollapsed] = useState(false);
  const [internalNav, setInternalNav] = useState("protection");
  const currentNav = activeNav ?? internalNav;
  const selectNav = (key: string) => {
    setInternalNav(key);
    onNavigate?.(key);
  };
  const [demoActiveTitle, setDemoActiveTitle] = useState<string | null>(null);
  const [workspaceOpen, setWorkspaceOpen] = useState(false);
  const [workspacePosition, setWorkspacePosition] = useState({ top: 0, left: 0 });
  const [searchOpen, setSearchOpen] = useState(false);
  const [query, setQuery] = useState("");
  const workspaceButtonRef = useRef<HTMLButtonElement>(null);
  const searchRef = useRef<HTMLInputElement>(null);

  const selectedTitle = activeTitle === undefined ? demoActiveTitle : activeTitle;
  const visibleRecents = recents.filter((item) => item.label.toLowerCase().includes(query.trim().toLowerCase()));

  useEffect(() => {
    if (!workspaceOpen) return;
    document.querySelector<HTMLButtonElement>('[data-workspace-menu] button')?.focus();
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { event.preventDefault(); setWorkspaceOpen(false); workspaceButtonRef.current?.focus(); }
      if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
        const items = [...document.querySelectorAll<HTMLButtonElement>('[data-workspace-menu] button')];
        if (!items.length) return;
        event.preventDefault();
        const current = items.indexOf(document.activeElement as HTMLButtonElement);
        items[event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1 : (current + (event.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length]?.focus();
      }
    };
    const onFocus = (event: FocusEvent) => {
      const target = event.target;
      if (target instanceof Element && !target.closest('[data-workspace-menu]') && !target.closest('[data-workspace-trigger]')) setWorkspaceOpen(false);
    };
    const close = (event: PointerEvent) => {
      const target = event.target;
      if (target instanceof Element && !target.closest("[data-workspace-trigger]") && !target.closest("[data-workspace-menu]")) {
        setWorkspaceOpen(false);
      }
    };
    document.addEventListener("pointerdown", close);
    document.addEventListener('keydown', onKey);
    document.addEventListener('focusin', onFocus);
    return () => { document.removeEventListener("pointerdown", close); document.removeEventListener('keydown', onKey); document.removeEventListener('focusin', onFocus); };
  }, [workspaceOpen]);

  useEffect(() => {
    if (searchOpen) searchRef.current?.focus();
  }, [searchOpen]);

  const collapse = () => {
    setCollapsed(true);
    setWorkspaceOpen(false);
    setSearchOpen(false);
    setQuery("");
  };

  return (
    <aside
      data-sidebar-collapsed={collapsed}
      aria-label="Workspace navigation"
      className={`relative flex shrink-0 overflow-hidden transition-[width] ${fill ? "h-full" : "h-[600px]"} ${className}`}
      style={{
        width: collapsed ? SIDEBAR_MOTION.collapsedWidth : SIDEBAR_MOTION.expandedWidth,
        transitionDuration: `${SIDEBAR_MOTION.duration}ms`,
        transitionTimingFunction: SIDEBAR_MOTION.easing,
        "--sidebar-copy-duration": `${SIDEBAR_MOTION.copyDuration}ms`,
        "--sidebar-copy-offset": `${SIDEBAR_MOTION.copyOffset}px`,
        "--sidebar-easing": SIDEBAR_MOTION.easing,
      } as CSSProperties}
    >
      <div className="flex min-h-0 w-[224px] shrink-0 flex-col">
        <div className="relative mb-2.5 h-10 shrink-0">
          <button
            ref={workspaceButtonRef}
            data-workspace-trigger
            type="button"
            aria-expanded={workspaceOpen}
            aria-haspopup="menu"
            aria-hidden={collapsed}
            tabIndex={collapsed ? -1 : 0}
            onClick={() => {
              if (!workspaceOpen && workspaceButtonRef.current) {
                const rect = workspaceButtonRef.current.getBoundingClientRect();
                setWorkspacePosition({ top: rect.bottom + 6, left: rect.left });
              }
              setWorkspaceOpen((open) => !open);
            }}
            className="sidebar-workspace-control absolute left-2 top-1 flex h-8 w-[164px] items-center rounded-[8px] px-2 text-left transition-[background-color,transform] duration-100 hover:bg-hover-2 active:scale-[0.99]"
          >
            <span className="sidebar-logo flex size-5 shrink-0 items-center justify-center text-ink">
              <span className="flex size-5 items-center justify-center rounded-[7px] bg-ink text-[11px] font-semibold text-surface">{workspace.logo ?? workspace.monogram}</span>
            </span>
            <span className="sidebar-copy ml-1.5 min-w-0 flex-1 truncate text-[14px] font-medium text-ink-2">
              {workspace.name}
            </span>
            <span className="sidebar-copy ml-1 flex shrink-0 text-ink-3">
              <IconChevronDownSmall size={16} />
            </span>
          </button>

          {workspaceOpen && <WorkspaceMenu workspace={workspace} position={workspacePosition} onClose={() => { setWorkspaceOpen(false); workspaceButtonRef.current?.focus(); }} onSettings={onWorkspaceSettings} />}

          <button
            type="button"
            aria-label="Collapse sidebar"
            aria-hidden={collapsed}
            tabIndex={collapsed ? -1 : 0}
            onClick={collapse}
            className="sidebar-collapse-control absolute right-2 top-1 flex size-8 items-center justify-center rounded-[8px] text-ink-3 transition-[opacity,background-color,color] duration-150 hover:bg-hover-2 hover:text-ink"
          >
            <IconSidebarLeftArrow size={18} />
          </button>
          <button
            type="button"
            aria-label="Expand sidebar"
            aria-hidden={!collapsed}
            tabIndex={collapsed ? 0 : -1}
            onClick={() => setCollapsed(false)}
            className="sidebar-expand-control absolute left-2 top-0.5 flex size-9 items-center justify-center rounded-[8px] text-ink-3 transition-[opacity,background-color,color] duration-150 hover:bg-hover-2 hover:text-ink"
          >
            <IconSidebarLeftArrow size={18} className="rotate-180" />
          </button>
        </div>

        <GlideGroup>
          <RailButton
            icon={primaryActionIcon ?? <IconEditBig size={18} />}
            label={primaryActionLabel}
            disabled={primaryActionDisabled}
            help={primaryActionHelp}
            onClick={
              primaryActionDisabled
                ? undefined
                : () => {
                    if (activeTitle === undefined) setDemoActiveTitle(null);
                    (onPrimaryAction ?? onNewChat)?.();
                  }
            }
          />
          {navItems.map((item) => (
            <RailButton
              key={item.key}
              icon={item.icon}
              label={item.label}
              count={item.count?.toString()}
              active={currentNav === item.key}
              onClick={() => selectNav(item.key)}
            />
          ))}
        </GlideGroup>

        <div className="mt-3 min-h-0 flex-1 overflow-y-auto">
          <div className="sidebar-copy relative mx-2 mb-1 h-8">
            <div
              aria-hidden={searchOpen}
              className={`absolute inset-0 flex items-center gap-1.5 px-2 text-[12.5px] font-medium text-ink-3 transition-[opacity,transform] ${searchOpen ? "pointer-events-none -translate-x-1 opacity-0" : "translate-x-0 opacity-100"}`}
              style={{ transitionDuration: `${CHAT_SEARCH_MOTION.duration}ms`, transitionTimingFunction: CHAT_SEARCH_MOTION.easing }}
            >
              <IconChevronDownSmall size={16} />
              <span>{recentLabel}</span>
            </div>

            <button
              type="button"
              aria-label={recentSearchLabel}
              aria-expanded={searchOpen}
              onClick={() => setSearchOpen(true)}
              className={`absolute right-0 top-0 z-10 flex size-8 items-center justify-center rounded-[8px] text-ink-3 transition-[opacity,background-color,color,transform] hover:bg-hover-2 hover:text-ink active:scale-[0.96] ${searchOpen ? "pointer-events-none opacity-0" : "opacity-100"}`}
              style={{ transitionDuration: `${CHAT_SEARCH_MOTION.duration}ms` }}
            >
              <IconMagnifyingGlass size={16} />
            </button>

            <div
              className={`absolute right-0 top-0 z-20 flex h-8 items-center overflow-hidden rounded-[8px] bg-field text-ink-3 shadow-hairline transition-[width,opacity] focus-within:text-ink-2 ${searchOpen ? "pointer-events-auto opacity-100" : "pointer-events-none opacity-0"}`}
              style={{
                width: searchOpen ? "100%" : CHAT_SEARCH_MOTION.closedWidth,
                transitionDuration: `${CHAT_SEARCH_MOTION.duration}ms`,
                transitionTimingFunction: CHAT_SEARCH_MOTION.easing,
              }}
            >
              <span className="ml-2 flex shrink-0 items-center justify-center">
                <IconMagnifyingGlass size={15} />
              </span>
              <input
                ref={searchRef}
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === "Escape") {
                    setSearchOpen(false);
                    setQuery("");
                  }
                }}
                placeholder={recentSearchLabel}
                aria-label={recentSearchLabel}
                className="ml-1.5 min-w-0 flex-1 bg-transparent text-[13px] font-medium text-ink outline-none placeholder:text-ink-3"
              />
              <button
                type="button"
                aria-label="Close search"
                onClick={() => {
                  setSearchOpen(false);
                  setQuery("");
                }}
                className="flex size-8 shrink-0 items-center justify-center rounded-[8px] text-ink-3 transition-[background-color,color,transform] duration-150 hover:bg-hover-2 hover:text-ink active:scale-[0.96]"
              >
                <IconCrossSmall size={16} />
              </button>
            </div>
          </div>

          <GlideGroup>
            {visibleRecents.map((item) => {
              const active = item.label === selectedTitle;
              return (
                <button
                  key={item.id}
                  data-row
                  type="button"
                  title={item.label}
                  onClick={() => {
                    if (!onPick) selectNav("activity");
                    if (activeTitle === undefined) setDemoActiveTitle(item.label);
                    onPick?.(item.id, item.label, item.prompt);
                  }}
                  className={`sidebar-row relative z-10 mx-2 flex h-8 items-center rounded-[8px] px-2 text-left transition-[width,background-color,color,transform] duration-150 active:scale-[0.98] ${
                    active ? "bg-hover-2 group-hover/glide:bg-transparent" : ""
                  }`}
                >
                  <span className={`sidebar-copy min-w-0 flex-1 truncate text-[14px] font-medium ${active ? "text-ink" : "text-ink-2"}`}>
                    {item.label}
                  </span>
                </button>
              );
            })}
            {query && visibleRecents.length === 0 && (
              <div className="sidebar-copy mx-2 px-2 py-2 text-[12.5px] text-ink-3">No backups found</div>
            )}
          </GlideGroup>
        </div>

        {footerLabel && (
          <div className="mx-2 mt-3 border-t border-line pt-3">
            <button
              type="button"
              disabled={footerDisabled}
              aria-label={footerLabel}
              title={footerLabel}
              aria-current={footerActive ? 'page' : undefined}
              onClick={onFooterClick}
              className={`flex h-8 w-full items-center justify-center gap-1.5 rounded-control ${footerActive ? 'bg-hover-2 text-ink' : 'text-ink-2'} text-[12.5px] font-medium transition-[background-color,transform] duration-150 hover:bg-line-strong active:scale-[0.98] disabled:pointer-events-none disabled:opacity-50`}
            >
              {footerIcon}
              {!collapsed && <span className="sidebar-copy">{footerLabel}</span>}
            </button>
          </div>
        )}
      </div>
    </aside>
  );
}
