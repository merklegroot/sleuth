## Cat Detective

A shooter game where you fight bad guys by launching cats at them.

## Gameplay

### Wandering cats

- **Health:** Each wandering cat has a health bar. Both **your** cat shots and **enemy** bullets can damage cats on hit.
- **Disabled (0 HP):** When a cat’s health reaches zero, it does **not** disappear. It becomes **disabled**: it stays where it is, plays idle animation only, and **no longer wanders or returns toward the player** on its own.
- **Pickup:** You can still walk into a disabled cat to pick it up into your inventory (same as a healthy cat), if you have room.

### Navigation

- **NPC pathfinding:** Enemies and cats use a lightweight tile-based pathfinder to pick reachable targets and take steps around walls instead of constantly ramming into them.

### Controls (see on-screen hints)

- **Tab:** Status and inventory.
- **Cmd+Enter** (macOS) or **Win+Enter** (Windows), or **F11:** Toggle fullscreen.
- **Esc:** Quit.
