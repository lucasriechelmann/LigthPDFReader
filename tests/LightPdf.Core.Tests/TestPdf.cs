using System.Text;

namespace LightPdf.Core.Tests;

/// <summary>Builds tiny, valid PDFs in code so tests need no binary fixtures.</summary>
internal static class TestPdf
{
    /// <summary>
    /// US Letter pages (612x792 pt). Each page has a black square from (100,100) to (300,300)
    /// in PDF coordinates (origin bottom-left).
    /// </summary>
    public static byte[] Create(int pageCount = 1)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "", // pages tree, filled below
        };
        const string content = "0 0 0 rg 100 100 200 200 re f";
        var kids = new List<string>();
        for (int i = 0; i < pageCount; i++)
        {
            int pageObj = objects.Count + 1;
            kids.Add($"{pageObj} 0 R");
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {pageObj + 1} 0 R >>");
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }
        objects[1] = $"<< /Type /Pages /Kids [{string.Join(' ', kids)}] /Count {pageCount} >>";

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
        sb.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    public static string WriteToTemp(int pageCount = 1, string fileName = "test.pdf")
    {
        string dir = Path.Combine(Path.GetTempPath(), "lightpdf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, Create(pageCount));
        return path;
    }
}
