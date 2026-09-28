#!/usr/bin/env bash
# Refresh the connected Unity Editor, wait for compilation, print compile errors (if any).
export UNITY_NO_PAGER=1
cd "$(dirname "$0")/../Ricochet" || exit 1

state() {
  unity command console_status --format json 2>/dev/null | python -c "
import sys,json
try:
    g=json.load(sys.stdin)['data']['result']['groundTruth']
    print('compiling' if g['compiling'] else ('failed' if g['compilationFailed'] else 'ok'))
except Exception:
    print('busy')"
}

unity command eval --code 'UnityEditor.AssetDatabase.Refresh(); return "refreshed";' >/dev/null 2>&1
sleep 3
for i in $(seq 1 150); do
  s=$(state)
  [ "$s" = "ok" ] || [ "$s" = "failed" ] && break
  sleep 2
done

if [ "$s" = "failed" ]; then
  echo "COMPILE FAILED:"
  unity command console --level error --format json 2>/dev/null | python -c "
import sys,json
d=json.load(sys.stdin)
seen=set()
for e in d['data']['result']['entries']:
    m=e['message'].split('\n')[0]
    if 'error CS' in m and m not in seen:
        seen.add(m); print(m)"
  exit 1
fi
# Wait for domain reload to finish (main thread responsive).
for i in $(seq 1 120); do
  unity command eval --code 'return "alive";' --format json 2>/dev/null | grep -q '"alive"' && break
  sleep 2
done
echo "COMPILE $s"
