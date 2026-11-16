# Fruit Drop AR: gameplay plan (V0.4 → V1.0)

This file is the single source of truth for the gameplay phase. Any session that picks the work up reads it first, does the **Next step**, ticks it here with the measured values, and commits.

## Status

- **Frozen version:** v0.3.1 on `main` (V0.3 game plus the iOS build settings). Tags `v*` are locked on GitHub, so they cannot be moved or deleted.
- **Work branch:** `gameplay`.
- **Next step:** V0.7 step 1 (sounds).
- **Goal of the current run** (agreed with the user): finish V0.6, V0.7 and V0.8 in XR Simulation, then build a v1.0 release candidate (`v1.0-rc1`: APK and iOS export). The full v1.0 release and the merge into `main` wait for the user's phone test. The user allowed GitHub pre-releases to be published from an interactive session. The report and deck are not part of this run.
- **Waiting on user:**
  - Phone test of V0.6 (`Builds/V0.6.apk`, also on the v0.6 GitHub pre-release) on the Realme 7, with the checklist in its release notes. It includes the V0.5 points, which were never tested on a phone: the Start button, the score bar, hearts, countdown and Game Over panel. V0.4's Start button cannot receive taps (no GraphicRaycaster; fixed in V0.5 step 3).
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

## v1.0 release candidate

- [ ] 1. **Full regression:** every sim scenario and all automated tests, with 0 console errors.
- [ ] 2. **Builds:** `Builds/V1.0-rc1.apk`, and the iOS Xcode project exported to a new folder and zip. The v0.3.1 export in `Builds/` stays as it is.
- [ ] 3. **Tag `v1.0-rc1`** and a pre-release with both files and one phone checklist covering V0.5 to V0.8.

## V1.0 (needs the user)

- [ ] Realme 7 phone test, a playtest with 3–5 players, profiling and a 15-minute session.
- [ ] Fix what the phone tests and playtests found.
- [ ] Final APK and iOS Xcode export, tag `v1.0`, full release, merge `gameplay` into `main`.
- [ ] Update the report and deck in `Reports/`, local only.

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
