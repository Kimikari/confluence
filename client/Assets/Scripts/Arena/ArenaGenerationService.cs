using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Confluence.Arena.Generation;
using Confluence.Core.Content;

namespace Confluence.Client.Arena
{
    public enum ArenaGenerationState
    {
        Idle,
        Running,
        Done,
        Failed,
    }

    /// <summary>
    /// Runs Confluence.Arena's seeded <see cref="ArenaGenerator"/> (spec §7 L302-L335: up to eight validated attempts,
    /// then the prevalidated fallback) on a worker thread and publishes the released arena on the main thread through
    /// <see cref="Poll"/>. Generation takes seconds (hundreds of navigation builds), so it never runs on a frame. The
    /// client generates only for offline modes and previews; in online play the server owns generation (spec L325) and
    /// the client renders the layout it receives. A newer request supersedes an older one still running.
    /// </summary>
    public sealed class ArenaGenerationService
    {
        private static readonly object SharedLock = new object();
        private static ArenaGenerator _sharedGenerator;
        private static Catalogue _sharedCatalogue;

        private readonly ArenaGenerator _generator;
        private Task<GeneratedArena> _task;
        private Stopwatch _stopwatch;

        public ArenaGenerationService(Catalogue catalogue)
        {
            _generator = GeneratorFor(catalogue ?? throw new ArgumentNullException(nameof(catalogue)));
        }

        public ArenaGenerationState State { get; private set; } = ArenaGenerationState.Idle;

        /// <summary>The request of the last completed (or running) generation.</summary>
        public ArenaRequest Request { get; private set; }

        public GeneratedArena Result { get; private set; }

        public string Error { get; private set; }

        /// <summary>Real seconds the last generation took (presentation only; rules never read it).</summary>
        public double Seconds { get; private set; }

        public bool IsRunning => State == ArenaGenerationState.Running;

        public ArenaGenerator Generator => _generator;

        /// <summary>Raised on the main thread (from <see cref="Poll"/>) when a generation finishes or fails.</summary>
        public event Action<ArenaGenerationService> Completed;

        /// <summary>
        /// One generator per catalogue: its module library and fallback cache are immutable or thread-safe
        /// (ArenaGenerator docs), so every screen shares it.
        /// </summary>
        public static ArenaGenerator GeneratorFor(Catalogue catalogue)
        {
            lock (SharedLock)
            {
                if (_sharedGenerator == null || !ReferenceEquals(_sharedCatalogue, catalogue))
                {
                    _sharedGenerator = new ArenaGenerator(catalogue);
                    _sharedCatalogue = catalogue;
                }

                return _sharedGenerator;
            }
        }

        /// <summary>Starts generating on a worker thread; a running request is superseded (its result is dropped).</summary>
        public void Begin(ArenaRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ArenaRequest copy = request.Clone();
            Request = copy;
            Error = null;
            State = ArenaGenerationState.Running;
            _stopwatch = Stopwatch.StartNew();
            ArenaGenerator generator = _generator;
            _task = Task.Run(() => generator.Generate(copy));
        }

        /// <summary>Generates synchronously (tests, tools).</summary>
        public GeneratedArena GenerateNow(ArenaRequest request)
        {
            Request = request.Clone();
            Stopwatch watch = Stopwatch.StartNew();
            Result = _generator.Generate(Request);
            Seconds = watch.Elapsed.TotalSeconds;
            State = ArenaGenerationState.Done;
            return Result;
        }

        /// <summary>Publishes a finished generation on the calling (main) thread.</summary>
        public void Poll()
        {
            if (_task == null || !_task.IsCompleted)
            {
                return;
            }

            Task<GeneratedArena> task = _task;
            _task = null;
            Seconds = _stopwatch != null ? _stopwatch.Elapsed.TotalSeconds : 0.0;
            if (task.IsFaulted || task.IsCanceled)
            {
                Exception error = task.Exception != null ? task.Exception.GetBaseException() : null;
                Error = error != null ? error.Message : "Arena generation was cancelled.";
                State = ArenaGenerationState.Failed;
            }
            else
            {
                Result = task.Result;
                State = ArenaGenerationState.Done;
            }

            Completed?.Invoke(this);
        }
    }
}
