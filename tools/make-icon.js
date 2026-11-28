#!/usr/bin/env node
/*  Erzeugt assets/icon/profilekit.ico (16–256 px, PNG-Einträge) und profilekit-256.png
    aus assets/icon/profilekit.svg. Braucht Playwright mit Chromium.

      node tools/make-icon.js  */
'use strict';
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const dir = path.join(__dirname, '..', 'assets', 'icon');
const svg = fs.readFileSync(path.join(dir, 'profilekit.svg'), 'utf8');
const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ deviceScaleFactor: 1 });
  const pngs = [];
  for (const size of sizes) {
    await page.setViewportSize({ width: size, height: size });
    await page.setContent(`<html><body style="margin:0;background:transparent">
      <img src="data:image/svg+xml;base64,${Buffer.from(svg).toString('base64')}" width="${size}" height="${size}" style="display:block"></body></html>`);
    pngs.push(await page.screenshot({ omitBackground: true, clip: { x: 0, y: 0, width: size, height: size } }));
  }
  await browser.close();

  // ICO: Kopf (6 Byte), je Eintrag 16 Byte Verzeichnis, dann die PNG-Daten
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0); header.writeUInt16LE(1, 2); header.writeUInt16LE(sizes.length, 4);
  let offset = 6 + 16 * sizes.length;
  const entries = sizes.map((size, i) => {
    const e = Buffer.alloc(16);
    e.writeUInt8(size >= 256 ? 0 : size, 0); e.writeUInt8(size >= 256 ? 0 : size, 1);
    e.writeUInt8(0, 2); e.writeUInt8(0, 3); e.writeUInt16LE(1, 4); e.writeUInt16LE(32, 6);
    e.writeUInt32LE(pngs[i].length, 8); e.writeUInt32LE(offset, 12);
    offset += pngs[i].length;
    return e;
  });
  fs.writeFileSync(path.join(dir, 'profilekit.ico'), Buffer.concat([header, ...entries, ...pngs]));
  fs.writeFileSync(path.join(dir, 'profilekit-256.png'), pngs[sizes.indexOf(256)]);
  console.log('make-icon: profilekit.ico mit ' + sizes.join(', ') + ' px');
})().catch((e) => { console.error(e); process.exit(1); });
