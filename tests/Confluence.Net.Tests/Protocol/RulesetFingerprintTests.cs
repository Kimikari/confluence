using System;
using System.IO;
using System.Text;
using Confluence.Core.Content;
using Confluence.Net.Protocol;
using Confluence.Net.Serialization;

namespace Confluence.Net.Tests.Protocol
{
    public sealed class RulesetFingerprintTests
    {
        [Fact]
        public void Real_Catalogue_Has_A_Stable_Nonzero_Fingerprint()
        {
            string path = Catalogue.FindDefaultPath();
            Catalogue catalogue = Catalogue.Load(path);
            Assert.NotNull(catalogue);

            RulesetFingerprint a = RulesetFingerprint.ForCurrentRules(path);
            RulesetFingerprint b = RulesetFingerprint.Compute(File.ReadAllBytes(path), NetRules.RulesVersion);
            Assert.Equal(a, b);
            Assert.NotEqual(RulesetFingerprint.Zero, a);
            Assert.Equal(64, a.ToHex().Length);
            Assert.Equal(a.ToHex().Substring(0, 12), a.ShortHex);
        }

        [Fact]
        public void Line_Endings_And_Byte_Order_Mark_Do_Not_Matter()
        {
            byte[] lf = Encoding.UTF8.GetBytes("{\n  \"id\": \"SPL-001\",\n  \"cost\": 3\n}\n");
            byte[] crlf = Encoding.UTF8.GetBytes("{\r\n  \"id\": \"SPL-001\",\r\n  \"cost\": 3\r\n}\r\n");
            var bom = new byte[lf.Length + 3];
            bom[0] = 0xEF;
            bom[1] = 0xBB;
            bom[2] = 0xBF;
            Buffer.BlockCopy(lf, 0, bom, 3, lf.Length);

            RulesetFingerprint reference = RulesetFingerprint.Compute(lf, "v1");
            Assert.Equal(reference, RulesetFingerprint.Compute(crlf, "v1"));
            Assert.Equal(reference, RulesetFingerprint.Compute(bom, "v1"));
        }

        [Fact]
        public void Any_Content_Or_Rules_Version_Change_Changes_The_Fingerprint()
        {
            string path = Catalogue.FindDefaultPath();
            byte[] bytes = File.ReadAllBytes(path);
            RulesetFingerprint original = RulesetFingerprint.Compute(bytes, NetRules.RulesVersion);

            // Flip one digit somewhere in the middle of the catalogue.
            int at = Array.FindIndex(bytes, bytes.Length / 2, b => b >= (byte)'0' && b <= (byte)'8');
            bytes[at]++;
            Assert.NotEqual(original, RulesetFingerprint.Compute(bytes, NetRules.RulesVersion));
            bytes[at]--;
            Assert.Equal(original, RulesetFingerprint.Compute(bytes, NetRules.RulesVersion));

            Assert.NotEqual(original, RulesetFingerprint.Compute(bytes, NetRules.RulesVersion + "-hotfix"));

            // The version and the content are separated, so shifting text between them is still a change.
            Assert.NotEqual(RulesetFingerprint.Compute(Encoding.UTF8.GetBytes("bc"), "a"),
                RulesetFingerprint.Compute(Encoding.UTF8.GetBytes("c"), "ab"));
        }

        [Fact]
        public void Fingerprints_Round_Trip_On_The_Wire()
        {
            RulesetFingerprint fp = RulesetFingerprint.Compute(Encoding.UTF8.GetBytes("{}"), "v1");
            var w = new NetWriter();
            fp.Write(w);
            Assert.Equal(RulesetFingerprint.Size, w.Length);
            var r = new NetReader(w.ToArray());
            Assert.Equal(fp, RulesetFingerprint.Read(r));
            Assert.Equal(fp, RulesetFingerprint.FromBytes(fp.ToArray()));
            Assert.Throws<ArgumentException>(() => RulesetFingerprint.FromBytes(new byte[16]));
            Assert.Throws<NetReadException>(() => RulesetFingerprint.Read(new NetReader(new byte[31])));
            Assert.Throws<ArgumentException>(() => RulesetFingerprint.Compute(new byte[1], ""));
        }
    }
}
