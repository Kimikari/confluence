namespace Confluence.Casting
{
    /// <summary>
    /// Exactly why a cast earned no mastery reward. The grammar overlay reports it instead of "bad cast" (spec L500:
    /// "late beat, wrong order, incorrect ratio, broken tether, insufficient travel, or inaccurate stroke").
    /// </summary>
    public enum MissReason : byte
    {
        None = 0,

        /// <summary>Guided or assisted preparation completes a normal action without reward (spec L470, L490).</summary>
        Guided,

        /// <summary>The cast has no grammar reward (signature activations, spec L472).</summary>
        NotApplicable,

        // Timing
        EarlyRelease,
        LateRelease,
        /// <summary>Held to the end of the window; the gauge vented a normal cast (WIZ-002).</summary>
        AutoVented,
        /// <summary>Still holding when the release window closed (WIZ-010).</summary>
        HeldPastWindow,
        EarlyBeat,
        LateBeat,
        TooSlow,

        // Gauges and bands
        /// <summary>The gauge left its safe band and vented (WIZ-010).</summary>
        LeftSafeBand,
        /// <summary>Released while the gauge was outside the mastery band.</summary>
        OutsideMasteryBand,
        /// <summary>Inside the mastery band, but not continuously for the required time before release.</summary>
        BandHeldTooShort,
        /// <summary>Toggles arrived faster than the accepted input interval and were not applied (spec L478).</summary>
        TooFastToggling,

        // Sequences, widgets, puzzles
        WrongOrder,
        MissingInput,
        Corrected,
        WrongChoice,
        IncorrectRatio,
        NotMinimal,
        ExtraInputs,

        // Gestures and aim
        InaccurateStroke,
        EndpointsOff,
        AimDrift,

        // Movement
        BrokenTether,
        InsufficientTravel,
        InsufficientSpeed,
        OffVector,
        InvalidatedByCollision,

        // Combat-event grammars
        NoCombatEvent,
        EventWindowMissed,
        ArmedRewardExpired,

        /// <summary>The selected spell is not eligible for this grammar's reward category.</summary>
        NotEligible,
    }

    public static class MissReasons
    {
        /// <summary>Short player-facing explanation (localization key text).</summary>
        public static string Describe(MissReason reason)
        {
            switch (reason)
            {
                case MissReason.None: return "mastered";
                case MissReason.Guided: return "guided cast: no mastery reward";
                case MissReason.NotApplicable: return "no grammar reward for this cast";
                case MissReason.EarlyRelease: return "early release";
                case MissReason.LateRelease: return "late release";
                case MissReason.AutoVented: return "held too long: auto-vented";
                case MissReason.HeldPastWindow: return "held past the release window";
                case MissReason.EarlyBeat: return "early beat";
                case MissReason.LateBeat: return "late beat";
                case MissReason.TooSlow: return "too slow";
                case MissReason.LeftSafeBand: return "left the safe band";
                case MissReason.OutsideMasteryBand: return "released outside the mastery band";
                case MissReason.BandHeldTooShort: return "not held in the band long enough";
                case MissReason.TooFastToggling: return "toggling faster than the accepted interval";
                case MissReason.WrongOrder: return "wrong order";
                case MissReason.MissingInput: return "missing input";
                case MissReason.Corrected: return "corrected input";
                case MissReason.WrongChoice: return "wrong choice";
                case MissReason.IncorrectRatio: return "incorrect ratio";
                case MissReason.NotMinimal: return "not the minimum solution";
                case MissReason.ExtraInputs: return "extra inputs";
                case MissReason.InaccurateStroke: return "inaccurate stroke";
                case MissReason.EndpointsOff: return "stroke endpoints off";
                case MissReason.AimDrift: return "aim drifted";
                case MissReason.BrokenTether: return "broken tether";
                case MissReason.InsufficientTravel: return "insufficient travel";
                case MissReason.InsufficientSpeed: return "insufficient speed";
                case MissReason.OffVector: return "off the declared vector";
                case MissReason.InvalidatedByCollision: return "terrain collision";
                case MissReason.NoCombatEvent: return "no qualifying combat event";
                case MissReason.EventWindowMissed: return "missed the event window";
                case MissReason.ArmedRewardExpired: return "armed reward expired";
                case MissReason.NotEligible: return "spell not eligible for this reward";
                default: return reason.ToString();
            }
        }
    }
}
