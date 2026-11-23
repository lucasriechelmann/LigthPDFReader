namespace LightPdf.Core.Tests;

/// <summary>
/// PDFium is not thread-safe, and xUnit runs test classes in parallel. Every class that calls PDFium
/// directly joins this collection so those tests run one at a time (the app gets the same guarantee
/// from <see cref="PdfWorker"/>). Without it the test host crashes with an access violation.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PdfiumCollection
{
    public const string Name = "PDFium (single-threaded)";
}
