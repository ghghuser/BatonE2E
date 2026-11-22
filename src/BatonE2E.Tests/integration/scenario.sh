#!/usr/bin/env bash
# 1シナリオ: 視聴者(receive.mjs)を先に接続 → 送信 → 受信した音声が元のMP3とバイト単位で一致するか確認。
# 使い方: scenario.sh <名前>    環境変数: SRC_PORT VIEW_PORT SRC_PASS VIEW_PASS TEST_MP3 CERT_DIR WORK
set -euo pipefail
name="$1"; here="$(cd "$(dirname "$0")" && pwd)"
out="$WORK/$name.bin"; log="$WORK/$name.recv.log"
rm -f "$out" "$log"

node "$here/receive.mjs" "ws://127.0.0.1:$VIEW_PORT/live-abc/ws" "$VIEW_PASS" "$out" 3000 60000 >"$log" 2>&1 &
rpid=$!
for _ in $(seq 1 100); do grep -q READY "$log" 2>/dev/null && break; sleep 0.2; done
grep -q READY "$log" || { echo "::error::[$name] 視聴者側で確認ブロックの検証に失敗"; cat "$log"; exit 1; }

case "$name" in
  curl-put)      curl -sS -m 30 -T "$TEST_MP3" -H "Content-Type: audio/mpeg" -u "source:$SRC_PASS" "http://127.0.0.1:$SRC_PORT/stream" ;;
  curl-chunked)  cat "$TEST_MP3" | curl -sS -m 30 -T - -H "Content-Type: audio/mpeg" -u "source:$SRC_PASS" "http://127.0.0.1:$SRC_PORT/stream" ;;
  ffmpeg-icecast) ffmpeg -loglevel error -re -i "$TEST_MP3" -c copy -f mp3 -write_xing 0 -id3v2_version 0 \
                    -content_type audio/mpeg "icecast://source:$SRC_PASS@127.0.0.1:$SRC_PORT/stream" ;;
  raw-source10)  node "$here/send-raw.mjs" source10 "$SRC_PORT" "$TEST_MP3" "$SRC_PASS" ;;
  raw-ice4)      node "$here/send-raw.mjs" ice4 "$SRC_PORT" "$TEST_MP3" "$SRC_PASS" ;;
  raw-ice3)      node "$here/send-raw.mjs" ice3 "$SRC_PORT" "$TEST_MP3" "$SRC_PASS" ;;
  tls12)         node "$here/send-raw.mjs" tls12 "$SRC_PORT" "$TEST_MP3" "$SRC_PASS" "$CERT_DIR/source.cer" ;;
  *) echo "unknown scenario: $name"; exit 2 ;;
esac

wait "$rpid" || { echo "::error::[$name] 受信側が異常終了"; cat "$log"; exit 1; }
tail -n1 "$log"
if cmp -s "$out" "$TEST_MP3"; then
  echo "OK  [$name] 受信・復号した音声が元のMP3と一致（$(stat -c %s "$out") バイト）"
else
  echo "::error::[$name] 受信した音声が元のMP3と一致しない（元 $(stat -c %s "$TEST_MP3") / 受信 $(stat -c %s "$out") バイト）"
  exit 1
fi
