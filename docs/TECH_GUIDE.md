# RICOCHET: Engineering & Polish Guide

These are the rules we build by. Each one comes from Meta's v207 docs, Unity's OpenXR Meta docs, Meta's hand design guidelines, and the open-source **Spirit Sling** sample (a hand-tracked slingshot tabletop MR game). Where a rule is our own judgement, it says so.

**Our constraint:** we have **no headset**. We can't measure on-device frame time, so we build with a large performance margin and treat every budget below as a ceiling, not a target.

---

## 1. Frame budget

| Target | Value |
|---|---|
| Refresh rate | 90 Hz on Quest 3 (11.1 ms/frame). 72 Hz is the floor. Judges require ≥60 fps. |
| Our ceiling (self-imposed, ~20% of the device max) | **≤ 150 draw calls, ≤ 300k triangles** per eye-pair |
| Device max for reference | Quest 3/3S: 1.3–1.8M triangles |
| GC allocations per frame in gameplay | **0** (pool everything) |
| Cold start → first interaction | < 5 s |

Transparent overdraw (particles, glows, UI) is the main GPU killer in MR, because everything composites over passthrough. We budget it deliberately (§4).

## 2. Project settings (locked)

| Setting | Value | Why |
|---|---|---|
| XR provider | **OpenXR** plugin 1.18 + Meta XR Feature group | Meta's recommended path. Oculus XR Plugin is deprecated. |
| OpenXR features | Meta XR Feature, Meta XR Foveation, Meta XR Subsampled Layout, hand tracking, eye gaze | From the v207 setup docs |
| Graphics API | **Vulkan** only | Required for foveation and subpass optimizations |
| Scripting | **IL2CPP, ARM64 only** | VR Glasses *cannot run* 32-bit binaries |
| Stereo mode | Multiview (Single Pass Instanced) | Halves draw-call CPU cost |
| Color space | Linear | |
| Texture compression | ASTC | |
| URP HDR | **Off** | HDR buffers force an extra blit and negate FFR/MSAA gains |
| MSAA | **4×** (set manually in URP; OVRManager doesn't do it for URP) | Stereo aliasing is very visible |
| Post-processing | **Off** in the renderer | Full-screen passes are too expensive. Glow is faked instead (§4). |
| Depth Texture / Opaque Texture | **Off** | They trigger copy passes that break Vulkan subpass merging |
| Intermediate texture | Auto | |
| Main-light shadows | Off (fake shadows instead) | |
| Rendering path | Forward (Forward+ only if we need many real lights; Unity ≥6000.3 has Quest optimizations built in) | |
| SRP Batcher | On. Every custom shader must be SRP-Batcher compatible (all properties in `UnityPerMaterial`). | |
| Foveated rendering | Fixed foveation High + dynamic. Eye-tracked foveation on devices that have it. | Large fragment-cost saving, barely visible |
| Dynamic resolution | On, as a safety valve | |
| Physics fixed timestep | Match the refresh rate (1/90) | Avoids stutter in orb motion |
| App SpaceWarp | **Not used** | Needs a URP fork plus motion vectors on every shader, and it smears fast transparent trails. That's exactly our orb. |

We use the Project Setup Tool (Meta > Tools > Project Setup Tool) to catch anything we've missed, and we keep its report clean.

## 3. Mixed reality

- **Room data:** MRUK loads the room from the device and falls back to bundled room prefabs. In the Editor we test against **every bundled MRUK room prefab** in an automated sweep. That's how we honestly answer "holds up in rooms the developer never tested."
- **Physics against the room:** EffectMesh generates *invisible* MeshColliders from the room anchors (walls, furniture, ceiling). The orb Rigidbody follows the MRUK Bouncing Ball sample: **Continuous Speculative** collision detection, interpolation, low mass.
- **Placement:** we follow Spirit Sling's approach. Wait for `MRUK.Instance.IsInitialized`, check `IsPositionInRoom()` and reject `IsPositionInSceneVolume()`. We raycast to prove the play position isn't blocked by furniture, and cache room queries once rather than every frame.
- **No room model** (plane, train, never scanned): offer Space Setup once, then fall back to a self-contained virtual "pocket arena" in front of the player. The game must never dead-end.
- **Occlusion:**
  - Static room geometry: EffectMesh rendered **depth-only**. It's cheap, stable, and correctly hides pegs behind your real couch.
  - Hands: render the tracked hand mesh depth-only, so real hands occlude the orb and sling.
  - Depth API: **hard** occlusion only where needed (people moving). Soft occlusion looks nicer but costs GPU.
- **Relighting (the signature effect):** the orb and peg explosions should *light up your real walls*. Meta has a PTRL (Passthrough Relighting) highlights-and-shadows shader for this. Our cheaper custom version is a "RoomGlow" shader on the EffectMesh that reads a small global array of glow sources (position, color, radius). Because passthrough composites premultiplied (`out = src.rgb + (1 − src.a) · passthrough`):
  - **Glow on a real surface:** output `rgb = glow, a = 0`. That's purely additive over passthrough.
  - **Shadow on a real surface:** output `rgb = 0, a = shadow`.
  - One shader and one draw call per room surface, with no real-time lights or shadow maps.
- **Mood:** passthrough brightness, contrast and saturation are adjustable at runtime. We dim and desaturate the real room during Fever and at run start, so the game's light reads as the only light in the room.
- **Anchors:** keep the player's calibrated seat/aim origin across sessions with spatial anchors (as Spirit Sling does with `OVRSpatialAnchor`).

## 4. Making it look expensive on a mobile GPU

- **Unlit or simple custom shaders everywhere.** No URP Lit PBR on gameplay objects. Crystal pegs use a fresnel rim, an emissive core, a view-dependent fake refraction gradient and a matcap. That reads as "premium glass" at a fraction of PBR's cost.
- **Fake bloom:** small camera-facing additive halo quads (radial gradient texture) behind emissive objects. Halo size scales with intensity and pulses on hit. No post-processing.
- **Orb trail:** a ribbon mesh we build ourselves (a pooled ring buffer of points), additive and tapering. Much cheaper than a particle trail.
- **Particles:**
  - Mesh or low-count bursts, 8–24 particles, short lifetimes.
  - Additive or premultiplied blending, **no soft particles** (they need the depth texture).
  - Pooled systems, with a global cap on live transparent particles (~400).
- **Screen-space overdraw rule:** no transparent quad larger than ~15% of view, ever. Big effects are rings and edges, not filled discs.
- **Color:** one tight palette of 4–5 hues plus white, saturated neon against the dimmed room. Cohesion beats detail.
- **Motion does the polish work:**
  - Easing on every scale and position change (pop-in overshoot, `OutBack`).
  - Squash-and-stretch on the orb along its velocity.
  - Anticipation before big events.
  - Small, fast, frequent animation beats large and slow.
- **Text:** TextMeshPro SDF, world-space, one font family. Minimum angular size ~1.5° (about 2.5 cm cap height at 1 m). Score pop-ups billboard to the camera and ease out.

## 5. Game feel in VR (comfort-safe "juice")

| Flat-game trick | VR-safe replacement |
|---|---|
| Camera shake | **Never move the camera.** Shake the *object* hit, pulse the room glow, ripple the peg field. |
| Hit-stop (freeze frame) | Slow down *game time* for the orb and effects only (Peggle's last-peg slow-mo). The head camera stays at full rate. |
| Zoom on the last peg | Dim everything else, spotlight the orb (glow sources), rising drumroll audio |
| Screen flash | Brief world-space light pulse on the room surfaces (RoomGlow). No full-view white flash. |

**Peggle's dopamine engine, reproduced:**
- Each consecutive peg hit in one shot plays the **next note of a rising scale**. This is the single most important sound-design trick in the genre.
- Score pop-ups escalate in size and color with combo count.
- **Near-miss drama:** when the orb is heading for the last target, time slows and the drumroll swells.
- The end-of-board celebration (Fever) fills the room.

## 6. Hands and gaze UX (Meta guidelines and Spirit Sling)

- **One core interaction, used everywhere:** *pinch–pull–release*. The same gesture launches orbs and is also used for menus (pinch a floating orb-button and release).
- **Sling mechanics** (from Spirit Sling):
  - Pinch within reach of the sling anchor to grab it.
  - Aim is the vector from pinch point to anchor.
  - Launch **only if pull distance > ~3 cm**.
  - Draw a **trajectory preview** (first bounce only, to keep skill in the game).
  - Launch direction comes from the smoothed aim line, **never from hand velocity**. Hand velocity is noisy, and on a device we can't test, the aim line is what we can trust.
- **Tracking loss while pulling:** cancel softly (the orb returns to the sling). Never fire on tracking loss.
- **Feedback replaces haptics:** every pinch, grab and release gets a distinct sound plus a visual state change (glow, scale). The sound is the primary confirmation.
- **Ergonomics:**
  - Sling anchor about 35–45 cm in front of the chest, slightly below shoulder height.
  - Elbows near the body, nothing above heart level, nothing closer than 10 cm to the face.
  - The whole game is turn-based, so rest is built in and there are no reflex demands.
- **Hands visibility:** in MR, show the real hands (passthrough) with a subtle fingertip glow when a pinch is detected. Hide the virtual hand mesh (it's used for occlusion only).
- **Gaze:**
  - ISDK v207 Gaze Interaction / Look & Pinch: look at a UI target and pinch anywhere to select.
  - Accessibility aim mode: look where you want to shoot, then pinch to fire.
  - Requires Eye Tracking Support set to Supported in OVRManager.
  - The rig falls back to head gaze plus hand ray on Quest 3/3S. We design every gaze feature so the fallback is fully playable.
- Use **ISDK components** (HandGrab, Ray, Poke, Gaze) rather than raw `OVRHand`. They include Meta's jitter filtering and tuning, which we can't do ourselves without a device.

## 7. FoV-aware layout (VR Glasses ≈ 70°×66°; Quest 3 ≈ 110°×96°)

- **Critical info** (score, orbs left, the sling, enemy intent) sits within **±25° horizontal, ±20° vertical** of the seated forward direction.
- The peg field is weighted toward the central view. Pegs outside it are worth less, or are off-screen bonuses with edge indicators.
- UI panels are **world-locked with lazy follow** (re-center only when more than 30° off-axis, eased). Never head-locked HUDs.
- Off-FoV orb: spatialized audio plus a soft edge-glow indicator pointing toward it.
- We test every screen with the **VR Glasses profile** in Meta XR Simulator.

## 8. Audio

- Meta XR Audio SDK spatializer: Spatial Blend = 1, Spatialize on, **mono clips only**.
- Room acoustics: use shoebox room acoustics sized from the MRUK room bounds, or Acoustic Ray Tracing against EffectMesh geometry if it's cheap enough. Bounces off *your* wall should sound like your room.
- Import settings:
  - Short SFX: ADPCM, Decompress on Load.
  - Music: Vorbis, Streaming.
- Cap at about 32 voices, with pooled AudioSources.
- Pitch-randomize repeated SFX by ±5% so they never sound machine-gunned.

## 9. Lifecycle (bus-stop test)

- Pause on headset removal or focus loss (`OVRManager.HMDUnmounted`, `InputFocusLost`). Resume exactly where you left off.
- Save the run state at the end of every shot, so quitting mid-run never loses progress.
- The main menu is a pinchable object, not a 2D panel wall.

## 10. Testing without a headset

- **Meta XR Simulator v207** (a standalone app, activated from Unity's Meta menu or the toolbar):
  - Devices: Quest 3 and VR Glasses profiles.
  - Synthetic rooms with passthrough and scene data.
  - Hands can be driven from the webcam.
  - Look & Pinch.
  - Session recording (`.vrs`) for replayable tests and trailer capture.
- **Editor sweep:** load each MRUK room prefab and generate a board. Simulate N random shots headlessly and assert board-quality metrics: reachable pegs, bounce count, how often the orb leaves the room.
- **Performance proxies** (PC profiling isn't representative of Quest):
  - Check draw calls, triangles and transparent-pixel coverage in the Frame Debugger against §1's ceilings.
  - Zero-GC checks in the Profiler.
- If we ever get access to a headset, even for an hour, we should run OVR Metrics Tool, then fix what it shows.

## Sources
- [Meta: Unity performance](https://developers.meta.com/horizon/documentation/unity/unity-perf)
- [Meta: URP render scale / HDR / MSAA](https://developers.meta.com/horizon/documentation/unity/unity-project-configuration)
- [Meta: Vulkan subpasses](https://developers.meta.com/horizon/documentation/unity/vulkan-subpasses)
- [Unity OpenXR Meta graphics settings](https://docs.unity3d.com/Packages/com.unity.xr.meta-openxr@2.1/manual/get-started/graphics-settings.html)
- [Meta: Forward+](https://developers.meta.com/horizon/documentation/unity/unity-forward-plus-rendering/)
- [Meta: App SpaceWarp](https://developers.meta.com/horizon/documentation/unity/unity-asw)
- [MRUK Bouncing Ball sample](https://developers.meta.com/horizon/documentation/unity/unity-sample-mruk-bouncing-ball)
- [Passthrough relighting](https://developers.meta.com/horizon/documentation/unity/unity-passthrough-relighting)
- [Depth API occlusion](https://developers.meta.com/horizon/documentation/unity/unity-depthapi-occlusions)
- [Spirit Sling](https://developers.meta.com/horizon/documentation/unity/spirit-sling/) / [GitHub](https://github.com/oculus-samples/Unity-SpiritSling)
- [Hands best practices](https://developers.meta.com/horizon/design/hands-best-practices/)
- [Hands UI best practices](https://developers.meta.com/horizon/design/hands-ui-best-practices/)
- [Meta XR Simulator](https://developers.meta.com/horizon/documentation/unity/xrsim-getting-started/)
- [Meta XR Audio spatialization](https://developers.meta.com/horizon/documentation/unity/meta-xr-audio-sdk-unity-spatialize)
