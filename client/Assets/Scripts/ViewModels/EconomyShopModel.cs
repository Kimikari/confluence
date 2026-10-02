using System;
using System.Collections.Generic;
using Confluence.Client.Laboratory;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy;
using Confluence.Economy.Loadouts;
using Confluence.Economy.Shop;

namespace Confluence.Client.ViewModels
{
    /// <summary>
    /// <see cref="IShopViewModel"/> over a local Confluence.Economy <see cref="EquipmentShop"/>. Every command is a
    /// request with a fresh request id, exactly as the server will receive it (spec L429); a refused request changes
    /// nothing and reports why. The phase timer is owned by the host (<see cref="PhaseTimer"/>).
    /// </summary>
    public sealed class EconomyShopModel : IShopViewModel
    {
        private readonly EquipmentShop _shop;
        private readonly PhaseTimer _timer;
        private readonly Func<long> _nextRequestId;
        private readonly LoadoutBuildModel _build;
        private readonly List<ShopCardView> _cards = new List<ShopCardView>();

        public EconomyShopModel(EquipmentShop shop, PhaseTimer timer, Func<long> nextRequestId, string title)
        {
            _shop = shop ?? throw new ArgumentNullException(nameof(shop));
            _timer = timer ?? throw new ArgumentNullException(nameof(timer));
            _nextRequestId = nextRequestId ?? throw new ArgumentNullException(nameof(nextRequestId));
            _build = new LoadoutBuildModel(shop.Player);
            Title = title;
            Rebuild();
        }

        public event Action Changed;

        /// <summary>Raised when the player toggles ready (the host ends the phase once everyone is ready, spec L45).</summary>
        public event Action<bool> ReadyChanged;

        public EquipmentShop Shop => _shop;
        public string Title { get; }
        public int Crowns => _shop.Player.Crowns;
        public float SecondsRemaining => _timer.SecondsRemaining;
        public bool IsOpen => _shop.IsOpen;
        public bool IsReady { get; private set; }
        public bool TimerPaused => _timer.Paused;
        public string Message { get; private set; } = "Buy, reroll, sell, and set six spells, then Ready.";
        public bool MessageIsError { get; private set; }
        public IReadOnlyList<ShopCardView> Cards => _cards;
        public IBuildViewModel Build => _build;

        public void Buy(int position)
        {
            ShopResult result = _shop.Buy(_nextRequestId(), position);
            Report(result, result.Succeeded && result.Item != null
                ? "Bought " + result.Item.Definition.Name + " for " + ContentFormat.Crowns(-result.CrownsDelta) + "."
                : null);
        }

        public void Reroll(int position)
        {
            ShopResult result = _shop.Reroll(_nextRequestId(), position);
            Report(result, result.Succeeded ? "Rerolled card " + (position + 1) + "." : null);
        }

        public void Sell(int instanceId)
        {
            ShopResult result = _shop.Sell(_nextRequestId(), instanceId);
            Report(result, result.Succeeded && result.Item != null
                ? "Sold " + result.Item.Definition.Name + " for " + ContentFormat.Crowns(result.CrownsDelta) + "." + DormancyText(result)
                : null);
        }

        public void ToggleReserve(int instanceId)
        {
            Loadout loadout = _shop.Player.Loadout;
            OwnedEquipment item = loadout.FindItem(instanceId, out Placement placement);
            if (item == null)
            {
                Report(TransactionCode.UnknownItem, null);
                return;
            }

            if (placement.IsSlot)
            {
                ShopResult result = _shop.Move(_nextRequestId(), instanceId, Placement.Reserve);
                Report(result, result.Succeeded ? item.Definition.Name + " moved to the reserve." + DormancyText(result) : null);
                return;
            }

            // Into the first legal slot that is free, else the first legal slot (swapping its occupant to the reserve).
            IReadOnlyList<EquipmentSlot> slots = SlotRules.SlotsFor(item.Definition);
            EquipmentSlot target = slots.Count > 0 ? slots[0] : EquipmentSlot.TrinketA;
            for (int i = 0; i < slots.Count; i++)
            {
                if (loadout.ItemIn(slots[i]) == null)
                {
                    target = slots[i];
                    break;
                }
            }

            ShopResult moved = _shop.Move(_nextRequestId(), instanceId, Placement.In(target));
            Report(moved, moved.Succeeded ? item.Definition.Name + " equipped in " + ContentFormat.SlotName(target) + "." + DormancyText(moved) : null);
        }

