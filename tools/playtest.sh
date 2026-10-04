#!/usr/bin/env bash
# Desktop (no-XR) smoke test: enter Play Mode, fire N scripted shots, capture frames, report logs and errors.
# Usage: tools/playtest.sh [shots=3]
export UNITY_NO_PAGER=1
SHOTS=${1:-3}
cd "$(dirname "$0")/../Ricochet" || exit 1
CAP="C:/Dev/MetaVRHackathon/Ricochet/Recordings"

u() { unity command "$@" 2>/dev/null; }
ev() { u eval --code "$1" --format json | python -c "import sys,json; d=json.load(sys.stdin); print(d['data']['result']['result'] if d.get('success') else d.get('errors'))"; }

u set_autotick --enable true --interval_ms 16 >/dev/null
u clear_console >/dev/null
u editor_play >/dev/null
sleep 8
for w in $(seq 1 10); do ev "return \"alive\";" | grep -q alive && break; sleep 2; done
ev 'UnityEngine.Application.runInBackground = true; UnityEditor.EditorApplication.isPaused = false; if (Ricochet.Gameplay.AssistAim.Enabled) Ricochet.Gameplay.AssistAim.Set(false); return "frame=" + UnityEngine.Time.frameCount;'

for i in $(seq 1 "$SHOTS"); do
  # Wait until the sling is ready again, then fire at a random upward-forward angle.
  for w in $(seq 1 30); do
    r=$(ev 'UnityEditor.EditorApplication.isPaused = false; var s = UnityEngine.Object.FindAnyObjectByType<Ricochet.Gameplay.Sling>(); var cam = UnityEngine.Camera.main.transform; var dir = UnityEngine.Quaternion.Euler(UnityEngine.Random.Range(-25f,5f), UnityEngine.Random.Range(-35f,35f), 0) * cam.forward; return s.FireForTest(dir, UnityEngine.Random.Range(0.5f,1f)).ToString();')
    [ "$r" = "True" ] && break
    sleep 1
  done
  echo "shot $i fired=$r"
  sleep 0.5
  u capture_game_view --save_path "$CAP/shot_$i.png" --source camera --width 1280 --height 720 >/dev/null
done
sleep 10

u console --format json | python -c "
import sys,json
d=json.load(sys.stdin)
for e in d['data']['result']['entries']:
    m=e['message']
    if '[Ricochet]' in m or e['level'] in ('error','exception'):
        print(e['level'][:4], m.split(chr(10))[0][:220])"
ev 'return "paused=" + UnityEditor.EditorApplication.isPaused + " frame=" + UnityEngine.Time.frameCount;'
u editor_stop >/dev/null
echo "captures in Ricochet/Assets/Recordings (delete before committing)"
