using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Match;
using Confluence.Net.Protocol;
using Confluence.Net.Serialization;

namespace Confluence.Net.Game.Wire
{
    /// <summary>A match phase began (spec L44-L49): which, for which round and pass, and its tick bounds.</summary>
    public sealed class PhaseMessage : INetMessage
    {
        public MatchPhase Phase { get; set; }
        public int Round { get; set; }
        public int BracketRound { get; set; }
        public int Attempt { get; set; }
        public int StartTick { get; set; }

        /// <summary>Match tick at which the phase ends at the latest, or -1 when open-ended.</summary>
        public int EndTick { get; set; } = -1;

        public void Serialize(NetWriter w)
        {
            w.WriteByte((byte)Phase);
            w.WriteVarUInt((uint)Round);
            w.WriteVarUInt((uint)BracketRound);
            w.WriteVarUInt((uint)Attempt);
            w.WriteVarUInt((uint)StartTick);
            w.WriteVarInt(EndTick);
        }

        public void Deserialize(NetReader r)
        {
            Phase = (MatchPhase)r.ReadByteInRange(0, (byte)MatchPhase.Finished);
            Round = r.ReadCountValue(0, 1000);
            BracketRound = (int)r.ReadVarUInt();
            Attempt = (int)r.ReadVarUInt();
            StartTick = (int)r.ReadVarUInt();
            EndTick = r.ReadVarIntInRange(-1, int.MaxValue);
        }
    }

    /// <summary>
    /// One player's round reveal (spec L44): the next opponent (or a bye), the pass, the spawn side, both Resolve values,
    /// the opponent's completed previous-round loadout (never live purchases; null in round 1), and the arena id whose
    /// <see cref="ArenaMessage"/> carries the public preview.
    /// </summary>
    public sealed class RoundRevealMessage : INetMessage
    {
        public int Round { get; set; }
        public int BracketRound { get; set; }
        public int Attempt { get; set; }
        public PassKind PassKind { get; set; }
        public int OpponentSeat { get; set; } = -1;
        public int Side { get; set; } = -1;
        public int OwnResolve { get; set; }
        public int OpponentResolve { get; set; }
        public string ArenaId { get; set; }
        public PublicLoadout OpponentLoadout { get; set; }

        public bool IsBye => OpponentSeat < 0;

        public void Serialize(NetWriter w)
        {
            w.WriteVarUInt((uint)Round);
            w.WriteVarUInt((uint)BracketRound);
            w.WriteVarUInt((uint)Attempt);
            w.WriteByte((byte)PassKind);
            w.WriteSeat(OpponentSeat);
            w.WriteVarInt(Side);
            w.WriteVarInt(OwnResolve);
            w.WriteVarInt(OpponentResolve);
            w.WriteId(ArenaId);
            w.WriteLoadout(OpponentLoadout);
        }

        public void Deserialize(NetReader r)
        {
            Round = (int)r.ReadVarUInt();
            BracketRound = (int)r.ReadVarUInt();
            Attempt = (int)r.ReadVarUInt();
            PassKind = (PassKind)r.ReadByteInRange(0, (byte)PassKind.VoidRerun);
            OpponentSeat = r.ReadSeat();
            Side = r.ReadVarIntInRange(-1, 1);
            OwnResolve = r.ReadVarIntInRange(-100, 100);
            OpponentResolve = r.ReadVarIntInRange(-100, 100);
            ArenaId = r.ReadId();
            OpponentLoadout = r.ReadLoadout();
        }
    }

    public sealed class PreviewEntryWire
    {
        public string Id;
        public string Name;
        public string Category;
        public string Effect;
        public Vec3 Position;
        public float YawDeg;
    }

    public sealed class PreviewShapeWire
    {
        public byte Kind;
        public Vec2 Center;
        public Vec2 HalfExtents;
        public float YawDeg;
        public float TopY;
        public string Material;
        public bool Walkable;
    }

    public sealed class MaterialNoteWire
    {
        public string Key;
        public string Description;
        public int Count;
    }

    public sealed class SpawnWire
    {
        public int Side;
        public Vec3 Position;
        public float YawDeg;
    }

