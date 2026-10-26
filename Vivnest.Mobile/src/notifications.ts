import Constants from "expo-constants";
import * as Device from "expo-device";
import * as Notifications from "expo-notifications";
import { Linking, Platform } from "react-native";

// Must match ExpoPushNotificationChannel on the server: the server names a
// channel and the phone decides how loud it is.
export const CHANNEL_FIRE_ALARM = "fire-alarm";
export const CHANNEL_ALERTS = "alerts";
export const CHANNEL_INFO = "info";

// Show alerts as a heads-up banner with sound even while the app is open.
export function configureForegroundBehaviour(): void {
  Notifications.setNotificationHandler({
    handleNotification: async () => ({
      shouldShowBanner: true,
      shouldShowList: true,
      shouldPlaySound: true,
      shouldSetBadge: false,
    }),
  });
}

// Android only. Importance MAX is what makes it a heads-up, sounding
// alert; bypassDnd is a REQUEST that only takes effect once the user grants
// this app Do Not Disturb access (see openDoNotDisturbAccess) and leaves
// "Override Do Not Disturb" on for the channel (see openChannelSettings).
// Channels are create-once: Android ignores later changes to importance, so
// these definitions are deliberately conservative and a changed one needs a
// new channel id.
export async function ensureChannels(): Promise<void> {
  if (Platform.OS !== "android") return;

  await Notifications.setNotificationChannelAsync(CHANNEL_FIRE_ALARM, {
    name: "Fire alarm",
    description: "A fire-alarm sound was heard. Rings through Do Not Disturb.",
    importance: Notifications.AndroidImportance.MAX,
    bypassDnd: true,
    sound: "default",
    vibrationPattern: [0, 800, 400, 800, 400, 800],
    lockscreenVisibility: Notifications.AndroidNotificationVisibility.PUBLIC,
    enableVibrate: true,
    showBadge: true,
  });

  await Notifications.setNotificationChannelAsync(CHANNEL_ALERTS, {
    name: "Offline alerts",
    description: "An agent or device has gone offline.",
    importance: Notifications.AndroidImportance.MAX,
    bypassDnd: true,
    sound: "default",
    vibrationPattern: [0, 500, 250, 500],
    lockscreenVisibility: Notifications.AndroidNotificationVisibility.PUBLIC,
  });

  await Notifications.setNotificationChannelAsync(CHANNEL_INFO, {
    name: "Recovered",
    description: "A device or agent is back online.",
    importance: Notifications.AndroidImportance.DEFAULT,
    bypassDnd: false,
  });
}

export type PermissionState = "granted" | "denied" | "undetermined";

export async function getPermissionState(): Promise<PermissionState> {
  const status = await Notifications.getPermissionsAsync();
  return status.granted ? "granted" : status.canAskAgain ? "undetermined" : "denied";
}

export async function requestPermission(): Promise<PermissionState> {
  const status = await Notifications.requestPermissionsAsync();
  return status.granted ? "granted" : status.canAskAgain ? "undetermined" : "denied";
}

export class PushSetupError extends Error {}

// Throws PushSetupError with a message fit to show the user.
export async function getExpoPushToken(): Promise<string> {
  if (!Device.isDevice) {
    throw new PushSetupError("Push notifications need a real phone, not an emulator.");
  }

  const projectId: string | undefined =
    Constants.expoConfig?.extra?.eas?.projectId ?? Constants.easConfig?.projectId;

  if (!projectId) {
    throw new PushSetupError(
      "This build has no EAS project id. Run `eas init` in Vivnest.Mobile and rebuild.",
    );
  }

  try {
    const token = await Notifications.getExpoPushTokenAsync({ projectId });
    return token.data;
  } catch (error) {
    // The usual cause on Android is a build made without Firebase's
    // google-services.json / an FCM key uploaded to EAS.
    const detail = error instanceof Error ? error.message : String(error);
    throw new PushSetupError(`Could not get a push token: ${detail}`);
  }
}

const ANDROID_PACKAGE = "com.vivnest.alerts";

// Where the user grants "Do Not Disturb access" - without it bypassDnd on
// the channels above does nothing.
export function openDoNotDisturbAccess(): Promise<void> {
  return openAndroidSettings("android.settings.NOTIFICATION_POLICY_ACCESS_SETTINGS");
}

// The per-channel page, where "Override Do Not Disturb" is switched on.
export function openChannelSettings(channelId: string = CHANNEL_FIRE_ALARM): Promise<void> {
  return openAndroidSettings("android.settings.CHANNEL_NOTIFICATION_SETTINGS", [
    { key: "android.provider.extra.APP_PACKAGE", value: ANDROID_PACKAGE },
    { key: "android.provider.extra.CHANNEL_ID", value: channelId },
  ]);
}

// Aggressive battery optimisation is the other common reason an alert
// arrives late or not at all.
export function openBatterySettings(): Promise<void> {
  return openAndroidSettings("android.settings.IGNORE_BATTERY_OPTIMIZATION_SETTINGS");
}

async function openAndroidSettings(
  action: string,
  extras?: { key: string; value: string }[],
): Promise<void> {
  if (Platform.OS !== "android") return;

  try {
    await Linking.sendIntent(action, extras);
  } catch {
    // Some OEM builds lack a specific page; the general settings screen is
    // the best remaining place to send the user.
    await Linking.openSettings();
  }
}
