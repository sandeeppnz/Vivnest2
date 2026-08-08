// Inline Tabler outline icons (MIT licensed, https://tabler.io/icons) -
// hand-picked SVGs, not a package dependency, matching this dashboard's
// "no UI framework" approach (ADR-018). stroke="currentColor" so every
// icon inherits color from its CSS context (.device-icon, .live-feed-placeholder)
// the same way the emoji it replaces inherited nothing and just rendered
// at whatever color emoji render at.

import type { ReactElement, ReactNode } from "react";

export interface IconProps {
  className?: string;
}

function Svg({ className, children }: IconProps & { children: ReactNode }) {
  return (
    <svg
      className={className}
      aria-hidden="true"
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      {children}
    </svg>
  );
}

function CameraIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M5 7h1a2 2 0 0 0 2 -2a1 1 0 0 1 1 -1h6a1 1 0 0 1 1 1a2 2 0 0 0 2 2h1a2 2 0 0 1 2 2v9a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-9a2 2 0 0 1 2 -2" />
      <path d="M9 13a3 3 0 1 0 6 0a3 3 0 0 0 -6 0" />
    </Svg>
  );
}

function PlugIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M9.785 6l8.215 8.215l-2.054 2.054a5.81 5.81 0 1 1 -8.215 -8.215l2.054 -2.054" />
      <path d="M4 20l3.5 -3.5" />
      <path d="M15 4l-3.5 3.5" />
      <path d="M20 9l-3.5 3.5" />
    </Svg>
  );
}

function RadarIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M21 12h-8a1 1 0 1 0 -1 1v8a9 9 0 0 0 9 -9" />
      <path d="M16 9a5 5 0 1 0 -7 7" />
      <path d="M20.486 9a9 9 0 1 0 -11.482 11.495" />
    </Svg>
  );
}

function DropletIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M7.502 19.423c2.602 2.105 6.395 2.105 8.996 0c2.602 -2.105 3.262 -5.708 1.566 -8.546l-4.89 -7.26c-.42 -.625 -1.287 -.803 -1.936 -.397a1.376 1.376 0 0 0 -.41 .397l-4.893 7.26c-1.695 2.838 -1.035 6.441 1.567 8.546" />
    </Svg>
  );
}

function DropletOffIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M18.963 14.938a6.54 6.54 0 0 0 -.899 -4.06l-4.89 -7.26c-.42 -.626 -1.287 -.804 -1.936 -.398a1.376 1.376 0 0 0 -.41 .397l-1.282 1.9m-1.625 2.415l-1.986 2.946c-1.695 2.837 -1.035 6.44 1.567 8.545c2.602 2.105 6.395 2.105 8.996 0a6.83 6.83 0 0 0 1.376 -1.499" />
      <path d="M3 3l18 18" />
    </Svg>
  );
}

function FlameIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M12 10.941c2.333 -3.308 .167 -7.823 -1 -8.941c0 3.395 -2.235 5.299 -3.667 6.706c-1.43 1.408 -2.333 3.294 -2.333 5.588c0 3.704 3.134 6.706 7 6.706c3.866 0 7 -3.002 7 -6.706c0 -1.712 -1.232 -4.403 -2.333 -5.588c-2.084 3.353 -3.257 3.353 -4.667 2.235" />
    </Svg>
  );
}

function ThermometerIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M19 5a2.828 2.828 0 0 1 0 4l-8 8h-4v-4l8 -8a2.828 2.828 0 0 1 4 0" />
      <path d="M16 7l-1.5 -1.5" />
      <path d="M13 10l-1.5 -1.5" />
      <path d="M10 13l-1.5 -1.5" />
      <path d="M7 17l-3 3" />
    </Svg>
  );
}

function DoorIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M14 12v.01" />
      <path d="M3 21h18" />
      <path d="M6 21v-16a2 2 0 0 1 2 -2h8a2 2 0 0 1 2 2v16" />
    </Svg>
  );
}

function AlertTriangleIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M12 9v4" />
      <path d="M10.363 3.591l-8.106 13.534a1.914 1.914 0 0 0 1.636 2.871h16.214a1.914 1.914 0 0 0 1.636 -2.87l-8.106 -13.536a1.914 1.914 0 0 0 -3.274 0z" />
      <path d="M12 16h.01" />
    </Svg>
  );
}

function ThumbsUpSvgIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M7 11v8a1 1 0 0 1 -1 1h-2a1 1 0 0 1 -1 -1v-7a1 1 0 0 1 1 -1h3a4 4 0 0 0 4 -4v-1a2 2 0 0 1 4 0v5h3a2 2 0 0 1 2 2l-1 5a2 3 0 0 1 -2 2h-7a3 3 0 0 1 -3 -3" />
    </Svg>
  );
}

function BotSvgIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M7 7h10a2 2 0 0 1 2 2v9a2 2 0 0 1 -2 2h-10a2 2 0 0 1 -2 -2v-9a2 2 0 0 1 2 -2z" />
      <path d="M10 3v4" />
      <path d="M9 12v.01" />
      <path d="M15 12v.01" />
      <path d="M9.5 16a3.5 3.5 0 0 0 5 0" />
    </Svg>
  );
}

function MapPinIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M9 11a3 3 0 1 0 6 0a3 3 0 0 0 -6 0" />
      <path d="M17.657 16.657l-4.243 4.243a2 2 0 0 1 -2.827 0l-4.244 -4.243a8 8 0 1 1 14.314 0z" />
    </Svg>
  );
}

function CopySvgIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M8 8m0 2a2 2 0 0 1 2 -2h8a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-8a2 2 0 0 1 -2 -2z" />
      <path d="M16 8v-2a2 2 0 0 0 -2 -2h-8a2 2 0 0 0 -2 2v8a2 2 0 0 0 2 2h2" />
    </Svg>
  );
}

function CheckSvgIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M5 12l5 5l10 -10" />
    </Svg>
  );
}

function HeartIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M19.5 12.572l-7.5 7.428l-7.5 -7.428a5 5 0 1 1 7.5 -6.566a5 5 0 1 1 7.5 6.572" />
    </Svg>
  );
}

function ClockSvgIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M12 12m-9 0a9 9 0 1 0 18 0a9 9 0 1 0 -18 0" />
      <path d="M12 7v5l3 3" />
    </Svg>
  );
}

function LogoutSvgIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M14 8v-2a2 2 0 0 0 -2 -2h-7a2 2 0 0 0 -2 2v12a2 2 0 0 0 2 2h7a2 2 0 0 0 2 -2v-2" />
      <path d="M9 12h12l-3 -3" />
      <path d="M18 15l3 -3" />
    </Svg>
  );
}

function ListSvgIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M9 6l11 0" />
      <path d="M9 12l11 0" />
      <path d="M9 18l11 0" />
      <path d="M5 6l0 .01" />
      <path d="M5 12l0 .01" />
      <path d="M5 18l0 .01" />
    </Svg>
  );
}

function HomeSvgIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M5 12l-2 0l9 -9l9 9l-2 0" />
      <path d="M5 12v7a2 2 0 0 0 2 2h10a2 2 0 0 0 2 -2v-7" />
      <path d="M9 21v-6a2 2 0 0 1 2 -2h2a2 2 0 0 1 2 2v6" />
    </Svg>
  );
}

function AntennaIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M20 4v8" />
      <path d="M16 4.5v7" />
      <path d="M12 5v16" />
      <path d="M8 5.5v5" />
      <path d="M4 6v4" />
      <path d="M20 8h-16" />
    </Svg>
  );
}

function ServerIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M3 7a3 3 0 0 1 3 -3h12a3 3 0 0 1 3 3v2a3 3 0 0 1 -3 3h-12a3 3 0 0 1 -3 -3v-2" />
      <path d="M3 15a3 3 0 0 1 3 -3h12a3 3 0 0 1 3 3v2a3 3 0 0 1 -3 3h-12a3 3 0 0 1 -3 -3l0 -2" />
      <path d="M7 8l0 .01" />
      <path d="M7 16l0 .01" />
      <path d="M11 8h6" />
      <path d="M11 16h6" />
    </Svg>
  );
}

function VideoIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M15 10l4.553 -2.276a1 1 0 0 1 1.447 .894v6.764a1 1 0 0 1 -1.447 .894l-4.553 -2.276v-4" />
      <path d="M3 8a2 2 0 0 1 2 -2h8a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-8a2 2 0 0 1 -2 -2l0 -8" />
    </Svg>
  );
}

const DEVICE_ICONS: Record<string, (props: IconProps) => ReactElement> = {
  Camera: CameraIcon,
  SmartPlug: PlugIcon,
  MotionSensor: RadarIcon,
  HumiditySensor: DropletIcon,
  SmokeAlarm: FlameIcon,
  WaterLeak: DropletOffIcon,
  HeatPump: ThermometerIcon,
  DoorSensor: DoorIcon,
};

export function DeviceIcon({ deviceType, className }: { deviceType: string } & IconProps) {
  const Icon = DEVICE_ICONS[deviceType] ?? AntennaIcon;
  return <Icon className={className} />;
}

export function AgentIcon(props: IconProps) {
  return <ServerIcon {...props} />;
}

export function LiveFeedIcon(props: IconProps) {
  return <VideoIcon {...props} />;
}

export function TriggerIcon(props: IconProps) {
  return <RadarIcon {...props} />;
}

export function LogoutIcon(props: IconProps) {
  return <LogoutSvgIcon {...props} />;
}

export function IntervalIcon(props: IconProps) {
  return <ClockSvgIcon {...props} />;
}

export function HeartbeatIcon(props: IconProps) {
  return <HeartIcon {...props} />;
}

export function CopyIcon(props: IconProps) {
  return <CopySvgIcon {...props} />;
}

export function CheckIcon(props: IconProps) {
  return <CheckSvgIcon {...props} />;
}

export function LocationIcon(props: IconProps) {
  return <MapPinIcon {...props} />;
}

export function AlertIcon(props: IconProps) {
  return <AlertTriangleIcon {...props} />;
}

export function OverviewIcon(props: IconProps) {
  return <HomeSvgIcon {...props} />;
}

export function DevicesIcon(props: IconProps) {
  return <AntennaIcon {...props} />;
}

export function EventsIcon(props: IconProps) {
  return <ListSvgIcon {...props} />;
}

// Rotated 180deg via CSS (.capture-thumb-sink-dirty) for "not clean" rather
// than a second hand-drawn thumb-down path - one icon, one color swap plus
// a flip, same visual result with less to get wrong from memory.
export function ThumbsUpIcon(props: IconProps) {
  return <ThumbsUpSvgIcon {...props} />;
}

export function BotIcon(props: IconProps) {
  return <BotSvgIcon {...props} />;
}

// Vivnest wordmark's companion icon: a live device node broadcasts a wifi
// signal that resolves upward into an "insight" spark — the platform's whole
// job (node · device · wifi · connectivity · insights) in one mark. Set in a
// rounded tile so it doubles as the favicon / installable app icon. The spark
// is the healthy/derived signal (--text-success); arcs step accent → light for
// depth. Multi-tone + fill, so it doesn't use the shared <Svg> currentColor
// wrapper — it pulls the app palette directly so it stays in sync with the theme.
export function VivnestLogo({ className }: IconProps) {
  return (
    <svg
      className={className}
      aria-hidden="true"
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 44 44"
      fill="none"
    >
      <rect
        x="4"
        y="4"
        width="36"
        height="36"
        rx="10"
        fill="var(--surface-1)"
        stroke="var(--border)"
      />
      <path
        d="M14.5 29 A10 10 0 0 1 29.5 29"
        stroke="var(--border-accent)"
        strokeWidth="2.6"
        strokeLinecap="round"
      />
      <path
        d="M11 26 A14 14 0 0 1 33 26"
        stroke="var(--text-accent)"
        strokeWidth="2.3"
        strokeLinecap="round"
      />
      <circle cx="22" cy="32" r="3.4" fill="var(--border-accent)" />
      <path
        d="M22 9 L23.3 13.7 L28 15 L23.3 16.3 L22 21 L20.7 16.3 L16 15 L20.7 13.7 Z"
        fill="var(--text-success)"
      />
    </svg>
  );
}