    /// <summary>
    /// The pass's public arena (spec L44, L329): the generator recipe and layout hash (clients regenerate the identical
    /// layout for rendering and movement prediction and verify the hash, L427), plus the preview metadata: diameter, biome
    /// and its announced rule, spawns, major terrain, props with icon categories, cover shapes, material notes. It holds
    /// no player, build, standing, or match-seed data.
    /// </summary>
    public sealed class ArenaMessage : INetMessage
    {
        public string ArenaId { get; set; }
        public int Round { get; set; }
        public ulong RecipeSeed { get; set; }
        public int Diameter { get; set; }
        public string BiomeId { get; set; }
        public List<string> AvailablePropIds { get; set; } = new List<string>();
        public ulong LayoutHash { get; set; }
        public string BiomeName { get; set; }
        public string EnvironmentRule { get; set; }
        public float FinalFloorRadius { get; set; }
        public List<SpawnWire> Spawns { get; } = new List<SpawnWire>();
        public List<PreviewEntryWire> Modules { get; } = new List<PreviewEntryWire>();
        public List<PreviewEntryWire> Props { get; } = new List<PreviewEntryWire>();
        public List<PreviewShapeWire> Shapes { get; } = new List<PreviewShapeWire>();
        public List<MaterialNoteWire> Materials { get; } = new List<MaterialNoteWire>();

        public void Serialize(NetWriter w)
        {
            w.WriteId(ArenaId);
            w.WriteVarUInt((uint)Round);
            w.WriteULong(RecipeSeed);
            w.WriteVarUInt((uint)Diameter);
            w.WriteId(BiomeId);
            w.WriteIdList(AvailablePropIds);
            w.WriteULong(LayoutHash);
            w.WriteText(BiomeName);
            w.WriteText(EnvironmentRule);
            w.WriteFloat(FinalFloorRadius);
            w.WriteVarUInt((uint)Spawns.Count);
            foreach (SpawnWire s in Spawns)
            {
                w.WriteByte((byte)s.Side);
                w.WriteVec3(s.Position);
                w.WriteFloat(s.YawDeg);
            }

            WriteEntries(w, Modules);
            WriteEntries(w, Props);
            w.WriteVarUInt((uint)Shapes.Count);
            foreach (PreviewShapeWire s in Shapes)
            {
                w.WriteByte(s.Kind);
                w.WriteFloat(s.Center.X);
                w.WriteFloat(s.Center.Y);
                w.WriteFloat(s.HalfExtents.X);
                w.WriteFloat(s.HalfExtents.Y);
                w.WriteFloat(s.YawDeg);
                w.WriteFloat(s.TopY);
                w.WriteText(s.Material);
                w.WriteBool(s.Walkable);
            }

            w.WriteVarUInt((uint)Materials.Count);
            foreach (MaterialNoteWire m in Materials)
            {
                w.WriteText(m.Key);
                w.WriteText(m.Description);
                w.WriteVarUInt((uint)m.Count);
            }
        }

        public void Deserialize(NetReader r)
        {
            ArenaId = r.ReadId();
            Round = r.ReadCountValue(1, 1000);
            RecipeSeed = r.ReadULong();
            Diameter = r.ReadCountValue(8, 256);
            BiomeId = r.ReadId();
            AvailablePropIds = r.ReadIdList();
            LayoutHash = r.ReadULong();
            BiomeName = r.ReadText();
            EnvironmentRule = r.ReadText();
            FinalFloorRadius = r.ReadFloat();
            int spawns = r.ReadCount(4, 17);
            for (int i = 0; i < spawns; i++) Spawns.Add(new SpawnWire { Side = r.ReadByteInRange(0, 1), Position = r.ReadVec3(), YawDeg = r.ReadFloat() });
            ReadEntries(r, Modules);
            ReadEntries(r, Props);
            int shapes = r.ReadCount(WireLimits.MaxPreviewEntries, 27);
            for (int i = 0; i < shapes; i++)
            {
                Shapes.Add(new PreviewShapeWire
                {
                    Kind = r.ReadByte(),
                    Center = new Vec2(r.ReadFloat(), r.ReadFloat()),
                    HalfExtents = new Vec2(r.ReadFloat(), r.ReadFloat()),
                    YawDeg = r.ReadFloat(),
                    TopY = r.ReadFloat(),
                    Material = r.ReadText(),
                    Walkable = r.ReadBool(),
                });
            }

            int notes = r.ReadCount(WireLimits.MaxPreviewEntries, 3);
            for (int i = 0; i < notes; i++) Materials.Add(new MaterialNoteWire { Key = r.ReadText(), Description = r.ReadText(), Count = (int)r.ReadVarUInt() });
        }

