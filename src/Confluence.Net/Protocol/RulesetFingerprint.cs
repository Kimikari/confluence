using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Confluence.Net.Serialization;

namespace Confluence.Net.Protocol
{
    /// <summary>
    /// SHA-256 identity of a ruleset: the rules version string plus the catalogue bytes (spec L427: replays and
    /// clients carry the ruleset hash; incompatible clients show an unsupported-version state). Two builds
    /// that would simulate differently must never share a fingerprint, so the server rejects any mismatch at
    /// the handshake.
    /// <para>
    /// Hash input: "CONFLUENCE-RULESET\0" + UTF-8 rules version + "\0" + canonical catalogue bytes. The
    /// canonical form drops a leading UTF-8 byte order mark and every carriage return byte. JSON cannot hold a
    /// raw CR inside a string, so this only removes line-ending whitespace and makes the hash immune to
    /// checkout line-ending conversion while staying sensitive to every content change.
    /// </para>
    /// </summary>
    public sealed class RulesetFingerprint : IEquatable<RulesetFingerprint>
    {
        public const int Size = 32;

        private static readonly byte[] Domain = Encoding.ASCII.GetBytes("CONFLUENCE-RULESET\0");

        private readonly byte[] _hash;

        private RulesetFingerprint(byte[] hash)
        {
            _hash = hash;
        }

        /// <summary>All-zero fingerprint; never produced by <see cref="Compute"/>.</summary>
        public static RulesetFingerprint Zero { get; } = new RulesetFingerprint(new byte[Size]);

        public static RulesetFingerprint Compute(byte[] catalogueBytes, string rulesVersion)
        {
            if (catalogueBytes == null) throw new ArgumentNullException(nameof(catalogueBytes));
            if (string.IsNullOrEmpty(rulesVersion)) throw new ArgumentException("A rules version is required.", nameof(rulesVersion));

            using (SHA256 sha = SHA256.Create())
            {
                byte[] version = Encoding.UTF8.GetBytes(rulesVersion);
                sha.TransformBlock(Domain, 0, Domain.Length, null, 0);
                sha.TransformBlock(version, 0, version.Length, null, 0);
                sha.TransformBlock(new byte[] { 0 }, 0, 1, null, 0);
                HashCanonical(sha, catalogueBytes);
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return new RulesetFingerprint(sha.Hash);
            }
        }

        public static RulesetFingerprint FromFile(string cataloguePath, string rulesVersion)
        {
            if (cataloguePath == null) throw new ArgumentNullException(nameof(cataloguePath));
            return Compute(File.ReadAllBytes(cataloguePath), rulesVersion);
        }

        /// <summary>Fingerprint of the catalogue at <paramref name="cataloguePath"/> with the current rules version.</summary>
        public static RulesetFingerprint ForCurrentRules(string cataloguePath) => FromFile(cataloguePath, NetRules.RulesVersion);

        public static RulesetFingerprint FromBytes(byte[] hash)
        {
            if (hash == null) throw new ArgumentNullException(nameof(hash));
            if (hash.Length != Size) throw new ArgumentException("A fingerprint is " + Size + " bytes.", nameof(hash));
            return new RulesetFingerprint((byte[])hash.Clone());
        }

        public static RulesetFingerprint Read(NetReader reader)
        {
            var hash = new byte[Size];
            reader.ReadBytes(hash, 0, Size);
            return new RulesetFingerprint(hash);
        }

        public void Write(NetWriter writer) => writer.WriteBytes(_hash, 0, Size);

        public byte[] ToArray() => (byte[])_hash.Clone();

        /// <summary>Lowercase hexadecimal of the full hash.</summary>
        public string ToHex()
        {
            var sb = new StringBuilder(Size * 2);
            for (int i = 0; i < Size; i++)
            {
                sb.Append(_hash[i].ToString("x2"));
            }

            return sb.ToString();
        }

        /// <summary>First 12 hex digits, for logs and the unsupported-version screen.</summary>
        public string ShortHex => ToHex().Substring(0, 12);

        public bool Equals(RulesetFingerprint other)
        {
            if (other is null) return false;
            for (int i = 0; i < Size; i++)
            {
                if (_hash[i] != other._hash[i]) return false;
            }

            return true;
        }

        public override bool Equals(object obj) => obj is RulesetFingerprint other && Equals(other);

        public override int GetHashCode() => BitConverter.ToInt32(_hash, 0);

        public override string ToString() => ShortHex;

        private static void HashCanonical(HashAlgorithm sha, byte[] bytes)
        {
            int start = 0;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                start = 3;
            }

            int runStart = start;
            for (int i = start; i < bytes.Length; i++)
            {
                if (bytes[i] != (byte)'\r')
                {
                    continue;
                }

                if (i > runStart)
                {
                    sha.TransformBlock(bytes, runStart, i - runStart, null, 0);
                }

                runStart = i + 1;
            }

            if (bytes.Length > runStart)
            {
                sha.TransformBlock(bytes, runStart, bytes.Length - runStart, null, 0);
            }
        }
    }
}
