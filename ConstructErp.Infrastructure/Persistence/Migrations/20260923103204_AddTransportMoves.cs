using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConstructErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransportMoves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransportMoves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ScheduledFor = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DepartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ArrivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    Destination_Ar = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Destination_En = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Notes_Ar = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Notes_En = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Origin_Ar = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Origin_En = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportMoves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransportMoves_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransportMoves_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransportMoves_Code",
                table: "TransportMoves",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransportMoves_DepartedAt_ScheduledFor",
                table: "TransportMoves",
                columns: new[] { "DepartedAt", "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportMoves_EquipmentId",
                table: "TransportMoves",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportMoves_ProjectId",
                table: "TransportMoves",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransportMoves");
        }
    }
}
