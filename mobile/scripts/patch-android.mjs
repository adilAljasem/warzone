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

// 2. Patch MainActivity.java for immersive fullscreen, display cutout support, keep-screen-on, and native hardware telemetry bridge
const mainActivitySource = `package com.warzonee.app;

import android.app.ActivityManager;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.os.BatteryManager;
import android.os.Build;
import android.os.Bundle;
import android.os.Debug;
import android.os.PowerManager;
import android.os.Process;
import android.os.SystemClock;
import android.view.WindowManager;
import android.webkit.JavascriptInterface;
import android.webkit.WebView;
import androidx.core.view.WindowCompat;
import androidx.core.view.WindowInsetsCompat;
import androidx.core.view.WindowInsetsControllerCompat;
import com.getcapacitor.BridgeActivity;
import java.io.BufferedReader;
import java.io.FileReader;
import org.json.JSONObject;

public class MainActivity extends BridgeActivity {
    private long lastWallMs = 0L;
    private long lastCpuMs = 0L;
    private double smoothedCpuPct = -1.0;

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
        if (getBridge() != null && getBridge().getWebView() != null) {
            WebView webView = getBridge().getWebView();
            webView.addJavascriptInterface(new NativeTelemetryBridge(), "WarzoneNativeTelemetry");
        }
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

    private double sampleProcessCpuPct() {
        long nowWall = SystemClock.elapsedRealtime();
        long nowCpu = Process.getElapsedCpuTime();
        if (lastWallMs > 0L) {
            long dWall = nowWall - lastWallMs;
            long dCpu = nowCpu - lastCpuMs;
            if (dWall >= 150L && dCpu >= 0L) {
                int cores = Math.max(1, Runtime.getRuntime().availableProcessors());
                double raw = Math.min(100.0, (dCpu * 100.0) / (dWall * (double) cores));
                smoothedCpuPct = smoothedCpuPct < 0.0 ? raw : (smoothedCpuPct * 0.65 + raw * 0.35);
                lastWallMs = nowWall;
                lastCpuMs = nowCpu;
            }
        } else {
            lastWallMs = nowWall;
            lastCpuMs = nowCpu;
        }
        return smoothedCpuPct;
    }

    private int sampleGpuBusyPct() {
        try (BufferedReader reader = new BufferedReader(new FileReader("/sys/class/kgsl/kgsl-3d0/gpubusy"))) {
            String line = reader.readLine();
            if (line != null) {
                String[] parts = line.trim().split("\\\\s+");
                if (parts.length >= 2) {
                    long busy = Long.parseLong(parts[0]);
                    long total = Long.parseLong(parts[1]);
                    if (total > 0L) return (int) Math.min(100L, Math.round((busy * 100.0) / total));
                }
            }
        } catch (Throwable ignored) { }
        try (BufferedReader reader = new BufferedReader(new FileReader("/sys/class/misc/mali0/device/utilization"))) {
            String line = reader.readLine();
            if (line != null) return Math.max(0, Math.min(100, Integer.parseInt(line.trim())));
        } catch (Throwable ignored) { }
        return -1;
    }

    private String thermalStatusLabel() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) return "";
        PowerManager pm = (PowerManager) getSystemService(Context.POWER_SERVICE);
        if (pm == null) return "";
        switch (pm.getCurrentThermalStatus()) {
            case PowerManager.THERMAL_STATUS_NONE: return "None";
            case PowerManager.THERMAL_STATUS_LIGHT: return "Light";
            case PowerManager.THERMAL_STATUS_MODERATE: return "Moderate";
            case PowerManager.THERMAL_STATUS_SEVERE: return "Severe";
            case PowerManager.THERMAL_STATUS_CRITICAL: return "Critical";
            case PowerManager.THERMAL_STATUS_EMERGENCY: return "Emergency";
            case PowerManager.THERMAL_STATUS_SHUTDOWN: return "Shutdown";
            default: return "";
        }
    }

    private final class NativeTelemetryBridge {
        @JavascriptInterface
        public String getMetrics() {
            JSONObject out = new JSONObject();
            try {
                out.put("cpuPct", sampleProcessCpuPct());
                out.put("gpuPct", sampleGpuBusyPct());

                Intent battery = registerReceiver(null, new IntentFilter(Intent.ACTION_BATTERY_CHANGED));
                if (battery != null) {
                    int tempTenths = battery.getIntExtra(BatteryManager.EXTRA_TEMPERATURE, -1);
                    if (tempTenths > 0) out.put("tempC", tempTenths / 10.0);
                    int level = battery.getIntExtra(BatteryManager.EXTRA_LEVEL, -1);
                    int scale = battery.getIntExtra(BatteryManager.EXTRA_SCALE, 100);
                    if (level >= 0 && scale > 0) out.put("batteryPct", (level * 100) / scale);
                    int status = battery.getIntExtra(BatteryManager.EXTRA_STATUS, -1);
                    out.put("charging", status == BatteryManager.BATTERY_STATUS_CHARGING || status == BatteryManager.BATTERY_STATUS_FULL);
                }

                out.put("thermalStatus", thermalStatusLabel());

                Debug.MemoryInfo mem = new Debug.MemoryInfo();
                Debug.getMemoryInfo(mem);
                out.put("appRamMb", mem.getTotalPss() / 1024);

                ActivityManager am = (ActivityManager) getSystemService(Context.ACTIVITY_SERVICE);
                if (am != null) {
                    ActivityManager.MemoryInfo mi = new ActivityManager.MemoryInfo();
                    am.getMemoryInfo(mi);
                    out.put("availRamMb", mi.availMem / (1024L * 1024L));
                }
            } catch (Throwable ignored) { }
            return out.toString();
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
