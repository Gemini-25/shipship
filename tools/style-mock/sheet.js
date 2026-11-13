const { chromium } = require('playwright');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 2000, height: 1400 }, deviceScaleFactor: 1 });
  await p.goto('file://' + __dirname + '/sheet.html'); await p.evaluate(() => document.fonts.ready); await p.waitForTimeout(500);
  await p.locator('#sheet').screenshot({ path: __dirname + '/style_sheet.jpg', type: 'jpeg', quality: 86 });
  for (const i of [1, 2, 3]) await p.locator('#a' + i).screenshot({ path: __dirname + '/style_' + i + '.jpg', type: 'jpeg', quality: 86 });
  await b.close(); console.log('ok');
})();
