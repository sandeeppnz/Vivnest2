import * as SecureStore from "expo-secure-store";

// The API key is a credential, so it lives in the platform keystore rather
// than AsyncStorage. SecureStore keys only allow [A-Za-z0-9._-].
const KEY_API_URL = "vivnest.apiUrl";
const KEY_API_KEY = "vivnest.apiKey";
const KEY_PHONE_NAME = "vivnest.phoneName";

export const DEFAULT_API_URL = "https://vivnestcloud2.azurewebsites.net/api";

export interface Settings {
  apiUrl: string;
  apiKey: string;
  phoneName: string;
}

export async function loadSettings(): Promise<Settings | null> {
  const apiKey = await SecureStore.getItemAsync(KEY_API_KEY);

  if (!apiKey) return null;

  return {
    apiKey,
    apiUrl: (await SecureStore.getItemAsync(KEY_API_URL)) ?? DEFAULT_API_URL,
    phoneName: (await SecureStore.getItemAsync(KEY_PHONE_NAME)) ?? "Phone",
  };
}

export async function saveSettings(settings: Settings): Promise<void> {
  await SecureStore.setItemAsync(KEY_API_URL, settings.apiUrl);
  await SecureStore.setItemAsync(KEY_API_KEY, settings.apiKey);
  await SecureStore.setItemAsync(KEY_PHONE_NAME, settings.phoneName);
}

export async function clearSettings(): Promise<void> {
  await SecureStore.deleteItemAsync(KEY_API_KEY);
  await SecureStore.deleteItemAsync(KEY_API_URL);
  await SecureStore.deleteItemAsync(KEY_PHONE_NAME);
}
