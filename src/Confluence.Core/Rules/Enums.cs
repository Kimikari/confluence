using System;

namespace Confluence.Core.Rules
{
    // Shared vocabulary used by every library. Values come from the catalogue JSON and design §3-§7.

    public enum Rarity
    {
        Silver = 0,
        Gold = 1,
        Prismatic = 2,
    }

    public enum MatchMode
    {
        Conclave = 0,
        Duel = 1,
    }

    // Damage channels determine resistance (spec §3 "Damage, shields, healing, and status").
    public enum DamageChannel
    {
        None = 0,
        Physical,
        Thermal,
        Electrical,
        Corrosive,
        Arcane,
    }

    // School labels describe physical affinity (spec §6).
    public enum School
    {
        Heat,
        Water,
        Frost,
        Storm,
        Earth,
        Metal,
        Growth,
        Light,
        Shadow,
        Sound,
        Gravity,
        Force,
        Spirit,
        Venom,
        Sand,
        Arcane,
    }

    public enum EquipmentCategory
    {
        Wand,
        Staff,
        Weapon,
        Armor,
        MagicalClothing,
        Boots,
        Trinket,
        Consumable,
    }

    // Equipment slots (spec §5 "Equipment shop"). Reserve inventory is separate (three reserve slots).
    public enum EquipmentSlot
    {
        MainFocus,
        Sidearm,
        Armor,
        Clothing,
        Boots,
        TrinketA,
        TrinketB,
        Consumable,
    }

    // Loose prop mass classes (spec §4 "Terrain and impact rules").
    public enum PropMass
    {
        Light,
        Medium,
        Heavy,
        Anchored,
    }

    public enum ModuleCategory
    {
        Topology,
        Cover,
        Elevation,
        Material,
        Approach,
        Center,
    }

    public enum PropCategory
    {
        Conductive,
        Reservoir,
        Fuel,
        Optical,
        Kinetic,
        Acoustic,
        Reactive,
        Utility,
    }

    // Cell and surface material flags (spec §4 "Representation and consistency").
    [Flags]
    public enum MaterialFlags
    {
        None = 0,
        Conductive = 1 << 0,
        Combustible = 1 << 1,
        Porous = 1 << 2,
        Anchored = 1 << 3,
        Metal = 1 << 4,
        Brittle = 1 << 5,
        Insulating = 1 << 6,
        Reflective = 1 << 7,
        Acoustic = 1 << 8,
        Absorbent = 1 << 9,
        Grounded = 1 << 10,
        Growth = 1 << 11,
        Sand = 1 << 12,
        Ice = 1 << 13,
        Glass = 1 << 14,
        Water = 1 << 15,
    }
}
