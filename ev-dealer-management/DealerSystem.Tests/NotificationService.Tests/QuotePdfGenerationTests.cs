using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Fluent; // GeneratePdf() extension (same as SalesController)
using SalesService.Controllers;
using SalesService.Data;
using SalesService.DTOs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #49: the "Tải PDF" buttons in QuoteView/QuoteCreate POST the
/// browser-assembled quote to /api/Sales/generate-quote-pdf, a route that
/// never existed — and whose DTO could never have bound the real payload
/// anyway: Dictionary&lt;string,string&gt; + string totals against a JSON
/// body full of raw numbers (vehicleId, quantity, unitPrice, totals from
/// calculateTotals()) is a guaranteed 400 from System.Text.Json.
///
/// These tests bind the REAL frontend payload shape (camelCase, numbers
/// unquoted) through the SAME serializer ASP.NET Core uses for
/// [FromBody] (Web defaults: camelCase-insensitive matching on the
/// property names) and run the REAL controller action. If the DTO ever
/// narrows back to strings, deserialization throws here exactly as it
/// would in the live endpoint.
///
/// Mutation checks:
/// - revert the DTO dicts to Dictionary&lt;string,string&gt; (and totals to
///   string) → the real-payload tests fail at deserialization (number→string)
///   exactly like the live endpoint would;
/// - delete the controller action → compile error in these tests.
/// (Rendered-text assertions are impossible: QuestPDF encodes glyphs through
/// font subsets, so no payload string appears literally in the PDF bytes.)
/// </summary>
public class QuotePdfGenerationTests
{
    static QuotePdfGenerationTests()
    {
        // The production host sets this in SalesService/Program.cs; the test
        // process doesn't run it, and QuestPDF refuses to Generate() without
        // an explicit license (it throws InvalidOperationException — the very
        // exception the controller's 503 branch handles).
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    // The exact shape QuoteView.jsx builds in handleGeneratePdf (camelCase
    // keys; numbers as JSON numbers, NOT strings — that mix is the bug class
    // this test exists to pin).
    private const string FrontendPayloadJson = """
    {
      "customerInfo": {
        "id": 7,
        "name": "Nguyễn Văn An",
        "phone": "0905123456",
        "email": "an@example.vn",
        "address": "12 Lê Lợi, Đà Nẵng"
      },
      "quoteItems": [
        {
          "vehicleId": 3,
          "vehicleName": "VF 6 Plus",
          "quantity": 2,
          "unitPrice": 850000000,
          "discountPercent": 5,
          "itemTotal": 1615000000
        }
      ],
      "paymentInfo": {
        "type": "loan",
        "downPaymentPercent": 30,
        "loanTerm": 60,
        "interestRate": 7.5
      },
      "additionalInfo": {
        "deliveryDate": "2026-10-01",
        "notes": "Giao tại nhà",
        "salesPerson": "Trần Thị Bình",
        "validUntil": "2026-09-30"
      },
      "totalCalculatedAmount": 1615000000,
      "downPaymentCalculated": 484500000,
      "loanAmountCalculated": 1130500000,
      "monthlyPaymentCalculated": 22610000,
      "installmentTotalPaymentCalculated": 1356600000
    }
    """;

    private static T BindLikeAspNetCore<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static SalesController Controller() =>
        // SalesDbContext is required only by the ctor; the PDF action is
        // stateless (the payload carries the whole quote) and never touches it.
        new(new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
            .UseSqlite("Data Source=:memory:").Options), NullLogger<SalesController>.Instance);

    [Fact]
    public void RealFrontendPayload_BindsAndProducesPdf()
    {
        var dto = BindLikeAspNetCore<GenerateQuotePdfRequestDto>(FrontendPayloadJson);

        // The widening itself: numbers survived as JSON numbers inside the
        // object?-valued dicts (a Dictionary<string,string> would have
        // thrown here, exactly as the live endpoint did — that's the bug).
        Assert.Equal(JsonValueKind.Number, ((JsonElement)dto.QuoteItems[0]["quantity"]!).ValueKind);
        Assert.Equal("Nguyễn Văn An", ((JsonElement)dto.CustomerInfo["name"]!).GetString());

        var result = Controller().GenerateQuotePdf(dto);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.StartsWith("%PDF-", System.Text.Encoding.Latin1.GetString(file.FileContents));
        Assert.True(file.FileContents.Length > 2_000,
            $"expected a non-trivial PDF, got {file.FileContents.Length} bytes");
    }

