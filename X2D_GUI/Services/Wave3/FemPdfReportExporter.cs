using System.IO;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GeotechSlopeApp.Services.Wave3;

public static class FemPdfReportExporter
{
    static FemPdfReportExporter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static void Export(string pdfPath, string markdown, FemReportSnapshot snap)
    {
        var mdPath = Path.ChangeExtension(pdfPath, ".md");
        File.WriteAllText(mdPath, markdown, Encoding.UTF8);

        var options = snap.Options;
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Times New Roman"));
                page.Header().Text(options.ReportTitle).SemiBold().FontSize(14);
                page.Footer().AlignRight().Text(t =>
                {
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
                page.Content().Column(col =>
                {
                    if (options.IncludeCoverPage)
                    {
                        col.Item().Text(options.Company).FontSize(12);
                        col.Item().Text($"Client: {options.Client}");
                        col.Item().Text($"Location: {options.Location}");
                        col.Item().Text($"Project: {snap.ProjectName}");
                        col.Item().PaddingBottom(8);
                    }

                    col.Item().Text("Strength reduction").SemiBold().FontSize(12);
                    col.Item().Text(snap.EstimatedSrf is { } srf && double.IsFinite(srf)
                        ? $"Estimated SRF / FoS = {srf:F3}"
                        : "No SRF result is stored.");
                    col.Item().PaddingBottom(8);

                    if (options.IncludeMaterials && snap.Materials.Count > 0)
                    {
                        col.Item().Text("Materials").SemiBold().FontSize(12);
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(30);
                                c.RelativeColumn();
                                c.ConstantColumn(50);
                                c.ConstantColumn(50);
                                c.ConstantColumn(55);
                            });
                            table.Header(h =>
                            {
                                h.Cell().Text("Id").SemiBold();
                                h.Cell().Text("Name").SemiBold();
                                h.Cell().Text("c").SemiBold();
                                h.Cell().Text("φ").SemiBold();
                                h.Cell().Text("γ").SemiBold();
                            });
                            foreach (var m in snap.Materials)
                            {
                                table.Cell().Text(m.Id.ToString());
                                table.Cell().Text(m.Name);
                                table.Cell().Text($"{m.Cohesion:G4}");
                                table.Cell().Text($"{m.FrictionAngle:G4}");
                                table.Cell().Text($"{m.UnitWeight:G4}");
                            }
                        });
                    }

                    if (options.IncludeProbabilistic && snap.Probabilistic != null)
                    {
                        col.Item().PaddingTop(8).Text("Probabilistic").SemiBold().FontSize(12);
                        col.Item().Text(snap.Probabilistic.FormatSummary());
                    }

                    if (!string.IsNullOrWhiteSpace(options.Notes))
                    {
                        col.Item().PaddingTop(8).Text("Notes").SemiBold().FontSize(12);
                        col.Item().Text(options.Notes);
                    }
                });
            });
        }).GeneratePdf(pdfPath);
    }
}
