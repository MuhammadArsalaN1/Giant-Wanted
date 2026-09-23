# Giant Wanted

**Giant Wanted** is a hyper-casual mobile shooter built with Unity. Flying giants swarm
the city in waves. You hold a rooftop turret position: drag to aim, hold FIRE to shoot,
and bring every giant down before they tear the city apart.

## Features

- **Wave-based survival**: hand-authored waves, with an optional endless mode that scales giant health and speed every wave.
- **Weak-point combat**: body shots deal normal damage. Head shots deal 3x damage and pop a **CRITICAL** number.
- **Bullet kill cam**: the final shot of a wave follows the bullet in slow motion all the way to the kill.
- **Juicy feedback**: procedural recoil, camera shake, muzzle flash, tracers, hit flashes, damage popups and floating health bars.
- **City health, score and coins**: coins persist between runs through `PlayerPrefs`.
- **Mobile first**: touch drag aiming, a hold-to-fire button, zoom and reload buttons, and light aim assist.
- **Pooled everything**: bullets, effects, popups and giants are pooled, so long runs do not allocate.

## Controls

| Action | Mobile | Editor / Desktop |
|--------|--------|------------------|
| Aim    | Drag anywhere on screen | Left mouse drag |
| Fire   | Hold the **FIRE** button | Hold `Space` or right mouse button |
| Zoom   | **Zoom** button | **Zoom** button |
| Reload | **Reload** button (auto-reloads when empty) | `R` |

## Project Structure

All gameplay code lives in `Giant Wanted/Assets/Scripts`, under the `GiantWanted`
namespace. Editor tooling uses `GiantWanted.EditorTools`.

```
Assets/Scripts
├── Combat      # Damage contract, hit boxes and swept-raycast projectiles
├── Core        # Game flow, pooling, audio, camera shake, bullet kill cam
├── Enemy       # Giant AI and the wave spawner
├── Player      # Turret aiming, the weapon and procedural recoil
├── UI          # HUD, hold button, damage popups, world-space health bars
└── Editor      # One-click scene builder and setup utilities (editor only)
```
