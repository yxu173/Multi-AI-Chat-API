﻿// <auto-generated />
using System;
using System.Collections.Generic;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    partial class ApplicationDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "9.0.1")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("AiProvider", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("DefaultApiKey")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Description")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<bool>("IsEnabled")
                        .HasColumnType("boolean");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)");

                    b.HasKey("Id");

                    b.ToTable("AiProviders", (string)null);
                });

            modelBuilder.Entity("ChatSession", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<Guid?>("AiAgentId")
                        .HasColumnType("uuid");

                    b.Property<Guid>("AiModelId")
                        .HasColumnType("uuid");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("CustomApiKey")
                        .HasColumnType("text");

                    b.Property<Guid?>("FolderId")
                        .HasColumnType("uuid");

                    b.Property<DateTime?>("LastModifiedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("SystemPrompt")
                        .HasColumnType("text");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("AiModelId");

                    b.HasIndex("FolderId");

                    b.ToTable("ChatSessions", (string)null);
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.AiAgent", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<Guid>("AiModelId")
                        .HasColumnType("uuid");

                    b.Property<bool>("AssignCustomModelParameters")
                        .HasColumnType("boolean");

                    b.PrimitiveCollection<List<string>>("Categories")
                        .IsRequired()
                        .HasColumnType("text[]");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("Description")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("IconUrl")
                        .HasColumnType("text");

                    b.Property<DateTime?>("LastModifiedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("ModelParameters")
                        .HasColumnType("text");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("ProfilePictureUrl")
                        .HasColumnType("text");

                    b.Property<string>("SystemPrompt")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("AiModelId");

                    b.ToTable("AiAgents");
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.AiAgentPlugin", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<Guid>("AiAgentId")
                        .HasColumnType("uuid");

                    b.Property<bool>("IsActive")
                        .HasColumnType("boolean");

                    b.Property<int>("Order")
                        .HasColumnType("integer");

                    b.Property<Guid>("PluginId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("AiAgentId");

                    b.HasIndex("PluginId");

                    b.ToTable("AiAgentPlugins");
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.AiModel", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<Guid>("AiProviderId")
                        .HasColumnType("uuid");

                    b.Property<double>("InputTokenPricePer1M")
                        .HasColumnType("double precision");

                    b.Property<bool>("IsEnabled")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("boolean")
                        .HasDefaultValue(true);

                    b.Property<int?>("MaxInputTokens")
                        .HasColumnType("integer");

                    b.Property<int?>("MaxOutputTokens")
                        .HasColumnType("integer");

                    b.Property<string>("ModelCode")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)");

                    b.Property<string>("ModelType")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)");

                    b.Property<double>("OutputTokenPricePer1M")
                        .HasColumnType("double precision");

                    b.HasKey("Id");

                    b.HasIndex("AiProviderId");

                    b.ToTable("AiModels", (string)null);
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.ChatFolder", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("Description")
                        .HasColumnType("text");

                    b.Property<DateTime?>("LastModifiedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.ToTable("ChatFolders");
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.ChatSessionPlugin", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<Guid>("ChatSessionId")
                        .HasColumnType("uuid");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<bool>("IsActive")
                        .HasColumnType("boolean");

                    b.Property<DateTime?>("LastModifiedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<int>("Order")
                        .HasColumnType("integer");

                    b.Property<Guid>("PluginId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("ChatSessionId");

                    b.HasIndex("PluginId");

                    b.ToTable("ChatSessionPlugins");
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.ChatTokenUsage", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<Guid>("ChatId")
                        .HasColumnType("uuid");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<int>("InputTokens")
                        .HasColumnType("integer");

                    b.Property<DateTime>("LastUpdatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<int>("OutputTokens")
                        .HasColumnType("integer");

                    b.Property<decimal>("TotalCost")
                        .HasPrecision(18, 6)
                        .HasColumnType("numeric(18,6)");

                    b.HasKey("Id");

                    b.HasIndex("ChatId")
                        .IsUnique();

                    b.ToTable("ChatTokenUsages", (string)null);
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.FileAttachment", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("FileName")
                        .IsRequired()
                        .HasMaxLength(255)
                        .HasColumnType("character varying(255)");

                    b.Property<string>("FilePath")
                        .IsRequired()
                        .HasMaxLength(500)
                        .HasColumnType("character varying(500)");

                    b.Property<Guid>("MessageId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("MessageId");

                    b.ToTable("FileAttachments", (string)null);
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.Message", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<Guid>("ChatSessionId")
                        .HasColumnType("uuid");

                    b.Property<string>("Content")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<bool>("IsFromAi")
                        .HasColumnType("boolean");

                    b.Property<DateTime?>("LastModifiedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("ChatSessionId");

                    b.HasIndex("CreatedAt");

                    b.ToTable("Messages", (string)null);
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.Plugin", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("Description")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("IconUrl")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("PluginType")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<DateTime?>("UpdatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.HasKey("Id");

                    b.ToTable("Plugins");
                });

            modelBuilder.Entity("Domain.Aggregates.Prompts.PromptTemplate", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("Content")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Description")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("PromptTemplates", (string)null);
                });

            modelBuilder.Entity("Domain.Aggregates.Users.Role", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("ConcurrencyStamp")
                        .IsConcurrencyToken()
                        .HasColumnType("text");

                    b.Property<string>("Name")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.Property<string>("NormalizedName")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.HasKey("Id");

                    b.HasIndex("NormalizedName")
                        .IsUnique()
                        .HasDatabaseName("RoleNameIndex");

                    b.ToTable("AspNetRoles", (string)null);
                });

            modelBuilder.Entity("Domain.Aggregates.Users.User", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<int>("AccessFailedCount")
                        .HasColumnType("integer");

                    b.Property<string>("ConcurrencyStamp")
                        .IsConcurrencyToken()
                        .HasColumnType("text");

                    b.Property<string>("Email")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.Property<bool>("EmailConfirmed")
                        .HasColumnType("boolean");

                    b.Property<bool>("LockoutEnabled")
                        .HasColumnType("boolean");

                    b.Property<DateTimeOffset?>("LockoutEnd")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("NormalizedEmail")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.Property<string>("NormalizedUserName")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.Property<string>("PasswordHash")
                        .HasColumnType("text");

                    b.Property<string>("PhoneNumber")
                        .HasColumnType("text");

                    b.Property<bool>("PhoneNumberConfirmed")
                        .HasColumnType("boolean");

                    b.Property<string>("SecurityStamp")
                        .HasColumnType("text");

                    b.Property<bool>("TwoFactorEnabled")
                        .HasColumnType("boolean");

                    b.Property<string>("UserName")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.HasKey("Id");

                    b.HasIndex("NormalizedEmail")
                        .HasDatabaseName("EmailIndex");

                    b.HasIndex("NormalizedUserName")
                        .IsUnique()
                        .HasDatabaseName("UserNameIndex");

                    b.ToTable("AspNetUsers", (string)null);
                });

            modelBuilder.Entity("Domain.Aggregates.Users.UserAiModel", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<Guid>("AiModelId")
                        .HasColumnType("uuid");

                    b.Property<bool>("IsEnabled")
                        .HasColumnType("boolean");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("AiModelId");

                    b.HasIndex("UserId");

                    b.ToTable("UserAiModels", (string)null);
                });

            modelBuilder.Entity("Domain.Aggregates.Users.UserAiModelSettings", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<double?>("FrequencyPenalty")
                        .HasColumnType("double precision");

                    b.Property<bool>("IsDefault")
                        .HasColumnType("boolean");

                    b.Property<double?>("PresencePenalty")
                        .HasColumnType("double precision");

                    b.PrimitiveCollection<List<string>>("StopSequences")
                        .IsRequired()
                        .HasColumnType("text[]");

                    b.Property<double?>("Temperature")
                        .HasColumnType("double precision");

                    b.Property<int?>("TopK")
                        .HasColumnType("integer");

                    b.Property<double?>("TopP")
                        .HasColumnType("double precision");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("UserAiModelSettings");
                });

            modelBuilder.Entity("Domain.Aggregates.Users.UserApiKey", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<Guid>("AiProviderId")
                        .HasColumnType("uuid");

                    b.Property<string>("ApiKey")
                        .IsRequired()
                        .HasMaxLength(500)
                        .HasColumnType("character varying(500)");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<DateTime?>("LastUsed")
                        .HasColumnType("timestamp with time zone");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("AiProviderId");

                    b.HasIndex("UserId");

                    b.ToTable("UserApiKeys", (string)null);
                });

            modelBuilder.Entity("Domain.Aggregates.Users.UserPlugin", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<bool>("IsEnabled")
                        .HasColumnType("boolean");

                    b.Property<Guid>("PluginId")
                        .HasColumnType("uuid");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("PluginId");

                    b.HasIndex("UserId");

                    b.ToTable("UserPlugins");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityRoleClaim<System.Guid>", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("ClaimType")
                        .HasColumnType("text");

                    b.Property<string>("ClaimValue")
                        .HasColumnType("text");

                    b.Property<Guid>("RoleId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("RoleId");

                    b.ToTable("AspNetRoleClaims", (string)null);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserClaim<System.Guid>", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("ClaimType")
                        .HasColumnType("text");

                    b.Property<string>("ClaimValue")
                        .HasColumnType("text");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("AspNetUserClaims", (string)null);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserLogin<System.Guid>", b =>
                {
                    b.Property<string>("LoginProvider")
                        .HasColumnType("text");

                    b.Property<string>("ProviderKey")
                        .HasColumnType("text");

                    b.Property<string>("ProviderDisplayName")
                        .HasColumnType("text");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid");

                    b.HasKey("LoginProvider", "ProviderKey");

                    b.HasIndex("UserId");

                    b.ToTable("AspNetUserLogins", (string)null);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserRole<System.Guid>", b =>
                {
                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid");

                    b.Property<Guid>("RoleId")
                        .HasColumnType("uuid");

                    b.HasKey("UserId", "RoleId");

                    b.HasIndex("RoleId");

                    b.ToTable("AspNetUserRoles", (string)null);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserToken<System.Guid>", b =>
                {
                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid");

                    b.Property<string>("LoginProvider")
                        .HasColumnType("text");

                    b.Property<string>("Name")
                        .HasColumnType("text");

                    b.Property<string>("Value")
                        .HasColumnType("text");

                    b.HasKey("UserId", "LoginProvider", "Name");

                    b.ToTable("AspNetUserTokens", (string)null);
                });

            modelBuilder.Entity("ChatSession", b =>
                {
                    b.HasOne("Domain.Aggregates.Chats.AiModel", "AiModel")
                        .WithMany()
                        .HasForeignKey("AiModelId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Domain.Aggregates.Chats.ChatFolder", "Folder")
                        .WithMany("ChatSessions")
                        .HasForeignKey("FolderId");

                    b.Navigation("AiModel");

                    b.Navigation("Folder");
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.AiAgent", b =>
                {
                    b.HasOne("Domain.Aggregates.Chats.AiModel", "AiModel")
                        .WithMany()
                        .HasForeignKey("AiModelId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("AiModel");
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.AiAgentPlugin", b =>
                {
                    b.HasOne("Domain.Aggregates.Chats.AiAgent", "AiAgent")
                        .WithMany("AiAgentPlugins")
                        .HasForeignKey("AiAgentId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Domain.Aggregates.Chats.Plugin", "Plugin")
                        .WithMany()
                        .HasForeignKey("PluginId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("AiAgent");

                    b.Navigation("Plugin");
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.AiModel", b =>
                {
                    b.HasOne("AiProvider", "AiProvider")
                        .WithMany("Models")
                        .HasForeignKey("AiProviderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("AiProvider");
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.ChatSessionPlugin", b =>
                {
                    b.HasOne("ChatSession", "ChatSession")
                        .WithMany("ChatSessionPlugins")
                        .HasForeignKey("ChatSessionId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Domain.Aggregates.Chats.Plugin", "Plugin")
                        .WithMany()
                        .HasForeignKey("PluginId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("ChatSession");

                    b.Navigation("Plugin");
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.ChatTokenUsage", b =>
                {
                    b.HasOne("ChatSession", "ChatSession")
                        .WithOne()
                        .HasForeignKey("Domain.Aggregates.Chats.ChatTokenUsage", "ChatId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("ChatSession");
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.FileAttachment", b =>
                {
                    b.HasOne("Domain.Aggregates.Chats.Message", null)
                        .WithMany("FileAttachments")
                        .HasForeignKey("MessageId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.Message", b =>
                {
                    b.HasOne("ChatSession", "ChatSession")
                        .WithMany("Messages")
                        .HasForeignKey("ChatSessionId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("ChatSession");
                });

            modelBuilder.Entity("Domain.Aggregates.Prompts.PromptTemplate", b =>
                {
                    b.HasOne("Domain.Aggregates.Users.User", "User")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.OwnsMany("Domain.ValueObjects.Tag", "Tags", b1 =>
                        {
                            b1.Property<Guid>("PromptTemplateId")
                                .HasColumnType("uuid");

                            b1.Property<string>("Name")
                                .HasMaxLength(50)
                                .HasColumnType("character varying(50)");

                            b1.HasKey("PromptTemplateId", "Name");

                            b1.HasIndex("Name");

                            b1.ToTable("PromptTemplateTags", (string)null);

                            b1.WithOwner()
                                .HasForeignKey("PromptTemplateId");
                        });

                    b.Navigation("Tags");

                    b.Navigation("User");
                });

            modelBuilder.Entity("Domain.Aggregates.Users.UserAiModel", b =>
                {
                    b.HasOne("Domain.Aggregates.Chats.AiModel", "AiModel")
                        .WithMany("UserAiModels")
                        .HasForeignKey("AiModelId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Domain.Aggregates.Users.User", "User")
                        .WithMany("UserAiModels")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("AiModel");

                    b.Navigation("User");
                });

            modelBuilder.Entity("Domain.Aggregates.Users.UserAiModelSettings", b =>
                {
                    b.HasOne("Domain.Aggregates.Users.User", "User")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("User");
                });

            modelBuilder.Entity("Domain.Aggregates.Users.UserApiKey", b =>
                {
                    b.HasOne("AiProvider", "AiProvider")
                        .WithMany()
                        .HasForeignKey("AiProviderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Domain.Aggregates.Users.User", "User")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("AiProvider");

                    b.Navigation("User");
                });

            modelBuilder.Entity("Domain.Aggregates.Users.UserPlugin", b =>
                {
                    b.HasOne("Domain.Aggregates.Chats.Plugin", "Plugin")
                        .WithMany()
                        .HasForeignKey("PluginId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Domain.Aggregates.Users.User", "User")
                        .WithMany("UserPlugins")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Plugin");

                    b.Navigation("User");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityRoleClaim<System.Guid>", b =>
                {
                    b.HasOne("Domain.Aggregates.Users.Role", null)
                        .WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserClaim<System.Guid>", b =>
                {
                    b.HasOne("Domain.Aggregates.Users.User", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserLogin<System.Guid>", b =>
                {
                    b.HasOne("Domain.Aggregates.Users.User", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserRole<System.Guid>", b =>
                {
                    b.HasOne("Domain.Aggregates.Users.Role", null)
                        .WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Domain.Aggregates.Users.User", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserToken<System.Guid>", b =>
                {
                    b.HasOne("Domain.Aggregates.Users.User", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("AiProvider", b =>
                {
                    b.Navigation("Models");
                });

            modelBuilder.Entity("ChatSession", b =>
                {
                    b.Navigation("ChatSessionPlugins");

                    b.Navigation("Messages");
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.AiAgent", b =>
                {
                    b.Navigation("AiAgentPlugins");
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.AiModel", b =>
                {
                    b.Navigation("UserAiModels");
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.ChatFolder", b =>
                {
                    b.Navigation("ChatSessions");
                });

            modelBuilder.Entity("Domain.Aggregates.Chats.Message", b =>
                {
                    b.Navigation("FileAttachments");
                });

            modelBuilder.Entity("Domain.Aggregates.Users.User", b =>
                {
                    b.Navigation("UserAiModels");

                    b.Navigation("UserPlugins");
                });
#pragma warning restore 612, 618
        }
    }
}
