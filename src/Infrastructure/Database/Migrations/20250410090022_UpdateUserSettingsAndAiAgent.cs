using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class UpdateUserSettingsAndAiAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SystemMessage",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "EnableThinking",
                table: "AiAgents");

            migrationBuilder.AlterColumn<double>(
                name: "TopP",
                table: "UserAiModelSettings",
                type: "double precision",
                precision: 3,
                scale: 2,
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "TopK",
                table: "UserAiModelSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "Temperature",
                table: "UserAiModelSettings",
                type: "double precision",
                precision: 3,
                scale: 2,
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StopSequences",
                table: "UserAiModelSettings",
                type: "text",
                nullable: true,
                oldClrType: typeof(List<string>),
                oldType: "text[]");

            migrationBuilder.AlterColumn<string>(
                name: "SafetySettings",
                table: "UserAiModelSettings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "PresencePenalty",
                table: "UserAiModelSettings",
                type: "double precision",
                precision: 3,
                scale: 2,
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "MaxTokens",
                table: "UserAiModelSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "FrequencyPenalty",
                table: "UserAiModelSettings",
                type: "double precision",
                precision: 3,
                scale: 2,
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContextLimit",
                table: "UserAiModelSettings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SystemInstructions",
                table: "UserAiModelSettings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "SystemInstructions",
                table: "AiAgents",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultModel",
                table: "AiAgents",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SystemInstructions",
                table: "UserAiModelSettings");

            migrationBuilder.DropColumn(
                name: "DefaultModel",
                table: "AiAgents");

            migrationBuilder.AlterColumn<double>(
                name: "TopP",
                table: "UserAiModelSettings",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldPrecision: 3,
                oldScale: 2);

            migrationBuilder.AlterColumn<int>(
                name: "TopK",
                table: "UserAiModelSettings",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<double>(
                name: "Temperature",
                table: "UserAiModelSettings",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldPrecision: 3,
                oldScale: 2);

            migrationBuilder.AlterColumn<List<string>>(
                name: "StopSequences",
                table: "UserAiModelSettings",
                type: "text[]",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SafetySettings",
                table: "UserAiModelSettings",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "PresencePenalty",
                table: "UserAiModelSettings",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldPrecision: 3,
                oldScale: 2);

            migrationBuilder.AlterColumn<int>(
                name: "MaxTokens",
                table: "UserAiModelSettings",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<double>(
                name: "FrequencyPenalty",
                table: "UserAiModelSettings",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldPrecision: 3,
                oldScale: 2);

            migrationBuilder.AlterColumn<int>(
                name: "ContextLimit",
                table: "UserAiModelSettings",
                type: "integer",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<string>(
                name: "SystemMessage",
                table: "UserAiModelSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SystemInstructions",
                table: "AiAgents",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EnableThinking",
                table: "AiAgents",
                type: "boolean",
                nullable: true);
        }
    }
}
