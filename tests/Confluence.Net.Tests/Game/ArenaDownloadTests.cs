using System;
using Confluence.Arena;
using Confluence.Arena.Generation;
using Confluence.Core.Arena;
using Confluence.Core.Rules;
using Confluence.Hosting.Arenas;
using Confluence.Match;
using Confluence.Net.Game.Headless;
using Confluence.Net.Game.Server;
using Confluence.Net.Game.Wire;
using Confluence.Net.Serialization;
using Confluence.Net.Tests.TestSupport;
using Xunit.Abstractions;

namespace Confluence.Net.Tests.Game
{
    /// <summary>
    /// The authoritative layout fallback (research §2.7.1): a client that cannot regenerate an arena exactly (another
    /// float runtime, no generator) downloads the server's layout, verifies it against the announced hash, and predicts
    /// on it like a regenerated one.
    /// </summary>
    public sealed class ArenaDownloadTests
    {
        private readonly ITestOutputHelper _output;

        public ArenaDownloadTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData(24, 11UL)]
        [InlineData(48, 12UL)]
        public void A_Layout_Round_Trips_Bit_For_Bit(int diameter, ulong seed)
        {
            var recipe = new ArenaRecipe(seed, 1, diameter, "BIO-001", new string[0]);
            ArenaLayout layout = GameRig.Generator.Generate(recipe.ToGeneratorRequest(GameRig.Catalogue)).Layout;
            byte[] bytes = LayoutCodec.Encode(layout);
            ArenaLayout back = LayoutCodec.Decode(bytes);
            Assert.Equal(ArenaLayoutHash.Compute(layout), ArenaLayoutHash.Compute(back));
            Assert.Equal(layout.Solids.Count, back.Solids.Count);
            _output.WriteLine(diameter + " m: " + layout.Solids.Count + " solids, " + layout.Materials.Count + " patches, " + layout.Props.Count + " props -> " + bytes.Length + " bytes deflated");
            Assert.True(bytes.Length < ArenaLayoutMessage.MaxBytes);

            // Corruption is a read error, never a crash.
            for (int i = 0; i < bytes.Length; i += Math.Max(1, bytes.Length / 50))
            {
                var bad = (byte[])bytes.Clone();
                bad[i] ^= 0x5A;
                try
                {
                    LayoutCodec.Decode(bad);
                }
                catch (NetReadException)
                {
                }
            }
        }

        [Fact]
        public void A_Client_Without_Local_Generation_Plays_On_The_Downloaded_Layout()
        {
            var settings = new GameServerSettings
            {
                Mode = MatchMode.Duel,
                PlayerCount = 2,
                Seed = EndToEndTests.SeedWithFirstDiameter(MatchMode.Duel, 24, 300),
                Laboratory = true,
            };
            using (var rig = new GameRig(settings))
            {
                HeadlessPlayer a = rig.AddPlayer("Ash", "WIZ-002", seed: 1, downloadArenas: true);
                HeadlessPlayer b = rig.AddPlayer("Brook", "WIZ-010", seed: 2);
                Assert.True(rig.RunUntil(() => a.Client.Duel != null && a.Client.Duel.Layout != null && a.Client.Duel.HasPrediction &&
                    a.Client.Duel.Phase == Confluence.Sim.Duel.DuelPhase.Combat, 60 * 60 * 2), "no prediction on a downloaded layout");
                Assert.True(a.Client.Arenas.Downloads >= 1);
                Assert.Equal(0, a.Client.Arenas.DownloadFailures);
                Assert.Equal(0, b.Client.Arenas.Downloads); // a matching regeneration never downloads
                Assert.True(rig.Server.LayoutsSent >= 1);
                Assert.True(rig.RunUntil(() => a.Client.View.Results.Count >= 1, 60 * 60 * 2));
                Assert.True(a.Client.Duel.MaxCorrection < 1.5f, "max correction " + a.Client.Duel.MaxCorrection);

                // An arena never announced to the seat is not sent.
                long sent = rig.Server.LayoutsSent;
                a.Client.Transport.Send(Confluence.Net.Game.GameProtocol.ArenaLayoutRequest, new ArenaLayoutRequest { ArenaId = "arena:r9.a0:FFFF" });
                rig.RunUntil(() => false, 30);
                Assert.Equal(sent, rig.Server.LayoutsSent);
            }
        }
    }
}
