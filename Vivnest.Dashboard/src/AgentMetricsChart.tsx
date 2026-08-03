import type { AgentMetricSample } from "./api";
import { formatBytes } from "./format";

interface AgentMetricsChartProps {
  samples: AgentMetricSample[];
}

const CHART_WIDTH = 600;
const CHART_HEIGHT = 100;
const PADDING = 6;

// Simplification: null CPU values (only the first sample of a process's
// lifetime, see AgentMetricsWorker) are dropped from the line entirely
// rather than leaving a visual gap - rare enough (once per restart) that
// a straight line across it isn't misleading.
function buildPoints(
  samples: AgentMetricSample[],
  getValue: (s: AgentMetricSample) => number | null,
  minUtc: number,
  maxUtc: number,
  minValue: number,
  maxValue: number,
): string {
  const timeRange = maxUtc - minUtc || 1;
  const valueRange = maxValue - minValue || 1;

  return samples
    .map((s) => {
      const value = getValue(s);
      if (value === null) return null;

      const t = new Date(s.occurredAtUtc).getTime();
      const x = PADDING + ((t - minUtc) / timeRange) * (CHART_WIDTH - PADDING * 2);
      const y =
        CHART_HEIGHT - PADDING - ((value - minValue) / valueRange) * (CHART_HEIGHT - PADDING * 2);

      return `${x.toFixed(1)},${y.toFixed(1)}`;
    })
    .filter((p): p is string => p !== null)
    .join(" ");
}

function MetricLine({
  label,
  currentValueLabel,
  points,
}: {
  label: string;
  currentValueLabel: string;
  points: string;
}) {
  return (
    <div className="metrics-chart">
      <div className="metrics-chart-header">
        <span className="metrics-chart-label">{label}</span>
        <span className="metrics-chart-value">{currentValueLabel}</span>
      </div>
      <svg
        className="metrics-chart-svg"
        viewBox={`0 0 ${CHART_WIDTH} ${CHART_HEIGHT}`}
        preserveAspectRatio="none"
      >
        <polyline points={points} fill="none" stroke="var(--text-accent)" strokeWidth="2" />
      </svg>
    </div>
  );
}

export function AgentMetricsChart({ samples }: AgentMetricsChartProps) {
  if (samples.length === 0) {
    return <p className="metrics-chart-empty">No metrics reported yet.</p>;
  }

  const timestamps = samples.map((s) => new Date(s.occurredAtUtc).getTime());
  const minUtc = Math.min(...timestamps);
  const maxUtc = Math.max(...timestamps);

  const cpuValues = samples
    .map((s) => s.cpuUsagePercent)
    .filter((v): v is number => v !== null);

  const memoryValues = samples.map((s) => s.memoryUsedBytes);
  const maxMemory = Math.max(...memoryValues);

  const cpuPoints = buildPoints(samples, (s) => s.cpuUsagePercent, minUtc, maxUtc, 0, 100);
  const memoryPoints = buildPoints(
    samples,
    (s) => s.memoryUsedBytes,
    minUtc,
    maxUtc,
    0,
    maxMemory * 1.1 || 1,
  );

  const latestCpu = cpuValues.length > 0 ? cpuValues[cpuValues.length - 1] : null;
  const latestMemory = memoryValues[memoryValues.length - 1];

  return (
    <div className="metrics-chart-grid">
      <MetricLine
        label="CPU"
        currentValueLabel={latestCpu === null ? "—" : `${latestCpu.toFixed(1)}%`}
        points={cpuPoints}
      />
      <MetricLine label="Memory" currentValueLabel={formatBytes(latestMemory)} points={memoryPoints} />
    </div>
  );
}
