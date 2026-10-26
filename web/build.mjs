// Static build for Hostinger: bundles src/main.tsx with esbuild and compiles Tailwind v4 with its Node API.
// Output: dist/ (index.html, assets, .htaccess). No server runtime is needed.
import * as esbuild from "esbuild";
import { compile } from "tailwindcss";
import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";

const require = createRequire(import.meta.url);
const root = path.dirname(new URL(import.meta.url).pathname);
const src = path.join(root, "src");
const out = path.join(root, "dist");
const watch = process.argv.includes("--watch");

function walk(dir) {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((d) =>
    d.isDirectory() ? walk(path.join(dir, d.name)) : [path.join(dir, d.name)]);
}

async function buildCss() {
  const input = fs.readFileSync(path.join(src, "styles.css"), "utf8");
  const compiler = await compile(input, {
    base: src,
    async loadStylesheet(id, base) {
      const file = id === "tailwindcss" ? require.resolve("tailwindcss/index.css") : path.resolve(base, id);
      return { path: file, base: path.dirname(file), content: fs.readFileSync(file, "utf8") };
    },
  });
  // Candidate extraction: every class-like token in the sources; Tailwind ignores tokens that are not utilities.
  const candidates = new Set();
  for (const f of walk(src).filter((f) => /\.(tsx?|html)$/.test(f))) {
    for (const t of fs.readFileSync(f, "utf8").split(/[\s"'`{}<>;]+/)) if (t && t.length < 120) candidates.add(t);
  }
  const css = compiler.build([...candidates]);
  const min = await esbuild.transform(css, { loader: "css", minify: true });
  return min.code;
}

async function build() {
  const t0 = Date.now();
  fs.rmSync(out, { recursive: true, force: true });
  fs.mkdirSync(path.join(out, "assets"), { recursive: true });
  fs.cpSync(path.join(root, "public"), out, { recursive: true });

  const js = await esbuild.build({
    entryPoints: [path.join(src, "main.tsx")],
    bundle: true,
    minify: true,
    format: "esm",
    target: ["es2020", "safari15"],
    jsx: "automatic",
    alias: { "@": src },
    define: { "process.env.NODE_ENV": '"production"' },
    write: false,
    legalComments: "linked",
    outdir: path.join(out, "assets"),
  });
  const css = await buildCss();
  const hash = (s) => crypto.createHash("sha256").update(s).digest("hex").slice(0, 10);
  const jsFile = js.outputFiles.find((f) => f.path.endsWith(".js"));
  const jsName = `app.${hash(jsFile.text)}.js`;
  const cssName = `app.${hash(css)}.css`;
  fs.writeFileSync(path.join(out, "assets", jsName), jsFile.text);
  for (const f of js.outputFiles.filter((f) => !f.path.endsWith(".js"))) fs.writeFileSync(path.join(out, "assets", path.basename(f.path)), f.contents);
  fs.writeFileSync(path.join(out, "assets", cssName), css);
  const html = fs.readFileSync(path.join(src, "index.html"), "utf8")
    .replace("%CSS%", `/assets/${cssName}`)
    .replace("%JS%", `/assets/${jsName}`);
  fs.writeFileSync(path.join(out, "index.html"), html);
  fs.writeFileSync(path.join(out, "404.html"), html);
  console.log(`built in ${Date.now() - t0} ms → dist/ (js ${(jsFile.text.length / 1024).toFixed(0)} KB, css ${(css.length / 1024).toFixed(0)} KB)`);
}

await build();
if (watch) {
  console.log("watching src/ …");
  let timer;
  fs.watch(src, { recursive: true }, () => { clearTimeout(timer); timer = setTimeout(() => build().catch(console.error), 150); });
}
