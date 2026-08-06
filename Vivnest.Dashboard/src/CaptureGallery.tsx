import { useEffect, useRef, useState } from "react";
import {
  ApiError,
  getDeviceCaptureDaySummaries,
  getDeviceCapturesByDay,
  type DeviceEvent,
} from "./api";
import { formatDateTimeExact } from "./format";
import { TriggerIcon } from "./icons";

const SUMMARY_DAYS = 30;
const PAGE_SIZE = 50;

interface CaptureGalleryProps {
  apiKey: string;
  deviceId: string;
  timezone: string;
  selectedCapture: DeviceEvent | null;
  onSelectCapture: (capture: DeviceEvent) => void;
  onAuthError: () => void;
}

interface DayState {
  date: string;
  count: number;
  expanded: boolean;
  captures: DeviceEvent[];
  hasMore: boolean;
  loading: boolean;
  loaded: boolean;
  error: string | null;
}

// Day summaries are grouped server-side by the device's own timezone
// (DeviceSummary.timezone, stamped by the Agent from AgentOptions.Timezone -
// falls back to "UTC" if never configured), not the browser's timezone and
// not a hardcoded UTC - "Today"/"Yesterday" have to compare against that
// same zone here, so the heading always agrees with which bucket a capture
// actually landed in. Intl.DateTimeFormat's en-CA locale formats as
// YYYY-MM-DD, matching the API's date string format directly.
function localDateString(timezone: string, date: Date): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone: timezone }).format(date);
}

function todayInTimezone(timezone: string): string {
  return localDateString(timezone, new Date());
}

function yesterdayInTimezone(timezone: string): string {
  return localDateString(timezone, new Date(Date.now() - 24 * 60 * 60 * 1000));
}

// TriggerReason is only ever set on the JSON payload for a
// motion-triggered capture (see CameraCapturedData.TriggerReason,
// Vivnest.Core) - PascalCase because it's serialized straight from the
// .NET property name, no camelCase conversion applied anywhere in the
// pipeline.
export function isTriggeredCapture(capture: DeviceEvent): boolean {
  const data = capture.data;
  return (
    typeof data === "object" &&
    data !== null &&
    typeof (data as { TriggerReason?: unknown }).TriggerReason === "string"
  );
}

function dateHeading(dateStr: string, timezone: string): string {
  if (dateStr === todayInTimezone(timezone)) return "Today";
  if (dateStr === yesterdayInTimezone(timezone)) return "Yesterday";

  const [year, month, day] = dateStr.split("-").map(Number);
  const date = new Date(Date.UTC(year, month - 1, day));

  // dateStr is already a pure calendar date (no time-of-day component), so
  // re-rendering it with timeZone: "UTC" here is safe regardless of the
  // device's actual timezone - there's no time-of-day left to get wrong,
  // only the Today/Yesterday comparison above needed the real zone.
  const currentYear = Number(todayInTimezone(timezone).slice(0, 4));

  return date.toLocaleDateString(undefined, {
    weekday: "long",
    month: "long",
    day: "numeric",
    year: year === currentYear ? undefined : "numeric",
    timeZone: "UTC",
  });
}

