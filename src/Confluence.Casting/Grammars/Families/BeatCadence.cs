using System;
using System.Collections.Generic;

namespace Confluence.Casting.Grammars
{
    /// <summary>One timed input matched against a beat: time in sub-ticks from the cadence start, and its token.</summary>
    public readonly struct BeatInput
    {
        /// <summary>A rhythm-route press: matches whatever token the beat expects (spec L367).</summary>
        public const int AnyToken = -1;

        /// <summary>A syllable heard below the calibrated confidence threshold: it holds its place but never matches (spec L365).</summary>
        public const int UnrecognizedToken = -2;

        public BeatInput(long time, int token)
        {
            Time = time;
            Token = token;
        }

        public long Time { get; }
        public int Token { get; }
    }

    /// <summary>
    /// A fixed cadence of beats (VoiceRhythm family: WIZ-004 syllables, WIZ-007 staff beats). Times are in the
    /// <see cref="GrammarInputEvent"/> sub-tick base, relative to the cadence start; each beat may require a token
    /// (a syllable index) or accept any input (<see cref="BeatInput.AnyToken"/>), which is how the rhythm route
    /// stands in for voice with the same syllable count and cadence (spec L367).
    /// </summary>
    public sealed class BeatPattern
    {
        private readonly long[] _times;
        private readonly int[] _tokens;

        public BeatPattern(IReadOnlyList<long> beatTimes, IReadOnlyList<int> tokens)
        {
            if (beatTimes == null || tokens == null) throw new ArgumentNullException(beatTimes == null ? nameof(beatTimes) : nameof(tokens));
            if (beatTimes.Count == 0 || beatTimes.Count != tokens.Count) throw new ArgumentException("One token per beat is required.");
            _times = new long[beatTimes.Count];
            _tokens = new int[tokens.Count];
            for (int i = 0; i < _times.Length; i++)
            {
                if (i > 0 && beatTimes[i] <= beatTimes[i - 1]) throw new ArgumentException("Beats must be strictly increasing.");
                if (tokens[i] < BeatInput.AnyToken) throw new ArgumentException("A beat token is a syllable index or AnyToken.");
                _times[i] = beatTimes[i];
                _tokens[i] = tokens[i];
            }
        }

        /// <summary>Beats spaced <paramref name="intervalSec"/> apart, the first at <paramref name="firstSec"/>.</summary>
        public static BeatPattern Evenly(double firstSec, double intervalSec, IReadOnlyList<int> tokens)
        {
            var times = new long[tokens.Count];
            for (int i = 0; i < times.Length; i++) times[i] = GrammarInputEvent.SubTicksFromSeconds(firstSec + i * intervalSec);
            return new BeatPattern(times, tokens);
        }

        public int Count => _times.Length;
        public long BeatTime(int index) => _times[index];
        public int Token(int index) => _tokens[index];

        /// <summary>The same timing with every token relaxed to "any": the equivalent rhythm route (spec L367).</summary>
        public BeatPattern AsRhythm()
        {
            var any = new int[_tokens.Length];
            for (int i = 0; i < any.Length; i++) any[i] = BeatInput.AnyToken;
            return new BeatPattern(_times, any);
        }
    }

    /// <summary>Outcome of scoring a phrase: normal completion, mastery, and the first exact miss.</summary>
    public readonly struct BeatScore
    {
        public BeatScore(bool completed, bool mastered, MissReason miss, int beatIndex, long errorSubTicks)
        {
            Completed = completed;
            Mastered = mastered;
            Miss = miss;
            BeatIndex = beatIndex;
            ErrorSubTicks = errorSubTicks;
        }

        /// <summary>Every beat matched in order within the normal tolerance: a normal cast.</summary>
        public bool Completed { get; }

        /// <summary>Every beat within the mastery tolerance and nothing extra.</summary>
        public bool Mastered { get; }

        public MissReason Miss { get; }

        /// <summary>The beat that decided the miss (for "missing syllable 2" feedback), or -1.</summary>
        public int BeatIndex { get; }

        /// <summary>Signed timing error of that beat in sub-ticks (negative early).</summary>
        public long ErrorSubTicks { get; }
    }

