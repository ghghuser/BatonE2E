// C#が出力した試験データを、WebCrypto（ブラウザと同じAPI）で検証する。
// 使い方: node verify.mjs <vectors.json>
import { readFileSync } from 'node:fs';
import { webcrypto } from 'node:crypto';

const subtle = webcrypto.subtle;
const enc = new TextEncoder();
const b64 = (s) => new Uint8Array(Buffer.from(s, 'base64'));
const eq = (a, b) => a.length === b.length && a.every((x, i) => x === b[i]);

let failed = 0;
const check = (name, ok) => { console.log(`${ok ? 'OK  ' : 'NG  '}${name}`); if (!ok) failed++; };

const doc = JSON.parse(readFileSync(process.argv[2], 'utf8'));
const h = doc.header;
const salt = b64(h.salt), sid = b64(h.sid);

async function deriveKey(password) {
  const km = await subtle.importKey('raw', enc.encode(password.normalize('NFC')), 'PBKDF2', false, ['deriveKey']);
  return subtle.deriveKey({ name: 'PBKDF2', salt, iterations: h.iter, hash: 'SHA-256' }, km,
    { name: 'AES-GCM', length: 256 }, true, ['encrypt', 'decrypt']);
}
const prefix = enc.encode('E2ERv1');
const concat = (...p) => { const o = new Uint8Array(p.reduce((n, x) => n + x.length, 0)); let i = 0; for (const x of p) { o.set(x, i); i += x.length; } return o; };
const params = (frame) => {
  const seq = frame.slice(0, 8);
  return { name: 'AES-GCM', iv: concat(sid, seq), additionalData: concat(prefix, sid, seq), tagLength: 128 };
};
// フレーム = 連番8 + 暗号文 + タグ16（WebCryptoの「暗号文+タグ」と同じ並び）
const open = async (key, frame) => new Uint8Array(await subtle.decrypt(params(frame), key, frame.slice(8)));
const seal = async (key, frame, plain) => new Uint8Array(await subtle.encrypt(params(frame), key, plain));

const key = await deriveKey(doc.password);
check('ヘッダー: 仕様v1・反復600000', h.v === 1 && h.kdf === 'PBKDF2-SHA256' && h.iter === 600000);

// 確認ブロック
const confirm = b64(doc.confirm);
const cp = await open(key, confirm);
check('確認ブロック: 復号できる（先頭0x00 + E2ERADIO1）', cp[0] === 0 && Buffer.from(cp.slice(1)).toString() === 'E2ERADIO1');
check('確認ブロック: 連番0', new DataView(confirm.buffer).getBigUint64(0) === 0n);

// 誤パスワード
let wrongOk = false;
try { await open(await deriveKey(doc.password + 'x'), confirm); } catch { wrongOk = true; }
check('確認ブロック: 誤パスワードは復号失敗', wrongOk);

// 各フレーム: 復号一致、再暗号化で暗号文がバイト単位で一致（AES-GCMは同条件なら決定的）
let n = 1;
for (const f of doc.frames) {
  const frame = b64(f.frame), plain = b64(f.plain);
  check(`フレーム${n}: 連番が${n}`, new DataView(frame.buffer, frame.byteOffset).getBigUint64(0) === BigInt(n));
  const got = await open(key, frame);
  check(`フレーム${n}: 復号して種別と平文が一致`, got[0] === f.type && eq(got.slice(1), plain));
  const re = await seal(key, frame, concat(Uint8Array.of(f.type), plain));
  check(`フレーム${n}: 再暗号化がC#の出力と一致`, eq(re, frame.slice(8)));
  n++;
}

// 改ざん・AAD
const f0 = b64(doc.frames[0].frame);
const flip = f0.slice(); flip[20] ^= 1;
let t1 = false; try { await open(key, flip); } catch { t1 = true; }
check('改ざん: 暗号文のビット反転は復号失敗', t1);
const seqChg = f0.slice(); seqChg[7] ^= 0xff;
let t2 = false; try { await open(key, seqChg); } catch { t2 = true; }
check('改ざん: 連番の書き換えは復号失敗', t2);

console.log(failed === 0 ? '相互運用テスト成功' : `相互運用テスト失敗: ${failed}件`);
process.exit(failed === 0 ? 0 : 1);
