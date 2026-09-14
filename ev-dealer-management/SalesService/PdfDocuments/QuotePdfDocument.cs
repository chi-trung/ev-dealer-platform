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
        // the JSON payload. A JSON string renders as its raw text (no quotes).
        // A number is NORMALIZED, not expanded: the frontend computes money
        // with IEEE-754 annuity math (QuoteView.jsx/QuoteCreate.jsx post
        // monthlyPaymentCalculated with no rounding), so the raw JSON text
        // would print "22652900.887352396" on a customer-facing document
        // (Issue #49 review). Rendered values are integers or đồng amounts —
        // no sub-unit exists — so: round to 2 places, strip trailing zeros,
        // group thousands with '.' and use ',' as the decimal mark, matching
        // the Vietnamese convention the rest of the template follows.
        // Public for the formatting-contract tests (QuotePdfGenerationTests).
        public static string Render(object? v)
        {
            if (v is null) return "";
            if (v is System.Text.Json.JsonElement je)
            {
                if (je.ValueKind == System.Text.Json.JsonValueKind.String)
                    return je.GetString() ?? "";
                if (je.ValueKind == System.Text.Json.JsonValueKind.Number)
                    return FormatNumber(je);
                return je.ToString();
            }
            return System.Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        }

        private static string FormatNumber(System.Text.Json.JsonElement je)
        {
            // TryGetDecimal first: JSON integers and short decimals land there
            // losslessly; doubles catch the wide-magnitude cases decimals can't
            // (TryGetDecimal fails past ~28 significant digits).
            if (je.TryGetDecimal(out var d)) return FormatDecimal(d);
            if (je.TryGetDouble(out var dbl)) return FormatDecimal((decimal)dbl);
            return je.ToString();
        }

        internal static string FormatDecimal(decimal d)
        {
            d = System.Math.Round(d, 2, System.MidpointRounding.AwayFromZero);
            var s = d.ToString("0.##", CultureInfo.InvariantCulture); // 22652900.89 / 1615000000 / -2.5
            var neg = s.StartsWith("-");
            if (neg) s = s[1..];
            var dot = s.IndexOf('.');
            var intPart = dot < 0 ? s : s[..dot];
            var fracPart = dot < 0 ? "" : s[dot..];
            var grouped = new System.Text.StringBuilder(intPart.Length + intPart.Length / 3);
            for (var i = 0; i < intPart.Length; i++)
            {
                if (i > 0 && (intPart.Length - i) % 3 == 0) grouped.Append('.');
                grouped.Append(intPart[i]);
            }
            return (neg ? "-" : "") + grouped + fracPart.Replace('.', ',');
        }

        private static string Cell(string? key, IDictionary<string, object?>? map)
            => key is null || map is null || !map.TryGetValue(key, out var v) ? "" : Render(v);

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
                        // null-safe: an explicit "customerInfo": null in the JSON
                        // body overwrites the DTO initializer (review finding).
                        var ci = Model.CustomerInfo;
                        if (ci is { Count: > 0 })
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
                                // A JSON array hole ("quoteItems":[null]) binds
                                // straight through — [ApiController] guards only
                                // object graphs, not array elements (review
                                // reproduced NRE → anonymous 500).
                                if (item is null) continue;
                                table.Cell().Text(Cell("vehicleName", item));
                                table.Cell().Text(Cell("quantity", item));
                                table.Cell().AlignRight().Text(Cell("unitPrice", item));
                                table.Cell().AlignRight().Text(Cell("itemTotal", item));
                            }
                        });

                        var ai = Model.AdditionalInfo;
                        if (ai is { Count: > 0 })
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
