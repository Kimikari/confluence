using System.Collections.Generic;

namespace Confluence.Core.Content
{
    /// <summary>One of the 36 wizards: casting grammar text, signature, passive, and the eight-spell kit (spec §6).</summary>
    public sealed class WizardDef : ContentDef
    {
        internal WizardDef(RecordReader r)
            : base(r.RequireId(), r.String("name"), r.StringList("tags"), r.Json)
        {
            Title = r.String("title");
            CastingMode = r.String("casting_mode");
            InputSequence = r.String("input_sequence");
            NormalCast = r.String("normal_cast");
            Mastery = r.String("mastery");
            Signature = r.String("signature");
            Passive = r.String("passive");
            RangePreference = r.String("range_preference");
            Weakness = r.String("weakness");
            Counterplay = r.String("counterplay");
            SpellIds = r.StringList("spell_ids");
            DefaultSpellIds = r.StringList("default_spell_ids");
            Lore = r.String("lore");
            VisualIdentity = r.String("visual_identity");
            AudioIdentity = r.String("audio_identity");
            Difficulty = r.Int("difficulty");
            r.Finish();
        }

        public override ContentType Type => ContentType.Wizard;

        public string Title { get; }
        public string CastingMode { get; }
        public string InputSequence { get; }
        public string NormalCast { get; }
        public string Mastery { get; }
        public string Signature { get; }
        public string Passive { get; }
        public string RangePreference { get; }
        public string Weakness { get; }
        public string Counterplay { get; }

        /// <summary>The eight exclusive spells in kit order.</summary>
        public IReadOnlyList<string> SpellIds { get; }

        /// <summary>The six spells equipped by default (the first six; spec L292).</summary>
        public IReadOnlyList<string> DefaultSpellIds { get; }

        public string Lore { get; }
        public string VisualIdentity { get; }
        public string AudioIdentity { get; }
        public int Difficulty { get; }

        public bool OwnsSpell(string spellId)
        {
            for (int i = 0; i < SpellIds.Count; i++)
                if (SpellIds[i] == spellId) return true;
            return false;
        }
    }
}
