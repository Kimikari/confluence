using System;
using UnityEngine.UIElements;

namespace Confluence.Client.UI
{
    /// <summary>Small factory for the UI Toolkit elements every screen uses; styling lives in Resources/UI/Confluence.uss.</summary>
    public static class Ui
    {
        public static VisualElement Element(params string[] classes)
        {
            var element = new VisualElement();
            AddClasses(element, classes);
            return element;
        }

        public static Label Label(string text, params string[] classes)
        {
            var label = new Label(text);
            AddClasses(label, classes);
            return label;
        }

        public static Label Text(string text) => Label(text, "cf-text");

        public static Label Muted(string text) => Label(text, "cf-muted");

        public static Label Heading(string text) => Label(text, "cf-heading");

        public static Button Button(string text, Action onClick, params string[] classes)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("cf-button");
            AddClasses(button, classes);
            return button;
        }

        public static VisualElement Panel(params string[] classes)
        {
            VisualElement panel = Element("cf-panel");
            AddClasses(panel, classes);
            return panel;
        }

        public static VisualElement Row() => Element("cf-row");

        public static VisualElement Column() => Element("cf-column");

        public static TextField TextField(string label, string value, int maxLength, Action<string> onChanged)
        {
            var field = new TextField(label) { value = value ?? string.Empty, maxLength = maxLength };
            field.AddToClassList("cf-field");
            if (onChanged != null)
            {
                field.RegisterValueChangedCallback(e => onChanged(e.newValue));
            }

            return field;
        }

        public static IntegerField IntegerField(string label, int value, Action<int> onChanged)
        {
            var field = new IntegerField(label) { value = value };
            field.AddToClassList("cf-field");
            if (onChanged != null)
            {
                field.RegisterValueChangedCallback(e => onChanged(e.newValue));
            }

            return field;
        }

        public static SliderInt SliderInt(string label, int min, int max, int value, Action<int> onChanged)
        {
            var slider = new SliderInt(label, min, max) { value = value, showInputField = true };
            slider.AddToClassList("cf-field");
            if (onChanged != null)
            {
                slider.RegisterValueChangedCallback(e => onChanged(e.newValue));
            }

            return slider;
        }

        public static Slider Slider(string label, float min, float max, float value, Action<float> onChanged)
        {
            var slider = new Slider(label, min, max) { value = value, showInputField = true };
            slider.AddToClassList("cf-field");
            if (onChanged != null)
            {
                slider.RegisterValueChangedCallback(e => onChanged(e.newValue));
            }

            return slider;
        }

        public static Toggle Toggle(string label, bool value, Action<bool> onChanged)
        {
            var toggle = new Toggle(label) { value = value };
            toggle.AddToClassList("cf-field");
            if (onChanged != null)
            {
                toggle.RegisterValueChangedCallback(e => onChanged(e.newValue));
            }

            return toggle;
        }

        private static void AddClasses(VisualElement element, string[] classes)
        {
            if (classes == null)
            {
                return;
            }

            for (int i = 0; i < classes.Length; i++)
            {
                if (!string.IsNullOrEmpty(classes[i]))
                {
                    element.AddToClassList(classes[i]);
                }
            }
        }
    }
}
