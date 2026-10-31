# Changelog

All notable changes to public versions of F.A.F. (Fridges Absolutely Fly) are documented here. [简体中文](CHANGELOG.zh-CN.md)

## Changelog rules

- Append versions in reverse chronological order, newest first, with Unreleased at the top; retain published history.
- Use `Unreleased — X.Y.Z` until publication, then `[YYYY-MM-DD HH:MM UTC+8] Version X.Y.Z` with the actual release time.
- Use Added, Changed, Fixed, Deprecated, Removed and Security, with Packaging and Notes where useful; omit empty sections and put breaking changes first with their impact and required action.
- For the first public version, list the features being introduced, not fixes, removals or migrations from unpublished playtests.
- Give each notable player-facing change its own entry; keep mechanisms and instructions in the guides rather than recounting development.
- Keep both languages aligned, compare later versions with the previous public version, and record the initial Workshop upload only when it actually occurs.

## Unreleased — 0.1.0

First public release: hover and rotate compatible fridges, with a Nivarian household freezer that can be progressively upgraded.

### Added

- Unlock in-place hovering and rotation through Project T.O.F.U for storage buildings recognized by Nivarian Sleep In Fridges, using only a small steel-and-component research cost with no Nivarian-specific materials; the included freezer requires the same research.
- Reduce the included freezer's effective shell mass from 45 to 5 kg through T.O.F.U, including when carried by caravans, without requiring hover to be enabled before packing; contents retain their full mass.
- Control hovering and rotation separately, with smooth lift-off, leveling and landing; retain the building's footprint, connections and storage functions without an additional power or coolant charge.
- Move sleepers, sleeping pads and contents with the fridge while retaining upstream sleep functionality and normal pawn size.
- Build a transparent Nivarian household freezer with a 1×2 footprint, or 2×1 when rotated; filter storage, uninstall, carry, reinstall or deconstruct it.
- Store 12 stacks in the refrigerated compartment, with internal temperature affecting native rot, temperature damage and hatching; inspect frozen/chilled conditions, rot rate, progress and remaining time.
- Preserve the original freezer, contents, resources and settings while packed, carried by caravans, in flight or awaiting recovery, with continued thermal control, resource use and perishables; contents contribute their actual mass.
- Make a Nivarian portable sleeping pad without research, using 45 fabric/leather stuff and 5 Nivarian scales; use it independently as a quality-bearing bedroll, with a rolled-up appearance when packed.
- Install a deployed or packed pad through actual hauling work, with confirmation before clearing a nonempty compartment; retain its material, quality and hit points.
- Give an installed pad the entire refrigerated compartment and let it provide quality, rest and medical benefits to the freezer bed; independent pads provide only quality-dependent rest.
- Restrict freezer beds to Nivarians and retain upstream sleep effects; this freezer qualifies for upstream extra icy-core recovery below an actual internal temperature of 10°C, including residual cold off-grid.
- Use a 360 Wd internal battery with 100% charging efficiency, adjacent or automatic ranged connection, grid charging/discharging and off-grid operation.
- Separate paid temperature adjustment from holding the target: apply a continuous 120 W adjustment term plus 4.8 W per adverse exterior-to-target °C in normal mode, then remove the adjustment term when the target temperature is reached. Retain helpful natural exchange and use the same actual-paid-time cooling model for electricity and coolant; unpaid time remains passive.
- Share a 5-unit Nivarian coolant reserve between backup cooling, jumps and launches; each unit supplies 360 Wd of cooling. Backup is off by default, works only when electrical cooling is unavailable, and cannot heat or supply air.
- Adjust a draggable cooling controller from −30 to 10°C, defaulting to −5°C, using a slider or numeric input; pause and change game speed outside input fields while the window remains open.
- Manage refrigerated items, transport cargo, residents and boarding/rest arrangements on a separate cargo page, with scene-appropriate search, preservation details and item transfers.
- Read battery and coolant levels in one combined gizmo and drag the refill target; share that target with manual refill and thermostat controls on installed or directly selectable packed freezers.
- Refill from whole coolant bottles in the freezer's own storage or transport cargo; have colonists physically haul outside supplies without taking passengers' private stock.
- Rest, end rest or exit through dedicated controls; confirm hazardous exits, retain residents after ordinary arrivals and avoid automatic ejection on power loss.
- Feed held residents real food from their own inventory or same-freezer cargo under normal diet rules; map-bed sleepers retain upstream snack behavior.
- Research SPICE—Storage Preservation and Integrated Cooling Engineering—through the Nivarian uplink, paying real material costs to unlock retrofit groups.
- Show household-freezer research and T.O.F.U before they are unlocked; both require Nivarian cryosynthesis, but neither requires the other. Complete both to open subsequent retrofit groups, ending with S.A.L.T.
- Halve passive heat exchange through C.H.I.V.E without lowering active thermal demand.
- Raise maximum hit points from 260 to 390 through W.A.L.N.U.T without repairing damage.
- Expand refrigerated storage from 12 to 24 stacks through O.N.I.O.N without reducing cargo mass.
- Expand coolant capacity from 5 to 8 units through B.A.S.I.L without supplying free coolant.
- Unlock heating, a −30 to 40°C smart thermostat and cabin-atmosphere maintenance through C.R.E.A.M; view internal/external conditions, resources, consumption and endurance in a screen-adaptive window.
- Maintain cabin atmosphere with 80 W of electricity when thermal control is enabled for a living resident or explicitly arranged rest preparation; pressure drifts toward the exterior after power loss, and residents experience the actual cabin temperature and vacuum.
- Raise battery capacity from 360 to 720 Wd, reduce self-discharge from 5 to 4 W, and unlock reduced-bobbing steady hover and editable bidirectional rotation speed through G.A.R.L.I.C, without adding charge.
- Add nozzle hardware and optional rapid thermal control through P.E.P.P.E.R, tripling active thermal output and its corresponding electricity/cooling-coolant use.
- Jump up to 35 map cells through H.O.P.S for `0.05 + 0.003 × distance` coolant, preview the current freezer's shape and landing orientation with a building ghost during targeting, and animate the original freezer's takeoff and landing; at normal game speed, use 9 cells per second with a one-second minimum, about 3.9 seconds at maximum range.
- Unlock optional automatic replenishment through D.I.L.L, off by default, using whole bottles from the freezer's own stock first; have colonists haul outside supplies only after that stock is exhausted and refilling is still needed.
- Unlock the single-occupant space-shuttle appearance, grouped map departures and whole-caravan world launch through S.A.L.T; other races may board but cannot use Nivarian freezer beds.
- Allow 0 or 1 living occupants per freezer, with animals using the same single berth; carry corpses as cargo only when no living occupant is aboard.
- Apply one 250 kg net-payload budget to refrigerated storage and transport, excluding the shell but including the installed pad, passenger, equipment and carried items; limit accepted quantities by both mass and stack space, without counting internal transfers twice. Retain existing overloaded contents for unloading and block added loads or departure until eligible; also check berths, fuel and completed loading.
- Fly up to 60 destination-layer surface tiles for `0.20 + 0.035 × distance` coolant per freezer, with one-way travel that does not require reserved return fuel.
- Open orbital routes with Odyssey and C.R.E.A.M, up to 10 destination-layer orbital tiles; surface-to-orbit costs `1.00 + 0.70 × distance`, orbit-to-orbit costs `0.20 + 0.70 × distance`, and returns to the surface use surface-route costs.
- Use the original departure direction for world-flight timing and path, with a 0.00015 rad/tick peak cap, smooth acceleration and braking, and a shared departure/in-flight estimate. At normal game speed, same-layer travel has a 20-second minimum; ascent adds 90 seconds and descent 15 seconds to angular travel time without the same-layer minimum. Preserve saved plans already in flight. Share a 2500-tick per-freezer cooldown between jumps and launches and display its remaining time.
- Enable autonomous cargo and player-animal flights with both S.A.L.T and D.I.L.L; ordinary autonomous routes require generated, receivable maps and do not create empty or animal-only walking caravans.
- Gift actual contents on autonomous delivery to a non-hostile foreign settlement while leaving the original freezer, pad and built-in resources for recovery; require confirmation to relinquish the freezer and entire load at a hostile settlement.
- Keep unreceivable payloads and flights with no survivors at persistent freezer recovery sites, allowing another caravan to recover accepted portions; retain the original remainder across saves without recreating delivered contents.
- Provide English and Simplified Chinese text, stage-specific freezer appearances, dedicated action icons and both English and Chinese covers.
- Offer four independent developer actions to fill/empty the battery and fill/empty coolant.

### Packaging

- Include separate RimWorld 1.6/1.7 assemblies and provide dev, Steam upload-directory and GitHub player-ZIP outputs with version, channel and source identity.
- Maintain readable source and selected ArtSource in the public repository; ship only runtime files and necessary player information, without source, art sources, external references or game/dependency DLLs.
- Include the full MPL-2.0 license and source-location notice in runtime packages; keep visual-asset permission separate from the source-code license.

### Notes

- Require Harmony, Humanoid Alien Races, Nivarian Race and Nivarian Sleep In Fridges; Odyssey enables orbital features only and is not required for other gameplay.
- See the [Chinese mechanics guide](MECHANICS.md) for gameplay and the [installation guide](../Mod/README.md) for setup and basic controls.
