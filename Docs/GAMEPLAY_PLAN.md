# Fruit Drop AR: gameplay plan (V0.4 → V1.0)

This file is the single source of truth for the gameplay phase. Any session that picks the work up reads it first, does the **Next step**, ticks it here with the measured values, and commits.

## Status

- **Frozen version:** v0.3.1 on `main` (V0.3 game plus the iOS build settings). Tags `v*` are locked on GitHub, so they cannot be moved or deleted.
- **Work branch:** `gameplay`.
- **Next step:** V0.5 step 2 (full game states; add ScoreManager to the `Game` object and wire it to the spawner).
- **Waiting on user:** phone test of V0.4 (`Builds/V0.4.apk`, GitHub pre-release v0.4) on the Realme 7.

## Working rules

- **Code style:** match `FloorBasketFollower.cs` and `ARStatusText.cs`.
  - `m_` fields with `[SerializeField]` and `[Tooltip]`
  - XML `<summary>` docs and comments that explain why
  - `Debug.Log("[ClassName] ...")`
  - scripts in `Assets/Scripts/`, prefabs in `Assets/Prefabs/Fruit/`, data in `Assets/Data/`
- **A step is done when:**
  1. Unity reports 0 compile errors. Check `EditorUtility.scriptCompilationFailed` and search the console for "error CS": compile errors arrive as plain log entries, so an Error-type filter misses them.
  2. The step's own XR Simulation check passes, scripted in Play mode with the sim camera moved by script, plus a Game-view capture where it is visual.
  3. V0.3 behaviour still holds: the plane floor reads y ≈ 0.006 and the edge stop is about 0.15 m inside the edge.
  4. It is committed on `gameplay` as "V0.x step n: ...".
  5. It is ticked below with the measured values.
- **Never pause with `Time.timeScale`.** FloorBasketFollower smooths with `Time.deltaTime` and times tracking loss with `Time.time`, so the basket would freeze. Pausing is a flag that Fruit and FruitSpawner read.
- **Usage gate:** check plan usage before each step and after each sim test.
  - At 80 % or more of any window, finish or revert to the last commit, then update **Status** above.
  - Then create a one-time scheduled task `fruitdrop-resume-<yyyymmdd-hhmm>` at that window's reset time + 10 min, using the resume prompt at the end of this file, and stop.
- **At the end of each version:**
  1. Build `Builds/V0.x.apk`.
  2. Send it to the user with a short phone checklist.
  3. Push `gameplay`, tag `v0.x` on its tip, and publish a GitHub pre-release with the APK, noted "checked in XR Simulation; phone test pending".
  4. Continue with the next version.
  - Once the user reports a good phone test, mark the release as full and merge `gameplay` into `main`.
  - Fixes after a tag get a new tag (v0.x.1).
- **Phone-only work** (on-device feel, profiling, playtests) goes under "Waiting on user" and is never faked.
- **Generated churn:** XR plug-in management rewrites `preloadedAssets` in `ProjectSettings.asset` on every Play mode or build, and builds flip URP shader-prefilter flags in `Assets/Settings/URP-Performant.asset`. Leave both uncommitted; they are not part of any step.
- **Builds:** `BuildPipeline.BuildPlayer` inside `Unity_RunCommand` returns thousands of lines of shader warnings. Start the build from `EditorApplication.delayCall`, return at once, and poll for the APK file, which keeps the tool output short.
- **Wording:** no "built in one day" and no clock times in notes, release text or reports.

## V0.4 Fruit drop core

- [x] 1. **FloorBasketFollower API**, with no behaviour change:
  - `hasPlaneFloor`
  - public `ClampToFloor(Vector3)`
  - `IsOnDetectedFloor(Vector3)`, using the existing polygon test and edge inset
  - **Done.** `IsOnDetectedFloor` checks the centre plus four points 0.15 m away, so spots across a seam between two floor planes still count.
  - Added `Assets/Editor/SimTestDriver.cs`, an editor-only test driver. `SimTestDriver.RunInPlayMode("floor-edge")` writes `Temp/SimTest.log`. XR Simulation only rescans when the camera moves, so the driver sweeps the camera while it waits for a plane.
  - **Sim:** plane floor after 0.3–0.5 s; floor y 0.004–0.005; phone 0.93 m above it; `IsOnDetectedFloor` is true at the basket and false 10 m out.
  - **Note for V0.8:** at corners of small, newly found planes the edge clamp leaves the basket 0.05–0.07 m from the nearest edge, because the inset moves it toward the plane centre, not straight in from the edge. Clamp code unchanged since V0.3.
