using System.Collections.Generic;
using Confluence.Client.Arena;
using Confluence.Client.Mapping;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using UnityEngine;

namespace Confluence.Client.Presentation
{
    /// <summary>
    /// Renders the Sim's player-owned fields and summons (<see cref="OwnedEntityRegistry"/>, spec L128) from whatever
    /// creates them: a translucent disc of the entity's radius in its owner's color (neutral reaction fields in white)
    /// for fields, and a body whose size follows remaining health for summons (visible and destructible, spec L496).
    /// Nothing in this build creates owned entities yet (the spell runtime does), so the view is usually empty; it
    /// follows the registry each frame and needs no per-spell code.
    /// </summary>
    public sealed class OwnedEntityViews
    {
        private readonly Transform _root;
        private readonly ArenaMaterialLibrary _materials;
        private readonly Dictionary<int, GameObject> _views = new Dictionary<int, GameObject>();
        private readonly HashSet<int> _live = new HashSet<int>();
        private readonly List<int> _scratch = new List<int>();

        public OwnedEntityViews(Transform parent, ArenaMaterialLibrary materials)
        {
            _root = new GameObject("Owned entities").transform;
            _root.SetParent(parent, false);
            _materials = materials;
        }

        public int Count => _views.Count;

        public void Sync(DuelWorld world)
        {
            _live.Clear();
            IReadOnlyList<OwnedEntity> entities = world.Owned.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                OwnedEntity e = entities[i];
                if (!e.Active)
                {
                    continue;
                }

                _live.Add(e.Id);
                if (!_views.TryGetValue(e.Id, out GameObject view))
                {
                    view = Create(e);
                    _views.Add(e.Id, view);
                }

                Vector3 position = CoreSpace.ToUnity(e.Position);
                if (e.Kind == OwnedEntityKind.Field)
                {
                    view.transform.position = position + Vector3.up * 0.05f;
                    view.transform.localScale = new Vector3(e.Radius * 2f, 0.05f, e.Radius * 2f);
                }
                else
                {
                    float health = e.MaxHealth > 0f ? Mathf.Clamp01(e.Health / e.MaxHealth) : 1f;
                    float size = Mathf.Max(0.3f, e.Radius) * (0.6f + 0.4f * health);
                    view.transform.position = position + Vector3.up * size;
                    view.transform.localScale = Vector3.one * size * 2f;
                }
            }

            _scratch.Clear();
            foreach (KeyValuePair<int, GameObject> pair in _views)
            {
                if (!_live.Contains(pair.Key))
                {
                    _scratch.Add(pair.Key);
                }
            }

            for (int i = 0; i < _scratch.Count; i++)
            {
                ArenaMaterialLibrary.DestroyObject(_views[_scratch[i]]);
                _views.Remove(_scratch[i]);
            }
        }

        private GameObject Create(OwnedEntity e)
        {
            bool field = e.Kind == OwnedEntityKind.Field;
            GameObject go = GameObject.CreatePrimitive(field ? PrimitiveType.Cylinder : PrimitiveType.Sphere);
            go.name = (field ? "Field " : "Summon ") + e.Id + " " + e.DefinitionId + " (player " + e.Owner + ")";
            go.transform.SetParent(_root, false);
            ArenaMaterialLibrary.DestroyObject(go.GetComponent<Collider>());
            Color color = e.Neutral ? Color.white : FighterView.SideColors[e.Owner % FighterView.SideColors.Length];
            color.a = field ? 0.3f : 0.75f;
            go.GetComponent<MeshRenderer>().sharedMaterial = _materials.Glow(color);
            return go;
        }

        public void Clear()
        {
            foreach (GameObject view in _views.Values)
            {
                ArenaMaterialLibrary.DestroyObject(view);
            }

            _views.Clear();
        }
    }
}