        public void SwapSpells(string equippedSpellId, string benchSpellId)
        {
            Loadout loadout = _shop.Player.Loadout;
            var spells = new List<string>(loadout.EquippedSpellIds);
            int slot = spells.IndexOf(equippedSpellId);
            if (slot < 0 || spells.Contains(benchSpellId))
            {
                Report(TransactionCode.InvalidSpellSelection, null);
                return;
            }

            spells[slot] = benchSpellId;
            ShopResult result = _shop.SetEquippedSpells(_nextRequestId(), spells);
            Report(result, result.Succeeded
                ? ContentFormat.SpellName(loadout.Catalogue, benchSpellId) + " equipped in slot " + (slot + 1) + "." + DormancyText(result)
                : null);
        }

        public void SetEnabledActive(int? instanceId)
        {
            ShopResult result = _shop.SetEnabledActive(_nextRequestId(), instanceId);
            Report(result, result.Succeeded ? "Enabled active: " + (instanceId.HasValue && result.Item != null ? result.Item.Definition.Name : "none") + "." : null);
        }

        public void SetReady(bool ready)
        {
            if (IsReady == ready)
            {
                return;
            }

            IsReady = ready;
            Message = ready ? "Ready. The phase ends early when every player is ready." : "Not ready.";
            MessageIsError = false;
            Changed?.Invoke();
            ReadyChanged?.Invoke(ready);
        }

        /// <summary>Re-reads the shop and loadout (for example after the timer ticked or the phase closed).</summary>
        public void Rebuild()
        {
            _cards.Clear();
            Loadout loadout = _shop.Player.Loadout;
            IReadOnlyList<ShopCard> cards = _shop.Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                _cards.Add(ToView(cards[i], loadout, _shop.IsOpen));
            }

            _build.Refresh();
            Changed?.Invoke();
        }

        /// <summary>Notifies listeners that the timer moved (cheap: no rebuild).</summary>
        public void Tick() => Changed?.Invoke();

        public static ShopCardView ToView(ShopCard card, Loadout loadout, bool open)
        {
            EquipmentDef def = card.Definition;
            var view = new ShopCardView
            {
                Position = card.Position,
                Status = (ShopCardStatus)(int)card.State,
                RerollAvailable = card.RerollAvailable,
                CanBuy = open && card.IsPurchasable,
                CanReroll = open && card.RerollAvailable && card.State != ShopCardState.Purchased,
            };

            if (def != null)
            {
                view.DefinitionId = def.Id;
                view.Name = def.Name;
                view.Category = def.CategoryText;
                view.Tier = def.Tier;
                view.Price = def.Price;
                view.SlotText = def.Slot;
                view.Effect = def.Effect;
                view.Activation = def.Activation;
                view.Drawback = def.Drawback;
                view.Notice = Notice(def, loadout);
            }

            switch (card.State)
            {
                case ShopCardState.Purchased:
                    view.Name = view.Name != null ? view.Name + " (bought)" : "Bought";
                    break;
                case ShopCardState.OutsideBudget:
                    view.Notice = "Outside budget: unaffordable after your purchase; its reroll can draw an affordable item (spec L208).";
                    break;
                case ShopCardState.SaveCrowns:
                    view.Name = "Save Crowns";
                    view.Effect = "No affordable unlocked item can be shown. The reroll is kept (spec L208).";
                    break;
            }

            return view;
        }

        // Spec L353: "An inactive or capped component is labeled before purchase." Activation is the one component the
        // economy can already judge: a second active stays dormant until chosen, and the Sidearm never activates (L577).
        private static string Notice(EquipmentDef def, Loadout loadout)
        {
            if (!def.IsActive)
            {
                return null;
            }

            if (loadout.EnabledActive != null)
            {
                return "Active stays dormant: " + loadout.EnabledActive.Definition.Name + " is the enabled active (one at a time, spec L575).";
            }

            IReadOnlyList<EquipmentSlot> slots = SlotRules.SlotsFor(def);
            if (slots.Count == 1 && !SlotRules.SlotAllowsActivation(slots[0]))
            {
                return "Activation unusable in " + ContentFormat.SlotName(slots[0]) + ".";
            }

            return null;
        }

        private static string DormancyText(ShopResult result)
        {
            if (result.Dormancy == null || result.Dormancy.NewlyDormant.Count == 0)
            {
                return string.Empty;
            }

            return " Now dormant: " + string.Join(", ", result.Dormancy.NewlyDormant) + ".";
        }

        private void Report(ShopResult result, string success)
        {
            Report(result.Code, success);
        }

        private void Report(TransactionCode code, string success, string failure = null)
        {
            bool ok = code == TransactionCode.Success;
            Message = ok ? success ?? ContentFormat.Transaction(code) : failure ?? ContentFormat.Transaction(code);
            MessageIsError = !ok && code != TransactionCode.SaveCrowns;
            Rebuild();
        }
    }
}
