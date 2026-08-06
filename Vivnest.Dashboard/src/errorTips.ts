// Pattern-matched troubleshooting tips for known error strings written by
// the Agent (CameraCaptureExecutor, MotionSensorMonitorService,
// SmartPlugMonitorService, HomeAssistantLivenessTracker) - falls back to a
// per-device-type default, then no tip at all, since new/unrecognized error
// text shouldn't invent a misleading suggestion.

interface TipRule {
  pattern: RegExp;
  tip: string;
}

const RULES: TipRule[] = [
  {
    pattern: /did not exit within|killed/i,
    tip: "The camera didn't respond in time. Check that it's powered on and reachable on the network from the agent host.",
  },
  {
    pattern: /ffmpeg snapshot failed/i,
    tip: "The camera rejected the connection. Double-check the RTSP URL and credentials in its device settings.",
  },
  {
    pattern: /ffmpeg not found/i,
    tip: "ffmpeg is missing on the agent host. This is a configuration issue, not a device fault.",
  },
  {
    pattern: /home assistant reports .* unavailable/i,
    tip: "Home Assistant has lost contact with this device. Check the entity's status directly in Home Assistant.",
  },
];

const DEVICE_TYPE_DEFAULT_TIPS: Record<string, string> = {
  MotionSensor: "Communication with the sensor failed. Check its power and wireless connection.",
  SmartPlug: "Communication with the plug failed. Check its power and wireless connection.",
};

export function getTroubleshootingTip(message: string, deviceType?: string): string | null {
  for (const rule of RULES) {
    if (rule.pattern.test(message)) return rule.tip;
  }

  if (deviceType && DEVICE_TYPE_DEFAULT_TIPS[deviceType]) {
    return DEVICE_TYPE_DEFAULT_TIPS[deviceType];
  }

  return null;
}
