# RICOCHET: Concept & Design

**Status:** approved (2026-09-28)
**Track:** Gaming · **Division:** New Experience
**Engine:** Unity 6000.5.6f1 (URP) + Meta XR SDK v207
**Deadline:** Nov 18, 2026, 12:00 PM PST. Internal submit target: **Nov 16**.
**Team:** 2 (design + code). **No headset**: all development, testing and video capture go through Meta XR Simulator. See [TECH_GUIDE.md](TECH_GUIDE.md).

> **Tagline (≤140):** *Your room is the pinball table. Pull back, let go, and watch light ricochet off your real walls to seal the rifts opening in your home.*

---

## 1. The pitch

Cracks of light are opening in the walls of your home, and small void creatures are pushing through. From your chair, you pinch a glowing **Spark**, pull back and let go. It **ricochets off your real walls, couch, table and ceiling**, shattering crystals that have grown on your furniture. Every crystal you hit adds to the light that seals the rift. Chain enough hits and the last one triggers slow motion, then the whole room erupts in light.

It's **Peggle's dopamine loop** plus **Peglin/Balatro's run-building**, played on **the one board no other game can ship: your room.**

## 2. Why it wins (mapped to judging and prizes)

| Criterion / prize | Our answer |
|---|---|
| **Innovation**: uniquely uses spatial depth, passthrough, scene understanding, persistence | The level *is* your scanned room. Every home plays differently. Sealed rifts leave anchored trophies on your walls. |
| **Experience Design**: seated, hands-first, onboarding, habit | One gesture (pinch–pull–release), turn-based, fits a 2 ft radius. First payoff in under 30 s. Daily seeded run. |
| **Passthrough is purposeful** | Your furniture is the bumpers and your walls are where the rifts open. With passthrough off the game has no board. |
| **Technical**: MRUK, anchors, gaze, hands, FoV, 60 fps, untested rooms | Procedural board generation from MRUK, validated automatically against every bundled room layout. ISDK hands plus v207 gaze. Performance-first rendering. |
| **Polish** | One cohesive look (neon light in a dimmed real room), a rising-scale combo audio system, and light that *glows on your real walls* |
| Best New **Gaming** | Primary target |
| Best **First Five Minutes** | Zero text. The first shot is guaranteed spectacular. |
| Best **Reason to Come Back** | Roguelite unlocks, daily seeded run, trophies that build up in your home |
| **Accessibility Forward** | Turn-based, no reflexes. Eyes-only aim plus dwell-to-fire. One-handed. Shape-coded crystals. |
| **Boldest Original Concept** | "Would fall apart on any other platform": with no room there is no board |

## 3. Core loop

### Shot (~10–20 s)
1. **Pinch** the Spark floating in front of you (the sling anchor is ~40 cm ahead, just below shoulder height).
2. **Pull back** to aim and add power. A short trajectory preview shows the path up to the *first* bounce only, so skill still matters.
3. **Glance** at a crystal to **Focus** it. If the Spark hits the focused crystal, it's a critical hit. (Eye gaze on VR Glasses and Quest Pro, head gaze elsewhere.)
4. **Release.** The Spark flies, bounces off real surfaces and shatters crystals. Each hit plays the next note of a rising scale, and the multiplier climbs.
5. When the Spark dies (it hits the floor, or runs out of bounces or time), the accumulated light **beams into the rift** as damage.

### Encounter (~1–2 min)
- A **rift** opens on a real wall inside your forward view, and a creature emerges.
- Each turn you fire one Spark. Then the creature acts, and its next move is shown in advance (Slay-the-Spire-style intent): it charges an attack on your shield, grows armor, or corrupts crystals.
- The encounter is won when the creature's HP reaches 0 and the rift seals, which triggers **Fever**: slow motion, then the room fills with light.

### Run (~8–10 min, passes the bus-stop test)
- 5 encounters plus a boss, with a choice after each: **pick 1 of 3** upgrades.
  - **Spark types:** Splitter (splits on first bounce), Bomb (bursts on third hit), Ghost (passes through furniture once), Magnet (pulls toward crystals), Heavy (keeps momentum).
  - **Relics:** passive rules. "Bounces off the ceiling ×2", "gold crystals respawn", "the first hit each shot is a critical".
- Crystal types: **Normal**, **Gold** (critical), **Prism** (refreshes the board), **Bomb** (area), **Amp** (+1 multiplier for the rest of the shot).
- The run auto-saves after every shot, so you can take the headset off and resume exactly where you were.

### Meta (reason to come back)
- **Trophies in your home:** each boss you seal leaves a crystal trophy **spatially anchored** where its rift was. Over weeks your room becomes a record of your runs.
- **Unlocks:** new Spark types, relics and a harder "Ascension" tier.
- **Daily Rift:** a seeded daily run with fixed upgrades and a leaderboard. The board adapts to each player's room, but the scoring is normalized.

