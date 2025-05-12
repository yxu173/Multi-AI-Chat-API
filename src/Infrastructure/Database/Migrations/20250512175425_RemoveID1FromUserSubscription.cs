using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class RemoveID1FromUserSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserSubscriptions_SubscriptionPlans_SubscriptionPlanId1",
                table: "UserSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_UserSubscriptions_SubscriptionPlanId1",
                table: "UserSubscriptions");

            migrationBuilder.DropColumn(
                name: "SubscriptionPlanId1",
                table: "UserSubscriptions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubscriptionPlanId1",
                table: "UserSubscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_SubscriptionPlanId1",
                table: "UserSubscriptions",
                column: "SubscriptionPlanId1");

            migrationBuilder.AddForeignKey(
                name: "FK_UserSubscriptions_SubscriptionPlans_SubscriptionPlanId1",
                table: "UserSubscriptions",
                column: "SubscriptionPlanId1",
                principalTable: "SubscriptionPlans",
                principalColumn: "Id");
        }
    }
}
