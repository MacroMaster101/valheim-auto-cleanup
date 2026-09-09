using System;
using System.Collections.Generic;

namespace ValheimAutoCleanup.Policy
{
    /// <summary>
    /// A case-insensitive, whitespace-trimmed set of prefab names parsed from a
    /// comma-separated configuration string. Empty entries are ignored.
    /// </summary>
    public sealed class NameSet
    {
        private readonly HashSet<string> _names =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The raw configuration string this set was parsed from.</summary>
        public string Raw { get; }

        /// <summary>True when the set contains no usable entries.</summary>
        public bool IsEmpty => _names.Count == 0;

        /// <summary>Number of distinct entries.</summary>
        public int Count => _names.Count;

        public NameSet(string commaSeparated)
        {
            Raw = commaSeparated ?? string.Empty;

            foreach (var part in Raw.Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    _names.Add(trimmed);
                }
            }
        }

        /// <summary>Case-insensitive membership test. A null or empty name is never a member.</summary>
        public bool Contains(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                return false;
            }

            return _names.Contains(prefabName.Trim());
        }

        /// <summary>The entries, for logging and the <c>status</c> command.</summary>
        public IEnumerable<string> Entries => _names;
    }
}
