# Fruit Drop AR: gameplay plan (V0.4 → V1.0)

This file is the single source of truth for the gameplay phase. Any session that picks the work up reads it first, does the **Next step**, ticks it here with the measured values, and commits.

## Status

- **Frozen version:** v0.3.1 on `main` (V0.3 game plus the iOS build settings). Tags `v*` are locked on GitHub, so they cannot be moved or deleted.
- **Work branch:** `gameplay`.
- **Next step:** V1.0 polish step 4 (start and Game Over screens).
- **Goal of this run** (the user, 2026-11-22): polish the game until it is finished and test it in XR Simulation only; the phone test is no longer a condition. Then the final `v1.0` release and the merge into `main`. The report and deck stay out of this run.
- **Goal of the current run** (agreed with the user): finish V0.6, V0.7 and V0.8 in XR Simulation, then build a v1.0 release candidate (`v1.0-rc1`: APK and iOS export). The full v1.0 release and the merge into `main` wait for the user's phone test. The user allowed GitHub pre-releases to be published from an interactive session. The report and deck are not part of this run.
- **Waiting on user:**
  - Phone test of release candidate 2 (`Builds/V1.0-rc2.apk`, also on the v1.0-rc2 GitHub pre-release; it replaces rc1) on the Realme 7, with the 11-point checklist in its release notes. It is the "Fruit Drop AR" app (new app ID since V0.8). Nothing since V0.3 has been tested on a phone, so it covers the V0.5 points (Start button, score bar, hearts, countdown, Game Over panel), the V0.6 points (telling the fruits apart, rings, difficulty, doubles), the V0.7 ones, and V0.8's: the resume countdown, any pauses that come by themselves, and a 15-minute session with profiling.
  - V0.7 points that only a person can judge: how the five sounds sound (also as WAVs in `Assets/Audio/`), whether the 40 ms vibration is felt, whether the sparkles and splash are large enough, and the shadow on a real floor.
  - A playtest with 3–5 players.
- **Sim workflow:** Unity_RunCommand refuses code that enters Play mode or builds. Run a scenario with Unity_ManageMenuItem on `Fruit Drop/Sim/<scenario>` (or `Fruit Drop/Sim/All` for the whole regression), then wait for a line "All done" in `Temp/SimTest.log`. A menu item that is new needs one `AssetDatabase.Refresh()` through Unity_RunCommand first. For builds, see V0.5 step 5.

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
- [x] 2. **Full game states:**
  - Scanning, Ready, Countdown (3-2-1), Playing, Paused (tracking lost), GameOver, then Restart
  - Start is allowed only on a plane floor
  - **Done.** ScoreManager is on the `Game` object, wired to the spawner, GameStateManager and ARStatusText. The score resets when the countdown starts, so the HUD shows full lives during it. The countdown holds while the basket is hidden. Losing the last life stops the spawner and clears falling fruit. `Restart()` goes straight to the countdown when the floor is still good, otherwise back to Scanning. `m_RestartButton` is optional until step 3 adds the panel.
  - New hints "Get ready..." and "Game over"; debug lines add `Score: n   Lives: n`.
  - **Sim scenario `flow`:**
    - Start before any plane: still Scanning; floor y 0.006 after the scan
    - Start: Countdown, 2.99 s to go, not spawning, score 0, 3 lives
    - tracking off during the countdown: held (moved 0.000 s in 1 s), then Playing 0.99 s after tracking came back
    - Start pressed mid-round or at Game Over: ignored
    - tracking off while playing: Paused after 1.0 s, back to Playing 0.1 s after it returned
    - looking down: Game Over after 3 misses, 0 lives, spawner stopped, no fruit left, nothing more dropped in 3 s
    - Restart: Countdown with score 0, 3 lives, streak 0; Playing after 3.00 s
  - `states` scenario updated for the countdown and still passes; 0 console errors.
  - Unity_RunCommand now refuses code that enters Play mode ("User interactions are not supported"). The scenario is queued through SessionState key `SimTestDriver.Pending` and Play mode is entered with Unity_ManageEditor `Play`.
- [x] 3. **Game HUD** (uGUI + TMP, inside the safe area):
  - score, ×combo, hearts for lives
  - countdown text; Start button
  - Game Over panel with score, best and Restart
  - hide the leftover template UI
  - **Done.** On Status Canvas, a `Safe Area` object (`SafeAreaFitter.cs`) now holds the status band, the Start button and the new HUD, run by `GameHUD.cs`:
    - `Round Bar` (forest, 70 %) under the status band: score left, ×multiplier in lemon (hidden at ×1), three red hearts right; lost hearts dim to 25 %. Shown from the countdown to Game Over.
    - `Countdown Text`: lemon 3-2-1, size 320, forest outline.
    - `Game Over Panel` (forest, 92 %, 820 × 820): "Game Over", the score, "Best n" or "New best!", and a lemon "Play again" button wired to `GameStateManager.Restart`.
    - Heart sprite `Assets/Textures/Heart.png`, generated from the heart curve, because LiberationSans has no ♥ glyph. `ScoreManager.isNewHighScore` added.
    - The template UI canvas was already inactive.
  - **Fix found on the way:** Status Canvas had no GraphicRaycaster, so on a phone a tap could not reach the Start button. The sim tests pressed it through `onClick`, which skips raycasting. Added one; the driver now checks taps with `CanTap` (EventSystem raycast at the button centre). This affects V0.4 on the phone.
  - **Sim scenario `hud`:**
    - a tap reaches Start when Ready, and Restart at Game Over
    - countdown "3" with the bar showing score 0, no combo, 3 full hearts
    - camera following the fruit for 16 s: 7 caught, score 90 = hand-worked 5×10 + 2×20; HUD shows "90" and "×2"
    - misses: hearts 1/1/0.25, then 1/0.25/0.25, combo cleared
    - Game Over: panel shown, bar hidden, final "90", "New best!" (best 0 → 90)
    - Restart: panel hidden, score "0", hearts full, countdown "3", then Playing
    - captures `Temp/SimTest-hud-{countdown,playing,gameover}.png` look right; 0 console errors
  - In the editor the debug lines spill below the status band, under the round bar. On the phone, with debug off, only the hint line shows. The portrait layout on the phone still needs a look.
