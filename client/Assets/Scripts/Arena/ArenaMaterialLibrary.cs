using System;
using System.Collections.Generic;
using Confluence.Core.Rules;
using UnityEngine;

namespace Confluence.Client.Arena
{
    /// <summary>
    /// Creates and owns the arena's runtime materials. Each material is a copy of one of three base materials
    /// that ProjectConfigurator generates under Resources/Materials (so the player build always contains the
    /// URP shader variants they use: opaque Lit, transparent Lit, transparent double-sided Unlit). Only
    /// textures, colors, and floats are changed at runtime, never shader keywords. Meshes carry UVs in
    /// meters; each material scales them by its style's tile size.
    /// </summary>
    public sealed class ArenaMaterialLibrary : IDisposable
    {
        public const string OpaqueResource = "Materials/ArenaLit";
        public const string TransparentResource = "Materials/ArenaLitTransparent";
        public const string UnlitResource = "Materials/ArenaUnlitTransparent";

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        // Spec L678: final-center soft cover is marked at 83 s; the hazard tint keeps the surface pattern.
        private static readonly Color WarningTint = new Color(1f, 0.55f, 0.1f, 1f);

        private readonly Material _opaqueBase;
        private readonly Material _transparentBase;
        private readonly Material _unlitBase;
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private readonly Dictionary<SurfaceKind, Texture2D> _surfaceTextures = new Dictionary<SurfaceKind, Texture2D>();
        private readonly Dictionary<int, Material> _cache = new Dictionary<int, Material>();

        public ArenaMaterialLibrary()
        {
            _opaqueBase = LoadBase(OpaqueResource, "Universal Render Pipeline/Lit", false);
            _transparentBase = LoadBase(TransparentResource, "Universal Render Pipeline/Lit", true);
            _unlitBase = LoadBase(UnlitResource, "Universal Render Pipeline/Unlit", true);
        }

        /// <summary>True when all three generated base materials were found (false falls back to Shader.Find).</summary>
        public bool UsesGeneratedBases { get; private set; } = true;

        public Texture2D SurfaceTexture(SurfaceKind kind)
        {
            if (!_surfaceTextures.TryGetValue(kind, out Texture2D texture))
            {
                texture = Own(PatternTextures.Surface(kind));
                _surfaceTextures.Add(kind, texture);
            }

            return texture;
        }

        /// <summary>Material for a collision solid of this surface kind.</summary>
        public Material Solid(SurfaceKind kind)
        {
            return Cached(Key(0, (int)kind), () =>
            {
                SurfaceStyle style = Surfaces.Style(kind);
                Material m = Copy(style.Translucent ? _transparentBase : _opaqueBase, "Solid " + kind);
                ApplyStyle(m, style, style.Tint);
                return m;
            });
        }

        /// <summary>Hazard-tinted solid material for final-center cover about to retire (spec L678).</summary>
        public Material Warning(SurfaceKind kind)
        {
            return Cached(Key(1, (int)kind), () =>
            {
                SurfaceStyle style = Surfaces.Style(kind);
                Material m = Copy(style.Translucent ? _transparentBase : _opaqueBase, "Warning " + kind);
                Color tint = Color.Lerp(style.Tint, WarningTint, 0.65f);
                tint.a = Mathf.Max(style.Tint.a, 0.6f);
                ApplyStyle(m, style, tint);
                return m;
            });
        }

        /// <summary>Translucent ground decal for a material patch.</summary>
        public Material Patch(SurfaceKind kind)
        {
            return Cached(Key(2, (int)kind), () =>
            {
                SurfaceStyle style = Surfaces.Style(kind);
                Material m = Copy(_transparentBase, "Patch " + kind);
                ApplyStyle(m, style, style.Tint);
                return m;
            });
        }

        /// <summary>Spawn ward volume; the two sides differ in pattern as well as tint.</summary>
        public Material Ward(int side)
        {
            return Cached(Key(3, side), () =>
            {
                Material m = Copy(_unlitBase, "Ward " + side);
                m.SetTexture(BaseMapId, Own(PatternTextures.Ward(side)));
                m.SetTextureScale(BaseMapId, new Vector2(0.5f, 0.5f));
                m.SetColor(BaseColorId, side == 0 ? new Color(0.35f, 0.7f, 1f, 1f) : new Color(1f, 0.62f, 0.3f, 1f));
                return m;
            });
        }

        /// <summary>Boundary wall (UVs: meters along the contour, 0-1 up the wall).</summary>
        public Material Boundary()
        {
            return Cached(Key(4, 0), () =>
            {
                Material m = Copy(_unlitBase, "Boundary");
                m.SetTexture(BaseMapId, Own(PatternTextures.Boundary()));
                m.SetTextureScale(BaseMapId, new Vector2(0.5f, 1f));
                m.SetColor(BaseColorId, new Color(1f, 0.25f, 0.4f, 1f));
                return m;
            });
        }

