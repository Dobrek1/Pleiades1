# Pleiades1 / Плеяды

Sprint-1 playable scaffold: Earth–Moon map, Hohmann transfers, time warp, Russian watch HUD.

**Unity:** `6000.6.0f1` (open the repo root as a Unity project).

## Architecture

| Folder | Role |
|--------|------|
| `Assets/Pleiades.CoreSim/` | Pure sim (no `UnityEngine`): AstroMath, GravityBody, Kepler, Hohmann, SimShip, SimWorld |
| `Assets/Pleiades/` | View only: `PleiadesBoot`, `MapView`, `WatchHud` |

1 uu = 1000 km (`AstroMath.MetersPerUnit = 1e6`). Ecliptic XZ, camera +Y. No PhysX orbits.

## Play / Как играть

### EN
1. Install **Unity 6000.6.0f1** (Hub → Installs).
2. **Add** → open this repository folder (the one with `Assets/` + `ProjectSettings/`).
3. Wait for script compile. Enter **Play** (▶) — `PleiadesBoot` auto-spawns the scene (empty scene is fine).
4. Controls:
   - **Space** — pause / resume  
   - **1 / 2 / 3 / 4** — warp ×1 / ×60 / ×3600 / ×86400  
   - **Scroll** — zoom · **RMB drag** — pan  
   - **G** or HUD button — Hohmann to **GSO** (~3.94 km/s total from LEO 200 km)  
   - **L** or HUD button — Hohmann toward **Moon** (384 400 km)  
   - Payload slider reduces available Δv · **Esc / Enter** clears interrupt  
5. Barge «Ржавая баржа»: Isp 900 s, ~18 t LH2, modules (frame, cabins×4, hold, engine, LH2 tank, radiator).

### RU
1. Установите **Unity 6000.6.0f1**.
2. Откройте корень репозитория в Hub (папка с `Assets/` и `ProjectSettings/`).
3. Дождитесь компиляции → **Play** (▶). Сцена собирается booting-скриптом.
4. **Пробел** — пауза · **1–4** — варп · колёсико — зум · **ПКМ** — панорама · **G** — ГСО · **L** — Луна · слайдер нагрузки ест Δv.
5. Прерывания: топливо / прибытие (циркуляризация, варп → 1×).

## Notes

- CoreSim has `noEngineReferences: true` — keep it free of UnityEngine.
- Sprint-1 has no dock / captain / pawns.
