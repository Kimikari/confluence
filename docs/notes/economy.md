# Confluence.Economy — design notes

Owner paths: `src/Confluence.Economy/**`, `tests/Confluence.Economy.Tests/**`. Depends only on `Confluence.Core`.

## Layout
- `PlayerEconomy` — per-player aggregate for one match: Crowns, `Loadout`, `FeatLedger`, request ledger, `SyncRoot`.
- `Loadouts/` — `Loadout` (wizard, kit, six equipped spells, eight slots, three reserve slots, enabled active, six augment
  slots; every mutation validates the whole arrangement then commits or returns a `TransactionCode`), `OwnedEquipment`
  (instance id, paid price, consumable charge), `Placement`, `SlotRules`, `IssuedEquipment`, `OwnedAugment`.
- `Eligibility/` — `StructuralProfile` (two-profile rule), `IStructuralChangeSource` (Core `StructuralDeltas` by default,
  `StructuralChangeTable` for tests/Laboratory/equipment modes), `EquipmentLiveness` (live tags), `EligibilityContext`,
  `PredicateEvaluator` (every leaf), `EligibilityExplainer` (missing requirements), `AcquisitionRecord` (frozen
  acquisition-only leaves), `CandidateJudge`/`LoadoutEligibility`.
- `Augments/` — `DormancyResolver` (transformations on base profile → profile with enabled transformations → shared,
  topological within each group), `AugmentPool` (legal alternatives), `AugmentClassification`, `AugmentAcquisition`
  (new/replace/upgrade), `AugmentDraft` (five cards, guarantees, per-card reroll, select/pass/timeout).
- `Shop/` — `EquipmentPool` (D-020 draw), `EquipmentShop` (cards, rerolls, placeholders, Save Crowns, buy/sell/move/
  enable/spells, request ids), `ShopCard`, `ShopResult`.
- `EconomyFloors` — match-start floors: ten legal alternatives per rarity per wizard, six tier-1 equipment definitions.
- `Builds/WorkedBuilds` — build loadouts and draft-plan parsing.

## Key rules encoded (spec lines)
- Slots L210/L560-569; issued gear L202/L571; actives L575/L577 (no activation from the Sidearm); live tags L585.
- Shop L204-213; D-020 draw; folding and Save Crowns L208; sell 50% / used consumable 0 L212; request ids L429.
- Predicates L604-637; acquisition-only L247/L602; two-profile rule L243/L543/L637; dormancy L639-646.
- Draft L214-233, L594, L645; rank 2 L231/L600; floors L233.

## Status / next steps
- Implemented: everything listed above. See final report for interpretations.
- Next: Match wires `EquipmentShop` → `AugmentDraft` per round and calls `DormancyResolver.Resolve` at duel start; Sim
  reports consumable use (`Loadout.SpendConsumableCharge`) and feats (`FeatLedger.RecordCompletedDuel`).
- Stage 5: register transformation deltas in Core `StructuralDeltas`; re-run the floor and worked-build tests.
