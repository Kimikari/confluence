using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Confluence.Client.App;
using Confluence.Core.Content;
using Confluence.Core.Json;
using UnityEngine.UIElements;

namespace Confluence.Client.UI
{
    /// <summary>
    /// Catalogue browser (spec L341 Codex): pick a content type, filter by ID or name, and read a record's exact
    /// catalogue fields. Shows the loaded data as-is; implementation status and locked-prerequisite display
    /// arrive with the interface stage (IMPLEMENTATION_PLAN Stage 8).
    /// </summary>
    public static class CodexScreen
    {
        public const int ListItemHeight = 30;

        public static VisualElement Build(ClientContext context)
        {
            VisualElement screen = Ui.Element("cf-screen", "cf-screen--menu");
            screen.style.flexDirection = FlexDirection.Column;

            VisualElement header = Ui.Row();
            header.Add(Ui.Label("Codex", "cf-heading"));
            VisualElement spacer = Ui.Element("cf-grow");
            header.Add(spacer);
            header.Add(Ui.Button("Back", () => context.States.TryTransitionTo(AppState.MainMenu), "cf-button--small"));
            screen.Add(header);

            Catalogue catalogue = context.Catalogue.Catalogue;
            if (catalogue == null)
            {
                screen.Add(Ui.Text(context.Catalogue.Describe()));
                return screen;
            }

            var state = new CodexState(catalogue);
            VisualElement tabs = Ui.Row();
            tabs.style.flexWrap = Wrap.Wrap;
            tabs.style.marginBottom = 8;
            screen.Add(tabs);

            TextField search = Ui.TextField("Filter", string.Empty, 64, null);
            search.style.maxWidth = 640;
            screen.Add(search);

            VisualElement body = Ui.Row();
            body.style.flexGrow = 1;
            body.style.alignItems = Align.Stretch;
            screen.Add(body);

            var list = new ListView
            {
                fixedItemHeight = ListItemHeight,
                selectionType = SelectionType.Single,
                makeItem = () => Ui.Label(string.Empty, "cf-codex-item"),
            };
            list.AddToClassList("cf-codex-list");
            list.bindItem = (element, index) =>
            {
                ContentDef def = state.Visible[index];
                ((Label)element).text = def.Id + "  " + def.Name;
            };
            body.Add(list);

            var detail = new ScrollView(ScrollViewMode.Vertical);
            detail.AddToClassList("cf-codex-detail");
            body.Add(detail);

            var tabButtons = new List<Button>();
            void Refresh()
            {
                state.ApplyFilter(search.value);
                list.itemsSource = state.Visible;
                list.RefreshItems();
                for (int i = 0; i < tabButtons.Count; i++)
                {
                    tabButtons[i].EnableInClassList("cf-button--selected", ContentIds.AllTypes[i] == state.Type);
                }

                if (state.Visible.Count > 0)
                {
                    list.SetSelection(0);
                }
                else
                {
                    ShowDetail(detail, null);
                }
            }

            foreach (ContentType type in ContentIds.AllTypes)
            {
                ContentType captured = type;
                Button tab = Ui.Button(Plural(type) + " (" + state.Count(type) + ")", () =>
                {
                    state.Type = captured;
                    Refresh();
                }, "cf-button--small");
                tabButtons.Add(tab);
                tabs.Add(tab);
            }

            list.selectionChanged += selected =>
            {
                foreach (object item in selected)
                {
                    ShowDetail(detail, item as ContentDef);
                    return;
                }
            };
            search.RegisterValueChangedCallback(_ => Refresh());
            Refresh();
            return screen;
        }

        private static void ShowDetail(ScrollView detail, ContentDef def)
        {
            detail.Clear();
            if (def == null)
            {
                detail.Add(Ui.Muted("No matching records."));
                return;
            }

            detail.Add(Ui.Heading(def.Name));
            detail.Add(Ui.Muted(def.Id + " | " + def.Type + (def.Tags != null && def.Tags.Count > 0 ? " | tags: " + string.Join(", ", def.Tags) : string.Empty)));
            if (def.Raw == null || !def.Raw.IsObject)
            {
                return;
            }

            foreach (KeyValuePair<string, JsonValue> field in def.Raw.AsObject())
            {
                if (field.Key == "id" || field.Key == "name" || field.Key == "tags")
                {
                    continue;
                }

                detail.Add(Ui.Label(Humanize(field.Key), "cf-codex-key"));
                detail.Add(Ui.Text(FormatValue(field.Value)));
            }
        }

        /// <summary>Readable text for a catalogue value: strings as-is, string lists joined, anything else as compact JSON.</summary>
        public static string FormatValue(JsonValue value)
        {
            if (value == null || value.IsNull)
            {
                return "-";
            }

            if (value.IsString)
            {
                return value.AsString();
            }

            if (value.IsNumber)
            {
                return JsonWriter.FormatNumber(value.AsDouble());
            }

            if (value.IsBool)
            {
                return value.AsBool() ? "yes" : "no";
            }

            if (value.IsArray)
            {
                bool allScalars = true;
                foreach (JsonValue item in value.AsArray())
                {
                    allScalars &= item.IsString || item.IsNumber;
                }

                if (allScalars)
                {
                    var sb = new StringBuilder();
                    foreach (JsonValue item in value.AsArray())
                    {
                        if (sb.Length > 0)
                        {
                            sb.Append(", ");
                        }

                        sb.Append(item.IsString ? item.AsString() : JsonWriter.FormatNumber(item.AsDouble()));
                    }

                    return sb.ToString();
                }
            }

            return JsonWriter.Write(value, false);
        }

        public static string Humanize(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            string spaced = key.Replace('_', ' ');
            return char.ToUpper(spaced[0], CultureInfo.InvariantCulture) + spaced.Substring(1);
        }

        private static string Plural(ContentType type)
        {
            switch (type)
            {
                case ContentType.Wizard: return "Wizards";
                case ContentType.Spell: return "Spells";
                case ContentType.Equipment: return "Equipment";
                case ContentType.Augment: return "Augments";
                case ContentType.Biome: return "Biomes";
                case ContentType.Module: return "Modules";
                case ContentType.Prop: return "Props";
                default: return "Builds";
            }
        }

        /// <summary>Selected type plus the filtered record list, in catalogue order.</summary>
        private sealed class CodexState
        {
            private readonly Catalogue _catalogue;

            public CodexState(Catalogue catalogue)
            {
                _catalogue = catalogue;
            }

            public ContentType Type { get; set; } = ContentType.Wizard;

            public List<ContentDef> Visible { get; } = new List<ContentDef>();

            public int Count(ContentType type) => Records(type).Count;

            public void ApplyFilter(string filter)
            {
                Visible.Clear();
                string f = (filter ?? string.Empty).Trim();
                foreach (ContentDef def in Records(Type))
                {
                    if (f.Length == 0
                        || def.Id.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                        || (def.Name != null && def.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        Visible.Add(def);
                    }
                }
            }

            private IReadOnlyList<ContentDef> Records(ContentType type)
            {
                switch (type)
                {
                    case ContentType.Wizard: return _catalogue.Wizards;
                    case ContentType.Spell: return _catalogue.Spells;
                    case ContentType.Equipment: return _catalogue.EquipmentItems;
                    case ContentType.Augment: return _catalogue.Augments;
                    case ContentType.Biome: return _catalogue.Biomes;
                    case ContentType.Module: return _catalogue.Modules;
                    case ContentType.Prop: return _catalogue.Props;
                    default: return _catalogue.Builds;
                }
            }
        }
    }
}