- [x] 4. **Sim test:**
  - a scripted round with planned catches and misses; the score matches a hand-worked total
  - 3 misses lead to Game Over; Restart resets
  - the best score survives leaving and re-entering Play mode
  - **Done.** Driver helpers `ScanAndStart` and `PlayPlan` play a round to a plan of C (catch: basket under the next fruit) and M (miss: look straight down).
  - **Sim scenario `score-round`** (editor best set to 40 first):
    - plan `CCCCCCMCCMCM`, got `CCCCCCMCCMCM`
    - score 100, matching the hand-worked 5×10 + 1×20 (×2) + 2×10 + 1×10
    - the third miss ends the round: Game Over, 0 lives, panel "100" and "New best!", best 40 → 100
    - Restart: Countdown with score 0, streak 0, 3 lives, ×1, HUD "0" and full hearts, best still 100; then Playing
  - **Sim scenario `best`** in a new Play mode: best 100 loaded (saved 100); plan `CMMM` played as planned; Game Over on 10 with "Best 100" on the panel.
  - 0 console errors. The editor's saved best is left at 100.
- [x] 5. **Version end:** V0.5.
  - **Done.** `Builds/V0.5.apk`, 35.9 MB: 0 errors, 359 warnings (the same package shader notes as V0.4). The IL2CPP build took 3.6 min. `gameplay` pushed and tagged `v0.5`.
  - The GitHub pre-release was not published at first: the automated session's permission check refused `gh release create`. It was published later from an interactive session, with the notes and phone checklist from `Builds/V0.5-release-notes.md` (not in git).
  - Unity_RunCommand also refuses `BuildPipeline.BuildPlayer` now, so builds go through the new editor menu item **Fruit Drop > Build Android APK** (`Assets/Editor/BuildTools.cs`). Set `BuildTools.apkPath` through Unity_RunCommand, run the menu item with Unity_ManageMenuItem, and poll `Temp/Build.log` for a line starting with `Succeeded` or `Failed`.

## V0.6 Variety and difficulty

- [x] 1. **FruitType assets for all six types:**

  | Fruit | Points | Fall speed | Share of spawns | Note |
  |---|---|---|---|---|
  | Red apple | 10 | 0.8 m/s | 40 % | |
  | Green apple | 15 | 0.9 m/s | 25 % | |
  | Orange | 20 | 1.0 m/s | 20 % | |
  | Lemon | 30 | 1.2 m/s | 10 % | |
  | Golden | 100 | 1.0 m/s | 3 % | adds 2 to the streak |
  | Rotten | −20 and −1 life when caught | 0.9 m/s | 2 %, rising to 10 % by 120 s | a missed rotten fruit costs nothing |

  - new prefabs, plus golden and rotten materials
  - **Done.** Prefabs `GreenApple`, `Orange`, `Lemon`, `GoldenApple` and `RottenApple` are copies of `RedApple.prefab` with the basket's own shapes at ×1.5 (the orange and lemon have no stem). Data assets with the same names are in `Assets/Data/`, with the table's values.
    - Sizes (width × height × depth) and FruitType radius, measured as pivot to the lowest point:
      green apple 0.105 × 0.110 × 0.105 m, 0.048; orange 0.117 m ball, 0.058; lemon 0.078 × 0.078 × 0.112 m, 0.039; golden apple 0.113 × 0.118 × 0.113 m, 0.052; rotten apple 0.113 × 0.100 × 0.113 m, 0.044. The red apple keeps its 0.056.
    - `Fruit_Golden`: gold FFC21A, smoothness 0.85, a light glow. `Fruit_Rotten`: olive brown, matte. The rotten apple is also flatter and has four dark spots (`Fruit_RottenSpot`), so it differs in more than colour.
    - `FruitType` gained a `color` field, set from each material, for the V0.7 splash.
    - **Check:** a side-by-side render of the six prefabs shows each one clearly different; no colliders on any; 0 compile errors. The spawner still drops only red apples until step 2.
- [x] 2. **Weighted random pick**, with the rising rotten share.
  - **Done.** `FruitSpawner` now holds a list of types. `FruitSpawner.PickType(types, hazardShare, roll)` is a static function with the random number passed in, so it can be checked without Play mode: fruit to avoid gets its share first and the other types split the rest by weight.
  - New `DifficultyCurve` ScriptableObject (`Assets/Data/DifficultyCurve.asset`), for now with the rotten share only: 2 % rising to 10 % by 120 s. The spawner keeps a round clock that stops while paused.
  - `FruitSpawner.QueueTypes` lines up types ahead of the random picks, for scripted rounds whose score has to be known beforehand (step 5).
  - **Edit-mode check** (seed 2026), shares in % for red / green / orange / lemon / golden / rotten:
    - t = 0, expected 40 / 25 / 20 / 10 / 3 / 2: 1,000 picks gave 39.9 / 24.6 / 18.9 / 11.0 / 3.6 / 2.0; 100,000 picks gave 39.8 / 25.2 / 19.9 / 10.0 / 3.1 / 2.0
    - t = 120 s, expected 36.7 / 23.0 / 18.4 / 9.2 / 2.8 / 10: 1,000 picks gave 36.7 / 22.7 / 17.6 / 8.8 / 3.4 / 10.8; 100,000 picks gave 36.5 / 23.3 / 18.3 / 9.0 / 2.8 / 10.1
    - the share stays 10 % after 120 s; a roll of 0 and a roll just under 1 both return a type; an empty list returns nothing
  - **Sim scenario `play`:** 20 s aimed: 10 dropped, 9 caught, 0 missed; looking down: 2 dropped, 2 missed; spots 0.88–1.67 m away, at most 15.0° off; 5 of the 6 types appeared in 12 drops; 0 errors. The capture shows a rotten apple falling beside the basket.