export function CaptureGallery({
  apiKey,
  deviceId,
  timezone,
  selectedCapture,
  onSelectCapture,
  onAuthError,
}: CaptureGalleryProps) {
  const [days, setDays] = useState<DayState[] | null>(null);
  const [summaryError, setSummaryError] = useState<string | null>(null);

  // Guards against a slow load-more/day-expand from a previous device
  // landing after the user has already switched devices.
  const currentKeyRef = useRef(`${apiKey}:${deviceId}`);

  function handleAuthError(err: unknown): boolean {
    if (err instanceof ApiError && err.status === 401) {
      onAuthError();
      return true;
    }
    return false;
  }

  useEffect(() => {
    currentKeyRef.current = `${apiKey}:${deviceId}`;
    let cancelled = false;

    setDays(null);
    setSummaryError(null);

    getDeviceCaptureDaySummaries(apiKey, deviceId, SUMMARY_DAYS)
      .then((summaries) => {
        if (cancelled) return;

        const today = todayInTimezone(timezone);

        setDays(
          summaries.map((s) => ({
            date: s.date,
            count: s.count,
            expanded: s.date === today,
            captures: [],
            hasMore: false,
            loading: false,
            loaded: false,
            error: null,
          })),
        );
      })
      .catch((err) => {
        if (cancelled) return;
        if (handleAuthError(err)) return;
        setSummaryError(err instanceof Error ? err.message : "Failed to load captures.");
      });

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiKey, deviceId, onAuthError]);

  // Single place that decides "this day is expanded but has never been
  // fetched" - covers both the initial Today auto-load and any day the
  // user expands by hand, so toggling a day only needs to flip a flag.
  useEffect(() => {
    const target = days?.find((d) => d.expanded && !d.loaded && !d.loading);
    if (!target) return;

    loadDay(target.date, 0);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [days]);

  function updateDay(date: string, patch: Partial<DayState>) {
    setDays((prev) =>
      prev ? prev.map((d) => (d.date === date ? { ...d, ...patch } : d)) : prev,
    );
  }

  function loadDay(date: string, skip: number) {
    const key = currentKeyRef.current;

    updateDay(date, { loading: true, error: null });

    getDeviceCapturesByDay(apiKey, deviceId, date, skip, PAGE_SIZE)
      .then((page) => {
        if (currentKeyRef.current !== key) return;

        setDays((prev) => {
          if (!prev) return prev;

          return prev.map((d) => {
            if (d.date !== date) return d;

            const captures = skip === 0 ? page.captures : [...d.captures, ...page.captures];

            return { ...d, captures, hasMore: page.hasMore, loading: false, loaded: true };
          });
        });
      })
      .catch((err) => {
        if (currentKeyRef.current !== key) return;
        if (handleAuthError(err)) return;

        updateDay(date, {
          loading: false,
          error: err instanceof Error ? err.message : "Failed to load captures.",
        });
      });
  }

  function toggleDay(date: string) {
    setDays((prev) =>
      prev ? prev.map((d) => (d.date === date ? { ...d, expanded: !d.expanded } : d)) : prev,
    );
  }

  if (summaryError) return <p className="error">{summaryError}</p>;
  if (!days) return <p>Loading captures...</p>;
  if (days.length === 0) return <p>No captures in the last {SUMMARY_DAYS} days.</p>;

  return (
    <div className="captures-timeline">
      {days.map((day) => (
        <div className="timeline-date-section" key={day.date}>
          <button
            type="button"
            className="timeline-date-heading"
            onClick={() => toggleDay(day.date)}
          >
            <span className="timeline-date-toggle">{day.expanded ? "▾" : "▸"}</span>
            {dateHeading(day.date, timezone)}
            <span className="timeline-date-count"> ({day.count})</span>
          </button>

          {day.expanded && (
            <>
              {day.error && <p className="error">{day.error}</p>}

              <div className="capture-gallery">
                {day.captures.map((capture) => (
                  <button
                    key={capture.occurredAtUtc}
                    type="button"
                    className={`capture-thumb${capture === selectedCapture ? " selected" : ""}`}
                    onClick={() => onSelectCapture(capture)}
                    title={formatDateTimeExact(capture.occurredAtUtc)}
                  >
                    {capture.imageUrl && <img src={capture.imageUrl} alt="" />}
                    {isTriggeredCapture(capture) && (
                      <TriggerIcon className="capture-thumb-badge" />
                    )}
                  </button>
                ))}
              </div>

              {day.loading && <p className="timeline-loading">Loading...</p>}

              {!day.loading && day.hasMore && (
                <button
                  type="button"
                  className="load-more-button"
                  onClick={() => loadDay(day.date, day.captures.length)}
                >
                  Load more
                </button>
              )}
            </>
          )}
        </div>
      ))}
    </div>
  );
}
