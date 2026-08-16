import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const tsRoot = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const outDir = path.join(tsRoot, "out");
const dest = path.join(tsRoot, "..", "Assets", "StreamingAssets", "ZTS");

function copyDir(src, dst) {
  fs.mkdirSync(dst, { recursive: true });
  for (const ent of fs.readdirSync(src, { withFileTypes: true })) {
    const from = path.join(src, ent.name);
    const to = path.join(dst, ent.name);
    if (ent.isDirectory()) {
      copyDir(from, to);
    } else {
      fs.copyFileSync(from, to);
    }
  }
}

if (!fs.existsSync(outDir)) {
  throw new Error(`missing emit dir: ${outDir}`);
}

copyDir(outDir, dest);
console.log(`copied ${outDir} -> ${dest}`);
