import * as esbuild from 'esbuild';

// Bundled rather than shipped with `node_modules`, so the extension is one file and the
// package holds what it needs and nothing else. `vscode` is provided by the editor.
const watching = process.argv.includes('--watch');

const options = {
  entryPoints: ['src/extension.ts'],
  bundle: true,
  outfile: 'dist/extension.js',
  platform: 'node',
  format: 'cjs',
  target: 'node18',
  external: ['vscode'],
  minify: !watching,
  sourcemap: watching,
};

if (watching) {
  const context = await esbuild.context(options);
  await context.watch();
} else {
  await esbuild.build(options);
}
