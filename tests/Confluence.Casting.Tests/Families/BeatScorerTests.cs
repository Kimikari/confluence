using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Casting.Grammars;

namespace Confluence.Casting.Tests.Families
{
    public class BeatScorerTests
    {
        // Three syllables a quarter second apart, scored with 0.10 s normal and 0.04 s mastery tolerance.
        private static readonly BeatPattern Phrase = BeatPattern.Evenly(0.25, 0.25, new[] { 3, 1, 4 });
        private static readonly long Normal = GrammarInputEvent.SubTicksFromSeconds(0.10);
        private static readonly long Mastery = GrammarInputEvent.SubTicksFromSeconds(0.04);
        private const float Threshold = 0.7f;

        private static GrammarInputEvent Syllable(int tick, int value, float confidence = 0.9f)
        {
            var e = new GrammarInputEvent(GrammarInputKind.Syllable, tick, 0, value);
            e.Confidence = confidence;
            return e;
        }

        private static GrammarInputEvent Tap(int tick, int subTick = 0) => new GrammarInputEvent(GrammarInputKind.RhythmTap, tick, subTick, 0);

        private static BeatScore Voice(params GrammarInputEvent[] events) =>
            BeatScorer.Score(Phrase, BeatScorer.FromEvents(events, InputRoute.Voice, 0, Threshold), Normal, Mastery);

        private static BeatScore Rhythm(params GrammarInputEvent[] events) =>
            BeatScorer.Score(Phrase.AsRhythm(), BeatScorer.FromEvents(events, InputRoute.Rhythm, 0, Threshold), Normal, Mastery);

        [Fact]
        public void On_Beat_Phrase_Masters_By_Voice_And_By_Rhythm()
        {
            var voice = Voice(Syllable(15, 3), Syllable(30, 1), Syllable(45, 4));
            var rhythm = Rhythm(Tap(15), Tap(30), Tap(45));
            Assert.True(voice.Completed && voice.Mastered);
            Assert.True(rhythm.Completed && rhythm.Mastered);
        }

        [Theory]
        [InlineData(33, true, false, MissReason.LateBeat, 3000)]     // 3 ticks late: normal cast, exact late beat
        [InlineData(27, true, false, MissReason.EarlyBeat, -3000)]   // 3 ticks early
        [InlineData(37, false, false, MissReason.LateBeat, 7000)]    // beyond the normal tolerance: no cast yet
        [InlineData(32, true, true, MissReason.None, 0)]             // 2 ticks late is inside the mastery tolerance
        public void Routes_With_Equal_Timing_Score_Identically(int secondBeatTick, bool completed, bool mastered, MissReason miss, long error)
        {
            // Spec L367: the rhythm route has the same syllable count, cadence and maximum reward as voice.
            var voice = Voice(Syllable(15, 3), Syllable(secondBeatTick, 1), Syllable(45, 4));
            var rhythm = Rhythm(Tap(15), Tap(secondBeatTick), Tap(45));
            foreach (var score in new[] { voice, rhythm })
            {
                Assert.Equal(completed, score.Completed);
                Assert.Equal(mastered, score.Mastered);
                Assert.Equal(miss, score.Miss);
                if (miss != MissReason.None)
                {
                    Assert.Equal(1, score.BeatIndex);
                    Assert.Equal(error, score.ErrorSubTicks);
                }
            }
        }

        [Fact]
        public void Low_Confidence_Never_Commits_And_Names_The_Syllable()
        {
            // Spec L365: confidence must exceed the calibrated threshold before the game commits the cast.
            var score = Voice(Syllable(15, 3), Syllable(30, 1, confidence: 0.5f), Syllable(45, 4));
            Assert.False(score.Completed);
            Assert.Equal(MissReason.UnrecognizedSyllable, score.Miss);
            Assert.Equal(1, score.BeatIndex);
        }

        [Fact]
        public void Wrong_Syllable_And_Missing_Syllable_Are_Reported_Exactly()
        {
            var wrong = Voice(Syllable(15, 3), Syllable(30, 4), Syllable(45, 1));
            Assert.Equal(MissReason.WrongOrder, wrong.Miss);
            Assert.Equal(1, wrong.BeatIndex);

            var missing = Voice(Syllable(15, 3), Syllable(30, 1));
            Assert.False(missing.Completed);
            Assert.Equal(MissReason.MissingInput, missing.Miss);
            Assert.Equal(2, missing.BeatIndex);
        }

        [Fact]
        public void Extra_Taps_Never_Add_A_Reward()
        {
            // Spec L373/L478: spam is not useful input; one reward per spell.
            var score = Rhythm(Tap(15), Tap(30), Tap(45), Tap(50), Tap(55));
            Assert.True(score.Completed);
            Assert.False(score.Mastered);
            Assert.Equal(MissReason.ExtraInputs, score.Miss);
        }

        [Fact]
        public void Inputs_Are_Ordered_By_Timestamp_Then_Arrival()
        {
            var events = new List<GrammarInputEvent> { Syllable(30, 1), Syllable(15, 3), Syllable(45, 4) };
            var inputs = BeatScorer.FromEvents(events, InputRoute.Voice, 0, Threshold);
            Assert.Equal(new[] { 3, 1, 4 }, new[] { inputs[0].Token, inputs[1].Token, inputs[2].Token });
            Assert.True(BeatScorer.Score(Phrase, inputs, Normal, Mastery).Mastered);

            // Sub-tick timestamps keep precision below one simulation tick (spec L490 local timestamps).
            var precise = BeatScorer.FromEvents(new[] { Tap(30, 500) }, InputRoute.Rhythm, 0, Threshold);
            Assert.Equal(30500, precise[0].Time);
        }

        [Fact]
        public void Each_Route_Reads_Only_Its_Own_Events()
        {
            var mixed = new[] { Syllable(15, 3), Tap(30) };
            Assert.Single(BeatScorer.FromEvents(mixed, InputRoute.Voice, 0, Threshold));
            Assert.Single(BeatScorer.FromEvents(mixed, InputRoute.Rhythm, 0, Threshold));
        }
    }
}