- [x] 2. **Fruit model:**
  - `RedApple` prefab, built from the basket's apple primitive and stem with the existing materials, about 11 cm across
  - `FruitType` ScriptableObject (name, prefab, points, fall speed, spawn weight, combo bonus, isHazard)
  - `Assets/Data/RedApple.asset`: 10 points, 0.8 m/s
  - **Done.** `RedApple.prefab` has an empty root with a `Body` child: the basket's apple ×1.5, colliders removed. Bounds 0.113 × 0.118 × 0.113 m, with the pivot at the centre of the apple. FruitType radius 0.056 m; spawn weight 40, matching the 40 % share.
- [x] 3. **`Fruit.cs`:**
  - falls at type speed × speed factor, with a slow spin, frozen while paused
  - tests the segment from the previous to the current position against the basket
  - reports Caught or Missed on reaching the live `floorHeight`, then goes back to the pool
- [x] 4. **`CatchDetector.cs`** on the FruitBasket prefab:
  - rim height 0.07 m and catch radius 0.15 m, both tunable
  - a geometric rim-crossing test instead of trigger colliders
  - **Done together with step 3,** because each needs the other to be tested. Sim scenario `drop` (2.5 m rooftop, 0.8 m/s):
    - above the basket: caught after 3.02 s (expected 3.04 s)
    - 0.4 m beside it: missed after 3.06 s, resting 0.053 m above the floor (radius 0.056)
    - paused halfway: moved 0.0000 m in 2 s, then caught
- [x] 5. **Basic `LandingMarker`:**
  - a flat ring on the floor under each falling fruit
  - the ring texture is generated by an editor script; unlit transparent URP material
  - It is brought forward from V0.6 because a drop can't be tested without it.
  - **Done.** `LandingMarker.prefab` is a 0.30 m quad, so its 0.15 m radius equals the catch radius. It is nested in `RedApple.prefab` and placed 4 mm above the floor in `LateUpdate`.
  - Ring texture `Assets/Textures/LandingRing.png`: white ring at r 0.80–0.93 with dark outlines and a 15 % fill. The material is a copy of BasketShadow tinted lemon F4C430.
  - **Sim scenario `marker`:** 0.0000 m from under the fruit, 0.0040 m above the floor; hidden after landing; the capture shows the ring clearly.
- [x] 6. **`FruitSpawner.cs`:**
  - every 2.0 s while Playing, a landing spot ±20° of the flat forward direction, 0.8–2.5 m away
  - the spot must pass `IsOnDetectedFloor`, with up to 10 tries
  - fruit spawns at floor + 2.5 m, from an object pool
  - counts catches and misses
  - **Done.** A `Game` object in SampleScene holds the FruitSpawner. Fruit from each FruitType has its own pool. `fruitLanded` is the event for the V0.5 ScoreManager, and `isPaused` freezes the fall and the drop timer.
  - Saving the scene also re-serialised FloorBasketFollower's fields: the stale `m_MinFloorPoints` went out and the V0.3 fields came in at their code defaults, so behaviour is unchanged.
  - **Sim scenario `spawner`** (with a 10 s room scan first; floor about 6 m²):
    - camera following the fruit that lands next: 10 dropped, 9 caught, 0 missed, the last one still falling
    - looking straight down: 6 dropped, 0 caught
    - landing spots 0.88–1.68 m away, at most 18.9° off straight ahead
  - **Finding:** right after the first plane appears, the floor patch is too small for the 0.8–2.5 m band and every drop is skipped. Step 7's Ready state must wait for enough floor; see that step.
