using System;
using System.IO;
using System.Threading.Tasks;
using Confluence.Core.Content;
using UnityEngine;

namespace Confluence.Client.Content
{
    public enum CatalogueLoadState
    {
        NotStarted,
        Loading,
        Loaded,
        Failed,
    }

    /// <summary>
    /// Loads the supplied catalogue, unchanged, through Confluence.Core (DECISIONS D-006). The player reads the
    /// copy that tools/sync-unity.ps1 places in StreamingAssets; the editor falls back to the repository's
    /// docs/confluence/ file when that copy is missing. Parsing runs on a worker thread (Core is engine-free and
    /// the result is immutable); <see cref="Poll"/> publishes the result on the main thread.
    /// </summary>
    public sealed class CatalogueService
    {
        public const string FileName = "confluence-catalogue.json";

        private Task<Catalogue> _task;
        private float _startRealtime;

        public CatalogueLoadState State { get; private set; } = CatalogueLoadState.NotStarted;

        public Catalogue Catalogue { get; private set; }

        /// <summary>The file that was (or is being) loaded.</summary>
        public string SourcePath { get; private set; }

        public string Error { get; private set; }

        /// <summary>Real seconds the load took (presentation only).</summary>
        public float LoadSeconds { get; private set; }

        public bool IsLoaded => State == CatalogueLoadState.Loaded;

        /// <summary>Raised once on the main thread when loading finishes, successfully or not.</summary>
        public event Action<CatalogueService> Completed;

        /// <summary>StreamingAssets copy, else (editor only) the repository catalogue; null when neither exists.</summary>
        public static string ResolvePath()
        {
            string streaming = Path.Combine(Application.streamingAssetsPath, FileName);
            if (File.Exists(streaming))
            {
                return streaming;
            }

            if (Application.isEditor && Catalogue.TryFindDefaultPath(out string repoPath))
            {
                return repoPath;
            }

            return null;
        }

        public void BeginLoad()
        {
            if (State == CatalogueLoadState.Loading || State == CatalogueLoadState.Loaded)
            {
                return;
            }

            SourcePath = ResolvePath();
            if (SourcePath == null)
            {
                Fail("Catalogue not found in " + Application.streamingAssetsPath
                    + ". Run: powershell -ExecutionPolicy Bypass -File tools/sync-unity.ps1");
                return;
            }

            State = CatalogueLoadState.Loading;
            _startRealtime = Time.realtimeSinceStartup;
            string path = SourcePath;
            _task = Task.Run(() => Catalogue.Load(path));
        }

        /// <summary>Synchronous load for tests and editor tools.</summary>
        public void LoadNow()
        {
            SourcePath = ResolvePath();
            if (SourcePath == null)
            {
                Fail("Catalogue not found.");
                return;
            }

            float start = Time.realtimeSinceStartup;
            try
            {
                Succeed(Catalogue.Load(SourcePath), start);
            }
            catch (Exception e)
            {
                Fail(e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>Call every frame while loading; completes the load on the main thread.</summary>
        public void Poll()
        {
            if (State != CatalogueLoadState.Loading || _task == null || !_task.IsCompleted)
            {
                return;
            }

            Task<Catalogue> task = _task;
            _task = null;
            if (task.IsFaulted || task.IsCanceled)
            {
                Exception e = task.Exception?.GetBaseException();
                Fail(e == null ? "Catalogue load was cancelled." : e.GetType().Name + ": " + e.Message);
            }
            else
            {
                Succeed(task.Result, _startRealtime);
            }
        }

        /// <summary>One-line summary for menus and logs.</summary>
        public string Describe()
        {
            switch (State)
            {
                case CatalogueLoadState.Loaded:
                    return Catalogue.Title + " v" + Catalogue.DesignVersion + ": " + Catalogue.Wizards.Count + " wizards, "
                        + Catalogue.Spells.Count + " spells, " + Catalogue.EquipmentItems.Count + " equipment, "
                        + Catalogue.Augments.Count + " augments, " + Catalogue.Biomes.Count + " biomes, "
                        + Catalogue.Modules.Count + " modules, " + Catalogue.Props.Count + " props, "
                        + Catalogue.Builds.Count + " builds";
                case CatalogueLoadState.Loading:
                    return "Loading catalogue...";
                case CatalogueLoadState.Failed:
                    return "Catalogue unavailable: " + Error;
                default:
                    return "Catalogue not loaded.";
            }
        }

        private void Succeed(Catalogue catalogue, float startRealtime)
        {
            Catalogue = catalogue;
            Error = null;
            LoadSeconds = Time.realtimeSinceStartup - startRealtime;
            State = CatalogueLoadState.Loaded;
            Debug.Log("[Catalogue] Loaded " + SourcePath + " in " + LoadSeconds.ToString("0.00") + " s. " + Describe());
            Completed?.Invoke(this);
        }

        private void Fail(string error)
        {
            Catalogue = null;
            Error = error;
            State = CatalogueLoadState.Failed;
            Debug.LogError("[Catalogue] " + error);
            Completed?.Invoke(this);
        }
    }
}