        private static void WriteEntries(NetWriter w, List<PreviewEntryWire> entries)
        {
            w.WriteVarUInt((uint)entries.Count);
            foreach (PreviewEntryWire e in entries)
            {
                w.WriteId(e.Id);
                w.WriteText(e.Name);
                w.WriteText(e.Category);
                w.WriteText(e.Effect);
                w.WriteVec3(e.Position);
                w.WriteFloat(e.YawDeg);
            }
        }

        private static void ReadEntries(NetReader r, List<PreviewEntryWire> into)
        {
            int n = r.ReadCount(WireLimits.MaxPreviewEntries, 20);
            for (int i = 0; i < n; i++)
            {
                into.Add(new PreviewEntryWire
                {
                    Id = r.ReadId(),
                    Name = r.ReadText(),
                    Category = r.ReadText(),
                    Effect = r.ReadText(),
                    Position = r.ReadVec3(),
                    YawDeg = r.ReadFloat(),
                });
            }
        }
    }

    /// <summary>At alcove entry the opponents' final loadouts become visible (spec L46).</summary>
    public sealed class FinalLoadoutsMessage : INetMessage
    {
        public int Round { get; set; }
        public int Attempt { get; set; }
        public int OpponentSeat { get; set; } = -1;
        public PublicLoadout Own { get; set; }
        public PublicLoadout Opponent { get; set; }

        public void Serialize(NetWriter w)
        {
            w.WriteVarUInt((uint)Round);
            w.WriteVarUInt((uint)Attempt);
            w.WriteSeat(OpponentSeat);
            w.WriteLoadout(Own);
            w.WriteLoadout(Opponent);
        }

        public void Deserialize(NetReader r)
        {
            Round = (int)r.ReadVarUInt();
            Attempt = (int)r.ReadVarUInt();
            OpponentSeat = r.ReadSeat();
            Own = r.ReadLoadout();
            Opponent = r.ReadLoadout();
        }
    }

    public sealed class StandingWire
    {
        public int Seat;
        public int Resolve;
        public int CombatWins;
        public int Losses;
        public int Draws;
        public int DuelHalfWins;
        public bool Eliminated;
        public bool Connected;
    }

    /// <summary>Public standings: remaining Resolve (L36, L44), wins, eliminations, Duel score, connection state.</summary>
    public sealed class StandingsMessage : INetMessage
    {
        public int Round { get; set; }
        public List<StandingWire> Seats { get; } = new List<StandingWire>();

        public void Serialize(NetWriter w)
        {
            w.WriteVarUInt((uint)Round);
            w.WriteVarUInt((uint)Seats.Count);
            foreach (StandingWire s in Seats)
            {
                w.WriteByte((byte)s.Seat);
                w.WriteVarInt(s.Resolve);
                w.WriteVarUInt((uint)s.CombatWins);
                w.WriteVarUInt((uint)s.Losses);
                w.WriteVarUInt((uint)s.Draws);
                w.WriteVarUInt((uint)s.DuelHalfWins);
                w.WriteByte((byte)((s.Eliminated ? 1 : 0) | (s.Connected ? 2 : 0)));
            }
        }

        public void Deserialize(NetReader r)
        {
            Round = (int)r.ReadVarUInt();
            int n = r.ReadCount(WireLimits.MaxPlayers, 7);
            for (int i = 0; i < n; i++)
            {
                var s = new StandingWire
                {
                    Seat = r.ReadByteInRange(0, WireLimits.MaxPlayers - 1),
                    Resolve = r.ReadVarIntInRange(-100, 100),
                    CombatWins = (int)r.ReadVarUInt(),
                    Losses = (int)r.ReadVarUInt(),
                    Draws = (int)r.ReadVarUInt(),
                    DuelHalfWins = (int)r.ReadVarUInt(),
                };
                byte bits = r.ReadByteInRange(0, 3);
                s.Eliminated = (bits & 1) != 0;
                s.Connected = (bits & 2) != 0;
                Seats.Add(s);
            }
        }
    }

