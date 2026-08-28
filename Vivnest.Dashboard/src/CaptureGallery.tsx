import { useEffect, useRef, useState } from "react";
import { getDeviceCapturesByDay, type DeviceEvent } from "./api";
import { ErrorState } from "./ErrorState";
import { useCaptureDaySummaries } from "./queries";
import { useApiKey } from "./session";
import { formatDateTimeExact, formatTimeOnly } from "./format";
import { BotIcon, ThumbsUpIcon, TriggerIcon } from "./icons";

const SUMMARY_DAYS = 30;
const PAGE_SIZE = 50;

// Generous vs. the Cloud-mediated classify round-trip's own worst case
// (Cloud Functions' queue-trigger polling backoff plus the High-type agent's own
// 5s poll interval - see decision-log.md ADR-035) - long enough that a
// still-Pending badge past this point more likely means the request was
// lost (no retry semantics anywhere in that path, by design) than that
// it's still genuinely in flight.
const AI_PENDING_TIMEOUT_MS = 2 * 60 * 1000;

interface AiCapableDevice {
  sinkCleanlinessEnabled: boolean;
  objectDetectionEnabled: boolean;
}

// True only while a result is plausibly still in flight - not "never
// classified" in general (see isAiDone below for how that's told apart
// from "disabled entirely").
export function isAiPending(capture: DeviceEvent, device: AiCapableDevice): boolean {
  if (!device.sinkCleanlinessEnabled && !device.objectDetectionEnabled) return false;
  if (isAiDone(capture, device)) return false;

  const ageMs = Date.now() - new Date(capture.occurredAtUtc).getTime();
  return ageMs >= 0 && ageMs < AI_PENDING_TIMEOUT_MS;
}

// ObjectsDetected fires on every capture ObjectDetection runs on, even
// when nothing unusual turns up (ADR-034's follow-up) - so when
// ObjectDetection is enabled, its presence is the reliable "the High-type
// agent finished this one" signal. SinkCleanliness alone can't be used for
// that: it never fires at all for a person-gated capture, so waiting on
// it would show Pending forever for those - only fall back to it when
// ObjectDetection isn't enabled at all.
function isAiDone(capture: DeviceEvent, device: AiCapableDevice): boolean {
  if (device.objectDetectionEnabled) return capture.detectedObjects !== null;
  return capture.sinkCleanlinessResult !== null;
}

interface CaptureGalleryProps {
  deviceId: string;
  timezone: string;
  sinkCleanlinessEnabled: boolean;
  objectDetectionEnabled: boolean;
  selectedCapture: DeviceEvent | null;
  onSelectCapture: (capture: DeviceEvent) => void;
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
  deviceId,
  timezone,
  sinkCleanlinessEnabled,
  objectDetectionEnabled,
  selectedCapture,
  onSelectCapture,
}: CaptureGalleryProps) {
  const apiKey = useApiKey();
  const aiDevice = { sinkCleanlinessEnabled, objectDetectionEnabled };
  const summariesQuery = useCaptureDaySummaries(deviceId, SUMMARY_DAYS);
  const [days, setDays] = useState<DayState[] | null>(null);

  // Guards against a slow load-more/day-expand from a previous device
  // landing after the user has already switched devices.
  const currentKeyRef = useRef(deviceId);

  useEffect(() => {
    currentKeyRef.current = deviceId;
    setDays(null);
  }, [deviceId]);

  // Rebuild the day list whenever the summaries (re)arrive, but keep the
  // expansion state and already-loaded captures of days the user has
  // opened - a background refetch must not collapse the gallery.
  useEffect(() => {
    const summaries = summariesQuery.data;
    if (!summaries) return;

    const today = todayInTimezone(timezone);

    setDays((prev) =>
      summaries.map((summary) => {
        const existing = prev?.find((d) => d.date === summary.date);

        return existing
          ? { ...existing, count: summary.count }
          : {
              date: summary.date,
              count: summary.count,
              expanded: summary.date === today,
              captures: [],
              hasMore: false,
              loading: false,
              loaded: false,
              error: null,
            };
      }),
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [summariesQuery.data]);

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

  if (summariesQuery.isError) {
    return <ErrorState message={summariesQuery.error.message} onRetry={() => summariesQuery.refetch()} />;
  }
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
                    {capture.sinkCleanlinessResult !== null && (
                      <ThumbsUpIcon
                        className={`capture-thumb-badge capture-thumb-sink-badge${
                          capture.sinkCleanlinessResult
                            ? " capture-thumb-sink-clean"
                            : " capture-thumb-sink-dirty"
                        }`}
                      />
                    )}
                    {isAiPending(capture, aiDevice) && (
                      <BotIcon className="capture-thumb-badge capture-thumb-pending-badge" />
                    )}
                    <span className="capture-thumb-time">
                      {formatTimeOnly(capture.occurredAtUtc)}
                    </span>
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
