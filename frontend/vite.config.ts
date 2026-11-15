import { execSync } from "node:child_process";
import { defineConfig } from "vite";

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
});