    /// <summary>
    /// Public outcome of one duel (spec L49: health and winner): seats, winner or draw, end reason, health fractions.
    /// The duelists additionally receive their <see cref="DuelReportMessage"/>.
    /// </summary>
    public sealed class DuelResultMessage : INetMessage
    {
        public int DuelId { get; set; }
        public int Round { get; set; }
        public int Attempt { get; set; }
        public int Side0Seat { get; set; }
        public int Side1Seat { get; set; }

        /// <summary>Winning seat, or -1 for a draw.</summary>
        public int WinnerSeat { get; set; } = -1;

        public DuelEndReason Reason { get; set; }
        public float Health0 { get; set; }
        public float Health1 { get; set; }

        public void Serialize(NetWriter w)
        {
            w.WriteVarUInt((uint)DuelId);
            w.WriteVarUInt((uint)Round);
            w.WriteVarUInt((uint)Attempt);
            w.WriteByte((byte)Side0Seat);
            w.WriteByte((byte)Side1Seat);
            w.WriteSeat(WinnerSeat);
            w.WriteByte((byte)Reason);
            w.WriteFloat(Health0);
            w.WriteFloat(Health1);
        }

        public void Deserialize(NetReader r)
        {
            DuelId = (int)r.ReadVarUInt();
            Round = (int)r.ReadVarUInt();
            Attempt = (int)r.ReadVarUInt();
            Side0Seat = r.ReadByteInRange(0, WireLimits.MaxPlayers - 1);
            Side1Seat = r.ReadByteInRange(0, WireLimits.MaxPlayers - 1);
            WinnerSeat = r.ReadSeat();
            Reason = (DuelEndReason)r.ReadByteInRange(0, 3);
            Health0 = r.ReadFloat();
            Health1 = r.ReadFloat();
        }
    }

    public sealed class PlacementWire
    {
        public int Seat;
        public int FirstPosition;
        public int LastPosition;
    }

    /// <summary>The match finished (spec L38-L40, L65): placements (shared positions keep their range), champion, Duel score.</summary>
    public sealed class MatchEndMessage : INetMessage
    {
        public int ChampionSeat { get; set; } = -1;
        public bool CountsForRating { get; set; }
        public int DuelHalfWins0 { get; set; }
        public int DuelHalfWins1 { get; set; }
        public List<PlacementWire> Placements { get; } = new List<PlacementWire>();

        public void Serialize(NetWriter w)
        {
            w.WriteSeat(ChampionSeat);
            w.WriteBool(CountsForRating);
            w.WriteVarUInt((uint)DuelHalfWins0);
            w.WriteVarUInt((uint)DuelHalfWins1);
            w.WriteVarUInt((uint)Placements.Count);
            foreach (PlacementWire p in Placements)
            {
                w.WriteByte((byte)p.Seat);
                w.WriteByte((byte)p.FirstPosition);
                w.WriteByte((byte)p.LastPosition);
            }
        }

        public void Deserialize(NetReader r)
        {
            ChampionSeat = r.ReadSeat();
            CountsForRating = r.ReadBool();
            DuelHalfWins0 = (int)r.ReadVarUInt();
            DuelHalfWins1 = (int)r.ReadVarUInt();
            int n = r.ReadCount(WireLimits.MaxPlayers, 3);
            for (int i = 0; i < n; i++)
            {
                var p = new PlacementWire
                {
                    Seat = r.ReadByteInRange(0, WireLimits.MaxPlayers - 1),
                    FirstPosition = r.ReadByteInRange(1, WireLimits.MaxPlayers),
                    LastPosition = r.ReadByteInRange(1, WireLimits.MaxPlayers),
                };
                if (p.LastPosition < p.FirstPosition) throw new NetReadException(NetReadError.InvalidValue, "inverted placement range");
                Placements.Add(p);
            }
        }
    }

    /// <summary>An operator or rules notice (refused start, Laboratory label, disconnection policy).</summary>
    public sealed class NoticeMessage : INetMessage
    {
        public string Text { get; set; } = string.Empty;

        public void Serialize(NetWriter writer) => writer.WriteText(Text);

        public void Deserialize(NetReader reader) => Text = reader.ReadText();
    }
}
