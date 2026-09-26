using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SalesService.Data;

#nullable disable

namespace SalesService.Migrations
{
    [DbContext(typeof(SalesDbContext))]
    [Migration("20260925170040_PostgresColumnTypes")]
    /// <summary>
    /// Issue #92 (P2): SQLite-shaped columns -> real Postgres types.
    /// Full rationale in VehicleService/Migrations/20260925170000_PostgresColumnTypes.cs.
    ///
    /// The largest set in the codebase, and the one with the widest blast
    /// radius: three Guid primary keys (Deliveries.DeliveryId,
    /// Payments.PaymentId, Promotions.PromotionId) are `text` today, and
    /// Promotions.IsActive is `integer` against a `bool` model — so writing
    /// a promotion or a payment fails the same way user registration did.
    ///
    /// A NOTE ON Deliveries.OrderId
    /// It is a `text` column holding a Guid that is NOT this service's
    /// Payments->Orders relationship: 20260913171934_PaymentOrderLinkToIntFK
    /// fixed Payments.OrderId to a real int FK, but Deliveries keeps BOTH
    /// OrderId (Guid, orphaned) and OrderId1 (int, the one carrying
    /// FK_Deliveries_Orders_OrderId1). EF itself reports the shadow property
    /// at run time: "The foreign key property 'Delivery.OrderId1' was created
    /// in shadow state because a conflicting property with the simple name
    /// 'OrderId' exists". Converting OrderId to uuid here makes the model and
    /// database agree, but the column stays unused; collapsing the duplicate
    /// is a model change outside P2's scope and is NOT done here.
    ///
    /// Nullable and carrying the NULLIF(TRIM(x),'') guard:
    /// Deliveries.DeliveryDate, Payments.PaidDate.
    ///
    /// Deliberately NOT here — these are owned by the sibling migration
    /// 20260918020142_AddDecimalPrecision, which converts them with the same
    /// USING-clause reason: Payments.Amount and Promotions.Value.
    /// </summary>
    public partial class PostgresColumnTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
                return;

            migrationBuilder.Sql("""ALTER TABLE "Contracts" ALTER COLUMN "SignedDate" TYPE date USING "SignedDate"::date;""");
            migrationBuilder.Sql("""ALTER TABLE "Contracts" ALTER COLUMN "CreatedAt" TYPE timestamptz USING "CreatedAt"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "Contracts" ALTER COLUMN "UpdatedAt" TYPE timestamptz USING "UpdatedAt"::timestamptz;""");

            migrationBuilder.Sql("""ALTER TABLE "Deliveries" ALTER COLUMN "DeliveryId" TYPE uuid USING "DeliveryId"::uuid;""");
            migrationBuilder.Sql("""ALTER TABLE "Deliveries" ALTER COLUMN "OrderId" TYPE uuid USING "OrderId"::uuid;""");
            migrationBuilder.Sql("""ALTER TABLE "Deliveries" ALTER COLUMN "DeliveryDate" TYPE timestamptz USING NULLIF(TRIM("DeliveryDate"), '')::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "Deliveries" ALTER COLUMN "CreatedAt" TYPE timestamptz USING "CreatedAt"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "Deliveries" ALTER COLUMN "UpdatedAt" TYPE timestamptz USING "UpdatedAt"::timestamptz;""");

            migrationBuilder.Sql("""ALTER TABLE "Orders" ALTER COLUMN "DeliveryPreferredDate" TYPE timestamptz USING "DeliveryPreferredDate"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "Orders" ALTER COLUMN "DeliveryExpectedDate" TYPE timestamptz USING "DeliveryExpectedDate"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "Orders" ALTER COLUMN "CreatedAt" TYPE timestamptz USING "CreatedAt"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "Orders" ALTER COLUMN "UpdatedAt" TYPE timestamptz USING "UpdatedAt"::timestamptz;""");

            migrationBuilder.Sql("""ALTER TABLE "Payments" ALTER COLUMN "PaymentId" TYPE uuid USING "PaymentId"::uuid;""");
            migrationBuilder.Sql("""ALTER TABLE "Payments" ALTER COLUMN "PaidDate" TYPE timestamptz USING NULLIF(TRIM("PaidDate"), '')::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "Payments" ALTER COLUMN "CreatedAt" TYPE timestamptz USING "CreatedAt"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "Payments" ALTER COLUMN "UpdatedAt" TYPE timestamptz USING "UpdatedAt"::timestamptz;""");