- [x] 3. **`DifficultyCurve`:**
  - interval(t) = max(0.7, 2.0 − 0.015t)
  - speed(t) = min(1.6, 1 + 0.008t)
  - spawn angle from ±20° to half the horizontal FOV over 60 s
  - after 30 s, a 25 % chance of a double drop, with spots at least 0.5 m apart
  - **Done.** `DifficultyCurve` now holds all five functions of round time; the spawner reads the gap, spread, speed factor and double-drop chance from it at every drop. `Fruit.speed` and the `FruitSpawner.fruitSpawned` event are new.
  - The spread widens to half the horizontal field of view, read from the camera's projection matrix: on a phone AR sets that matrix from the real camera and leaves `Camera.fieldOfView` alone. On a phone that sees less than 20° to each side the spread stays at 20°.
  - **Added for fairness:** the second fruit of a double starts high enough to reach the floor 0.7 s after the first (never below the rooftop). Two fruits 0.5 m apart that land together cannot both be caught.
  - **Sim scenario `curve`:** at t = 0 / 30 / 60 / 90 / 120 s the gap is 2.00 / 1.55 / 1.10 / 0.70 / 0.70 s, the speed factor 1.00 / 1.24 / 1.48 / 1.60 / 1.60, the spread 20 / 25 / 30 / 30 / 30° (for a view of 30° to each side), the rotten share 2 / 4 / 6 / 8 / 10 %, and the double chance 0 / 25 / 25 / 25 / 25 %. All as worked by hand.
  - **Sim scenario `long-round`:** three minutes through the spawner alone (no lives), the camera going to the fruit that lands next and leaving rotten fruit alone. The editor's Game view sees 52.7° to each side.

    | Part of the round | Drops (doubles) | Gap, measured (curve) | Speed factor | Widest spot (limit) | Caught | Missed |
    |---|---|---|---|---|---|---|
    | 0–30 s | 17 (0) | 1.77 s (1.77) | 1.12 | 29.1° (36.3°) | 15 | 0 |
    | 30–60 s | 22 (4) | 1.37 s (1.30) | 1.37 | 42.6° (52.7°) | 24 | 0 |
    | 60–90 s | 31 (9) | 0.94 s (0.85) | 1.58 | 51.6° (52.7°) | 35 | 0 |
    | 90–120 s | 40 (5) | 0.75 s (0.70) | 1.60 | 51.8° (52.7°) | 41 | 1 |
    | 120–150 s | 41 (12) | 0.73 s (0.70) | 1.60 | 51.9° (52.7°) | 46 | 2 |
    | 150–180 s | 43 (12) | 0.70 s (0.70) | 1.60 | 49.5° (52.7°) | 48 | 5 |

    - Every fruit's speed factor was exactly on the curve and no spot was past the spread limit.
    - 42 doubles in 177 drops after 30 s (24 %); closest pair 0.52 m apart; shortest time between the two landings 0.70 s.
    - 9 drops were skipped for lack of floor at the wider angles, which is why some measured gaps are longer than the curve.
    - No rotten fruit was caught; 0 errors. The misses appear only late, so the round does get harder, even for a camera that aims perfectly.
- [x] 4. **LandingMarker upgrade:** shrinks from 0.3 m to basket size as the fruit falls; red for rotten fruit.
  - **Done, with the sizes changed** (agreed with the user in the run plan): the ring starts at twice the catch size, 0.60 m across, and closes in to the 0.30 m catch size as the fruit comes down. So it is easier to spot from afar, its size shows how long is left, and on arrival it still means "a basket whose centre is inside catches the fruit". `Fruit.fallProgress` (0 on the rooftop, 1 on the floor) drives it.
  - Under fruit to avoid the ring is red E63836, set through a `MaterialPropertyBlock`, so all rings still share one material.
  - **Sim scenario `marker`** (a red apple and a rotten apple falling side by side):
    - both rings 0.0000 m from under their fruit and 0.0040 m above the floor
    - red apple 0.72 m up: ring 0.381 m across (worked by hand 0.381), colour F4C430
    - rotten apple 0.50 m up: ring 0.355 m across (worked by hand 0.355), colour E63836
    - 0.597 m across two frames after the start, 0.300 m just before landing, hidden after landing
    - the capture shows the red and the yellow ring side by side
  - **Sim runs are now started from the menu:** `Fruit Drop > Sim > <scenario>` or `All` (`SimTestDriver.RunQueue`). It compiles changed scripts, plays each scenario in its own Play mode session, and ends `Temp/SimTest.log` with "All done". This replaces setting SessionState through Unity_RunCommand.
