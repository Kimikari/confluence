namespace Confluence.Client.App
{
    /// <summary>
    /// Top-level client flow. The match states follow the round sequence of spec L42-L49: shop (35 s), the
    /// augment draft on augment rounds (30 s), protected alcove (5 s), combat (up to 90 s), results (8 s).
    /// States after Connecting are placeholders until Confluence.Net provides a session.
    /// </summary>
    public enum AppState
    {
        /// <summary>Loading the catalogue and settings.</summary>
        Boot,

        /// <summary>Home: Play, Training, Codex, Settings, Quit (spec L341).</summary>
        MainMenu,

        /// <summary>Connecting to a match server.</summary>
        Connecting,

        /// <summary>Pre-match lobby with practice space and wizard selection (spec L343).</summary>
        Lobby,

        /// <summary>Equipment shop and arena preview (spec L45, L329).</summary>
        Shop,

        /// <summary>Five-choice free augment draft on augment rounds (spec L45, L227).</summary>
        Draft,

        /// <summary>Protected spawn alcove before combat (spec L46).</summary>
        Alcove,

        /// <summary>Combat (spec L47-L48).</summary>
        Duel,

        /// <summary>Round results (spec L49, L359-L361).</summary>
        Results,

        /// <summary>Training hub (spec L377-L381).</summary>
        Training,

        /// <summary>Catalogue browser (spec L341, L353).</summary>
        Codex,

        /// <summary>Free walk/fly and top-down preview of an arena layout (debug and training).</summary>
        ArenaPreview,
    }
}
