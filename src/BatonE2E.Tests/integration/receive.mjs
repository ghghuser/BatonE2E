// 視聴者の代わり: WebSocketで受信し、WebCryptoで復号して、音声をファイルへ書き出す（ブラウザと同じ手順）。
// 使い方: node receive.mjs <ws-url> <password> <outfile> [idleMs=3000] [maxMs=40000]
// 標準出力: 確認ブロックの検証後に "READY"、終了時に "RESULT ..." を1行出力。
import { writeFileSync } from 'node:fs';

const [url, pw, out, idleArg, maxArg] = process.argv.slice(2);
const idleMs = +(idleArg ?? 3000), maxMs = +(maxArg ?? 40000);
const subtle = globalThis.crypto.subtle;
const enc = new TextEncoder(), dec = new TextDecoder(), PFX = enc.encode('E2ERv1');
const b64 = (s) => new Uint8Array(Buffer.from(s, 'base64'));

let key, sid, confirmed = false, expect = null, last = 0, done = false;
const audio = [], types = { audio: 0, meta: 0, format: 0 }, problems = [];
let chain = Promise.resolve();

async function open(buf) {
  const u = new Uint8Array(buf), seq = u.subarray(0, 8);
  const iv = new Uint8Array(12); iv.set(sid, 0); iv.set(seq, 4);
  const aad = new Uint8Array(PFX.length + 12); aad.set(PFX, 0); aad.set(sid, PFX.length); aad.set(seq, PFX.length + 4);
  const pt = await subtle.decrypt({ name: 'AES-GCM', iv, additionalData: aad, tagLength: 128 }, key, u.subarray(8));
  return { seq: Number(new DataView(buf).getBigUint64(0)), data: new Uint8Array(pt) };
}

async function onMsg(d) {
  if (typeof d === 'string') {
    const h = JSON.parse(d);
    if (h.v !== 1 || h.kdf !== 'PBKDF2-SHA256') throw new Error('unsupported header');
    sid = b64(h.sid);
    const km = await subtle.importKey('raw', enc.encode(pw.normalize('NFC')), 'PBKDF2', false, ['deriveKey']);
    key = await subtle.deriveKey({ name: 'PBKDF2', salt: b64(h.salt), iterations: h.iter, hash: 'SHA-256' }, km,
      { name: 'AES-GCM', length: 256 }, false, ['decrypt']);
    return;
  }
  if (!confirmed) {
    const r = await open(d);
    if (r.seq !== 0 || r.data[0] !== 0 || dec.decode(r.data.subarray(1)) !== 'E2ERADIO1') throw new Error('confirm block mismatch');
    confirmed = true; console.log('READY'); return;
  }
  const r = await open(d);
  if (expect !== null && r.seq !== expect) throw new Error(`sequence gap: expected ${expect}, got ${r.seq}`);
  expect = r.seq + 1; last = Date.now();
  const t = r.data[0], p = r.data.subarray(1);
  if (t === 1) { audio.push(Buffer.from(p)); types.audio++; }
  else if (t === 2) types.meta++;
  else if (t === 3) types.format++;
  else throw new Error('unknown frame type ' + t);
}

function finish() {
  if (done) return; done = true;
  const all = Buffer.concat(audio);
  writeFileSync(out, all);
  const ok = problems.length === 0 && all.length > 0;
  console.log(`RESULT ${ok ? 'ok' : 'ng'} bytes=${all.length} audio=${types.audio} meta=${types.meta} format=${types.format}` +
    (problems.length ? ` problems=${problems.join(' | ')}` : ''));
  process.exit(ok ? 0 : 1);
}

const ws = new WebSocket(url); ws.binaryType = 'arraybuffer';
ws.onmessage = (ev) => { chain = chain.then(() => onMsg(ev.data)).catch((e) => { problems.push(e.message); finish(); }); };
ws.onerror = () => { problems.push('websocket error'); finish(); };
setInterval(() => { if (audio.length && Date.now() - last > idleMs) finish(); }, 200);
setTimeout(() => { problems.push('timeout'); finish(); }, maxMs);
