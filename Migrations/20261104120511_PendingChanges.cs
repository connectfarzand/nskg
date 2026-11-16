using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nskg.Migrations
{
    /// <inheritdoc />
    public partial class PendingChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BiltyId",
                table: "VoDet",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoucherId",
                table: "ISSHEAD",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LinkedGroupId",
                table: "GLChart3",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ChallanId",
                table: "CommDetail",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BiltyId",
                table: "ChallanDet",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoDet_BiltyId",
                table: "VoDet",
                column: "BiltyId");

            migrationBuilder.CreateIndex(
                name: "IX_ISSHEAD_VoucherId",
                table: "ISSHEAD",
                column: "VoucherId");

            migrationBuilder.AddForeignKey(
                name: "FK_ISSHEAD_VoHead_VoucherId",
                table: "ISSHEAD",
                column: "VoucherId",
                principalTable: "VoHead",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_VoDet_ISSHEAD_BiltyId",
                table: "VoDet",
                column: "BiltyId",
                principalTable: "ISSHEAD",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ISSHEAD_VoHead_VoucherId",
                table: "ISSHEAD");

            migrationBuilder.DropForeignKey(
                name: "FK_VoDet_ISSHEAD_BiltyId",
                table: "VoDet");

            migrationBuilder.DropIndex(
                name: "IX_VoDet_BiltyId",
                table: "VoDet");

            migrationBuilder.DropIndex(
                name: "IX_ISSHEAD_VoucherId",
                table: "ISSHEAD");

            migrationBuilder.DropColumn(
                name: "BiltyId",
                table: "VoDet");

            migrationBuilder.DropColumn(
                name: "VoucherId",
                table: "ISSHEAD");

            migrationBuilder.DropColumn(
                name: "LinkedGroupId",
                table: "GLChart3");

            migrationBuilder.DropColumn(
                name: "ChallanId",
                table: "CommDetail");

            migrationBuilder.DropColumn(
                name: "BiltyId",
                table: "ChallanDet");
        }
    }
}
