const STATUS_ORDER = ["Online", "Warning", "Offline", "Error", "Unknown"];

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
