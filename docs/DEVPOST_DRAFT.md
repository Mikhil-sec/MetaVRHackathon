# RICOCHET: Devpost submission (draft)

*Draft written 2026-10-04 (session 11). Numbers come from docs/STATUS.md; refresh them at content lock (Week 7).*

**Tagline:** Your room is the pinball table. Pull back, let go, and watch light ricochet off your real walls to seal the rifts opening in your home.

**Track:** Gaming · New Experience · Meta Quest 3/3S, Quest Pro, Meta VR Glasses

---

## Inspiration
Peggle's "one more shot" feeling and the run-building of Peglin and Balatro, on the one board no other game can ship: **your room**. In mixed reality the walls, couch and table you already own become the bumpers. Switch passthrough off and the game has no board at all.

## What it does
Cracks of light open in the walls of your home, and small void creatures push through. Seated, you **pinch** a glowing Spark, **pull back**, and **let go**. It ricochets off your real walls, furniture and ceiling, shattering crystals that have grown on them. Each hit plays the next note of a rising scale; the light you gather beams into the rift as damage.

- **Turn-based encounters:** each creature shows its next move in advance (attack your shield, raise armour, or corrupt crystals). Seal the rift and the room erupts in a wave of light.
- **Runs of about 9 minutes:** five creatures, then the Hollow Queen. After each win, pinch one of three upgrades: five Spark types (Splitter, Bomb, Ghost, Magnet, Heavy) and ten relics that bend the rules.
- **Five crystal kinds**, told apart by silhouette as well as colour: Gold (critical), Amp (multiplier), Bomb (area), Prism (fresh board) and Normal.
- **Focus:** glance at a crystal while aiming and a hit on it is critical (eye gaze where the headset has it, head gaze everywhere else).
- **Your room keeps score:** when the Queen falls, her crown streams as gold light to the wall where her rift opened and grows there, spatially anchored, so over weeks your walls become a trophy shelf.
- **Daily Rift:** the first run of each day is the same run for everyone: same seed, same starting gift, same rewards, on a dawn-gold rift.
- **Ascension:** every run you complete makes the next one a tier harder (up to five), marked by chevrons beside your shield.

## Why it only works in mixed reality
- **The level is your scanned room.** Boards are generated from Meta's Mixed Reality Utility Kit scene: crystals grow on your actual surfaces, the guaranteed first shot is predicted with the real ball physics against your room, and the rift opens on your largest clear wall.
- **Our light lands on your real walls.** Every bounce, hit and victory wave glows on the room mesh, so the virtual light visibly belongs to your space.
- **Persistence:** the trophies are spatial anchors and stay where you earned them.
- **Sound from your walls:** the spatializer's room acoustics are sized from your scanned room, and the rift hums from where it opened, so you can find it by ear.
- **No scan? Still playable.** If a player declines Space Setup, a self-contained glass "Pocket Arena" stage appears instead.

## First five minutes (zero text)
The room dims, a hairline crack splits your real wall, the crystals spill out of it as streams of light, the creature follows, and the Spark flies out of the rift into your hand's reach. If you wait, a ghost hand made of light shows the one gesture once. The first board is built so the most natural straight shot is **guaranteed** to chain several crystals, and the first lethal hit slows time.

## Accessibility
- **Look and Fire:** aim with your eyes (or head) and dwell to fire, fully hands-free; or one hand pinching anywhere. Switched on by looking at an icon, with no menus.
- **Seated, turn-based, no reflexes**, all within a 2 ft radius, and the seat re-centres when you do.
- **Shape-coded crystals** that read in greyscale; flash-safe room lighting (repeated hits warm a wall instead of strobing it); zero required reading.
- **Fully playable without controllers**: hands first, controllers optional.

## How we built it
- **Unity 6 (URP) with Meta XR SDK v207:** MRUK, Interaction SDK hands, OpenXR eye gaze, spatial anchors, passthrough, Meta XR Audio, and the Platform SDK (leaderboards).
- **Built without a headset.** Every feature was developed and verified in **Meta XR Simulator** with the **XR Operator** driving synthetic hands through real pinch, pull and release gestures, judged from composited passthrough captures.
- **An automated room sweep** runs the board generator on all 50 bundled MRUK room layouts and fires thousands of simulated shots per pass. Targets: an aimed shot hits in at least 90% of attempts, the straight first shot hits in 49 of 50 rooms, and no Spark escapes the room.
- **Performance first:** about 50–60 draw calls in play against a 150 budget, a few thousand triangles, no post-processing (all glow is faked in shaders), and zero garbage per frame, measured with a profiler probe we wrote.
- **Everything is original and procedural:** all shaders (creatures that crack with damage and shatter, a rift that tears open and zips shut, comet-like motes), all sound effects synthesized at startup, and music generated from the same pentatonic scale as the combo notes.

## Challenges
- **No headset.** We could not feel the game, so we built the tooling to see it: simulator automation, frame-stepped rendering for video, and data-driven checks (the room sweep, a GC probe, FoV measurements) wherever a human would normally just look.
- **Rooms we will never see.** Every system had to survive any layout: small tables, cluttered walls, doors and windows, no scan at all.
- **Readability over passthrough:** light-only visuals have to read against any real room, so we built text-free intent icons, scrim cards behind words, and a dimmed, slightly desaturated room.

## Accomplishments we're proud of
- A first shot that feels spectacular in any room, with no tutorial text.
- A complete roguelite loop (encounters, upgrades, boss, save and resume, trophies, daily run) that fits in about ten seated minutes.
- An accessibility mode that makes the whole game playable with eyes alone.

## What's next
More creatures and relics, Daily Rift leaderboards shown in the headset, and tuning on a real device.

## Built with
Unity 6 · URP · Meta XR SDK v207 (Core, Interaction SDK, MR Utility Kit, Audio, Platform) · OpenXR · Meta XR Simulator · Meta XR Operator · C# · HLSL · Python (synthesized music and tooling)
