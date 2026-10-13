using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConstructErp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserName",
                table: "Users",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // Existing accounts get the part of their email before the "@" as a
            // username (a numbered suffix where two would collide), so everyone
            // can sign in with a username straight away. Email still works too.
            migrationBuilder.Sql("""
                WITH named AS (
                    SELECT Id,
                           LOWER(LEFT(LEFT(Email, CHARINDEX('@', Email + '@') - 1), 28)) AS Base,
                           ROW_NUMBER() OVER (
                               PARTITION BY LOWER(LEFT(LEFT(Email, CHARINDEX('@', Email + '@') - 1), 28))
                               ORDER BY CreatedAt, Id) AS Seq
                    FROM Users
                )
                UPDATE u
                SET UserName = CASE WHEN n.Seq = 1 THEN n.Base
                                    ELSE n.Base + '-' + CAST(n.Seq AS nvarchar(3)) END
                FROM Users u
                JOIN named n ON n.Id = u.Id;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_UserName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UserName",
                table: "Users");
        }
    }
}
