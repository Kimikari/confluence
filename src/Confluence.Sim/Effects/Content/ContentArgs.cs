using System;

namespace Confluence.Sim.Effects.Content
{
    /// <summary>Argument checks shared by the content behaviors.</summary>
    internal static class ContentArgs
    {
        public static void Require(IEffectHost host)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }
        }
    }
}