        /// <summary>Ground line under the boundary wall.</summary>
        public Material BoundaryLine()
        {
            return Cached(Key(5, 0), () =>
            {
                Material m = Copy(_unlitBase, "Boundary line");
                m.SetTexture(BaseMapId, Own(PatternTextures.FinalRing()));
                m.SetTextureScale(BaseMapId, new Vector2(1f, 1f));
                m.SetColor(BaseColorId, new Color(1f, 0.3f, 0.45f, 1f));
                return m;
            });
        }

        /// <summary>Dashed ring marking the anchored final floor (spec L48, L678).</summary>
        public Material FinalRing()
        {
            return Cached(Key(6, 0), () =>
            {
                Material m = Copy(_unlitBase, "Final ring");
                m.SetTexture(BaseMapId, Own(PatternTextures.FinalRing()));
                m.SetTextureScale(BaseMapId, new Vector2(1f, 1f));
                m.SetColor(BaseColorId, new Color(1f, 0.85f, 0.4f, 1f));
                return m;
            });
        }

        /// <summary>Billboard icon for a prop category.</summary>
        public Material Glyph(PropCategory category)
        {
            return Cached(Key(7, (int)category), () =>
            {
                Material m = Copy(_unlitBase, "Glyph " + category);
                m.SetTexture(BaseMapId, Own(PatternTextures.Glyph(category)));
                m.SetTextureScale(BaseMapId, Vector2.one);
                m.SetColor(BaseColorId, Color.white);
                return m;
            });
        }

        /// <summary>Opaque lit material with a flat tint (prop bodies, markers).</summary>
        public Material Flat(Color color, SurfaceKind pattern)
        {
            int key = Key(8, ((int)pattern << 24) ^ ((Color32)color).GetHashCode());
            return Cached(key, () =>
            {
                SurfaceStyle style = Surfaces.Style(pattern);
                Material m = Copy(_opaqueBase, "Flat " + pattern);
                Color opaque = color;
                opaque.a = 1f;
                ApplyStyle(m, style, opaque);
                return m;
            });
        }

        public void Dispose()
        {
            for (int i = _owned.Count - 1; i >= 0; i--)
            {
                DestroyObject(_owned[i]);
            }

            _owned.Clear();
            _cache.Clear();
            _surfaceTextures.Clear();
        }

        internal static void DestroyObject(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(obj);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        private void ApplyStyle(Material m, SurfaceStyle style, Color tint)
        {
            m.SetTexture(BaseMapId, SurfaceTexture(style.Kind));
            float scale = 1f / Mathf.Max(0.05f, style.TileMeters);
            m.SetTextureScale(BaseMapId, new Vector2(scale, scale));
            m.SetColor(BaseColorId, tint);
            if (m.HasProperty(MetallicId))
            {
                m.SetFloat(MetallicId, style.Metallic);
            }

            if (m.HasProperty(SmoothnessId))
            {
                m.SetFloat(SmoothnessId, style.Smoothness);
            }
        }

        private Material Cached(int key, Func<Material> create)
        {
            if (!_cache.TryGetValue(key, out Material material))
            {
                material = Own(create());
                _cache.Add(key, material);
            }

            return material;
        }

        private static int Key(int family, int value) => unchecked(family * 397 ^ value * 7919);

        private Material Copy(Material source, string name) => new Material(source) { name = name };

        private T Own<T>(T obj) where T : UnityEngine.Object
        {
            _owned.Add(obj);
            return obj;
        }

        private Material LoadBase(string resource, string shaderName, bool transparent)
        {
            Material material = Resources.Load<Material>(resource);
            if (material != null)
            {
                return material;
            }

            // Fallback for an unconfigured project: every variant exists in the editor, but a player build
            // without the generated assets may strip them, so warn loudly.
            UsesGeneratedBases = false;
            Debug.LogWarning("[Arena] Missing Resources/" + resource + ".mat; run Confluence > Configure Project. Using " + shaderName + ".");
            Shader shader = Shader.Find(shaderName);
            var fallback = Own(new Material(shader) { name = resource + " (fallback)" });
            if (transparent)
            {
                MaterialSetup.MakeTransparent(fallback, shaderName.EndsWith("Unlit", StringComparison.Ordinal));
            }

            return fallback;
        }
    }

    /// <summary>
    /// URP surface setup shared by the editor configurator and the runtime fallback: alpha-blended transparency
    /// using URP's own property names and keywords.
    /// </summary>
    public static class MaterialSetup
    {
        public static void MakeTransparent(Material m, bool doubleSided)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", doubleSided ? 0f : 2f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }
}