- [x] 7. **`GameStateManager.cs` (minimal):**
  - Scanning → Ready (plane floor and basket visible) → Start button → Playing
  - paused while the basket is hidden
  - check that a tap on Start doesn't trigger the template Object Spawner
  - Ready only once the spawner can find a landing spot. Expose `FruitSpawner.HasLandingSpot()` and show "Scan more floor ahead" until it is true.
  - **Done.** GameStateManager is on the `Game` object. "Start Button" on Status Canvas: lemon F4C430 background, forest 1F3A2E text, 520 × 150, 220 px above the bottom.
  - Ready → Scanning only after 4 failed floor checks in a row (1 s), so the random spot test doesn't make the button flicker.
  - The template's Object Spawner and menu UI are already inactive, so a tap on Start can't spawn template shapes.
  - **Sim scenario `states`:**
    - Scanning at start, and still Scanning just after the first plane (patch too small)
    - Ready with the button shown after a 10 s scan; Start leads to Playing
    - AR session off for 4 s: basket hidden, paused, fruit moved 0.0000 m
    - back on: resumed after 0.3 s, fruit moved 0.402 m in 0.5 s
  - The "Scan more floor ahead" hint belongs in step 8.
- [x] 8. **ARStatusText:**
  - Ready and Playing hints
  - caught and missed counts in the debug lines
  - optional GameStateManager reference
  - **Done.** Hints, checked in the `states` scenario:
    - "Finding the floor... move the phone slowly"
    - "Scan more of the floor ahead of you" (plane floor, no landing spot yet)
    - "Floor found. Tap Start"
    - "Catch the falling fruit!"
    - "Paused. Point the phone at the floor to carry on"
  - Debug lines add `Game: <state> (paused)   Caught: n   Missed: n`.
- [x] 9. **XR Simulation play test:**
  - with the camera aimed, every fruit is caught
  - with the camera turned away, every fruit is missed
  - a capture shows fruit and marker; the console has no errors
  - **Done.** Sim scenario `play` is the `spawner` scenario run through the Start button:
    - Ready after a 10 s scan, then Start leads to Playing
    - camera following the next fruit: 10 dropped, 9 caught, 0 missed, the last one still falling
    - looking down: 6 dropped, 0 caught
    - spots 0.89–1.59 m away, at most 18.3° off
    - 0 errors logged during the round
    - the capture shows the basket on one landing ring, the next ring beside it, and an apple falling
- [x] 10. **Version end:** V0.4 APK, tag and pre-release.
  - **Done.** `Builds/V0.4.apk`, 35.9 MB: 0 errors, 359 warnings (package shader notes, as in earlier builds). The IL2CPP build took about 15 min. Tagged `v0.4` and published as a GitHub pre-release.
  - The Android version name is still 1.0.0; renaming and versioning are in V0.8 step 3.

## V0.5 Scoring and HUD

- [x] 1. **`ScoreManager.cs`:**
  - score; streak; multiplier = min(5, 1 + ⌊streak/5⌋)
  - 3 lives; a miss resets the streak and costs a life
  - high score saved in PlayerPrefs; change events
  - **Done.** The multiplier for a catch comes from the streak before it, so catches 1–5 score ×1 and catches 6–10 score ×2. Caught fruit to avoid adds its (negative) points with a floor of 0, resets the streak and costs a life; letting it fall costs nothing. Fruit landing after the last life is ignored. PlayerPrefs key `FruitDropAR.HighScore`.
  - It is not in the scene yet; step 2 wires it.
  - **Edit-mode rules check:** all pass.
    - 12 red apples → 210; a miss → 2 lives
    - golden → 310, streak 3; 2 red → 330; the 6th in a row ×2 → 350
    - rotten let fall: no change; rotten caught → 330, 1 life
    - last miss → 0 lives, `outOfLives` fired once; a catch after it is ignored
    - high score 350, saved
    - 30 red apples in a row → 1000 with the ×5 cap
- [ ] 2. **Full game states:**
  - Scanning, Ready, Countdown (3-2-1), Playing, Paused (tracking lost), GameOver, then Restart
  - Start is allowed only on a plane floor
- [ ] 3. **Game HUD** (uGUI + TMP, inside the safe area):
  - score, ×combo, hearts for lives
  - countdown text; Start button
  - Game Over panel with score, best and Restart
  - hide the leftover template UI
- [ ] 4. **Sim test:**
  - a scripted round with planned catches and misses; the score matches a hand-worked total
  - 3 misses lead to Game Over; Restart resets
  - the best score survives leaving and re-entering Play mode
- [ ] 5. **Version end:** V0.5.

## V0.6 Variety and difficulty