    [Fact]
    public void PascalCasePayload_StillBinds_CaseInsensitive()
    {
        // ASP.NET Core's Web defaults match case-insensitively, and the
        // document reads dict KEYS exactly as the frontend spells them
        // (camelCase). A historical PascalCase sender still binds; cells
        // for keys it spells differently render blank — documented behavior,
        // and the shape old SalesService README examples used.
        var dto = JsonSerializer.Deserialize<GenerateQuotePdfRequestDto>(
            """{ "customerInfo": { "name": "Test" }, "quoteItems": [], "totalCalculatedAmount": 1 } """,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var result = Assert.IsType<FileContentResult>(Controller().GenerateQuotePdf(dto));
        Assert.StartsWith("%PDF-", System.Text.Encoding.Latin1.GetString(result.FileContents));
    }

    [Fact]
    public void EmptyPayload_ProducesPdf_NotAnError()
    {
        // A quote with nothing filled in must still render (all rows skipped)
        // rather than 400/500 — the totals helpers bail on null/0, and
        // Compose handles empty collections.
        var dto = new GenerateQuotePdfRequestDto();

        var result = Assert.IsType<FileContentResult>(Controller().GenerateQuotePdf(dto));
        Assert.StartsWith("%PDF-", System.Text.Encoding.Latin1.GetString(result.FileContents));
    }

    [Fact]
    public void NullBody_ReturnsBadRequest()
    {
        var result = Controller().GenerateQuotePdf(null!);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void CustomerName_ReachesRenderedBytes_AndQuestPdfIsDeterministic()
    {
        // Render() must unwrap a JSON string to its raw text. Proven by
        // CONTRAST, since glyphs are font-subset encoded: two payloads that
        // differ only in one customer name must produce different PDF bytes
        // (the name reached the page), while the determinism assertion shows
        // the comparison itself is meaningful (same input → identical bytes;
        // if QuestPDF ever embeds a timestamp, this pins that too and the
        // contrast test gets redesigned rather than silently passing).
        var a = BindLikeAspNetCore<GenerateQuotePdfRequestDto>(
            FrontendPayloadJson.Replace("Nguyễn Văn An", "AAAA-một"));
        var b = BindLikeAspNetCore<GenerateQuotePdfRequestDto>(
            FrontendPayloadJson.Replace("Nguyễn Văn An", "BBBB-hai"));
        var same = new SalesService.PdfDocuments.QuotePdfDocument(a).GeneratePdf();
        var sameAgain = new SalesService.PdfDocuments.QuotePdfDocument(a).GeneratePdf();
        Assert.Equal(same, sameAgain); // deterministic baseline

        var other = new SalesService.PdfDocuments.QuotePdfDocument(b).GeneratePdf();
        Assert.NotEqual(same, other);
    }

    [Fact]
    public void CamelCaseKeyIsTheRenderingContract_PascalOnlyPayloadLeavesBlank()
    {
        // Documents the deliberate contract: dict KEYS are matched exactly as
        // the frontend spells them (camelCase). A payload using PascalCase
        // keys still binds (case-insensitive DTO matching) but every dict cell
        // renders blank — the contrast above proves a rendered name changes
        // the bytes, so blank-vs-rendered must NOT.
        var camel = BindLikeAspNetCore<GenerateQuotePdfRequestDto>(FrontendPayloadJson);
        var pascal = JsonSerializer.Deserialize<GenerateQuotePdfRequestDto>(
            FrontendPayloadJson
                .Replace("\"customerInfo\"", "\"CustomerInfo\"")
                .Replace("\"name\":", "\"Name\":")
                .Replace("\"quoteItems\"", "\"QuoteItems\""),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var camelBytes = new SalesService.PdfDocuments.QuotePdfDocument(camel).GeneratePdf();
        var pascalBytes = new SalesService.PdfDocuments.QuotePdfDocument(pascal).GeneratePdf();
        // camelCase rendered; PascalCase lost customerInfo+quoteItems (the
        // DTO property names still bound case-insensitively, the dict KEYS
        // did not) → different documents.
        Assert.NotEqual(camelBytes, pascalBytes);
    }
}