- [x] 5. **Sim test:**
  - the curve at t = 0, 30, 60, 90 and 120 s
  - 1,000 picks within ±2 % of the shares
  - a scripted 3-minute round gets harder
  - **Done.** The whole regression (`Fruit Drop > Sim > All`, 14 scenarios, each in its own Play mode session) ran to the end with 0 errors, at 70–150 frames a second.
    - `curve` and `long-round`: as in step 3. The second three-minute round had 0 / 0 / 1 / 4 / 3 / 3 misses in its six parts, 30 doubles (closest pair 0.52 m, shortest landing gap 0.70 s) and 5 skipped drops. Doubles came out at 16 % and 24 % of the drops in the two runs: a double is dropped only when a second spot at least 0.5 m away is free on the scanned floor.
    - `picks` (seed 2026): 1,000 picks are within 1.1 points of the table at t = 0 and within 0.8 at t = 120 s; 100,000 picks within 0.3.
    - `variety`: the capture shows all six types in a row over their rings, five yellow and the rotten apple's red.
    - `score-round`, now with planned fruit types (best set to 40 first): red, red, golden, red, rotten (let fall), red, rotten (caught), red (missed), green, orange, lemon, red (missed). Planned `CCCCMCCMCCCM`, got `CCCCMCCMCCCM`; score 205, matching the total worked by hand (the list is in the scenario's comment); Game Over with 0 lives; "New best!", best 40 → 205; Restart resets score, streak, lives and HUD.
    - `best` in a new Play mode: best 205 loaded; plan `CMMM` played as planned; Game Over on 10 with "Best 205".
    - `hud` (red apples only, best cleared first): 7 caught for 90 = 5×10 + 2×20; hearts dim on each miss; "New best!" 0 → 90; taps reach Start and Play again; Restart leads to the countdown.
    - V0.4 and V0.5 scenarios `floor-edge`, `drop`, `marker`, `spawner`, `play`, `states` and `flow` behave as before: floor y 0.004–0.006, a caught fruit after 3.03 s, a miss 0.055 m above the floor, a paused fruit moving 0.0000 m.
  - **Fixes found on the way:**
    - `GameStateManager.Restart` checked the floor once. Landing spots are random, so on a small floor one unlucky check sent the player back to "Tap Start" instead of the countdown. It now tries as often as the Ready state does (4 times).
    - Unity does not run `EditorApplication.delayCall` while the editor is in the background, so a chain of scenarios stalled after the first one whenever another window was in front. The driver and the build menu item now hand over on `EditorApplication.update`. Asking to enter Play mode while an exit was still pending also cancelled the exit; the driver now checks `isPlaying` too.
    - Scenarios that start the spawner without a round (`spawner`, `long-round`) switch the ScoreManager off, because it scored every fruit and left the editor with a best score of 11,375. The driver sets the saved best before `hud` (none) and `score-round` (40).
    - Each scenario's "Done" line now shows play time, real time and frames a second, so a held-back editor is visible in the log.
- [x] 6. **Version end:** V0.6.
  - **Done.** `Builds/V0.6.apk`, 35.9 MB: 0 errors, 351 warnings (package shader notes, as before). The IL2CPP build took 4.3 min. `gameplay` pushed, tagged `v0.6`, and published as a GitHub pre-release with the notes and phone checklist from `Builds/V0.6-release-notes.md` (not in git).

## V0.7 Feedback and polish

- [x] 1. **Sounds**, generated as WAV by an editor script: catch pop, miss thud, 3D spawn cue, rotten buzz, game-over jingle.
  - **Done.** `Assets/Editor/SoundGenerator.cs` (menu **Fruit Drop > Generate Sounds**) makes the five sounds from formulas and writes 16-bit mono WAVs to `Assets/Audio/`: `SpawnCue` 0.16 s, `CatchPop` 0.12 s, `MissThud` 0.28 s, `RottenBuzz` 0.32 s, `GameOver` 1.10 s (three notes going down). Running it again keeps the file names, so nothing has to be wired again.
  - `GameAudio.cs` on the `Game` object listens to `FruitSpawner.fruitSpawned`, `fruitLanded` and `GameStateManager.stateChanged`. The cue is 3D, placed where the fruit appears, from a pool of 4 sources at half volume; the other sounds are 2D. Rotten fruit left to fall makes no sound. The jingle starts 0.4 s after the last miss, so it does not cover the thud. The Main Camera already had the AudioListener.
  - **Sim scenario `feedback`:** planned red (catch), red (miss), rotten (catch), rotten (let fall), red (miss); got `CMCMM` and Game Over. Sounds in order: CatchPop, MissThud, RottenBuzz, MissThud, GameOver, as planned. 6 cues for 6 fruits, each 0.000 m from where its fruit appeared. 0 errors.
  - **Not checked:** how the sounds actually sound. They were tested by event, not by ear. Listening to them (on the phone, or the WAVs in `Assets/Audio/`) is under "Waiting on user"; each is one formula in `SoundGenerator.cs` if one needs changing.
- [x] 2. **Effects:**
  - catch sparkle, and a miss splash in the fruit's colour
  - basket bounce on catch
  - about 40 ms of vibration on a miss or a rotten catch
  - **Design notes:**
    - Build it like `GameAudio`: one component on the `Game` object that listens to `FruitSpawner.fruitLanded`, with pooled particle systems. The sparkle goes at the basket rim, the splash at the landing spot, tinted with `FruitType.color`. No splash for rotten fruit left to fall is fine; a small one is fine too.
    - The bounce must scale a child of the basket, never its root: `FloorBasketFollower` owns the root's pose every frame.
    - 40 ms needs the Android vibrator through `AndroidJavaObject` and the `VIBRATE` permission (`Handheld.Vibrate` is a fixed, much longer buzz). It does nothing in the editor, so how it feels goes under "Waiting on user".
    - Extend the `feedback` scenario: one effect per event, and the basket back at its normal size afterwards.
  - **Done.** `CatchEffects.cs` on the `Game` object listens to `FruitSpawner.fruitLanded`. The two particle systems are set up in code (no effect prefabs) and use `Assets/Materials/EffectParticle.mat`, a copy of URP's unlit particle material.
    - Catch: 16 lemon sparkles rising from the basket, and the basket squashes by 15 % for 0.25 s. Miss: a 16-particle splash in the fruit's colour where it landed, plus the vibration. Rotten catch: a splash in its colour at the basket, the bounce and the vibration. Rotten fruit left to fall: a splash only.
    - The bounce scales the basket's children except the shadow, never the root.
    - Vibration: 40 ms through the Android vibrator, with `Handheld.Vibrate` as the fallback (which also makes Unity add the VIBRATE permission). It does nothing in the editor.
    - **Sim scenario `feedback`** (red catch, red miss, rotten catch, rotten left to fall, red miss): effects in order Sparkle, Bounce, Splash, Vibration, Splash, Bounce, Vibration, Splash, Splash, Vibration, as planned. The basket squashed to 0.850 of its height at most and was back at (1.000, 1.000, 1.000) at the end. Sounds and cues as in step 1; 0 errors. The capture shows the sparkles above the basket just after a catch.
    - **Not checked:** the splash on the floor was not seen in a capture (the camera looks down during a planned miss), and the vibration cannot be felt in the editor. Both, and whether the particles are large enough on a phone, go under "Waiting on user".
- [x] 3. **Shadow:** a darker core plus a contact ring.
  - **Design note:** `BasketShadow.png` was made by a one-off script that was not kept. Add a kept editor menu item that regenerates it, as `SoundGenerator` does for sounds.
  - **Started, not finished.** `Assets/Editor/ShadowGenerator.cs` (menu **Fruit Drop > Generate Shadow Texture**) is committed and its menu item was run once successfully, but the texture in the repository is still the V0.3 one.
    - The V0.3 texture, measured (darkness by distance from the middle, 1 = edge of the 0.42 m quad): 0.70 from the middle to 0.55, then 0.66 at 0.62, 0.53 at 0.7, 0.32 at 0.8, 0.12 at 0.9, 0 at 1. The basket's base ends at 0.62.
    - A first formula came out lighter than that everywhere (0.60 in the middle, 0.24 at 0.7), so that texture was discarded.
    - The formula in the file now keeps the V0.3 falloff and adds to it: about 0.85 in the middle, 0.76 at 0.55, a contact ring of about 0.87 just outside the base, and from 0.7 outward the same as V0.3. These constants have not been compiled or run yet.
  - **Done.** The corrected formula was run: `BasketShadow.png` is now 256 px (was 128), with darkness 0.85 in the middle, 0.76 at 0.55, 0.86 on the contact ring just outside the base, then 0.53 at 0.7, 0.29 at 0.8 and 0.09 at 0.9 (V0.3: 0.53, 0.32, 0.12). Same file and material, so nothing was rewired.
    - **Check:** in the `marker` capture the basket has a darker edge where its base meets the floor, inside the same soft shadow as before. `floor-edge` reads floor y 0.005 as before. The ring is subtle on the grey sim floor; whether it is right on a real floor is for the phone test, and it is three numbers in `ShadowGenerator.Darkness`.
- [x] 4. **Onboarding:** a first-run "how to play" screen and a clear-space safety reminder.
  - **Done.** `HowToPlayPanel.cs` on the `Safe Area` object builds the panel in code on a first run: a dim layer over the HUD, a forest card with "How to play", four numbered steps, the safety reminder in lemon, and a lemon "Got it" button. It is sized with anchors and its text shrinks to fit, so it suits any screen shape. PlayerPrefs key `FruitDropAR.SeenHowToPlay` remembers that it was read.
  - **Sim scenarios `onboarding` and `onboarding-again`:**
    - first run: panel shown, read flag 0; with the game Ready, a tap on Start does not reach it and a tap on "Got it" does
    - after "Got it": panel gone, read flag 1, a tap on Start reaches it
    - next Play mode session: panel not shown, read flag 1
    - the capture shows the whole panel readable in the editor's wide view; the portrait layout on the phone is for the phone test
  - The driver marks the panel as read before every other scenario, as planned in the design notes.
  - **Design notes:**
    - A panel under `Safe Area`, shown until "Got it" is tapped, remembered with a PlayerPrefs flag. It covers the Start button.
    - The sim scenarios press buttons through `onClick`, which a covering panel does not stop, but `CanTap` checks would fail. Have the driver set the flag before every scenario except the one that tests the panel, the way it sets the best score in `NextInQueue`.
- [x] 5. **Sim test, then version end:** V0.7. A playtest with 3–5 players goes under "Waiting on user".
  - **Done.** The whole regression (`Fruit Drop > Sim > All`, now 17 scenarios) ran to the end with 0 errors at 76–166 frames a second: floor y 0.004–0.006; `score-round` 205 as worked by hand with "New best!"; `best` loads 205; `hud` 90 with "New best!"; `feedback` and both `onboarding` scenarios as in steps 2 and 4; `long-round` on the curve with 44 doubles (closest pair 0.50 m, shortest landing gap 0.70 s) and 0 / 0 / 2 / 6 / 3 / 2 misses in its six parts.
  - `Builds/V0.7.apk`, 36.0 MB: 0 errors, 351 warnings, built in 4.2 min. The APK lists the `VIBRATE` permission (checked with `aapt dump permissions`). `gameplay` pushed, tagged `v0.7`, and published as a GitHub pre-release with the notes and phone checklist from `Builds/V0.7-release-notes.md` (not in git).

## V0.8 Robustness

- [x] 1. **Pause and resume:**
  - a 3-2-1 countdown on resume after tracking loss
  - pause if the floor source drops below Plane during a round
  - **Design note:** add a `Resuming` state at the end of the `GameState` enum (so saved values keep their meaning). It keeps the score and the spawner as they are, shows the HUD countdown, and fruit stays held until it ends.
  - **Done.** After a pause the game goes Paused → `Resuming` (3 s, the HUD's 3-2-1, hint "Get ready...", fruit still held) → Playing. Losing tracking during the resume countdown goes back to Paused, and a fresh countdown follows.
  - A round also pauses when the floor is not a plane for more than 0.5 s while the basket is still shown, and resumes only once a plane floor is back. `FloorBasketFollower` keeps a plane floor once it has one and only starts over after a long tracking loss, so in practice this covers the moment tracking returns but the floor is still an assumed one; before, the round carried on there.
  - **Sim scenarios:**
    - `states`: tracking off for 4 s holds the fruit (0.0000 m); tracking back → Resuming after 0.1 s, countdown "2" one second in with the fruit still held (0.0000 m in 1 s); Playing 2.95 s later, fruit moving again (0.504 m in 0.5 s)
    - `flow`: Paused 1.0 s after tracking goes; Resuming 0.1 s after it returns (2.90 s to go, spawner still paused); tracking off again during the countdown → Paused; back → a fresh countdown of 2.95 s, Playing after 3.0 s
    - `flow`, floor staged by hand (the follower switched off with its floor source set through reflection, because the simulation cannot lose the floor mid-round): not a plane for 0.3 s → still Playing; for longer → Paused after 0.51 s with the basket shown and the floor Assumed; plane back → Playing after 3.0 s
    - `hud` unchanged: 90 as worked by hand, hearts, "New best!", Restart to the countdown; 0 errors
  - **For the phone test:** whether the floor ever flickers off a plane during a real round, which would show as unexpected pauses.
- [x] 2. **Memory bound:** clear the sampled point IDs past 5,000 entries.
  - **Done.** `FloorBasketFollower` forgets all remembered point IDs once there are more than 5,000. The points still in view are then sampled once more; the samples themselves are a fixed ring of 500.
  - **Sim scenario `point-memory`** (the set filled by hand through reflection, since the simulation has about ten points): 5,000 remembered → kept; 5,001 → 0 after the next sampling. The floor and the basket were unaffected.
- [x] 3. **Clean-up:**
  - remove unused template objects and assets, only after checking nothing references them
  - product name "Fruit Drop AR", company "kiriseka", Android ID `com.kiriseka.fruitdropar`, app icon
  - **Notes:** the iOS ID already is `com.kiriseka.fruitdropar`. With the new Android ID the app installs beside the older builds instead of over them, and the best score starts again at 0; say so in the release notes. Set the version name to match the tag from here on (it is still 1.0.0).
  - **Done.**
    - Removed from the scene, each only after a scan of every other component's references found none pointing into it: the inactive template `UI` canvas (231 objects: object menu, options, coaching, greeting, debug menu) and the inactive `Object Spawner`. The scene file went from about 11,300 changed lines to 1,700.
    - Kept: the template's active touch-input objects under `Camera Offset` (they are part of the XR rig), and the template asset folders. Assets that nothing in the scene uses are not put into a build, so deleting those folders would only risk breaking the rig.
    - Identity: product name "Fruit Drop AR", company "kiriseka", Android ID `com.kiriseka.fruitdropar`, version name 0.8.0, Android version code 8.
    - Icon: `Assets/Editor/IconGenerator.cs` (menu **Fruit Drop > Generate App Icon**) renders the game's own basket on the lemon colour to `Assets/Textures/AppIcon.png` (1024 px) and sets it as the default icon.
    - **Check:** the whole regression (18 scenarios) passed on the cleaned-up scene with 0 errors; the icon render shows the basket whole and centred.
    - `ProjectSettings.asset` is committed with the identity lines only; its `preloadedAssets` churn stays out, as before.
- [x] 4. **Automated tests:**
  - EditMode: scoring, difficulty, catch geometry
  - PlayMode: XR Simulation floor and edge
  - **Done.** The game scripts are now the assembly `FruitDropAR` (`Assets/Scripts/FruitDropAR.asmdef`) and the editor tools `FruitDropAR.Editor`, so that test assemblies can refer to them. Scene and prefab links are by file ID and were not affected.
  - `Assets/Tests/EditMode` (36 tests, 0.4 s):
    - `ScoreManagerTests`: the rising multiplier and its cap at 5, a miss, the golden apple's streak bonus, rotten fruit left to fall and caught, the score never below 0, the round ending once on the third lost life, the planned 205-point round of the sim test, a new round keeping the best score. The editor's saved best score is put back after each test.
    - `DifficultyAndPickTests`: the curve against the table at 0, 30, 60, 90, 120 and 600 s, that it only ever gets harder, doubles starting at 30 s, a narrow phone, time before the round; pick shares at the start and after two minutes (100,000 seeded picks within 0.5 points), the ends of the roll, an empty list, only good fruit, a type with no weight.
    - `CatchDetectorTests`: through the middle, just inside and outside the radius, a step that stays above or starts below the rim, moving up, a very long step, a slanted path judged where it crosses the rim, the basket's own position.
  - `Assets/Tests/PlayMode` (1 test, 4.0 s): in XR Simulation the plane floor is found, its height is 0 ± 0.03 m, the basket stands on it, a point 10 m away is not floor, and clamping pulls it back and is stable. The Test Runner drives XR Simulation without trouble, so no fallback was needed; the `floor-edge` scenario stays as the more detailed check of the edge distances.
  - **Run them** with **Fruit Drop > Run Tests** (EditMode, then PlayMode); the result goes to `Temp/TestResults.log`, ending "Tests done". They also show in the Test Runner window.
  - **Result:** EditMode 36 passed, 0 failed; PlayMode 1 passed, 0 failed. `score-round` and `best` still pass through the menu-driven driver after the assembly split.
- [x] 5. **Version end:** V0.8. Realme 7 profiling and a 15-minute session go under "Waiting on user".
  - **Done.** `Builds/V0.8.apk`, 35.1 MB: 0 errors, 350 warnings, built in 5.0 min. `aapt dump badging` reads package `com.kiriseka.fruitdropar`, version name 0.8.0, version code 8, label "Fruit Drop AR", and the CAMERA, INTERNET and VIBRATE permissions. `gameplay` pushed, tagged `v0.8`, and published as a GitHub pre-release with the notes and phone checklist from `Builds/V0.8-release-notes.md` (not in git).
  - The full 18-scenario regression last ran after step 3. After step 4's assembly split, the automated tests and the `score-round` and `best` scenarios were run; the full regression is the first step of the release candidate.

## v1.0 release candidate

- [x] 1. **Full regression:** every sim scenario and all automated tests, with 0 console errors.
  - **Done.** `Fruit Drop > Sim > All`: 18 scenarios run, 18 finished, no FAIL line, 0 errors logged. Floor y 0.005–0.006; `score-round` 205 as worked by hand; `hud` 90; `best` loads 205; sounds, effects, onboarding, pause and resume, the floor rule and the memory bound as in their steps; `long-round` on the curve with 49 doubles (closest pair 0.50 m, shortest landing gap 0.70 s) and 0 / 1 / 3 / 6 / 5 / 3 misses in its six parts.
  - `Fruit Drop > Run Tests`: EditMode 36 passed, 0 failed; PlayMode 1 passed, 0 failed.
- [x] 2. **Builds:** `Builds/V1.0-rc1.apk`, and the iOS Xcode project exported to a new folder and zip. The v0.3.1 export in `Builds/` stays as it is.
  - **Done.** Version name 1.0.0, Android version code 10, iOS build number 10.
  - `Builds/V1.0-rc1.apk`, 35.1 MB: 0 errors, 347 warnings, 2.1 min. `aapt` reads package `com.kiriseka.fruitdropar`, version 1.0.0 (10), label "Fruit Drop AR".
  - `Builds/iOS-v1.0-rc1` (Xcode project, bundle ID `com.kiriseka.fruitdropar`): 0 errors, 379 warnings, 2.9 min; zipped as `Builds/FruitDropAR-iOS-Xcode-v1.0-rc1.zip`. It has not been built in Xcode or run on a device. The editor went back to the Android platform afterwards.
  - New menu item **Fruit Drop > Build Android APK and Xcode Project** (`BuildTools.xcodePath` sets the folder); `Temp/Build.log` ends with "Builds done".
- [x] 3. **Tag `v1.0-rc1`** and a pre-release with both files and one phone checklist covering V0.5 to V0.8.
  - **Done.** `gameplay` pushed, tagged `v1.0-rc1`, pre-release published with the APK, the Xcode zip and the notes from `Builds/V1.0-rc1-release-notes.md` (not in git).

- [x] 4. **Portrait check before the phone test.** Every capture until rc1 came from the editor's wide Game view, but the game runs in fixed portrait.
  - New sim scenario `portrait`: the Game view is set to the Realme 7's 1080 × 2400 (and put back afterwards), the debug lines are off as on a phone (`ARStatusText.showDebugInEditor`), and the top 96 px are kept clear as for its camera hole. It captures the first-run panel, Ready, the countdown, a round and Game Over (`Temp/SimTest-portrait-*.png`) and logs where each HUD part is, whether it is inside the safe area, whether its text fits, and which parts overlap.
  - **Found and fixed:**
    - The "How to play" card half covered the Start button, whose label showed through under "Got it", and a quick second tap on "Got it" could land on Start. The card now ends above the Start button (anchors 0.20–0.88 of the safe area); Start stays visible, dimmed, under the panel.
    - The round bar covered the bottom 50 px of the hint band, leaving the hint pressed against the bar. It now starts 10 units under the band (`Round Bar` y −175 → −230).
  - **After the fixes:** all parts inside the safe area, all texts fit, the only overlaps are the card with its own button and two Game Over rectangles whose text does not touch.
  - The driver had a bug from adding this scenario (the "How to play" flag was set again right after being cleared for `onboarding`); fixed before the regression below.
- [x] 5. **Release candidate 2** with the two layout fixes: full regression, automated tests, version code 11, APK and Xcode export, tag `v1.0-rc2`, pre-release.
  - **Done.** `Fruit Drop > Sim > All`: 19 scenarios run, 19 finished, no FAIL line, no text that does not fit, 0 errors logged; `score-round` 205 and `hud` 90 as worked by hand. `Fruit Drop > Run Tests`: EditMode 36 passed, PlayMode 1 passed.
  - `Builds/V1.0-rc2.apk`, 35.1 MB: 0 errors, 350 warnings, 3.8 min; `aapt` reads `com.kiriseka.fruitdropar`, version 1.0.0 (11), "Fruit Drop AR". Xcode project `Builds/iOS-v1.0-rc2` (0 errors, 2.0 min, bundle ID `com.kiriseka.fruitdropar`, build number 11), zipped as `Builds/FruitDropAR-iOS-Xcode-v1.0-rc2.zip`.
  - `gameplay` pushed, tagged `v1.0-rc2`, pre-release published with both files and `Builds/V1.0-rc2-release-notes.md` (not in git). rc1 stays as it was; its tag is locked.

## V1.0 polish (sim only, agreed with the user)

The user waived the phone test on 2026-11-22: polish the game, verify it in XR Simulation, then release v1.0. The release notes must say it was not tested on a phone.

- [x] 0. **Setup.** Unity had been left with a "Scene Backup Detected" dialog from an unclean exit. The backup was the automatic copy Unity makes on entering Play mode, taken after the last saved and committed scene with no edits since, so it was moved aside (not deleted) and the editor restarted. Baseline: `floor-edge` floor y 0.005; `score-round` 205 as worked by hand.
- [x] 1. **Mobile basics:** keep the screen awake while scanning and playing; pause when the app goes to the background; the Android back key pauses and resumes a round.
  - **Done.** `GameStateManager` now has a player pause (`PauseByPlayer`, `ResumeByPlayer`, `RestartFromPause`, `isPausedByPlayer`, `playerPauseChanged`). A player pause holds the round, or the countdown, until the player resumes, even with tracking good; resuming goes through the existing 3-2-1.
    - Screen sleep: never while scanning, counting down and playing; the phone's own setting at Game Over and during a player pause.
    - `OnApplicationPause(true)` during a round is a player pause, so the round waits for the player when they come back.
    - Back key (Escape in the Input System): resumes a player pause, pauses a round, and outside a round sends the app to the background on Android (`moveTaskToBack`, so nothing is lost).
    - `FruitDropAR` and `FruitDropAR.Editor` now refer to `Unity.InputSystem`.
  - **Sim scenario `mobile`:** sleep "never" while scanning and playing; app to the background mid-round → Paused by the player, sleep back to the phone's setting, fruit 0.0000 m in 2 s, still paused 2 s after coming back with tracking good; back key → Resuming with the fruit held (0.0000 m in 1 s), then Playing with the fruit moving (0.245 m in 0.3 s); back key in a round → Paused; restart from the pause → Countdown with score 0, 3 lives, no fruit left falling; a pause in the countdown holds it (0.000 s in 1 s); Game Over → the phone's setting; back at Game Over leaves the game state alone. All three back presses arrived through the Input System (the driver lets input through without Game view focus for this scenario).
- [x] 2. **Pause menu and settings:** a pause button in rounds; a panel with Resume, Restart, sound on/off and vibration on/off, kept in PlayerPrefs.
  - **Done.** `PauseMenu.cs` on `Safe Area` builds a pause button (two white bars on forest, at the right end of the hint band, shown only in rounds) and a menu card: "Paused", Resume (lemon), Restart, "Sound: On/Off", "Vibration: On/Off". The menu follows the game's player pause, so leaving the app or the back key opens it too.
    - `GameSettings.cs`: sound and vibration, on by default, in PlayerPrefs (`FruitDropAR.SoundOn`, `FruitDropAR.VibrationOn`). Sound off sets `AudioListener.volume` to 0, which silences everything including the placed cues; `GameAudio` applies it at start. `CatchEffects` skips the vibration when it is off.
    - The hint reads "Paused" during a player pause.
    - `UiBuilder.cs` now holds the panel-building helpers and the two game colours; `HowToPlayPanel` uses it.
  - **Sim scenarios `pause-menu` and `pause-menu-again`:** pause button hidden before a round, shown and tappable in a round; the button pauses (fruit 0.0000 m in 2 s, hint "Paused") and every menu button takes taps; both switches off → listener volume 0.0 and labels "Sound: Off", "Vibration: Off"; Resume → Playing after 3.1 s; a miss with vibration off gives only the splash; Restart from the menu → Countdown, score 0, 3 lives, nothing falling. In a new session both are still off and the volume is 0.0 from the start; switched on again → 1.0.
  - `portrait`: the pause button sits inside the hint band at x 912–1035, y 2069–2192 px; the menu card and its buttons are inside the safe area and every label fits. `onboarding` and `feedback` unchanged.
  - EditMode tests: 40 passed (4 new for `GameSettings`); PlayMode 1 passed.
- [x] 3. **HUD feedback:** "+points" pop-ups, a combo call-out, a score that counts up, a punch on the multiplier, hearts that shake before they dim.
  - **Done.** `ScoreManager.fruitScored` reports how much each catch changed the score. `ScorePopups.cs` on `Safe Area` shows it as "+20" rising from the basket and fading over 0.9 s (lemon; "-20" in red for a rotten catch; "+100" larger), and "Combo ×N" under the score bar when the multiplier goes up. Pop-ups never start so high that they would rise into the score bar or the call-out.
    - `GameHUD`: the score counts to its new value over 0.3 s (and snaps to 0 for a new round); the multiplier swells to ×1.5 size and settles in 0.25 s when it goes up; a lost heart shakes and shrinks for 0.4 s, then dims.
  - **Sim scenario `hud-feedback`** (the planned 205-point round): pop-ups "+10, +10, +100, Combo ×2, +20, +20, -20, +15, +20, +30", as worked by hand. The score text read "20" as "+100" appeared, "74" 0.15 s later and "120" after 0.5 s; "205" at Game Over. The multiplier was at 1.39 size 0.05 s after "Combo ×2" and 1.00 after 0.4 s. The heart lost at the first miss was at 0.92 size and full 0.1 s after, and dimmed (0.25) at normal size 0.6 s after.
  - `portrait`: a capture just after the call-out shows "Combo ×2" under the score bar and "+10" over the basket (`Builds/V1.0-polish-popups.png`, not in git). In the editor's short landscape view the call-out crosses the basket; the game only runs in portrait.
  - `hud` and `score-round` unchanged in their results; `hud` now waits for the counting score before reading it.
- [ ] 4. **Start and Game Over screens:** title and version on the start screen, a pulsing Start button; round stats on Game Over; confetti for a new best.
- [ ] 5. **Scene polish:** fade the AR plane overlay during rounds; a sparkle trail and a chime for the golden apple.
- [ ] 6. **Balance check** with a modelled human player (120°/s turns, 0.25 s reaction, 5 cm aim error). If the median round lasts under 75 s, ease the late curve (shortest gap 0.85 s, top speed ×1.45) and measure again; otherwise keep the accepted numbers.
- [ ] 7. **Final v1.0:** full regression and tests, version code 12, APK and Xcode export, tag `v1.0`, full GitHub release, fast-forward `main` to `gameplay`.

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
>    - if `gh release create` is refused, leave the notes in `Builds/` and list the release under "Waiting on user"
> 5. Stop after the v1.0 release candidate, or at anything that needs the user, and say what is needed. Never tag `v1.0`.
