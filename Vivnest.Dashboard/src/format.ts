// Intervals come over the wire as .NET's TimeSpan "c" format (e.g.
// "00:01:00"), not a number - reformat to something readable.
export function formatInterval(value: string): string {
  const [hours, minutes, seconds] = value.split(":").map(Number);
  const totalSeconds = hours * 3600 + minutes * 60 + seconds;

  if (totalSeconds === 0) return "—";
  if (totalSeconds % 60 === 0) return `${totalSeconds / 60}m`;
  return `${totalSeconds}s`;
}

// Relative for anything recent ("just now", "5 mins ago"), falling back to
// a friendly absolute date+time (with the viewer's own timezone name, e.g.
// "Tue, 23 Aug 2026, 8:30 PM NZST") once it's more than a week old - recent
// timestamps are more useful as "how long ago", older ones as "when".
export function formatDateTime(isoUtc: string): string {
  const date = new Date(isoUtc);
  const diffSeconds = Math.round((Date.now() - date.getTime()) / 1000);

  if (diffSeconds >= 0 && diffSeconds < 7 * 24 * 3600) {
    if (diffSeconds < 45) return "just now";

    const diffMinutes = Math.round(diffSeconds / 60);
    if (diffMinutes < 60) return `${diffMinutes} min${diffMinutes === 1 ? "" : "s"} ago`;

    const diffHours = Math.round(diffMinutes / 60);
    if (diffHours < 24) return `${diffHours} hour${diffHours === 1 ? "" : "s"} ago`;

    const diffDays = Math.round(diffHours / 24);
    return `${diffDays} day${diffDays === 1 ? "" : "s"} ago`;
  }

  return formatDateTimeExact(isoUtc);
}

// Full precision, always absolute - used as a hover title on relative
// timestamps, and as the display value once formatDateTime falls back.
export function formatDateTimeExact(isoUtc: string): string {
  const date = new Date(isoUtc);

  const datePart = date.toLocaleDateString(undefined, {
    weekday: "short",
    day: "numeric",
    month: "short",
    year: "numeric",
  });

  const timePart = date.toLocaleTimeString(undefined, {
    hour: "numeric",
    minute: "2-digit",
    timeZoneName: "short",
  });

  return `${datePart}, ${timePart}`;
}
