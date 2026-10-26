import { StatusBar } from "expo-status-bar";
import * as Notifications from "expo-notifications";
import { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator,
  Alert,
  Platform,
  Pressable,
  SafeAreaView,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from "react-native";

import { registerPhone, sendTestAlert, unregisterPhone, type RegisteredPhone } from "./src/api";
import {
  CHANNEL_FIRE_ALARM,
  configureForegroundBehaviour,
  ensureChannels,
  getExpoPushToken,
  getPermissionState,
  openBatterySettings,
  openChannelSettings,
  openDoNotDisturbAccess,
  requestPermission,
  type PermissionState,
} from "./src/notifications";
import {
  DEFAULT_API_URL,
  clearSettings,
  loadSettings,
  saveSettings,
  type Settings,
} from "./src/storage";

configureForegroundBehaviour();

interface ReceivedAlert {
  id: string;
  title: string;
  body: string;
  at: Date;
}

function toAlert(notification: Notifications.Notification): ReceivedAlert {
  return {
    id: notification.request.identifier,
    title: notification.request.content.title ?? "Alert",
    body: notification.request.content.body ?? "",
    at: new Date(notification.date),
  };
}

export default function App() {
  const [loading, setLoading] = useState(true);
  const [settings, setSettings] = useState<Settings | null>(null);

  useEffect(() => {
    loadSettings()
      .then(setSettings)
      .finally(() => setLoading(false));
  }, []);

  return (
    <SafeAreaView style={styles.screen}>
      <StatusBar style="light" />
      <ScrollView contentContainerStyle={styles.content}>
        <Text style={styles.title}>Vivnest Alerts</Text>
        {loading ? (
          <ActivityIndicator color="#fff" />
        ) : settings ? (
          <Home
            settings={settings}
            onSignOut={async () => {
              await clearSettings();
              setSettings(null);
            }}
          />
        ) : (
          <Setup onSaved={setSettings} />
        )}
      </ScrollView>
    </SafeAreaView>
  );
}

function Setup({ onSaved }: { onSaved: (settings: Settings) => void }) {
  const [apiUrl, setApiUrl] = useState(DEFAULT_API_URL);
  const [apiKey, setApiKey] = useState("");
  const [phoneName, setPhoneName] = useState("My phone");

  async function save() {
    const trimmedKey = apiKey.trim();

    if (!trimmedKey) {
      Alert.alert("API key needed", "Paste the Vivnest API key.");
      return;
    }

    const next: Settings = {
      apiUrl: apiUrl.trim() || DEFAULT_API_URL,
      apiKey: trimmedKey,
      phoneName: phoneName.trim() || "Phone",
    };

    await saveSettings(next);
    onSaved(next);
  }

  return (
    <View style={styles.card}>
      <Text style={styles.label}>Server address</Text>
      <TextInput
        style={styles.input}
        value={apiUrl}
        onChangeText={setApiUrl}
        autoCapitalize="none"
        autoCorrect={false}
        keyboardType="url"
      />
      <Text style={styles.label}>API key</Text>
      <TextInput
        style={styles.input}
        value={apiKey}
        onChangeText={setApiKey}
        autoCapitalize="none"
        autoCorrect={false}
        secureTextEntry
        placeholder="Paste your Vivnest API key"
        placeholderTextColor="#6b7280"
      />
      <Text style={styles.label}>Name for this phone</Text>
      <TextInput style={styles.input} value={phoneName} onChangeText={setPhoneName} />
      <Button label="Continue" onPress={save} />
    </View>
  );
}

type Registration =
  | { state: "working" }
  | { state: "ready"; phone: RegisteredPhone }
  | { state: "failed"; message: string };

function Home({ settings, onSignOut }: { settings: Settings; onSignOut: () => void }) {
  const [permission, setPermission] = useState<PermissionState>("undetermined");
  const [registration, setRegistration] = useState<Registration>({ state: "working" });
  const [alerts, setAlerts] = useState<ReceivedAlert[]>([]);
  const [testing, setTesting] = useState(false);

  const register = useCallback(async () => {
    setRegistration({ state: "working" });

    try {
      await ensureChannels();

      let current = await getPermissionState();
      if (current !== "granted") current = await requestPermission();
      setPermission(current);

      if (current !== "granted") {
        setRegistration({
          state: "failed",
          message: "Notifications are switched off for this app. Turn them on in Android settings.",
        });
        return;
      }

      const token = await getExpoPushToken();
      const phone = await registerPhone(settings, token, Platform.OS);
      setRegistration({ state: "ready", phone });
    } catch (error) {
      setRegistration({
        state: "failed",
        message: error instanceof Error ? error.message : String(error),
      });
    }
  }, [settings]);

  // Registers on every launch, which also refreshes the server's LastSeen
  // and picks up a rotated token.
  useEffect(() => {
    void register();
  }, [register]);

  useEffect(() => {
    void Notifications.getPresentedNotificationsAsync().then((presented) =>
      setAlerts(presented.map(toAlert)),
    );

    const subscription = Notifications.addNotificationReceivedListener((notification) =>
      setAlerts((previous) => [toAlert(notification), ...previous].slice(0, 30)),
    );

    return () => subscription.remove();
  }, []);

  async function test() {
    setTesting(true);

    try {
      const result = await sendTestAlert(settings, CHANNEL_FIRE_ALARM);

      if (result.phones === 0) {
        Alert.alert("No phones registered", "This phone is not registered yet.");
      } else if (result.accepted === 0) {
        Alert.alert(
          "Not delivered",
          "The server could not hand the alert to Expo. Check the Cloud logs.",
        );
      } else {
        Alert.alert(
          "Test sent",
          "Lock the phone and switch Do Not Disturb on first - a real fire alarm has to ring through it.",
        );
      }
    } catch (error) {
      Alert.alert("Test failed", error instanceof Error ? error.message : String(error));
    } finally {
      setTesting(false);
    }
  }

  async function signOut() {
    if (registration.state === "ready") {
      // Best effort: a phone that cannot reach the server can still sign out.
      await unregisterPhone(settings, registration.phone.pushDeviceId).catch(() => undefined);
    }

    onSignOut();
  }

  return (
    <>
      <View style={styles.card}>
        <Text style={styles.label}>Status</Text>
        {registration.state === "working" && <ActivityIndicator color="#fff" />}
        {registration.state === "ready" && (
          <Text style={styles.ok}>Registered as "{registration.phone.name}"</Text>
        )}
        {registration.state === "failed" && (
          <>
            <Text style={styles.error}>{registration.message}</Text>
            <Button label="Try again" onPress={() => void register()} />
          </>
        )}
        <Text style={styles.muted}>Notifications: {permission}</Text>
      </View>

      <View style={styles.card}>
        <Text style={styles.heading}>Make alerts ring through Do Not Disturb</Text>
        <Text style={styles.muted}>
          Android only lets an alert override silent mode after you allow it. Do all three, then
          send a test with Do Not Disturb on.
        </Text>
        <Button label="1. Allow Do Not Disturb access" onPress={() => void openDoNotDisturbAccess()} />
        <Button label="2. Override Do Not Disturb for fire alarms" onPress={() => void openChannelSettings()} />
        <Button label="3. Stop battery limits on this app" onPress={() => void openBatterySettings()} />
        <Button
          label={testing ? "Sending..." : "Send a test fire alarm"}
          onPress={test}
          disabled={testing || registration.state !== "ready"}
          danger
        />
      </View>

      <View style={styles.card}>
        <Text style={styles.heading}>Recent alerts</Text>
        {alerts.length === 0 ? (
          <Text style={styles.muted}>Nothing yet.</Text>
        ) : (
          alerts.map((alert) => (
            <View key={alert.id + alert.at.getTime()} style={styles.alertRow}>
              <Text style={styles.alertTitle}>{alert.title}</Text>
              <Text style={styles.muted}>{alert.body}</Text>
              <Text style={styles.time}>{alert.at.toLocaleString()}</Text>
            </View>
          ))
        )}
      </View>

      <Button label="Sign out and unregister this phone" onPress={signOut} />
    </>
  );
}

function Button(props: { label: string; onPress: () => void; disabled?: boolean; danger?: boolean }) {
  return (
    <Pressable
      onPress={props.onPress}
      disabled={props.disabled}
      style={({ pressed }) => [
        styles.button,
        props.danger && styles.buttonDanger,
        props.disabled && styles.buttonDisabled,
        pressed && styles.buttonPressed,
      ]}
    >
      <Text style={styles.buttonText}>{props.label}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: "#0b1220" },
  content: { padding: 16, paddingTop: 48, gap: 16 },
  title: { color: "#fff", fontSize: 26, fontWeight: "700" },
  card: { backgroundColor: "#151e33", borderRadius: 12, padding: 16, gap: 10 },
  heading: { color: "#fff", fontSize: 16, fontWeight: "600" },
  label: { color: "#9ca3af", fontSize: 13 },
  muted: { color: "#9ca3af", fontSize: 13 },
  ok: { color: "#34d399", fontSize: 15, fontWeight: "600" },
  error: { color: "#f87171", fontSize: 14 },
  input: {
    backgroundColor: "#0b1220",
    color: "#fff",
    borderRadius: 8,
    padding: 12,
    borderWidth: 1,
    borderColor: "#27334f",
  },
  button: { backgroundColor: "#2563eb", borderRadius: 10, padding: 14, alignItems: "center" },
  buttonDanger: { backgroundColor: "#dc2626" },
  buttonDisabled: { opacity: 0.45 },
  buttonPressed: { opacity: 0.8 },
  buttonText: { color: "#fff", fontWeight: "600", fontSize: 15, textAlign: "center" },
  alertRow: { borderTopWidth: 1, borderTopColor: "#27334f", paddingTop: 10, gap: 2 },
  alertTitle: { color: "#fff", fontWeight: "600" },
  time: { color: "#6b7280", fontSize: 12 },
});
