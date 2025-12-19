using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SushiBE.Models;
using SushiBE.Data;
using Microsoft.EntityFrameworkCore;

namespace SushiBE.Services
{
    public interface IInvoicePdfService
    {
        Task<string> GeneratePdfAsync(Invoice invoice, Order order, Customer customer);
        Task<string?> GeneratePdfByInvoiceIdAsync(Guid invoiceId);
    }

    public class InvoicePdfService : IInvoicePdfService
    {
        private readonly IWebHostEnvironment _env;
        private readonly SushiDbContext _db;

        public InvoicePdfService(IWebHostEnvironment env, SushiDbContext db)
        {
            _env = env;
            _db = db;
        }

        // Public: generate PDF when caller already has models
        public async Task<string> GeneratePdfAsync(Invoice invoice, Order order, Customer customer)
        {
            // Ensure folder
            var invoicesFolder = Path.Combine(_env.ContentRootPath, "invoices");
            if (!Directory.Exists(invoicesFolder))
                Directory.CreateDirectory(invoicesFolder);

            // Clean file name
            var safeNumber = string.Join("_", invoice.InvoiceNumber.Split(Path.GetInvalidFileNameChars()));
            var fileName = $"{safeNumber}_{invoice.InvoiceId}.pdf";
            var filePath = Path.Combine(invoicesFolder, fileName);

            // Build document
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11).FontColor(Colors.Black));

                    page.Header()
                        .Height(80)
                        .Row(row =>
                        {
                            // Left: logo / company
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().AlignLeft().Text("SushiBE").FontSize(20).Bold();
                                col.Item().Text("Delicious Sushi Co.").FontSize(10);
                                col.Item().Text("123 Sushi Street").FontSize(9);
                                col.Item().Text("City, Country").FontSize(9);
                                col.Item().Text("support@example.com").FontSize(9);
                            });

                            // Right: invoice metadata
                            row.ConstantItem(220).Column(col =>
                            {
                                col.Item().AlignRight().Text($"INVOICE").FontSize(18).Bold();
                                col.Item().AlignRight().Text($"Invoice #: {invoice.InvoiceNumber}").FontSize(10);
                                col.Item().AlignRight().Text($"Invoice ID: {invoice.InvoiceId}").FontSize(9);
                                col.Item().AlignRight().Text($"Date: {invoice.InvoiceDate:yyyy-MM-dd}").FontSize(9);
                                col.Item().AlignRight().Text($"Order ID: {invoice.OrderId}").FontSize(9);
                            });
                        });

                    page.Content()
                        .Column(col =>
                        {
                            // Billing / customer
                            col.Item().PaddingVertical(10).Row(r =>
                            {
                                r.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("Bill To:").FontSize(12).Bold();
                                    c.Item().Text(customer?.Name ?? "Unknown").FontSize(11);
                                    if (!string.IsNullOrEmpty(customer?.Address)) c.Item().Text(customer.Address).FontSize(9);
                                    if (!string.IsNullOrEmpty(customer?.Email)) c.Item().Text(customer.Email).FontSize(9);
                                    if (!string.IsNullOrEmpty(customer?.Phone)) c.Item().Text(customer.Phone).FontSize(9);
                                });

                                r.ConstantItem(160).AlignRight().Column(c =>
                                {
                                    c.Item().Text("Payment Terms:").Bold().FontSize(10);
                                    c.Item().Text("Due on receipt").FontSize(9);
                                    c.Item().PaddingTop(10).Text($"Total Due:").FontSize(11);
                                    c.Item().Text($"{invoice.Amount:F2}").FontSize(16).Bold();
                                });
                            });

                            // Items table
                            col.Item().PaddingTop(5).Element(e =>
                            {
                                e.Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(5); // Description
                                        columns.ConstantColumn(80); // Unit price
                                        columns.ConstantColumn(60); // Qty
                                        columns.ConstantColumn(80); // Line total
                                    });

                                    // Header row
                                    table.Header(header =>
                                    {
                                        header.Cell().Element(CellStyle).Text("Description").Bold();
                                        header.Cell().Element(CellStyle).AlignRight().Text("Unit");
                                        header.Cell().Element(CellStyle).AlignRight().Text("Qty");
                                        header.Cell().Element(CellStyle).AlignRight().Text("Total");
                                    });

                                    foreach (var item in order?.Items ?? Enumerable.Empty<OrderItem>())
                                    {
                                        var lineTotal = item.Price * item.Quantity;
                                        table.Cell().Element(CellStyle).Text(item.Product?.Name ?? item.ProductId.ToString());
                                        table.Cell().Element(CellStyle).AlignRight().Text($"{item.Price:F2}");
                                        table.Cell().Element(CellStyle).AlignRight().Text($"{item.Quantity}");
                                        table.Cell().Element(CellStyle).AlignRight().Text($"{lineTotal:F2}");
                                    }

                                    static IContainer CellStyle(IContainer c) =>
                                        c.Border(1).BorderColor(Colors.Grey.Lighten3).Padding(6);
                                });
                            });

                            // Totals block
                            col.Item().PaddingTop(10).AlignRight().Column(c =>
                            {
                                var subtotal = order?.Items?.Sum(it => it.Price * it.Quantity) ?? 0m;
                                var tax = 0m; // adjust if you have tax logic
                                var total = invoice.Amount;

                                c.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Subtotal:");
                                    r.ConstantItem(120).AlignRight().Text($"{subtotal:F2}");
                                });

                                c.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Tax:");
                                    r.ConstantItem(120).AlignRight().Text($"{tax:F2}");
                                });

                                c.Item().PaddingTop(4).Row(r =>
                                {
                                    r.RelativeItem().Text("Total:").FontSize(12).Bold();
                                    r.ConstantItem(120).AlignRight().Text($"{total:F2}").FontSize(12).Bold();
                                });
                            });

                            // Notes / footer area
                            col.Item().PaddingTop(20).Text(text =>
                            {
                                text.Span("Notes: ").Bold();
                                text.Span("Thank you for your purchase. Please contact us if you have any questions about this invoice.");
                            });
                        });

                    // Footer: FontSize must be applied inside the Text delegate (Text returns void)
                    page.Footer()
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("SushiBE - ").FontSize(9);
                            x.Span("www.sushibe.example").Underline().FontSize(9);
                        });
                });
            });

            // Generate PDF into memory (GeneratePdf is synchronous void)
            using var ms = new MemoryStream();
            document.GeneratePdf(ms); // synchronous void method
            ms.Seek(0, SeekOrigin.Begin);

            // Write file to disk asynchronously
            await using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await ms.CopyToAsync(fileStream);
            }

            var relativeUrl = $"/invoices/{fileName}";
            return relativeUrl;
        }

        // New: load invoice + related data and generate the PDF file
        public async Task<string?> GeneratePdfByInvoiceIdAsync(Guid invoiceId)
        {
            var invoice = await _db.Invoices
                .Include(i => i.Order)
                    .ThenInclude(o => o.Items)
                        .ThenInclude(oi => oi.Product)
                .Include(i => i.Customer)
                .FirstOrDefaultAsync(i => i.InvoiceId == invoiceId);

            if (invoice == null)
                return null;

            var order = invoice.Order;
            var customer = invoice.Customer;

            var pdfUrl = await GeneratePdfAsync(invoice, order!, customer!);

            // update invoice record with PdfUrl if not already set or changed
            if (invoice.PdfUrl != pdfUrl)
            {
                invoice.PdfUrl = pdfUrl;
                await _db.SaveChangesAsync();
            }

            return pdfUrl;
        }
    }
}