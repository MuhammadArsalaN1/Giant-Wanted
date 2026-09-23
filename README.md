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

## Scripts Overview

### Combat
| Script | Purpose |
|--------|---------|
| `IDamageable` | Interface for anything a projectile can hurt. |
| `HitBox` | Collider on a giant that forwards damage to its owner with a multiplier. The head is the weak point. |
| `Projectile` | Pooled tracer bullet. Sweeps a raycast between frames so it never tunnels through targets. |

### Core
| Script | Purpose |
|--------|---------|
| `GameManager` | Owns the run: game state, wave order, city health, score and coins. Exposes events for other systems. |
| `SimplePool<T>` | Minimal generic component pool. |
| `FxPool` | Pooled short-lived effects: impacts, explosions and damage numbers. |
| `CameraShaker` | Additive trauma-based camera shake. |
| `GameAudio` | Event-driven one-shots and music. Every clip is optional. |
| `BulletCam` | Cinematic slow-motion kill cam for the last giant of a wave. |

### Enemy
| Script | Purpose |
|--------|---------|
| `Giant` | Flying giant AI: approach, hover, lunge-attack the city, die and sink. Drives animator states by name. |
| `WaveSpawner` | Spawns pooled giants on a ring around the city and tracks who is still alive. |

### Player
| Script | Purpose |
|--------|---------|
| `PlayerAim` | Turret-style drag aiming with smoothing, zoom, recoil recovery and aim assist. |
| `WeaponController` | Fire rate, spread, magazine and reload, muzzle flash, recoil and shake. |
| `WeaponRecoil` | Procedural gun kick and idle sway on the view-model. |

### UI
| Script | Purpose |
|--------|---------|
| `HUD` | Connects the canvas to the game: ammo, wave, score, coins, city health, reticle and banners. |
| `HoldButton` | Button that reports press and release, which drives full-auto fire. |
| `DamagePopup` | World-space floating damage number. |
| `WorldHealthBar` | Billboarded health bar above a giant. It only shows once the giant has been hit. |

## Architecture

- **Event-driven flow**: `GameManager`, `WaveSpawner` and `WeaponController` raise C# events.
  The HUD, audio and kill cam subscribe to them instead of polling, so systems stay decoupled.
- **Optional references**: most components resolve missing references at `Awake` and skip
  anything that is not assigned. You can strip parts of the HUD or leave audio clips empty.
- **Pooling**: `SimplePool<T>` backs bullets, effects, popups and giants, so there is no
  instantiation in the hot path.
- **Cinematic hold**: the kill cam can pause wave progression and hold the trigger, so the
  hero bullet and the wave banner never fight over the moment.

## Getting Started

### Requirements
- Unity **6000.0.60f1** (Unity 6) with the Universal Render Pipeline
- Android or iOS build support for mobile builds

### Setup
1. Clone the repository and open the `Giant Wanted` folder in Unity Hub.
2. Open `Assets/Scenes/SampleScene.unity`. It holds the environment, gun, bullet and monster art.
3. Run **Tools > Giant Wanted > Build Now (defaults)** to put together the playable scene.
4. Optional: run **Tools > Giant Wanted > Assign Sounds** and **Tools > Giant Wanted > Add Bullet Kill Cam**.
5. Press **Play**.
