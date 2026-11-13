const { chromium } = require('playwright');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1400, height: 900 }, deviceScaleFactor: 2 });
  p.on('pageerror', e => console.log('ERR', e.message));
  await p.goto('file://' + __dirname + '/mock.html');
  await p.waitForFunction(() => document.body.dataset.ready === '1', null, { timeout: 60000 });
  for (const id of ['s1', 's2', 's3']) await p.locator('#' + id).screenshot({ path: __dirname + '/' + id + '.jpg', type: 'jpeg', quality: 88 });
  await b.close(); console.log('ok');
})();
