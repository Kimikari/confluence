using System;
using Confluence.Core.Rules;

namespace Confluence.Core.Content
{
    /// <summary>
    /// One of the 288 exclusive base spells. Numeric fields are binding tuning targets (spec L523-L525):
    /// mana and cooldown are paid at commitment, cast time is intrinsic windup after preparation, range is in meters.
    /// </summary>
    public sealed class SpellDef : ContentDef
    {
        private readonly School _school;
        private readonly bool _schoolValid;
        private readonly DamageChannel _channel;
        private readonly bool _channelValid;

        internal SpellDef(RecordReader r)
            : base(r.RequireId(), r.String("name"), r.StringList("tags"), r.Json)
        {
            School = r.String("school");
            Role = r.String("role");
            DamageChannel = r.String("damage_channel");
            Mana = r.Number("mana");
            Cooldown = r.Number("cooldown");
            CastTime = r.Number("cast_time");
            Range = r.Number("range");
            Damage = r.String("damage");
            Effect = r.String("effect");
            WorldInteraction = r.String("world_interaction");
            Counterplay = r.String("counterplay");
            DeclaredWizardId = r.OptionalString("wizard_id");
            r.Finish();

            _schoolValid = ContentText.TryParseSchool(School, out _school);
            _channelValid = ContentText.TryParseChannel(DamageChannel, out _channel);
        }

        public override ContentType Type => ContentType.Spell;

        /// <summary>School label as written ("Heat"). See <see cref="SchoolEnum"/>.</summary>
        public string School { get; }

        public string Role { get; }

        /// <summary>Damage channel as written ("thermal"). See <see cref="Channel"/>.</summary>
        public string DamageChannel { get; }

        public double Mana { get; }
        public double Cooldown { get; }
        public double CastTime { get; }
        public double Range { get; }

        /// <summary>Explicit damage expression, e.g. "48 direct OR 28 splash".</summary>
        public string Damage { get; }

        public string Effect { get; }
        public string WorldInteraction { get; }
        public string Counterplay { get; }

        /// <summary>The record's own wizard_id field, or null if absent.</summary>
        public string DeclaredWizardId { get; }

        /// <summary>Owning wizard: the declared wizard_id, else derived from the ID (WIZ-n owns SPL-(8n-7)..SPL-8n, spec L294).</summary>
        public string WizardId => DeclaredWizardId ?? ContentIds.OwnerOfSpell(Id);

        public bool HasValidSchool => _schoolValid;
        public bool HasValidChannel => _channelValid;

        public School SchoolEnum
        {
            get
            {
                if (!_schoolValid) throw new InvalidOperationException(Id + " has unrecognised school '" + School + "'.");
                return _school;
            }
        }

        public DamageChannel Channel
        {
            get
            {
                if (!_channelValid) throw new InvalidOperationException(Id + " has unrecognised damage channel '" + DamageChannel + "'.");
                return _channel;
            }
        }

        public bool IsDamaging => HasTag("damaging");
    }
}
