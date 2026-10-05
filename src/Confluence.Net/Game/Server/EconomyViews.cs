using System;
using Confluence.Core.Rules;
using Confluence.Economy;
using Confluence.Economy.Augments;
using Confluence.Economy.Loadouts;
using Confluence.Economy.Shop;
using Confluence.Hosting;
using Confluence.Net.Game.Wire;

namespace Confluence.Net.Game.Server
{
    /// <summary>
    /// Builds one player's private shop and draft views from the host's economy objects (read under the player's
    /// economy lock). Only the owner ever receives them (spec L44: live purchases remain hidden).
    /// </summary>
    public static class EconomyViews
    {
        public static ShopStateMessage Shop(MatchHost host, int player)
        {
            var msg = new ShopStateMessage { Round = host.Flow.Round };
            EquipmentShop shop = host.Economy.OpenShopOf(player);
            msg.Open = shop != null;
            msg.Ready = host.Economy.IsShopReady(player);
            if (shop != null)
            {
                for (int tier = GlobalRules.Economy.TierCount; tier >= 1; tier--)
                {
                    if (shop.Round.IsTierUnlocked(tier))
                    {
                        msg.HighestTier = tier;
                        break;
                    }
                }

                foreach (ShopCard card in shop.Cards)
                {
                    var c = new ShopCardWire
                    {
                        Position = card.Position,
                        State = card.State,
                        DefinitionId = card.Definition?.Id,
                        Price = card.Price,
                        Tier = card.Definition != null ? Math.Max(0, Math.Min(4, card.Definition.Tier)) : 0,
                        RerollAvailable = card.RerollAvailable,
                    };
                    c.Slots.AddRange(card.Slots);
                    msg.Cards.Add(c);
                }
            }

            PlayerEconomy economy = host.Economy.Player(player);
            lock (economy.SyncRoot)
            {
                Loadout loadout = economy.Loadout;
                msg.Crowns = economy.Crowns;
                foreach (EquipmentSlot slot in (EquipmentSlot[])Enum.GetValues(typeof(EquipmentSlot)))
                {
                    OwnedEquipment item = loadout.ItemIn(slot);
                    if (item != null) msg.Items.Add(ItemOf(loadout, item, (int)slot));
                }

                foreach (OwnedEquipment item in loadout.Reserve)
                    if (item != null) msg.Items.Add(ItemOf(loadout, item, -1));

                msg.KitSpellIds.AddRange(loadout.KitSpellIds);
                msg.EquippedSpellIds.AddRange(loadout.EquippedSpellIds);
                var active = new System.Collections.Generic.HashSet<OwnedAugment>(loadout.ActiveAugments);
                var slots = loadout.AugmentSlots;
                for (int i = 0; i < slots.Count; i++)
                {
                    OwnedAugment a = slots[i];
                    if (a == null) continue;
                    msg.Augments.Add(new OwnedAugmentWire { Slot = i, AugmentId = a.Id, Rank = a.Rank, Dormant = !active.Contains(a) });
                }
            }

            return msg;
        }

        public static DraftStateMessage Draft(MatchHost host, int player)
        {
            var msg = new DraftStateMessage { Round = host.Flow.Round };
            AugmentDraft draft = host.Economy.CurrentDraftOf(player);
            if (draft == null)
            {
                msg.Open = false;
                msg.Outcome = DraftOutcome.Passed;
                return msg;
            }

            msg.Open = host.Economy.OpenDraftOf(player) != null;
            msg.Rarity = draft.Rarity;
            msg.Outcome = draft.Outcome;
            foreach (DraftCard card in draft.Cards)
            {
                msg.Cards.Add(new DraftCardWire
                {
                    Position = card.Position,
                    AugmentId = card.AugmentId,
                    Rank = Math.Max(1, Math.Min(2, card.Rank)),
                    IsUpgrade = card.IsUpgrade,
                    Rarity = card.Rarity,
                    TargetSpellId = card.TargetSpellId,
                    RerollAvailable = card.RerollAvailable,
                    EligibilityText = card.EligibilityText,
                });
            }

            if (draft.Selected != null)
            {
                msg.SelectedAugmentId = draft.Selected.Id;
                msg.SelectedRank = Math.Max(1, Math.Min(2, draft.Selected.Rank));
            }

            return msg;
        }

        private static OwnedItemWire ItemOf(Loadout loadout, OwnedEquipment item, int slot)
        {
            return new OwnedItemWire
            {
                InstanceId = item.InstanceId,
                DefinitionId = item.Id,
                Slot = slot,
                Charges = Math.Max(0, Math.Min(1000, item.Charges)),
                SellValue = Math.Max(0, item.SellValue),
                IsActiveItem = item.IsActive,
                ActivationEnabled = loadout.IsActivationEnabled(item),
            };
        }
    }
}
