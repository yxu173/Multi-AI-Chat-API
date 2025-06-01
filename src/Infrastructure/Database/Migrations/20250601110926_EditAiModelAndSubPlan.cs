using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class EditAiModelAndSubPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentDayUsage",
                table: "UserSubscriptions");

            migrationBuilder.DropColumn(
                name: "MaxRequestsPerDay",
                table: "SubscriptionPlans");

            migrationBuilder.AddColumn<double>(
                name: "CurrentMonthUsage",
                table: "UserSubscriptions",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "MaxRequestsPerMonth",
                table: "SubscriptionPlans",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "RequestCost",
                table: "AiModels",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentMonthUsage",
                table: "UserSubscriptions");

            migrationBuilder.DropColumn(
                name: "MaxRequestsPerMonth",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "RequestCost",
                table: "AiModels");

            migrationBuilder.AddColumn<int>(
                name: "CurrentDayUsage",
                table: "UserSubscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxRequestsPerDay",
                table: "SubscriptionPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
