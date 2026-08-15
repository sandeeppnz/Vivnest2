// NotApplicable (decision-log.md ADR-076) - a Disabled/Retired device or
// Inactive agent, listed last since it's not a live operational state to
// scan for first.
const STATUS_ORDER = ["Healthy", "Degraded", "Offline", "Error", "Unknown", "NotApplicable"];

export function countByStatus(items: { status: string }[] | null | undefined): Record<string, number> {
  const counts: Record<string, number> = {};
  for (const item of items ?? []) {
    counts[item.status] = (counts[item.status] ?? 0) + 1;
  }
  return counts;
}

interface StatusFilterChipsProps {
  counts: Record<string, number>;
  selected: string | null;
  onSelect: (status: string | null) => void;
}

export function StatusFilterChips({ counts, selected, onSelect }: StatusFilterChipsProps) {
  const total = Object.values(counts).reduce((sum, count) => sum + count, 0);

  return (
    <div className="filter-chips">
      <button
        type="button"
        className={`filter-chip${selected === null ? " active" : ""}`}
        onClick={() => onSelect(null)}
      >
        All <span className="filter-chip-count">{total}</span>
      </button>
      {STATUS_ORDER.filter((status) => counts[status] > 0).map((status) => (
        <button
          type="button"
          key={status}
          className={`filter-chip${selected === status ? " active" : ""}`}
          onClick={() => onSelect(status === selected ? null : status)}
        >
          {status} <span className="filter-chip-count">{counts[status]}</span>
        </button>
      ))}
    </div>
  );
}
