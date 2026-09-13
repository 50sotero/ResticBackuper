"use client";

import { useState, type ReactNode } from "react";

/* ─────────────────────────────────────────────────────────
 * FILTER TABLE
 * Status chips directly filter the task table.
 * ───────────────────────────────────────────────────────── */

export type FilterTableStatus = "todo" | "progress" | "done";
export type FilterTableSelection = "all" | FilterTableStatus;

export type TableRow = { id?: string; task: string; date: string; status: FilterTableStatus; statusLabel?: string; owner: string };

export type FilterTableLabels = {
  columns: { task: string; date: string; status: string; owner: string };
  filters?: Partial<Record<"all" | FilterTableStatus, string>>;
  statuses?: Partial<Record<FilterTableStatus, string>>;
};

const FILTERS: { key: "all" | FilterTableStatus; label: string; dot?: string }[] = [
  { key: "all", label: "All" },
  { key: "todo", label: "To do", dot: "#f09a2f" },
  { key: "progress", label: "In Progress", dot: "#16a6c7" },
  { key: "done", label: "Completed", dot: "#25a878" },
];

const ROWS: TableRow[] = [
  { task: "Restock mango sorbet", date: "Dec 03", status: "todo", owner: "Mango Moon Gelato" },
  { task: "Churn black sesame", date: "Sep 22", status: "progress", owner: "Kumo Creamery" },
  { task: "Print summer menu", date: "Jan 02", status: "todo", owner: "Coral Coast Sorbet" },
  { task: "Taste-test batch 42", date: "Nov 08", status: "progress", owner: "Maple Orbit" },
  { task: "Order waffle cones", date: "Apr 14", status: "done", owner: "Aurora Scoops" },
];

const LABELS: FilterTableLabels = {
  columns: { task: "Task name", date: "Date", status: "Status", owner: "Advisor" },
};

const PILLS: Record<FilterTableStatus, { label: string; cls: string }> = {
  todo: { label: "To do", cls: "filter-status-todo" },
  progress: { label: "In Progress", cls: "filter-status-progress" },
  done: { label: "Completed", cls: "filter-status-done" },
};

export type FilterTableProps = {
  rows?: TableRow[];
  labels?: FilterTableLabels;
  selectedRowId?: string | null;
  onRowClick?: (row: TableRow) => void;
  onRowOpen?: (row: TableRow) => void;
  filter?: FilterTableSelection;
  onFilterChange?: (filter: FilterTableSelection) => void;
  emptyContent?: ReactNode;
  tableLabel?: string;
  sortControl?: ReactNode;
  className?: string;
  variant?: string;
};

