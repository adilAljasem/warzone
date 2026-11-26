import type { CapacitorConfig } from "@capacitor/cli";

const config: CapacitorConfig = {
  appId: "com.warzonee.app",
  appName: "Warzonee",
  webDir: "www",
  server: {
    // Loads the live website directly so every web deployment updates the Android app immediately.
    url: "https://warzonee.com",
    cleartext: false,
    androidScheme: "https",
    errorPath: "index.html",
  },
  android: {
    backgroundColor: "#111e1a",
    allowMixedContent: false,
    captureInput: true,
    webContentsDebuggingEnabled: false,
  },
};

export default config;