## 4. First five minutes (zero text)
| Time | Beat |
|---|---|
| 0:00 | Passthrough. The room gently dims. A hairline crack of light **splits open on your real wall**, with a low spatial sound. |
| 0:05 | A Spark drifts in and hovers in front of you, pulsing. If you do nothing for 4 s, a ghost hand demonstrates pinch–pull–release once. |
| 0:15 | **First shot.** The tutorial board is generated so a straight shot is *guaranteed* to hit 6+ crystals. The rising notes play and the creature squeals. |
| 0:40 | Two more shots. The rift seals and **Fever** triggers. Your room is lit up. |
| 1:30 | Three glowing upgrade choices float in front of you. Pinch one. That teaches choosing by gesture, not a menu. |
| 2:00–5:00 | The first real encounter, with enemy intent. The loop is now fully understood. |

## 5. Room adaptation (holds up in rooms we never tested)
- **Board generation:**
  - Crystals are placed with Poisson-disk sampling on MRUK surfaces (walls, table tops, couch, shelves, floor area in front of you, a little on the ceiling).
  - Placement is weighted toward the forward ±35° view cone (VR Glasses FoV).
  - It then **self-validates** with simulated shots: it rejects boards where too few crystals are reachable, where Sparks escape through gaps, or where the average bounce count is too low.
- **Rift placement:** the largest clear wall region in the forward view, avoiding windows and doors (MRUK labels).
- **Fallbacks:**
  - **Small or cluttered room:** fewer, larger crystals, plus virtual "glass" bumpers to fill gaps.
  - **No scene model** (plane, train, never scanned): offer Space Setup once. If declined, use the **Pocket Arena**, a self-contained glass diorama about 1.2 m in front of you. It is fully playable.
- **Automated room sweep:** an Editor test runs the generator on **every bundled MRUK room layout**, simulates hundreds of shots and fails CI-style if board quality drops below thresholds.

## 6. Art & audio direction
- **Look:** "light in a dark room". Passthrough is dimmed and slightly desaturated. Every game element emits light: electric cyan Sparks, amethyst/gold crystals, magenta void creatures. **Our light glows on your real walls** (RoomGlow). One palette, with no textures that fight the real room.
- **Creatures:** small, stylized, silhouette-readable ink-and-light forms. Emissive, low-poly, animated through shader and vertex motion.
- **Audio:**
  - Every crystal hit steps up a **pentatonic scale** (it can never sound wrong).
  - The final crystal gets a drumroll and slow motion. Fever gets a full musical swell.
  - Everything is spatialized, and bounces off *your* wall come *from your wall*.

## 7. Scope guardrails
- **Must:** sling, bouncing, 4 crystal types, 5 enemies plus 1 boss, 5 Spark types, 10 relics, the run loop, trophies, Pocket Arena, accessibility modes, onboarding, pause/resume.
- **Should:** Daily Rift with leaderboard (Platform SDK), Ascension.
- **Won't (this competition):** multiplayer, AI features, controller-specific content (controllers work through ISDK for free).

## 8. Schedule
| Week | Dates | Deliverable |
|---|---|---|
| 1 | Sep 28 – Oct 4 | Setup (done). Simulator. **Grey-box:** MRUK room → invisible colliders → sling → Spark bouncing → crystals shattering. **Gate: is one shot fun?** |
| 2 | Oct 5 – 11 | Board generator + room-sweep harness. Combo/scoring. RoomGlow shader. Pentatonic audio. |
| 3 | Oct 12 – 18 | Encounter loop: rift, enemy intents, HP, shield. Crystal types. Focus (gaze). |
| 4 | Oct 19 – 25 | Run structure, 1-of-3 rewards, Spark types, relics, boss. Save/resume. Trophies (anchors). |
| 5 | Oct 26 – Nov 1 | First-five-minutes onboarding, accessibility modes, Pocket Arena, Daily Rift. |
| 6 | Nov 2 – 8 | Art/audio polish, performance pass, VR Glasses FoV pass, room-sweep hardening |
| 7 | Nov 9 – 15 | Content lock, bug bash, **trailer capture** (Simulator session recording), Devpost text, screenshots, APK to the "Competition" channel |
| — | **Nov 16** | **Submit.** Nov 17–18 is buffer only. |

## 9. Rejected concepts (for the record)
- **KAMI** (living origami): too low-stakes for the Gaming track, and its folding feel can't be tuned without a headset.
- **STRING** (string figures): too much hand-tracking risk.
- **Hand-shadow puppets:** *Silhouette* already owns the idea.
- **Memory palace:** *LOCI* exists.
- **Orchestra conducting:** *Maestro* exists.