            migrationBuilder.Sql("""ALTER TABLE "Promotions" ALTER COLUMN "PromotionId" TYPE uuid USING "PromotionId"::uuid;""");
            migrationBuilder.Sql("""ALTER TABLE "Promotions" ALTER COLUMN "IsActive" TYPE boolean USING ("IsActive" <> 0);""");
            migrationBuilder.Sql("""ALTER TABLE "Promotions" ALTER COLUMN "StartDate" TYPE timestamptz USING "StartDate"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "Promotions" ALTER COLUMN "EndDate" TYPE timestamptz USING "EndDate"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "Promotions" ALTER COLUMN "CreatedAt" TYPE timestamptz USING "CreatedAt"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "Promotions" ALTER COLUMN "UpdatedAt" TYPE timestamptz USING "UpdatedAt"::timestamptz;""");

            migrationBuilder.Sql("""ALTER TABLE "Quotes" ALTER COLUMN "CreatedAt" TYPE timestamptz USING "CreatedAt"::timestamptz;""");
            migrationBuilder.Sql("""ALTER TABLE "Quotes" ALTER COLUMN "UpdatedAt" TYPE timestamptz USING "UpdatedAt"::timestamptz;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
                return;

            migrationBuilder.Sql("""ALTER TABLE "Contracts" ALTER COLUMN "SignedDate" TYPE text USING "SignedDate"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Contracts" ALTER COLUMN "CreatedAt" TYPE text USING "CreatedAt"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Contracts" ALTER COLUMN "UpdatedAt" TYPE text USING "UpdatedAt"::text;""");

            migrationBuilder.Sql("""ALTER TABLE "Deliveries" ALTER COLUMN "DeliveryId" TYPE text USING "DeliveryId"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Deliveries" ALTER COLUMN "OrderId" TYPE text USING "OrderId"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Deliveries" ALTER COLUMN "DeliveryDate" TYPE text USING "DeliveryDate"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Deliveries" ALTER COLUMN "CreatedAt" TYPE text USING "CreatedAt"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Deliveries" ALTER COLUMN "UpdatedAt" TYPE text USING "UpdatedAt"::text;""");

            migrationBuilder.Sql("""ALTER TABLE "Orders" ALTER COLUMN "DeliveryPreferredDate" TYPE text USING "DeliveryPreferredDate"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Orders" ALTER COLUMN "DeliveryExpectedDate" TYPE text USING "DeliveryExpectedDate"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Orders" ALTER COLUMN "CreatedAt" TYPE text USING "CreatedAt"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Orders" ALTER COLUMN "UpdatedAt" TYPE text USING "UpdatedAt"::text;""");

            migrationBuilder.Sql("""ALTER TABLE "Payments" ALTER COLUMN "PaymentId" TYPE text USING "PaymentId"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Payments" ALTER COLUMN "PaidDate" TYPE text USING "PaidDate"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Payments" ALTER COLUMN "CreatedAt" TYPE text USING "CreatedAt"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Payments" ALTER COLUMN "UpdatedAt" TYPE text USING "UpdatedAt"::text;""");

            migrationBuilder.Sql("""ALTER TABLE "Promotions" ALTER COLUMN "PromotionId" TYPE text USING "PromotionId"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Promotions" ALTER COLUMN "IsActive" TYPE integer USING CASE WHEN "IsActive" THEN 1 ELSE 0 END;""");
            migrationBuilder.Sql("""ALTER TABLE "Promotions" ALTER COLUMN "StartDate" TYPE text USING "StartDate"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Promotions" ALTER COLUMN "EndDate" TYPE text USING "EndDate"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Promotions" ALTER COLUMN "CreatedAt" TYPE text USING "CreatedAt"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Promotions" ALTER COLUMN "UpdatedAt" TYPE text USING "UpdatedAt"::text;""");

            migrationBuilder.Sql("""ALTER TABLE "Quotes" ALTER COLUMN "CreatedAt" TYPE text USING "CreatedAt"::text;""");
            migrationBuilder.Sql("""ALTER TABLE "Quotes" ALTER COLUMN "UpdatedAt" TYPE text USING "UpdatedAt"::text;""");
        }
    }
}
