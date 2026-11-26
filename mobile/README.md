# Warzonee Mobile Wrapper (Android APK)

This folder wraps the live `https://warzonee.com` client using **Capacitor** so that:
- The Android app launches **locked in Landscape (`sensorLandscape`)**, **Immersive Fullscreen**, with **Keep Screen On**.
- Every update deployed to `warzonee.com` appears **immediately** inside the app without users redownloading the APK.
- When you are ready for Google Play or App Store later, this same Capacitor project can switch from `server.url` to bundling `../frontend/dist`.

## Building `warzonee.apk` (No Android Studio Needed!)

1. Push to `main` (or open **GitHub → Actions → Build Warzonee Android APK → Run workflow**).
2. Download the **`warzonee-android-apk`** artifact (`warzonee.apk`).
3. Place `warzonee.apk` at `frontend/public/downloads/warzonee.apk` and push — the site will automatically detect it and display the **"تحميل تطبيق الأندرويد (APK)"** button to Android visitors on the welcome screen!

## Building Locally (If Android Studio + JDK 17 Are Installed)

```bash
cd mobile
npm install
npx cap add android
npm run cap:sync
cd android
./gradlew assembleDebug
# Output: android/app/build/outputs/apk/debug/app-debug.apk
```
