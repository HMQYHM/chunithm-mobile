const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

test('desktop and offline shells include every script and stylesheet used by the page', () => {
  const root = path.resolve(__dirname, '..');
  const html = fs.readFileSync(path.join(root, 'index.html'), 'utf8');
  const assets = [...html.matchAll(/(?:src|href)="([^"?#]+\.(?:js|css))"/g)].map(match => match[1]);
  const packaged = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8')).build.files;
  let cached;
  const listeners = {};
  vm.runInNewContext(fs.readFileSync(path.join(root, 'service-worker.js'), 'utf8'), {
    self: { addEventListener: (name, listener) => { listeners[name] = listener; } },
    caches: { open: async () => ({ addAll: files => { cached = [...files]; } }) }
  });
  let installed;
  listeners.install({ waitUntil: promise => { installed = promise; } });
  return installed.then(() => {
    for (const asset of assets) {
      assert(fs.existsSync(path.join(root, asset)), `Missing shell asset: ${asset}`);
      assert(packaged.includes(asset), `Desktop package omits ${asset}`);
      assert(cached.includes(asset), `Offline cache omits ${asset}`);
    }
  });
});
