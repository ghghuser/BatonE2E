// 生ソケットでsource接続を再現する（oddcastなどの送信形式）。
// 使い方: node send-raw.mjs <mode> <port> <file> <sourcePassword> [cerPath]
//   mode: source10 = SOURCE /stream HTTP/1.0 + Basic
//         ice4     = SOURCE <pass> /stream ICE/1.0（旧形式）
//         ice3     = SOURCE /stream ICE/1.0 + Basic（oddcastで確認した形式）
//         tls12    = TLS 1.2限定 + ホスト名(SAN)検証 + 指紋固定 の上で SOURCE /stream HTTP/1.0
import net from 'node:net';
import tls from 'node:tls';
import { readFileSync } from 'node:fs';
import { X509Certificate } from 'node:crypto';

const [mode, portArg, file, pass, cerPath] = process.argv.slice(2);
const port = +portArg, body = readFileSync(file);
const basic = 'Basic ' + Buffer.from('source:' + pass).toString('base64');
const heads = {
  source10: `SOURCE /stream HTTP/1.0\r\nAuthorization: ${basic}\r\nContent-Type: audio/mpeg\r\n\r\n`,
  ice4: `SOURCE ${pass} /stream ICE/1.0\r\ncontent-type: audio/mpeg\r\nice-name: test\r\n\r\n`,
  ice3: `SOURCE /stream ICE/1.0\r\ncontent-type: audio/mpeg\r\nAuthorization: ${basic}\r\nice-name: test\r\n\r\n`,
  tls12: `SOURCE /stream HTTP/1.0\r\nAuthorization: ${basic}\r\nContent-Type: audio/mpeg\r\n\r\n`,
};
if (!heads[mode]) { console.error('unknown mode'); process.exit(2); }

function run(sock) {
  let resp = '', sent = false;
  sock.on('data', (d) => {
    resp += d.toString('latin1');
    if (!sent && resp.includes('\r\n\r\n')) {
      sent = true;
      const line = resp.split('\r\n')[0];
      console.log('RESPONSE: ' + line);
      if (!/ 200/.test(line)) process.exit(1);
      sock.write(body, () => setTimeout(() => sock.end(), 300));
    }
  });
  sock.on('close', () => process.exit(sent ? 0 : 1));
  sock.on('error', (e) => { console.error('socket error: ' + e.message); process.exit(1); });
  sock.write(heads[mode]);
}

if (mode === 'tls12') {
  const pinned = new X509Certificate(readFileSync(cerPath)).fingerprint256;
  const s = tls.connect({ host: '127.0.0.1', port, minVersion: 'TLSv1.2', maxVersion: 'TLSv1.2', rejectUnauthorized: false });
  s.on('secureConnect', () => {
    const peer = s.getPeerCertificate();
    const errs = [];
    if (s.getProtocol() !== 'TLSv1.2') errs.push('protocol=' + s.getProtocol());
    if (peer.fingerprint256 !== pinned) errs.push('fingerprint mismatch');
    for (const h of ['127.0.0.1', 'localhost']) { const e = tls.checkServerIdentity(h, peer); if (e) errs.push('SAN ' + h + ': ' + e.message); }
    if (errs.length) { console.error('TLS check failed: ' + errs.join('; ')); process.exit(1); }
    console.log('TLS OK: ' + s.getProtocol() + ' SAN(127.0.0.1,localhost) fingerprint pinned');
    run(s);
  });
  s.on('error', (e) => { console.error('tls error: ' + e.message); process.exit(1); });
} else {
  run(net.connect(port, '127.0.0.1'));
}
