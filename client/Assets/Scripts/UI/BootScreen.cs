using Confluence.Client.App;
using UnityEngine.UIElements;

namespace Confluence.Client.UI
{
    /// <summary>Shown while the catalogue loads; reports a load failure with the fix.</summary>
    public static class BootScreen
    {
        public static VisualElement Build(ClientContext context)
        {
            VisualElement screen = Ui.Element("cf-screen", "cf-screen--menu");
            VisualElement column = Ui.Column();
            column.Add(Ui.Label("CONFLUENCE", "cf-title"));
            column.Add(Ui.Text(context.Catalogue.Describe()));
            screen.Add(column);
            return screen;
        }
    }
}
