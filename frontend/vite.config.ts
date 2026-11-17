import { execSync } from "node:child_process";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";

const rootDir = fileURLToPath(new URL(".", import.meta.url));

function resolveGitVersion(): string {
  if (process.env.VITE_APP_VERSION?.trim()) {
    return process.env.VITE_APP_VERSION.trim();
  }
  try {
    const count = execSync("git rev-list --count HEAD", { encoding: "utf8" }).trim();
    const sha = execSync("git rev-parse --short=7 HEAD", { encoding: "utf8" }).trim();
    const dirty = execSync("git status --porcelain", { encoding: "utf8" }).trim().length > 0 ? "-dirty" : "";
    return `v1.0.${count}-${sha}${dirty}`;
  } catch {
    return "v1.0.0-local";
  }
}

export default defineConfig({
  define: {
    "import.meta.env.VITE_APP_VERSION": JSON.stringify(resolveGitVersion()),
  },
  build: {
    rollupOptions: {
      input: {
        main: resolve(rootDir, "index.html"),
        en: resolve(rootDir, "en/index.html"),
        ar: resolve(rootDir, "ar/index.html"),
      },
    },
  },
});
