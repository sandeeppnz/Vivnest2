// Intervals come over the wire as .NET's TimeSpan "c" format (e.g.
// "00:01:00"), not a number - reformat to something readable.
export function formatInterval(value: string): string {
  const [hours, minutes, seconds] = value.split(":").map(Number);
  const totalSeconds = hours * 3600 + minutes * 60 + seconds;

  if (totalSeconds === 0) return "—";
  if (totalSeconds % 60 === 0) return `${totalSeconds / 60}m`;
  return `${totalSeconds}s`;
}
