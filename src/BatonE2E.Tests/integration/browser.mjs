// 実ブラウザ(Chromium)で視聴ページを開き、パスワード入力後に再生位置が進むことを確認する。
// 使い方: node browser.mjs <baseUrl> <password> <expectedTitle>
import { chromium } from 'playwright';

const [base, pw, title] = process.argv.slice(2);
const browser = await chromium.launch({ headless: true, args: ['--autoplay-policy=no-user-gesture-required'] });
const page = await browser.newPage();
const logs = [];
page.on('requestfailed', (r) => logs.push('requestfailed: ' + r.url()));
page.on('websocket', (w) => { logs.push('ws open: ' + w.url()); w.on('framereceived', (f) => logs.push('ws frame ' + (f.payload.length ?? '?'))); w.on('close', () => logs.push('ws close')); });
page.on('console', (m) => logs.push('console: ' + m.text()));
page.on('pageerror', (e) => logs.push('pageerror: ' + e.message));
let failed = false;
// 視聴ページのCSP(unsafe-evalなし)のため waitForFunction は使えない。evaluateで繰り返し確認する。
async function until(fn, timeoutMs) {
  const end = Date.now() + timeoutMs;
  while (Date.now() < end) { if (await page.evaluate(fn)) return true; await page.waitForTimeout(250); }
  return false;
}
const check = (name, ok) => { console.log(`${ok ? 'OK  ' : 'NG  '}${name}`); if (!ok) failed = true; };

try {
  await page.goto(base);
  check('視聴ページの題名が設定どおり（アプリ名なし）', (await page.title()) === title && !(await page.content()).includes('BatonE2E'));
  check('ブラウザがMediaSource(audio/mpeg)に対応', await page.evaluate(() => !!(window.MediaSource && MediaSource.isTypeSupported('audio/mpeg'))));

  // 誤パスワード
  await page.fill('#pw', 'definitely-wrong-password');
  await page.click('#go');
  const wrong = await until(() => document.getElementById('err').textContent.includes('パスワードが違います'), 30000);
  check('誤パスワードで「パスワードが違います」を表示', wrong);
  if (!wrong) console.log('  診断: ' + JSON.stringify(await page.evaluate(() => ({ status: document.getElementById('status').textContent, err: document.getElementById('err').textContent, ready: document.readyState }))));

  // 正しいパスワード → 再生位置が進む
  await page.reload();
  await page.fill('#pw', pw);
  await page.click('#go');
  const played = await until(() => document.getElementById('a').currentTime > 1.5, 45000);
  const st = await page.evaluate(() => ({ t: document.getElementById('a').currentTime, status: document.getElementById('status').textContent, err: document.getElementById('err').textContent }));
  check(`正しいパスワードで再生位置が進む（currentTime=${st.t.toFixed(2)}秒）`, played);
  check('状態表示が「受信・再生中」', st.status.includes('受信・再生中'));
  check('エラー表示なし', st.err === '');
} catch (e) {
  console.log('例外: ' + e.message); failed = true;
}
if (failed) console.log(logs.join('\n'));
await browser.close();
process.exit(failed ? 1 : 0);