- [ ] 1. **FruitType assets for all six types:**

  | Fruit | Points | Fall speed | Share of spawns | Note |
  |---|---|---|---|---|
  | Red apple | 10 | 0.8 m/s | 40 % | |
  | Green apple | 15 | 0.9 m/s | 25 % | |
  | Orange | 20 | 1.0 m/s | 20 % | |
  | Lemon | 30 | 1.2 m/s | 10 % | |
  | Golden | 100 | 1.0 m/s | 3 % | adds 2 to the streak |
  | Rotten | −20 and −1 life when caught | 0.9 m/s | 2 %, rising to 10 % by 120 s | a missed rotten fruit costs nothing |

  - new prefabs, plus golden and rotten materials
- [ ] 2. **Weighted random pick**, with the rising rotten share.
- [ ] 3. **`DifficultyCurve`:**
  - interval(t) = max(0.7, 2.0 − 0.015t)
  - speed(t) = min(1.6, 1 + 0.008t)
  - spawn angle from ±20° to half the horizontal FOV over 60 s
  - after 30 s, a 25 % chance of a double drop, with spots at least 0.5 m apart
- [ ] 4. **LandingMarker upgrade:** shrinks from 0.3 m to basket size as the fruit falls; red for rotten fruit.
- [ ] 5. **Sim test:**
  - the curve at t = 0, 30, 60, 90 and 120 s
  - 1,000 picks within ±2 % of the shares
  - a scripted 3-minute round gets harder
- [ ] 6. **Version end:** V0.6.

## V0.7 Feedback and polish

- [ ] 1. **Sounds**, generated as WAV by an editor script: catch pop, miss thud, 3D spawn cue, rotten buzz, game-over jingle.
- [ ] 2. **Effects:**
  - catch sparkle, and a miss splash in the fruit's colour
  - basket bounce on catch
  - about 40 ms of vibration on a miss or a rotten catch
- [ ] 3. **Shadow:** a darker core plus a contact ring.
- [ ] 4. **Onboarding:** a first-run "how to play" screen and a clear-space safety reminder.
- [ ] 5. **Sim test, then version end:** V0.7. A playtest with 3–5 players goes under "Waiting on user".

## V0.8 Robustness

- [ ] 1. **Pause and resume:**
  - a 3-2-1 countdown on resume after tracking loss
  - pause if the floor source drops below Plane during a round
- [ ] 2. **Memory bound:** clear the sampled point IDs past 5,000 entries.
- [ ] 3. **Clean-up:**
  - remove unused template objects and assets, only after checking nothing references them
  - product name "Fruit Drop AR", company "kiriseka", Android ID `com.kiriseka.fruitdropar`, app icon
- [ ] 4. **Automated tests:**
  - EditMode: scoring, difficulty, catch geometry
  - PlayMode: XR Simulation floor and edge
- [ ] 5. **Version end:** V0.8. Realme 7 profiling and a 15-minute session go under "Waiting on user".

## V1.0 Release candidate (needs the user)

- [ ] Fix what the phone tests and playtests found.
- [ ] Final APK and iOS Xcode export.
- [ ] Update the report and deck in `Reports/`, local only.
- [ ] Full v1.0 release.

## Resume prompt (for scheduled tasks)

> Continue the Fruit Drop AR gameplay work in the Unity project `C:\Users\Pongo\My project (1)` on git branch `gameplay`.
> 1. Read `Docs/GAMEPLAY_PLAN.md`. It holds the rules, the steps, Status and Next step. Read the memory index too.
> 2. Make sure Unity is open with Unity MCP reachable (try `Unity_GetConsoleLogs`). If it isn't, start `C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe -projectPath "C:\Users\Pongo\My project (1)"` and wait for it.
> 3. Run `git status`. If there are uncommitted changes from an unfinished step, review them, then finish or revert that step.
> 4. Do the Next step, then the following ones, one step at a time, following the working rules in that file:
>    - verify in XR Simulation, commit per step, tick the plan
>    - check usage before each step; at 80 % or more, schedule the next resume task the same way and stop
>    - at each version end, build the APK, send it to the user, tag it and publish a pre-release, then continue
>    - never merge to `main`, delete tags, or claim phone results without the user
>    - no "built in one day" wording
> 5. Stop at V1.0 or at anything that needs the user, and say what is needed.