    /// <summary>
    /// Scores timed inputs against a <see cref="BeatPattern"/>. Voice and rhythm inputs go through the same scorer
    /// with the same windows, so equal timing gives equal results whatever the route (spec L365-L367, L490).
    /// </summary>
    public static class BeatScorer
    {
        public static BeatScore Score(BeatPattern pattern, IReadOnlyList<BeatInput> inputs, long normalTolerance, long masteryTolerance)
        {
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (masteryTolerance > normalTolerance) throw new ArgumentException("The mastery tolerance cannot be looser than the normal one.");
            bool mastered = true;
            MissReason miss = MissReason.None;
            int missBeat = -1;
            long missError = 0;
            for (int i = 0; i < pattern.Count; i++)
            {
                if (i >= inputs.Count) return new BeatScore(false, false, MissReason.MissingInput, i, 0);
                int heard = inputs[i].Token;
                if (heard == BeatInput.UnrecognizedToken) return new BeatScore(false, false, MissReason.UnrecognizedSyllable, i, 0);
                int expected = pattern.Token(i);
                if (expected != BeatInput.AnyToken && heard != BeatInput.AnyToken && heard != expected) return new BeatScore(false, false, MissReason.WrongOrder, i, 0);
                long error = inputs[i].Time - pattern.BeatTime(i);
                long size = Math.Abs(error);
                MissReason timing = error < 0 ? MissReason.EarlyBeat : MissReason.LateBeat;
                if (size > normalTolerance) return new BeatScore(false, false, timing, i, error);
                if (size > masteryTolerance && mastered)
                {
                    mastered = false;
                    miss = timing;
                    missBeat = i;
                    missError = error;
                }
            }

            // Repeating beyond the phrase earns nothing extra (spec L373 "useful ordered inputs rather than spam").
            if (mastered && inputs.Count > pattern.Count) return new BeatScore(true, false, MissReason.ExtraInputs, pattern.Count, 0);
            return new BeatScore(true, mastered, miss, missBeat, missError);
        }

        /// <summary>
        /// Beat inputs from grammar events for one route, in time order. Voice keeps every syllable event; one whose
        /// confidence is below the calibrated threshold keeps its place as <see cref="BeatInput.UnrecognizedToken"/>,
        /// so the phrase cannot commit and the overlay names that syllable (spec L365). Rhythm keeps taps as
        /// <see cref="BeatInput.AnyToken"/>. Confidence never scales a reward (spec L367).
        /// </summary>
        public static List<BeatInput> FromEvents(IReadOnlyList<GrammarInputEvent> events, InputRoute route, long cadenceStart, float confidenceThreshold)
        {
            var result = new List<BeatInput>();
            if (events == null) return result;
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (route == InputRoute.Voice && e.Kind == GrammarInputKind.Syllable)
                {
                    int token = e.Confidence >= confidenceThreshold ? e.Value : BeatInput.UnrecognizedToken;
                    result.Add(new BeatInput(e.Time - cadenceStart, token));
                }
                else if (route == InputRoute.Rhythm && e.Kind == GrammarInputKind.RhythmTap)
                {
                    result.Add(new BeatInput(e.Time - cadenceStart, BeatInput.AnyToken));
                }
            }

            // Stable insertion sort: equal timestamps keep arrival order on every runtime (List.Sort is unstable).
            for (int i = 1; i < result.Count; i++)
            {
                var item = result[i];
                int j = i - 1;
                while (j >= 0 && result[j].Time > item.Time)
                {
                    result[j + 1] = result[j];
                    j--;
                }

                result[j + 1] = item;
            }

            return result;
        }
    }
}
