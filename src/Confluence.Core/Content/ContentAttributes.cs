using System;
using System.Collections.Generic;

namespace Confluence.Core.Content
{
    /// <summary>
    /// Marks a test (or every test in a class) as verifying the specific behavior of catalogue records.
    /// The content ledger counts a record as verified only when all its verifying tests ran and passed.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class VerifiesAttribute : Attribute
    {
        public VerifiesAttribute(params string[] ids)
        {
            Ids = Array.AsReadOnly(ids ?? new string[0]);
        }

        public IReadOnlyList<string> Ids { get; }
    }

    /// <summary>
    /// Marks the class that implements a catalogue record's gameplay behavior. The ledger treats a record as
    /// implemented when a class carrying this attribute exists in a shipped assembly.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class ContentBehaviorAttribute : Attribute
    {
        public ContentBehaviorAttribute(string id)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
        }

        public string Id { get; }
    }
}
