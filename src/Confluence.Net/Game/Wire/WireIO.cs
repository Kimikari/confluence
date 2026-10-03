using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Match;
using Confluence.Net.Serialization;

namespace Confluence.Net.Game.Wire
{
    /// <summary>
    /// Bounds of the game protocol's variable-size fields. Anything larger in received bytes is malformed (the reader
    /// throws <see cref="NetReadException"/>), so a forged count can never allocate more than these limits.
    /// </summary>
    public static class WireLimits
    {
        /// <summary>Catalogue and arena ids ("SPL-009", "arena:r3.a0:0123456789ABCDEF").</summary>
        public const int IdMaxBytes = 64;

        /// <summary>Display names, reasons, notices and catalogue prose shown in previews.</summary>
        public const int TextMaxBytes = 1024;

        public const int MaxPlayers = 8;
        public const int MaxIdList = 64;
        public const int MaxPreviewEntries = 512;
        public const int MaxDuelEventsPerMessage = 256;
        public const int MaxStatuses = 32;
        public const int MaxGrammarEventsPerInput = 16;
        public const int MaxEntities = 256;
        public const int MaxCells = 20000;
        public const int MaxProps = 256;
        public const int MaxSolids = 1024;
        public const int MaxVolumes = 64;
        public const int MaxVolumeBitmapBytes = 4096;
        public const int MaxPlacements = 8;
    }

    /// <summary>Reader and writer helpers shared by every game message.</summary>
    public static class WireIO
    {
        /// <summary>An id or null (null and the empty string are both written as the empty string).</summary>
        public static void WriteId(this NetWriter writer, string id) => writer.WriteString(id ?? string.Empty, WireLimits.IdMaxBytes);

        public static string ReadId(this NetReader reader)
        {
            string s = reader.ReadString(WireLimits.IdMaxBytes);
            return s.Length == 0 ? null : s;
        }

        public static void WriteText(this NetWriter writer, string text)
        {
            text = text ?? string.Empty;
            if (System.Text.Encoding.UTF8.GetByteCount(text) > WireLimits.TextMaxBytes)
            {
                // Truncate by characters until it fits; prose is informational and never parsed by rules.
                int length = Math.Min(text.Length, WireLimits.TextMaxBytes);
                while (length > 0 && System.Text.Encoding.UTF8.GetByteCount(text.Substring(0, length)) > WireLimits.TextMaxBytes) length--;
                text = text.Substring(0, length);
            }

            writer.WriteString(text, WireLimits.TextMaxBytes);
        }

        public static string ReadText(this NetReader reader) => reader.ReadString(WireLimits.TextMaxBytes);

        public static void WriteIdList(this NetWriter writer, IReadOnlyList<string> ids)
        {
            int count = ids == null ? 0 : ids.Count;
            if (count > WireLimits.MaxIdList) throw new ArgumentException("Too many ids for one list.");
            writer.WriteVarUInt((uint)count);
            for (int i = 0; i < count; i++) writer.WriteId(ids[i]);
        }

        public static List<string> ReadIdList(this NetReader reader)
        {
            int count = reader.ReadCount(WireLimits.MaxIdList, 1);
            var list = new List<string>(count);
            for (int i = 0; i < count; i++) list.Add(reader.ReadId());
            return list;
        }

        public static void WriteVec3(this NetWriter writer, Vec3 v)
        {
            writer.WriteFloat(v.X);
            writer.WriteFloat(v.Y);
            writer.WriteFloat(v.Z);
        }

        /// <summary>Exact float vector; non-finite components are malformed (spec L429 impossible values).</summary>
        public static Vec3 ReadVec3(this NetReader reader) => new Vec3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());

        public static void WriteYawBox(this NetWriter writer, YawBox box)
        {
            writer.WriteVec3(box.Center);
            writer.WriteVec3(box.HalfExtents);
            writer.WriteFloat(box.YawDeg);
        }

        public static YawBox ReadYawBox(this NetReader reader)
        {
            Vec3 center = reader.ReadVec3();
            Vec3 half = reader.ReadVec3();
            float yaw = reader.ReadFloat();
            if (half.X < 0f || half.Y < 0f || half.Z < 0f) throw new NetReadException(NetReadError.InvalidValue, "negative box extents");
            return new YawBox(center, half, yaw);
        }

        /// <summary>Enum written as a byte; values outside the defined range are malformed.</summary>
        public static T ReadEnum<T>(this NetReader reader, int maxInclusive) where T : struct
        {
            byte b = reader.ReadByteInRange(0, (byte)maxInclusive);
            return (T)Enum.ToObject(typeof(T), b);
        }

        /// <summary>A player seat (0..7) or -1 for none.</summary>
        public static void WriteSeat(this NetWriter writer, int seat) => writer.WriteVarInt(seat < 0 ? -1 : seat);

        public static int ReadSeat(this NetReader reader) => reader.ReadVarIntInRange(-1, WireLimits.MaxPlayers - 1);

        public static void WriteNullableInt(this NetWriter writer, int? value)
        {
            writer.WriteBool(value.HasValue);
            if (value.HasValue) writer.WriteVarInt(value.Value);
        }

        public static int? ReadNullableInt(this NetReader reader, int min, int max)
        {
            return reader.ReadBool() ? reader.ReadVarIntInRange(min, max) : (int?)null;
        }

        // ---- Public loadouts (spec L44, L46: completed previous-round and final loadouts) ----------------------

        public static void WriteLoadout(this NetWriter writer, PublicLoadout loadout)
        {
            writer.WriteBool(loadout != null);
            if (loadout == null) return;
            writer.WriteId(loadout.WizardId);
            writer.WriteIdList(loadout.SpellIds);
            writer.WriteIdList(loadout.EquipmentIds);
            writer.WriteIdList(loadout.AugmentIds);
            for (int i = 0; i < loadout.AugmentIds.Count; i++) writer.WriteByte((byte)loadout.AugmentRanks[i]);
        }

        public static PublicLoadout ReadLoadout(this NetReader reader)
        {
            if (!reader.ReadBool()) return null;
            string wizard = reader.ReadId();
            List<string> spells = reader.ReadIdList();
            List<string> equipment = reader.ReadIdList();
            List<string> augments = reader.ReadIdList();
            var ranks = new List<int>(augments.Count);
            for (int i = 0; i < augments.Count; i++) ranks.Add(reader.ReadByteInRange(1, 2));
            return new PublicLoadout(wizard, spells, equipment, augments, ranks);
        }
    }
}