export default function FilterTable({
  rows = ROWS,
  labels = LABELS,
  selectedRowId,
  onRowClick,
  onRowOpen,
  filter: controlledFilter,
  onFilterChange,
  emptyContent,
  tableLabel = "Scrollable task table",
  sortControl,
  className = "",
}: FilterTableProps = {}) {
  const [internalFilter, setFilter] = useState<FilterTableSelection>("all");
  const filter = controlledFilter ?? internalFilter;
  const [internalSelectedRowId, setInternalSelectedRowId] = useState<string | null>(null);
  const resolvedSelectedRowId = selectedRowId === undefined ? internalSelectedRowId : selectedRowId;
  const filterLabels = labels.filters ?? {};
  const statusLabels = labels.statuses ?? {};
  const countFor = (key: "all" | FilterTableStatus) =>
    key === "all" ? rows.length : rows.filter((row) => row.status === key).length;

  return (
    <div className={`w-full max-w-105${className ? ` ${className}` : ""}`}>
      {/* filter chips */}
      <div
        className="-mx-1 mb-1 flex items-center gap-1 overflow-x-auto px-1 py-1"
        style={{ scrollbarWidth: "none" }}
      >
        {FILTERS.map((f) => {
          const active = filter === f.key;
          return (
            <button
              key={f.key}
              type="button"
              aria-pressed={active}
              onClick={() => { setFilter(f.key); onFilterChange?.(f.key); }}
              className={`flex h-6.5 shrink-0 items-center gap-1.5 rounded-full px-2.5 text-[12px]
                font-medium transition-[background-color,box-shadow,color] duration-200
                ${active ? "bg-surface text-ink shadow-btn" : "text-ink-2 hover:bg-hover"}`}
            >
              {f.dot && <span className="size-1.5 rounded-full" style={{ background: f.dot }} />}
              {filterLabels[f.key] ?? f.label}
              <span
                className={`rounded-[4px] px-1 text-[10.5px] tabular-nums
                  ${active ? "bg-field text-ink-2" : "text-ink-3"}`}
              >
                {countFor(f.key)}
              </span>
            </button>
          );
        })}
        {sortControl}
      </div>

      {/* table */}
      <div
        aria-label={tableLabel}
        className="overflow-x-auto rounded-card bg-surface shadow-card"
        role="region"
        tabIndex={0}
        style={{ scrollbarWidth: "none" }}
      >
        <div className="min-w-[420px]">
          <div className="grid grid-cols-[minmax(0,1.3fr)_minmax(0,0.6fr)_minmax(0,0.95fr)_minmax(0,0.9fr)] border-b border-[var(--grid-line)] text-[12.5px] font-medium text-ink-2">
            <span className="border-r border-[var(--grid-line)] px-3 py-2">{labels.columns.task}</span>
            <span className="border-r border-[var(--grid-line)] px-3 py-2">{labels.columns.date}</span>
            <span className="border-r border-[var(--grid-line)] px-3 py-2">{labels.columns.status}</span>
            <span className="px-3 py-2">{labels.columns.owner}</span>
          </div>
          {rows.map((row, index) => {
            const shown = filter === "all" || row.status === filter;
            const pill = { ...PILLS[row.status], label: row.statusLabel ?? statusLabels[row.status] ?? PILLS[row.status].label };
            const rowId = row.id ?? `${row.task}-${index}`;
            const selected = resolvedSelectedRowId === rowId;
            return (
              <div
                key={rowId}
                aria-hidden={!shown}
                inert={!shown}
                className="grid transition-[grid-template-rows,opacity] duration-300"
                style={{
                  gridTemplateRows: shown ? "1fr" : "0fr",
                  opacity: shown ? 1 : 0,
                  transitionTimingFunction: "cubic-bezier(0.23, 1, 0.32, 1)",
                }}
              >
                <div className="overflow-hidden">
                  <div
                    className={`grid grid-cols-[minmax(0,1.3fr)_minmax(0,0.6fr)_minmax(0,0.95fr)_minmax(0,0.9fr)] border-b
                      border-[var(--grid-line)] text-[13px] transition-colors duration-100 hover:bg-hover ${selected ? "bg-hover" : ""}`}
                    role={onRowClick ? "button" : undefined}
                    tabIndex={onRowClick && shown ? 0 : -1}
                    aria-pressed={onRowClick ? selected : undefined}
                    title={onRowOpen ? "Press Enter or double-click to open run details" : undefined}
                    onDoubleClick={() => onRowOpen?.(row)}
                    onClick={() => {
                      setInternalSelectedRowId(rowId);
                      onRowClick?.(row);
                    }}
                    onKeyDown={(event) => {
                      if (onRowClick && (event.key === "Enter" || event.key === " ") ) {
                        event.preventDefault();
                        setInternalSelectedRowId(rowId);
                        if (event.key === "Enter" && onRowOpen) onRowOpen(row);
                        else onRowClick(row);
                      }
                    }}
                  >
                    <span className="flex min-w-0 items-center border-r border-[var(--grid-line)] px-3 py-2">
                      <span className="truncate font-medium text-ink">{row.task}</span>
                    </span>
                    <span className="flex items-center whitespace-nowrap border-r border-[var(--grid-line)] px-3 py-2 text-ink-2 tabular-nums">
                      {row.date}
                    </span>
                    <span className="flex items-center border-r border-[var(--grid-line)] px-3 py-2">
                      <span
                        className={`inline-flex h-[23px] shrink-0 items-center whitespace-nowrap rounded-[8px] border px-[7px]
                          text-[13px] font-medium ${pill.cls}`}
                      >
                        {pill.label}
                      </span>
                    </span>
                    <span className="flex min-w-0 items-center px-3 py-2 text-ink-2">
                      <span className="truncate">{row.owner}</span>
                    </span>
                  </div>
                </div>
              </div>
            );
          })}
          {countFor(filter) === 0 && emptyContent}
        </div>
      </div>
    </div>
  );
}
