# RICOCHET: store listing kit (paste-ready)

*Prepared 2026-10-04 (session 12) for the Meta Horizon Developer Dashboard and the competition release channel. Mikhil pastes these into the Dashboard; refresh the numbers at content lock.*

## Before anything else (Mikhil)
- [ ] **Name check:** search the Horizon Store for "Ricochet". If the name is taken, fallbacks in order: **RICOCHET: Rift Light**, **Ricochet Rooms**, **Riftlight**.
- [ ] **App ID:** create the app in the Dashboard. This also unlocks the leaderboards (`daily_rift` and `best_run`: Higher is better, client authoritative).
- [ ] **Privacy policy URL:** host `docs/PRIVACY_POLICY.md` (GitHub Pages, Google Sites or a Notion page all work). Fill in the support email and date first.
- [ ] **Support email:** any address you will read.
- [ ] **Release keystore:** see STATUS "Waiting on the user".

## Short description (150 characters max; this one is 142)
Pinch, pull and let go: light ricochets off your real walls to seal rifts opening in your home. A seated, hands-first mixed reality roguelite.

## Long description (4000 characters max; this one is about 2,200)
Cracks of light are opening in the walls of your home, and small void creatures are pushing through.

RICOCHET turns your real room into the board. Seated, you pinch a glowing Spark, pull back and let go. It ricochets off your actual walls, furniture and ceiling, shattering the crystals that have grown on them. Every hit plays the next note of a rising scale, and the light you gather beams into the rift as damage.

YOUR ROOM IS THE LEVEL
- Crystals grow on your real surfaces, and every board is built for your room.
- The first shot is guaranteed: the board is laid out so a natural straight throw chains several crystals.
- Your light lands on your walls: every bounce and every victory wave glows across the room around you.
- No room scan? Play on a holographic glass stage instead.

A ROGUELITE IN TEN SEATED MINUTES
- Turn-based encounters: each creature shows its next move before it acts. Plan the shot, then take it.
- Five creatures, then the Hollow Queen.
- After each win, pinch one of three rewards: five Spark types (Splitter, Bomb, Ghost, Magnet, Heavy) and ten relics that bend the rules.
- Five crystal kinds, told apart by shape and colour: Gold, Amp, Bomb, Prism and Normal.
- Glance at a crystal before you let go: a hit on it is critical.

YOUR WALLS KEEP SCORE
- Seal the Queen's rift and her crown grows on your wall, anchored where you earned it. Over weeks your room becomes a trophy shelf.
- The Daily Rift: after your first run, the first run of each day is the same run for everyone.
- Ascension: every run you complete makes the next one harder, up to five tiers.

PLAY YOUR WAY
- Hands first: one gesture, pinch, pull and release, plays the whole game. Controllers work too.
- Look and Fire: aim with your eyes (or head) and dwell to fire, completely hands-free, or play with one hand.
- Seated, turn-based and reflex-free. No text to read, shape-coded crystals, and flash-safe lighting.
- Pauses the moment you take the headset off, and resumes exactly where you left off.

Made with Meta's Mixed Reality Utility Kit, hand tracking, spatial anchors and spatial audio. Every sound and every note of music is generated from the same scale as your combo.

## Store fields
| Field | Value |
|---|---|
| Category | Games |
| Genres | Arcade, Casual, Strategy |
| Comfort rating | **Comfortable** (seated, no locomotion, the camera never moves) |
| Player modes | Single user |
| Play area | Seated / stationary (needs about 2 ft around you) |
| Supported input | Hand tracking (primary), Touch controllers |
| Supported devices | Quest 3, Quest 3S, Quest Pro (eye gaze used where available) |
| Required permissions | Spatial data (scene), hand tracking, eye tracking (optional), spatial anchors |
| Languages | English (the game itself uses almost no text) |
| Internet | Optional: only for leaderboards |
| In-app purchases / ads | None |

## Age rating questionnaire (IARC): suggested answers
Answer truthfully in the Dashboard; these reflect the game as built.
- **Violence:** fantasy, non-realistic, no blood. Abstract creatures of ink and light crack and shatter into light when defeated; the player is never harmed on screen (a shield meter goes down).
- **Fear:** none intended. Creatures are small and stylized; no jump scares.
- **Sexual content, nudity, language, crude humour:** none.
- **Drugs, alcohol, tobacco:** none.
- **Gambling or simulated gambling:** none. Rewards are a free choice of one from three, with no currency and no purchases.
- **User interaction:** none. No chat, no user-generated content, no sharing of location.
- **Personal data:** leaderboards (when enabled) show the player's Meta username and score through the Meta Platform SDK. Nothing else leaves the device.
- **Digital purchases:** none.

## Assets still to make (Week 7, after content lock)
- [ ] Icon, 1024 x 1024, no rounded corners and no text.
- [ ] Hero art, 2560 x 1440 (keep the title inside the inner 80%).
- [ ] At least 3 screenshots, 2560 x 1440, rendered from the app with the trailer pipeline.
- [ ] Trailer, 30 to 60 s.
