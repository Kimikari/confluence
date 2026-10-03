using Confluence.Client.Mapping;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using UnityEngine;
using UnityEngine.Rendering;

namespace Confluence.Client.Arena
{
    /// <summary>
    /// TEMPORARY ASSET (docs/notes/client.md): placeholder body for a placed prop. Shape follows the catalogue
    /// category (barrel, basin, crate, prism, boulder, gong, crystal, post), size follows the mass class
    /// (spec L173: light, medium, heavy, anchored), and a camera-facing icon shows the interaction category
    /// (spec L329 "prop interaction icons"). In a duel the renderer moves and hides it with the Sim's prop body
    /// (<see cref="ArenaRenderer.SyncProps"/>).
    /// </summary>
    public static class PropPlaceholder
    {
        public const float IconSize = 0.7f;
        public const float IconGap = 0.45f;

        public static GameObject Build(PropPlacement placement, PropDef def, Transform parent, ArenaMaterialLibrary materials)
        {
            PropCategory category = def != null && def.HasValidCategory ? def.Category : PropCategory.Utility;
            PropMass mass = def != null && def.HasValidMass ? def.Mass : PropMass.Medium;
            float size = SizeFor(mass);

            var root = new GameObject("Prop " + placement.Id + " " + (placement.PropId ?? "?") + (def != null ? " " + def.Name : string.Empty));
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(CoreSpace.ToUnity(placement.Position), CoreSpace.YawRotation(placement.YawDeg));

            BodyFor(category, size, out PrimitiveType primitive, out Vector3 scale, out Vector3 euler, out SurfaceKind pattern, out Color color);
            GameObject body = GameObject.CreatePrimitive(primitive);
            body.name = "Body";
            RemoveCollider(body);
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = scale;
            body.transform.localRotation = Quaternion.Euler(euler);
            body.transform.localPosition = new Vector3(0f, BodyHalfHeight(primitive, scale, euler), 0f);
            var bodyRenderer = body.GetComponent<MeshRenderer>();
            bodyRenderer.sharedMaterial = materials.Flat(color, pattern);
            bodyRenderer.shadowCastingMode = ShadowCastingMode.On;

            GameObject icon = GameObject.CreatePrimitive(PrimitiveType.Quad);
            icon.name = "Icon " + category;
            RemoveCollider(icon);
            icon.transform.SetParent(root.transform, false);
            icon.transform.localScale = Vector3.one * IconSize;
            icon.transform.localPosition = new Vector3(0f, 2f * BodyHalfHeight(primitive, scale, euler) + IconGap, 0f);
            var iconRenderer = icon.GetComponent<MeshRenderer>();
            iconRenderer.sharedMaterial = materials.Glyph(category);
            iconRenderer.shadowCastingMode = ShadowCastingMode.Off;
            iconRenderer.receiveShadows = false;
            icon.AddComponent<Billboard>();
            return root;
        }

        /// <summary>Placeholder body size in meters by mass class.</summary>
        public static float SizeFor(PropMass mass)
        {
            switch (mass)
            {
                case PropMass.Light: return 0.5f;
                case PropMass.Heavy: return 1.3f;
                case PropMass.Anchored: return 1.15f;
                default: return 0.9f;
            }
        }

        private static void BodyFor(PropCategory category, float size, out PrimitiveType primitive, out Vector3 scale, out Vector3 euler, out SurfaceKind pattern, out Color color)
        {
            euler = Vector3.zero;
            switch (category)
            {
                case PropCategory.Conductive: // barrel
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.7f * size, 0.5f * size, 0.7f * size);
                    pattern = SurfaceKind.Metal;
                    color = new Color(0.78f, 0.5f, 0.3f);
                    break;
                case PropCategory.Reservoir: // basin or urn
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(1.1f * size, 0.35f * size, 1.1f * size);
                    pattern = SurfaceKind.Water;
                    color = new Color(0.35f, 0.6f, 0.9f);
                    break;
                case PropCategory.Fuel: // crate or bundle
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(size, 0.8f * size, size);
                    pattern = SurfaceKind.Wood;
                    color = new Color(0.7f, 0.5f, 0.28f);
                    break;
                case PropCategory.Optical: // prism standing on a corner
                    primitive = PrimitiveType.Cube;
                    scale = Vector3.one * 0.75f * size;
                    euler = new Vector3(45f, 0f, 35.264f);
                    pattern = SurfaceKind.Glass;
                    color = new Color(0.75f, 0.95f, 1f);
                    break;
                case PropCategory.Kinetic: // boulder
                    primitive = PrimitiveType.Sphere;
                    scale = Vector3.one * size;
                    pattern = SurfaceKind.Stone;
                    color = new Color(0.6f, 0.58f, 0.55f);
                    break;
                case PropCategory.Acoustic: // upright gong
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(1.1f * size, 0.06f * size, 1.1f * size);
                    euler = new Vector3(90f, 0f, 0f);
                    pattern = SurfaceKind.Metal;
                    color = new Color(0.85f, 0.7f, 0.35f);
                    break;
                case PropCategory.Reactive: // crystal
                    primitive = PrimitiveType.Capsule;
                    scale = new Vector3(0.45f * size, 0.6f * size, 0.45f * size);
                    pattern = SurfaceKind.Ice;
                    color = new Color(0.75f, 0.6f, 1f);
                    break;
                default: // utility post
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(0.4f * size, 1.3f * size, 0.4f * size);
                    pattern = SurfaceKind.Stone;
                    color = new Color(0.85f, 0.85f, 0.8f);
                    break;
            }
        }

        // Half the world-space height of the rotated primitive, so it rests on the placement point.
        private static float BodyHalfHeight(PrimitiveType primitive, Vector3 scale, Vector3 euler)
        {
            Vector3 meshHalf;
            switch (primitive)
            {
                case PrimitiveType.Cylinder: meshHalf = new Vector3(0.5f, 1f, 0.5f); break;
                case PrimitiveType.Capsule: meshHalf = new Vector3(0.5f, 1f, 0.5f); break;
                default: meshHalf = new Vector3(0.5f, 0.5f, 0.5f); break;
            }

            Vector3 half = Vector3.Scale(meshHalf, scale);
            Quaternion q = Quaternion.Euler(euler);
            float y = 0f;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) != 0 ? half.x : -half.x, (i & 2) != 0 ? half.y : -half.y, (i & 4) != 0 ? half.z : -half.z);
                y = Mathf.Max(y, (q * corner).y);
            }

            return y;
        }

        private static void RemoveCollider(GameObject go)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                ArenaMaterialLibrary.DestroyObject(collider);
            }
        }
    }
}
