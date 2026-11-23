using System.Security.Cryptography;
using System.Text;

namespace LightPdf.Core.Tests;

/// <summary>Builds tiny, valid PDFs in code so tests need no binary fixtures.</summary>
internal static class TestPdf
{
    /// <summary>
    /// US Letter pages (612x792 pt). Each page has a black square from (100,100) to (300,300)
    /// in PDF coordinates (origin bottom-left). With <paramref name="userPassword"/>, the file is
    /// encrypted using the standard security handler, revision 2 (RC4 40-bit).
    /// </summary>
    public static byte[] Create(int pageCount = 1, string? userPassword = null)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "", // pages tree, filled below
        };
        const string content = "0 0 0 rg 100 100 200 200 re f";
        var crypt = userPassword is null ? null : new Rc4Security(userPassword);

        var kids = new List<string>();
        for (int i = 0; i < pageCount; i++)
        {
            int pageObj = objects.Count + 1;
            int contentObj = pageObj + 1;
            kids.Add($"{pageObj} 0 R");
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {contentObj} 0 R >>");
            string data = crypt?.EncryptString(contentObj, content) ?? content;
            objects.Add($"<< /Length {data.Length} >>\nstream\n{data}\nendstream");
        }
        objects[1] = $"<< /Type /Pages /Kids [{string.Join(' ', kids)}] /Count {pageCount} >>";

        string trailerExtra = "";
        if (crypt is not null)
        {
            objects.Add(crypt.EncryptDictionary);
            trailerExtra = $" /Encrypt {objects.Count} 0 R /ID [<{crypt.IdHex}> <{crypt.IdHex}>]";
        }

        // Latin-1 maps chars 0-255 to bytes 1:1, so string length == byte offset (binary-safe).
        var sb = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        int xref = sb.Length;
        sb.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (int off in offsets) sb.Append($"{off:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R{trailerExtra} >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    public static string WriteToTemp(int pageCount = 1, string fileName = "test.pdf", string? userPassword = null)
    {
        string dir = Path.Combine(Path.GetTempPath(), "lightpdf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, Create(pageCount, userPassword));
        return path;
    }

    /// <summary>PDF 1.7 spec §7.6.3, algorithms 2, 3 and 4 (revision 2, 40-bit key).</summary>
    private sealed class Rc4Security
    {
        private static readonly byte[] Padding = Convert.FromHexString(
            "28BF4E5E4E758A4164004E56FFFA01082E2E00B6D0683E802F0CA9FE6453697A");
        private const int Permissions = -4; // everything allowed
        private readonly byte[] _key;

        public Rc4Security(string userPassword)
        {
            byte[] id = RandomNumberGenerator.GetBytes(16);
            IdHex = Convert.ToHexString(id);
            byte[] user = Pad(userPassword);

            // Algorithm 3: O entry (owner password = user password here).
            byte[] ownerKey = MD5.HashData(user)[..5];
            byte[] o = Rc4(ownerKey, user);

            // Algorithm 2: file encryption key.
            _key = MD5.HashData([.. user, .. o, .. BitConverter.GetBytes(Permissions), .. id])[..5];

            // Algorithm 4: U entry.
            byte[] u = Rc4(_key, Padding);

            EncryptDictionary = $"<< /Filter /Standard /V 1 /R 2 /O <{Convert.ToHexString(o)}> " +
                                $"/U <{Convert.ToHexString(u)}> /P {Permissions} >>";
        }

        public string IdHex { get; }
        public string EncryptDictionary { get; }

        public string EncryptString(int objectNumber, string plain)
        {
            // Algorithm 1: per-object key = MD5(key + objnum(3 LE) + gen(2 LE)), first n+5 bytes.
            byte[] objKey = MD5.HashData([.. _key, (byte)objectNumber, (byte)(objectNumber >> 8),
                                          (byte)(objectNumber >> 16), 0, 0])[..10];
            return Encoding.Latin1.GetString(Rc4(objKey, Encoding.Latin1.GetBytes(plain)));
        }

        private static byte[] Pad(string password)
        {
            byte[] pw = Encoding.Latin1.GetBytes(password);
            return [.. pw.Take(32), .. Padding.Take(32 - Math.Min(32, pw.Length))];
        }

        private static byte[] Rc4(byte[] key, byte[] data)
        {
            byte[] s = new byte[256];
            for (int i = 0; i < 256; i++) s[i] = (byte)i;
            for (int i = 0, j = 0; i < 256; i++)
            {
                j = (j + s[i] + key[i % key.Length]) & 0xFF;
                (s[i], s[j]) = (s[j], s[i]);
            }
            byte[] output = new byte[data.Length];
            for (int n = 0, i = 0, j = 0; n < data.Length; n++)
            {
                i = (i + 1) & 0xFF;
                j = (j + s[i]) & 0xFF;
                (s[i], s[j]) = (s[j], s[i]);
                output[n] = (byte)(data[n] ^ s[(s[i] + s[j]) & 0xFF]);
            }
            return output;
        }
    }
}
