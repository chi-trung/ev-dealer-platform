using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SalesService.DTOs;
using System.Collections.Generic;
using System.Globalization;

namespace SalesService.PdfDocuments
{
    public class QuotePdfDocument : IDocument
    {
        public GenerateQuotePdfRequestDto Model { get; }

        public QuotePdfDocument(GenerateQuotePdfRequestDto model)
        {
            Model = model;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        // Dictionary/field values arrive as JsonElement (Issue #49 widened the
        // DTO from string to object?) because the frontend sends raw numbers in
        // the JSON payload. A JSON string renders as its raw text (no quotes);
        // a number renders in invariant spelling — exactly what a document cell
        // wants.
        private static string Render(object? v)
        {
            if (v is null) return "";
            if (v is System.Text.Json.JsonElement je)
                return je.ValueKind == System.Text.Json.JsonValueKind.String ? je.GetString() ?? "" : je.ToString();
            return System.Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        }

        private static string Cell(string? key, IDictionary<string, object?> map)
            => key is null || !map.TryGetValue(key, out var v) ? "" : Render(v);

        public void Compose(IDocumentContainer container)
        {
            container
                .Page(page =>
                {
                    page.Margin(50);
                    page.Header().Text("Báo Giá Xe").SemiBold().FontSize(20);

                    page.Content().Column(column =>
                    {
                        // Frontend payloads are camelCase (QuoteCreate.jsx /
                        // QuoteView.jsx): name/phone/email/address — the old
                        // PascalCase lookups rendered every cell blank (Issue #49).
                        var ci = Model.CustomerInfo;
                        if (ci.Count > 0)
                        {
                            column.Item().Text("Khách hàng").SemiBold();
                            TextRow(column, "Họ tên", Cell("name", ci));
                            TextRow(column, "Số điện thoại", Cell("phone", ci));
                            TextRow(column, "Email", Cell("email", ci));
                            TextRow(column, "Địa chỉ", Cell("address", ci));
                        }

                        column.Item().PaddingTop(16).Text("Chi tiết sản phẩm:").SemiBold();

                        // Table for items
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.ConstantColumn(70);
                                columns.ConstantColumn(110);
                                columns.ConstantColumn(110);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text("Sản phẩm").SemiBold();
                                header.Cell().Text("SL").SemiBold();
                                header.Cell().AlignRight().Text("Đơn giá").SemiBold();
                                header.Cell().AlignRight().Text("Thành tiền").SemiBold();
                            });

                            foreach (var item in Model.QuoteItems)
                            {
                                table.Cell().Text(Cell("vehicleName", item));
                                table.Cell().Text(Cell("quantity", item));
                                table.Cell().AlignRight().Text(Cell("unitPrice", item));
                                table.Cell().AlignRight().Text(Cell("itemTotal", item));
                            }
                        });

                        var ai = Model.AdditionalInfo;
                        if (ai.Count > 0)
                        {
                            column.Item().PaddingTop(16).Text("Thông tin khác").SemiBold();
                            TextRow(column, "Nhân viên kinh doanh", Cell("salesPerson", ai));
                            TextRow(column, "Ghi chú", Cell("notes", ai));
                        }

                        // Totals block: render every money figure the frontend
                        // computed (pre-#49 only Tổng cộng appeared; loan/monthly
                        // fields existed on the DTO but were never drawn, and
                        // loanAmountCalculated wasn't even on the DTO).
                        column.Item().PaddingTop(16).Column(totals =>
                        {
                            TotalRow(totals, "Tổng cộng", Model.TotalCalculatedAmount);
                            TotalRow(totals, "Trả trước", Model.DownPaymentCalculated);
                            TotalRow(totals, "Vay còn lại", Model.LoanAmountCalculated);
                            TotalRow(totals, "Trả hàng tháng", Model.MonthlyPaymentCalculated);
                            TotalRow(totals, "Tổng thanh toán trả góp", Model.InstallmentTotalPaymentCalculated);
                        });
                    });

                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("Trang ");
                        text.CurrentPageNumber();
                    });
                });
        }

        private static void TextRow(ColumnDescriptor column, string label, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            column.Item().Text(text =>
            {
                text.Span($"{label}: ").SemiBold();
                text.Span(value);
            });
        }

        private static void TotalRow(ColumnDescriptor column, string label, object? value)
        {
            var text = Render(value);
            if (string.IsNullOrWhiteSpace(text) || text == "0") return;
            column.Item().AlignRight().Text(t =>
            {
                t.Span($"{label}: ").SemiBold();
                t.Span(text);
            });
        }
    }
}
