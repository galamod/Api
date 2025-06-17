using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLicenseSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Licenses_Users_UserId",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "ApplicationName",
                table: "Licenses");

            migrationBuilder.RenameColumn(
                name: "ExpirationDate",
                table: "Licenses",
                newName: "ExpiresAt");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "Licenses",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "Application",
                table: "Licenses",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_Key",
                table: "Licenses",
                column: "Key",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Licenses_Users_UserId",
                table: "Licenses",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Licenses_Users_UserId",
                table: "Licenses");

            migrationBuilder.DropIndex(
                name: "IX_Licenses_Key",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "Application",
                table: "Licenses");

            migrationBuilder.RenameColumn(
                name: "ExpiresAt",
                table: "Licenses",
                newName: "ExpirationDate");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "Licenses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicationName",
                table: "Licenses",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddForeignKey(
                name: "FK_Licenses_Users_UserId",
                table: "Licenses",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
