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
/// - delete the controller action → compile error in these tests;
/// - (Issue #49 review round) Render back to raw je.ToString() → the
///   money-formatting contract tests fail; drop the null-item skip or the
///   >500 cap → the null-hole and oversized tests fail.
/// (Rendered-text assertions are impossible: QuestPDF encodes glyphs through
/// font subsets, so no payload string appears literally in the PDF bytes —
/// which is why the formatting contract is pinned on Render() directly.)
/// </summary>
public class QuotePdfGenerationTests
{
    static QuotePdfGenerationTests()
    {
        // The production host sets this in SalesService/Program.cs; the test
        // process doesn't run it. QuestPDF does refuse to Generate() without
        // an explicit license (review round: the throw is a bare
        // System.Exception, not InvalidOperationException) — the controller
        // now catches Exception → 503, which is what makes that path testable.
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

    private static byte[] Render(SalesService.PdfDocuments.QuotePdfDocument doc)
    {
        // #58 measured: QuestPDF stamps every GeneratePdf() with a
        // creation-date literal at second granularity ("D:...HHMMSS"), so
        // two renders that straddle a clock tick differ in exactly ONE
        // byte and the raw-bytes determinism assertion below flakes ~1 in
        // 20 runs (caught on d9abc78). Mask every date-shaped token — PDF
        // `D:` literals and ISO8601 stamps — so equality is about
        // RENDERING, not about wall-clock luck. Content bytes are
        // untouched by the mask, so the contrast assertion still proves
        // the customer name reaches the page.
        var raw = System.Text.Encoding.Latin1.GetString(doc.GeneratePdf());
        var masked = System.Text.RegularExpressions.Regex.Replace(raw, @"D:\d{6,14}[^\)\s]*", "D:MASKED");
        masked = System.Text.RegularExpressions.Regex.Replace(masked, @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}", "MASKED");
        return System.Text.Encoding.Latin1.GetBytes(masked);
    }

    [Fact]
    public void CustomerName_ReachesRenderedBytes_AndQuestPdfIsDeterministic()
    {
        // Render() must unwrap a JSON string to its raw text. Proven by
        // CONTRAST, since glyphs are font-subset encoded: two payloads that
        // differ only in one customer name must produce different PDF bytes
        // (the name reached the page), while the determinism assertion shows
        // the comparison itself is meaningful (same input → identical bytes,
        // modulo the creation-date mask in Render — the #58 run that
        // flipped one seconds-digit is what moved this from "if QuestPDF
        // ever embeds a timestamp" to "it does").
        var a = BindLikeAspNetCore<GenerateQuotePdfRequestDto>(
            FrontendPayloadJson.Replace("Nguyễn Văn An", "AAAA-một"));
        var b = BindLikeAspNetCore<GenerateQuotePdfRequestDto>(
            FrontendPayloadJson.Replace("Nguyễn Văn An", "BBBB-hai"));
        var same = Render(new SalesService.PdfDocuments.QuotePdfDocument(a));
        var sameAgain = Render(new SalesService.PdfDocuments.QuotePdfDocument(a));
        Assert.Equal(same, sameAgain); // deterministic baseline

        var other = Render(new SalesService.PdfDocuments.QuotePdfDocument(b));
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

    // ---------- Issue #49 adversarial-review round ----------

    [Theory]
    // The review's exact production shapes: QuoteView.jsx posts the raw
    // IEEE-754 annuity result with no rounding (e.g. the fixture inputs
    // compute 22652900.887352396), so the PDF must never print float dust.
    [InlineData("22652900.887352396", "22.652.900,89")]
    [InlineData("22610000", "22.610.000")]
    [InlineData("1615000000", "1.615.000.000")]
    [InlineData("32361286.98193201", "32.361.286,98")]
    [InlineData("0", "0")]
    [InlineData("1356600000.5", "1.356.600.000,5")]
    [InlineData("-2.5", "-2,5")]
    public void MoneyValues_RenderNormalized_VietnameseSpelling(string rawJson, string expected)
    {
        // Pins Render()'s number contract: rounded to 2dp, '.' thousands,
        // ',' decimal — and crucially NOT the raw JSON text (ToString() on
        // the JsonElement before this fix emitted "22652900.887352396").
        var je = JsonDocument.Parse(rawJson).RootElement;
        Assert.Equal(expected, SalesService.PdfDocuments.QuotePdfDocument.Render(je));
        // Strings must keep rendering as raw text (no quotes): the widening
        // test above relies on it; assert it here so a format-everything
        // refactor can't break it silently.
        var str = JsonDocument.Parse("\"850000000\"").RootElement;
        Assert.Equal("850000000", SalesService.PdfDocuments.QuotePdfDocument.Render(str));
    }

    [Fact]
    public void RealAnnuityDouble_DoesNotLeakRawDigitsIntoDocument()
    {
        // End-to-end counterpart of the Render contract: the un-rounded
        // payment in the totals block must produce a DIFFERENT document from
        // a hand-rounded integer stand-in only because formatting is applied
        // (before the fix the raw text 22652900.887352396 landed verbatim —
        // same bytes as the string "22652900.887352396"). Now both paths
        // converge on the same spelling, proving the PDF shows clean money
        // regardless of how the browser serialized it.
        var messy = FrontendPayloadJson.Replace("\"monthlyPaymentCalculated\": 22610000",
            "\"monthlyPaymentCalculated\": 22610000.083333336");
        var dto = BindLikeAspNetCore<GenerateQuotePdfRequestDto>(messy);
        var bytes = Assert.IsType<FileContentResult>(Controller().GenerateQuotePdf(dto));
        Assert.StartsWith("%PDF-", System.Text.Encoding.Latin1.GetString(bytes.FileContents));
        Assert.Equal("22.610.000,08", SalesService.PdfDocuments.QuotePdfDocument.Render(
            (JsonElement)dto.MonthlyPaymentCalculated!));
    }

    [Fact]
    public void NullItemHole_AndNullTotals_ProducePdf_NotA500()
    {
        // The review reproduced quoteItems:[null] → NullReferenceException →
        // anonymous 500 (developer exception page in Development). The null
        // entry must be skipped, not crash, and an explicit JSON null on the
        // whole collections must bind to null and still render.
        var dto = BindLikeAspNetCore<GenerateQuotePdfRequestDto>(
            """{"customerInfo":null,"quoteItems":[null,{"vehicleName":"VF 5","quantity":1,"unitPrice":500000000,"itemTotal":500000000},null],"paymentInfo":null,"additionalInfo":null,"totalCalculatedAmount":null,"downPaymentCalculated":null,"loanAmountCalculated":null,"monthlyPaymentCalculated":null,"installmentTotalPaymentCalculated":null}""");
        Assert.Null(dto.CustomerInfo);
        var file = Assert.IsType<FileContentResult>(Controller().GenerateQuotePdf(dto));
        Assert.StartsWith("%PDF-", System.Text.Encoding.Latin1.GetString(file.FileContents));
        Assert.True(file.FileContents.Length > 2_000);
    }

    [Fact]
    public void NullQuoteItemsList_IsTreatedAsEmpty()
    {
        var dto = BindLikeAspNetCore<GenerateQuotePdfRequestDto>(
            """{"customerInfo":{},"quoteItems":null,"paymentInfo":{},"additionalInfo":{}}""");
        Assert.Null(dto.QuoteItems);
        var file = Assert.IsType<FileContentResult>(Controller().GenerateQuotePdf(dto));
        Assert.StartsWith("%PDF-", System.Text.Encoding.Latin1.GetString(file.FileContents));
    }

    [Fact]
    public void OversizedItemCap_Rejects_BeforeRendering()
    {
        // The route is an anonymous CPU amplifier; the cap must 400 at the
        // controller without invoking the renderer. Mutation check: delete
        // the cap line and 501 items sail through to a FileContentResult.
        var items = string.Join(",", Enumerable.Repeat(
            """{"vehicleName":"x","quantity":1,"unitPrice":1,"itemTotal":1}""", 501));
        var dto = BindLikeAspNetCore<GenerateQuotePdfRequestDto>(
            """{"customerInfo":{},"quoteItems":[""" + items + """],"paymentInfo":{},"additionalInfo":{}}""");
        var result = Assert.IsType<BadRequestObjectResult>(Controller().GenerateQuotePdf(dto));
        Assert.Contains("500", result.Value!.ToString()!);
        // 500 exactly is still allowed (cap is >, not >=)
        var ok = BindLikeAspNetCore<GenerateQuotePdfRequestDto>(
            """{"customerInfo":{},"quoteItems":[""" + string.Join(",", Enumerable.Repeat("""{"vehicleName":"x","quantity":1,"unitPrice":1,"itemTotal":1}""", 500)) + """],"paymentInfo":{},"additionalInfo":{}}""");
        Assert.IsType<FileContentResult>(Controller().GenerateQuotePdf(ok));
    }
}
