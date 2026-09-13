import { readdirSync, readFileSync, writeFileSync, existsSync } from "node:fs";
import { join } from "node:path";

// Include license notices for every runtime dependency shipped in the static bundle.
const lock = JSON.parse(readFileSync("package-lock.json", "utf8"));
const notices = [];
for (const [path, entry] of Object.entries(lock.packages)) {
  if (!path || entry.dev) continue;
  const files = readdirSync(path).filter((name) =>
    /^(licen[cs]e|copying)(\.|$)/i.test(name),
  );
  for (const file of files)
    notices.push(
      `${entry.name || path.replace(/^node_modules\//, "")} ${entry.version}\n${readFileSync(join(path, file), "utf8")}`,
    );
}
writeFileSync(
  "dist/licenses/Dependencies.txt",
  notices.join("\n\n" + "—".repeat(60) + "\n\n"),
);
const names = readdirSync("dist/licenses").filter((name) =>
  name.endsWith(".txt"),
);
const links = names
  .map((name) => `<li><a href="./${name}">${name}</a></li>`)
  .join("");
writeFileSync(
  "dist/licenses/index.html",
  `<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Rewindle — Licenses</title><style>body{font:16px/1.7 system-ui;background:#f6f4ee;color:#101927;max-width:760px;margin:70px auto;padding:24px}a{color:#167c73}li{margin:12px 0}</style><a href="../">← Back to Rewindle</a><h1>Made with open source.</h1><p>Rewindle is MIT licensed. Its interface includes actual Beautiful UI components by Shane Levine, plus React, Motion, Lucide, and the other dependencies credited below.</p><ul>${links}</ul></html>`,
);
if (!existsSync("dist/.nojekyll")) writeFileSync("dist/.nojekyll", "");
