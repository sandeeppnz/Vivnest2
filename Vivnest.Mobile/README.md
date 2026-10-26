# Vivnest Alerts (phone app)

A small React Native / Expo app that does one job: ring a phone when
Vivnest hears a fire alarm or an agent or device goes offline. It is not a
second dashboard. Design and limits: **ADR-129** in
[the decision log](../docs/architecture/decision-log.md).

Android only for now. Telegram keeps receiving everything; this is the
louder second channel for a short list of alerts.

| Alert | Android channel | Rings through Do Not Disturb |
|---|---|---|
| Fire alarm heard | `fire-alarm` | yes, once set up below |
| Agent or device offline | `alerts` | yes, once set up below |
| Agent or device back online | `info` | no |

## One-time setup

### 1. Firebase (Android push needs it)
1. Create a free project at <https://console.firebase.google.com>.
2. Add an **Android app** with package name `com.vivnest.alerts`.
3. Download `google-services.json` and put it in this folder. It is
   gitignored.
4. Project settings > **Service accounts** > *Generate new private key*.
   Keep that JSON somewhere outside the repo; EAS needs it in step 3.

### 2. Expo / EAS
```
npm install -g eas-cli
eas login
cd Vivnest.Mobile
npm install
eas init            # writes extra.eas.projectId into app.json - commit that
```

### 3. Give EAS the Firebase key
```
eas credentials -p android
```
Choose *production* or *preview* > **Google Service Account** > *FCM V1
service account key* > upload the key from step 1.4.

### 4. Build and install
```
eas build -p android --profile preview
```
When it finishes, open the link on the phone and install the APK (allow
"install unknown apps" for the browser if asked).

## Using it

1. Open the app. The server address is pre-filled
   (`https://vivnestcloud2.azurewebsites.net/api`); paste a Vivnest **API
   key** and name the phone. Any tenant key works; a `user`-role key is
   enough.
2. Allow notifications when asked. The app registers itself and says
   *Registered as "..."*.
3. Do the three buttons in order. They are what make an alert ring through
   silent mode - Android refuses to let an app do that until you allow it:
   - **Allow Do Not Disturb access** - grant it to Vivnest Alerts.
   - **Override Do Not Disturb for fire alarms** - the channel's own switch.
   - **Stop battery limits** - set the app to *Unrestricted* so delivery is
     not delayed.
4. Turn Do Not Disturb **on**, lock the phone and press **Send a test fire
   alarm**. It should ring. If it does not, step 3 is incomplete.

## Developing

```
npm install
npx tsc --noEmit        # typecheck
npx expo export --platform android --output-dir /tmp/x   # does it bundle?
npx expo-doctor
```

Push tokens do not work in Expo Go or an emulator; use the installed build.

## Things that will bite

- **Notification channels are create-once.** Android ignores later changes to
  a channel's importance or sound. Changing how loud a channel is means a new
  channel id (in `src/notifications.ts` and in `ExpoPushNotificationChannel`
  on the server).
- **`Could not get a push token`** almost always means the build has no
  `google-services.json`, or no FCM key was uploaded to EAS (steps 1 and 3).
- **Every registered phone gets every alert** - Cloud does not yet route by
  tenant (ADR-129).
- This reduces the chance of missing an alert. It does not make it certain,
  and a camera microphone is not a substitute for a smoke alarm.
