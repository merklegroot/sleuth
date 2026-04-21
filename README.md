## Cat Detective

A shooter game where you fight bad guys by launching cats at them.

## Gameplay

### Wandering cats

- **Health:** Each wandering cat has a health bar. Both **your** cat shots and **enemy** bullets can damage cats on hit.
- **Disabled (0 HP):** When a cat’s health reaches zero, it does **not** disappear. It becomes **disabled**: it stays where it is, plays idle animation only, and **no longer wanders or returns toward the player** on its own.
- **Pickup:** You can still walk into a disabled cat to pick it up into your inventory (same as a healthy cat), if you have room.

### Navigation

- **NPC pathfinding:** Enemies and cats use a lightweight tile-based pathfinder to pick reachable targets and take steps around walls instead of constantly ramming into them.

### Enemy AI

Enemies are no longer simple “walk-and-shoot” bots. Each enemy runs a lightweight **finite state machine** and makes small, readable decisions every moment so fights feel dynamic without turning into unfair aim-bot chaos.

#### States (high level)

- **Patrol / Idle**: Wander with a little personality drift so they feel alive.
- **Chase Player**: Close distance (or reposition) when line-of-sight is blocked or they’re out of their preferred range.
- **Attack / Shoot**: Hold a preferred distance and **strafe/circle** while firing.
- **Avoid Danger**: If a lot of cats are nearby or an incoming “cat bullet” is on a collision course, they **sidestep / back off** instead of eating the hit.
- **Flee (low health)**: When hurt, some enemies disengage and try to survive long enough to re-enter the fight.
- **Hunt Disabled Cats (optional behavior)**: Certain enemies will opportunistically go after **disabled cats** to deny pickups (but won’t tunnel if it’s suicidal).

#### Awareness & fairness

- **Line-of-sight gating**: Enemies check line-of-sight before committing to shots.
- **Target selection**: Depending on pressure, enemies may shoot at the **player** or the most threatening nearby **healthy cat**.
- **Readable “smarts”**: Reactions include small delays, aim error, and burst pacing so you can outplay them.

#### Shooting upgrades

- **Burst fire + cooldowns**: Enemies shoot in short bursts with randomized cooloffs.
- **Leading aim (prediction)**: They can lead the player a bit based on player velocity (varies by enemy type).

#### Movement upgrades

- **Smoother steering**: Acceleration + turn smoothing (less jitter, more “body”).
- **Separation**: Enemies push away from each other so they don’t clump into one super-enemy.
- **Tile-based path steps**: When blocked, they take pathfinder steps around walls instead of face-planting into obstacles.

#### Enemy types (current roster)

- **Aggressive Chaser**: Plays mid-range, strafes hard, and fires frequent bursts. Great at pressuring you while cats are flying.
- **Sniper**: Prefers long range, takes higher-confidence shots, and leads aim more than others. Use cover and break line-of-sight.
- **Cat Hunter**: Opportunistically hunts **disabled cats** and likes shooting down nearby cats when they swarm.

#### Tuning / difficulty

Enemy behavior is intentionally **data-driven-ish**: each archetype has a parameter set (speed, ranges, burst size, reaction delay, dodge strength, separation, etc.). If you want a harder wave, increase enemy count and slightly tighten cooldowns; if you want “fair but scary”, prefer better positioning (ranges + LOS) over perfect aim.

### Controls (see on-screen hints)

- **Tab:** Status and inventory.
- **Cmd+Enter** (macOS) or **Win+Enter** (Windows), or **F11:** Toggle fullscreen.
- **Esc:** Quit.
