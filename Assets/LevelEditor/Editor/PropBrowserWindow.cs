using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sound2.LevelEditing.Editor
{
    public sealed class PropBrowserWindow : EditorWindow
    {
        Action<GameObject> choose;
        VisualElement grid;
        readonly Dictionary<GameObject, Image> previews = new();
        string search = "";
        public static void Open(Action<GameObject> selected)
        {
            var window = GetWindow<PropBrowserWindow>("道具浏览器");
            window.choose = selected; window.minSize = new Vector2(360, 300); window.Show(); window.Focus();
        }
        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/LevelEditor/Editor/PropBrowser.uss"));
            rootVisualElement.AddToClassList("prop-browser");
            var field = new ToolbarSearchField { name = "prop-search", value = search };
            field.RegisterValueChangedCallback(e => { search = e.newValue; Populate(); }); rootVisualElement.Add(field);
            rootVisualElement.Add(new Label("点击预览选择道具，再到 Scene 中放置。") { name = "prop-help" });
            var scroll = new ScrollView { name = "prop-scroll" }; rootVisualElement.Add(scroll);
            grid = new VisualElement { name = "prop-grid" }; scroll.Add(grid);
            Populate(); rootVisualElement.schedule.Execute(UpdatePreviews).Every(150);
        }
        void Populate()
        {
            if (grid == null) return;
            grid.Clear(); previews.Clear();
            foreach (var prefab in PropCatalog.All())
            {
                string title = PropCatalog.Name(prefab);
                if (!string.IsNullOrWhiteSpace(search) && title.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                    && prefab.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var card = new Button(() => { choose?.Invoke(prefab); }) { name = "prop-card-" + prefab.name };
                card.AddToClassList("prop-card");
                var preview = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                preview.AddToClassList("prop-preview"); card.Add(preview); previews.Add(prefab, preview);
                card.Add(new Label(title) { pickingMode = PickingMode.Ignore }); grid.Add(card);
            }
            if (previews.Count == 0) grid.Add(new Label("没有匹配的道具"));
            UpdatePreviews();
        }
        void OnProjectChange() => Populate();
        void UpdatePreviews()
        {
            foreach (var entry in previews)
                if (entry.Key != null && entry.Value.image == null)
                    entry.Value.image = AssetPreview.GetAssetPreview(entry.Key);
        }
    }
}
