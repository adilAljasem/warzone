import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const mobileDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const androidDir = path.join(mobileDir, "android");
const manifestPath = path.join(androidDir, "app/src/main/AndroidManifest.xml");
const mainActivityPath = path.join(androidDir, "app/src/main/java/com/warzonee/app/MainActivity.java");
const stylesPath = path.join(androidDir, "app/src/main/res/values/styles.xml");

if (!fs.existsSync(manifestPath)) {
  console.error("Android project not found. Run `npx cap add android` first.");
  process.exit(1);
}

// 1. Force sensorLandscape on MainActivity in AndroidManifest.xml
let manifest = fs.readFileSync(manifestPath, "utf8");
if (!manifest.includes("android:screenOrientation=")) {
  manifest = manifest.replace(
    /(<activity[\s\S]*?android:name="[^"]*MainActivity")/,
    '$1\n            android:screenOrientation="sensorLandscape"'
  );
} else {
  manifest = manifest.replace(
    /android:screenOrientation="[^"]*"/,
    'android:screenOrientation="sensorLandscape"'
  );
}
fs.writeFileSync(manifestPath, manifest, "utf8");
console.log("Patched AndroidManifest.xml -> sensorLandscape");

// 2. Patch MainActivity.java for immersive fullscreen, display cutout support, and keep-screen-on
const mainActivitySource = `package com.warzonee.app;

import android.os.Build;
import android.os.Bundle;
import android.view.WindowManager;
import androidx.core.view.WindowCompat;
import androidx.core.view.WindowInsetsCompat;
import androidx.core.view.WindowInsetsControllerCompat;
import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            WindowManager.LayoutParams lp = getWindow().getAttributes();
            lp.layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES;
            getWindow().setAttributes(lp);
        }
        applyImmersiveFullscreen();
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) {
            applyImmersiveFullscreen();
        }
    }

    private void applyImmersiveFullscreen() {
        WindowCompat.setDecorFitsSystemWindows(getWindow(), false);
        WindowInsetsControllerCompat controller = WindowCompat.getInsetsController(getWindow(), getWindow().getDecorView());
        if (controller != null) {
            controller.hide(WindowInsetsCompat.Type.systemBars());
            controller.setSystemBarsBehavior(WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
        }
    }
}
`;
fs.mkdirSync(path.dirname(mainActivityPath), { recursive: true });
fs.writeFileSync(mainActivityPath, mainActivitySource, "utf8");
console.log("Patched MainActivity.java -> immersive fullscreen + cutout + keep-screen-on");

// 3. Ensure dark launch background in styles.xml so there is no white flash on startup
if (fs.existsSync(stylesPath)) {
  let styles = fs.readFileSync(stylesPath, "utf8");
  if (!styles.includes("android:windowFullscreen")) {
    styles = styles.replace(
      /(<style name="AppTheme\.NoActionBar"[^>]*>)/,
      '$1\n        <item name="android:windowFullscreen">true</item>\n        <item name="android:windowBackground">#111e1a</item>'
    );
    fs.writeFileSync(stylesPath, styles, "utf8");
    console.log("Patched styles.xml -> fullscreen dark background");
  }
}

// 4. Copy Warzonee icon into Android mipmap resources if available
const iconSrc = path.resolve(mobileDir, "../frontend/public/icon-512.png");
if (fs.existsSync(iconSrc)) {
  const resDir = path.join(androidDir, "app/src/main/res");
  for (const dir of fs.readdirSync(resDir)) {
    if (!dir.startsWith("mipmap-")) continue;
    const mipmapDir = path.join(resDir, dir);
    for (const file of ["ic_launcher.png", "ic_launcher_round.png", "ic_launcher_foreground.png"]) {
      const target = path.join(mipmapDir, file);
      if (fs.existsSync(target)) {
        fs.copyFileSync(iconSrc, target);
      }
    }
  }
  console.log("Copied Warzonee icon into Android mipmap directories");
}
