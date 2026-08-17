import * as esbuild from "esbuild";
import fs from "fs";
import path from "path";

function walk(dir, acc = []) {
  if (!fs.existsSync(dir)) {
    return acc;
  }
  for (const ent of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, ent.name);
    if (ent.isDirectory()) {
      walk(p, acc);
    } else if (ent.name.endsWith(".ts") && !ent.name.endsWith(".d.ts")) {
      acc.push(p);
    }
  }
  return acc;
}

function exportNamesFromSource(src) {
  const names = [];
  const reFn = /export\s+(?:async\s+)?function\s+([A-Za-z_$][\w$]*)/g;
  const reConst = /export\s+(?:const|let|var)\s+([A-Za-z_$][\w$]*)/g;
  let m;
  while ((m = reFn.exec(src))) names.push(m[1]);
  while ((m = reConst.exec(src))) names.push(m[1]);
  const reList = /export\s*\{([^}]+)\}/g;
  while ((m = reList.exec(src))) {
    for (const part of m[1].split(",")) {
      const bit = part.trim();
      if (!bit) continue;
      const as = bit.split(/\s+as\s+/);
      names.push((as[1] || as[0]).trim());
    }
  }
  return [...new Set(names)];
}

const srcDir = "src";
const entries = walk(srcDir);
if (entries.length === 0) {
  console.log("no .ts entries under src/");
  process.exit(0);
}

await esbuild.build({
  entryPoints: entries,
  outdir: "out",
  outbase: srcDir,
  format: "esm",
  sourcemap: true,
  target: "es2020",
  bundle: false,
  minify: false,
});

const manifest = {};
for (const file of entries) {
  const rel = path.relative(srcDir, file).replace(/\\/g, "/").replace(/\.ts$/, "");
  manifest[rel] = exportNamesFromSource(fs.readFileSync(file, "utf8"));
}

fs.mkdirSync("generated", { recursive: true });
fs.writeFileSync("generated/js-exports.json", JSON.stringify(manifest, null, 2) + "\n");
console.log(`emitted ${entries.length} module(s)`);
