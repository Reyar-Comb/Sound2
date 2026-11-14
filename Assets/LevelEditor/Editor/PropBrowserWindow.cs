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
        VisualElement categories;
        Label results;
        readonly Dictionary<GameObject, Image> previews = new();
        [SerializeField] string search = "";
        [SerializeField] string category = "全部";
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
            categories = new VisualElement { name = "prop-categories" }; rootVisualElement.Add(categories);
            rootVisualElement.Add(new Label("点击预览选择道具，再到 Scene 中放置。") { name = "prop-help" });
            results = new Label { name = "prop-results" }; rootVisualElement.Add(results);
            var scroll = new ScrollView { name = "prop-scroll" }; rootVisualElement.Add(scroll);
            grid = new VisualElement { name = "prop-grid" }; scroll.Add(grid);
            Populate(); rootVisualElement.schedule.Execute(UpdatePreviews).Every(150);
        }
        void Populate()
        {
            if (grid == null) return;
            grid.Clear(); previews.Clear();
            var all = PropCatalog.All();
            if (category != "全部" && !PropCatalog.Categories.Contains(category)) category = "全部";
            categories.Clear();
            foreach (var name in new[] { "全部" }.Concat(PropCatalog.Categories))
            {
                int count = name == "全部" ? all.Count : all.Count(p => PropCatalog.Category(p) == name);
                var tab = new Button(() => { category = name; Populate(); }) { name = "prop-category-" + name, text = name + "（" + count + "）" };
                tab.AddToClassList("prop-category"); tab.EnableInClassList("prop-category-selected", category == name);
                categories.Add(tab);
            }
            string query = search.Trim();
            var matching = all.Where(p => (category == "全部" || PropCatalog.Category(p) == category)
                && (query.Length == 0 || PropCatalog.Name(p).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || p.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            foreach (var group in PropCatalog.Categories)
            {
                var members = matching.Where(p => PropCatalog.Category(p) == group).ToList();
                if (members.Count == 0) continue;
                var section = new VisualElement(); section.AddToClassList("prop-group");
                var heading = new Label(group + " · " + members.Count); heading.AddToClassList("prop-group-title"); section.Add(heading);
                var cards = new VisualElement(); cards.AddToClassList("prop-group-grid"); section.Add(cards); grid.Add(section);
                foreach (var prefab in members)
                {
                    var card = new Button(() => { choose?.Invoke(prefab); }) { name = "prop-card-" + prefab.name, tooltip = group + " / " + PropCatalog.Name(prefab) };
                    card.AddToClassList("prop-card");
                    var preview = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                    preview.AddToClassList("prop-preview"); card.Add(preview); previews.Add(prefab, preview);
                    card.Add(new Label(PropCatalog.Name(prefab)) { pickingMode = PickingMode.Ignore }); cards.Add(card);
                }
            }
            results.text = category + " · 显示 " + matching.Count + " / " + all.Count + " 种道具";
            if (previews.Count == 0) grid.Add(new Label("当前分类没有匹配的道具，请切换到“全部”或修改搜索词。"));
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
