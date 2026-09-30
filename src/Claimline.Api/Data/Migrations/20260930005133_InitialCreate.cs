using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Claimline.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "ClaimNumbers",
                startValue: 1001L);

            migrationBuilder.CreateTable(
                name: "Outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Policies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PolicyNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InsuredFirstName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    InsuredLastName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    InsuredEmail = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    State = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Coverages = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CollisionDeductible = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Vehicle = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    VehicleValue = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    PriorClaims = table.Column<int>(type: "int", nullable: false),
                    DemoScenario = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedMessages",
                columns: table => new
                {
                    Consumer = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedMessages", x => new { x.Consumer, x.MessageId });
                });

            migrationBuilder.CreateTable(
                name: "Claims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PolicyId = table.Column<int>(type: "int", nullable: false),
                    LossType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LossDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LossLocation = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    EstimatedAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    InjuriesReported = table.Column<bool>(type: "bit", nullable: false),
                    PoliceReportFiled = table.Column<bool>(type: "bit", nullable: false),
                    ContactPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TriageAttempt = table.Column<int>(type: "int", nullable: false),
                    LastSubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Route = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    SeverityScore = table.Column<int>(type: "int", nullable: true),
                    FraudScore = table.Column<int>(type: "int", nullable: true),
                    ReserveAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    Analyzer = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Analysis = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Signals = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TriagedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Claims_Policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClaimEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClaimEvents_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimEvents_ClaimId_At",
                table: "ClaimEvents",
                columns: new[] { "ClaimId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Claims_ClaimNumber",
                table: "Claims",
                column: "ClaimNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Claims_IdempotencyKey",
                table: "Claims",
                column: "IdempotencyKey",
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_PolicyId",
                table: "Claims",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_Status_ReportedAt",
                table: "Claims",
                columns: new[] { "Status", "ReportedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_SentAt",
                table: "Outbox",
                column: "SentAt",
                filter: "[SentAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_PolicyNumber",
                table: "Policies",
                column: "PolicyNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClaimEvents");

            migrationBuilder.DropTable(
                name: "Outbox");

            migrationBuilder.DropTable(
                name: "ProcessedMessages");

            migrationBuilder.DropTable(
                name: "Claims");

            migrationBuilder.DropTable(
                name: "Policies");

            migrationBuilder.DropSequence(
                name: "ClaimNumbers");
        }
    }
}
