﻿// <auto-generated />
using System;
using Alloy.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Alloy.Api.Migrations.PostgreSQL.Migrations
{
    [DbContext(typeof(AlloyContext))]
    [Migration("20210419180942_Share_Code_Migration")]
    partial class Share_Code_Migration
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("Npgsql:PostgresExtension:uuid-ossp", ",,")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .HasAnnotation("ProductVersion", "3.1.0")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            modelBuilder.Entity("Alloy.Api.Data.Models.EventEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasColumnType("uuid")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid>("CreatedBy")
                        .HasColumnName("created_by")
                        .HasColumnType("uuid");

                    b.Property<DateTime>("DateCreated")
                        .HasColumnName("date_created")
                        .HasColumnType("timestamp without time zone");

                    b.Property<DateTime?>("DateModified")
                        .HasColumnName("date_modified")
                        .HasColumnType("timestamp without time zone");

                    b.Property<string>("Description")
                        .HasColumnName("description")
                        .HasColumnType("text");

                    b.Property<DateTime?>("EndDate")
                        .HasColumnName("end_date")
                        .HasColumnType("timestamp without time zone");

                    b.Property<Guid?>("EventTemplateId")
                        .HasColumnName("event_template_id")
                        .HasColumnType("uuid");

                    b.Property<DateTime?>("ExpirationDate")
                        .HasColumnName("expiration_date")
                        .HasColumnType("timestamp without time zone");

                    b.Property<int>("FailureCount")
                        .HasColumnName("failure_count")
                        .HasColumnType("integer");

                    b.Property<int>("InternalStatus")
                        .HasColumnName("internal_status")
                        .HasColumnType("integer");

                    b.Property<int>("LastEndInternalStatus")
                        .HasColumnName("last_end_internal_status")
                        .HasColumnType("integer");

                    b.Property<int>("LastEndStatus")
                        .HasColumnName("last_end_status")
                        .HasColumnType("integer");

                    b.Property<int>("LastLaunchInternalStatus")
                        .HasColumnName("last_launch_internal_status")
                        .HasColumnType("integer");

                    b.Property<int>("LastLaunchStatus")
                        .HasColumnName("last_launch_status")
                        .HasColumnType("integer");

                    b.Property<DateTime?>("LaunchDate")
                        .HasColumnName("launch_date")
                        .HasColumnType("timestamp without time zone");

                    b.Property<Guid?>("ModifiedBy")
                        .HasColumnName("modified_by")
                        .HasColumnType("uuid");

                    b.Property<string>("Name")
                        .HasColumnName("name")
                        .HasColumnType("text");

                    b.Property<Guid?>("RunId")
                        .HasColumnName("run_id")
                        .HasColumnType("uuid");

                    b.Property<Guid?>("ScenarioId")
                        .HasColumnName("scenario_id")
                        .HasColumnType("uuid");

                    b.Property<string>("ShareCode")
                        .HasColumnName("share_code")
                        .HasColumnType("text");

                    b.Property<int>("Status")
                        .HasColumnName("status")
                        .HasColumnType("integer");

                    b.Property<DateTime>("StatusDate")
                        .HasColumnName("status_date")
                        .HasColumnType("timestamp without time zone");

                    b.Property<Guid>("UserId")
                        .HasColumnName("user_id")
                        .HasColumnType("uuid");

                    b.Property<string>("Username")
                        .HasColumnName("username")
                        .HasColumnType("text");

                    b.Property<Guid?>("ViewId")
                        .HasColumnName("view_id")
                        .HasColumnType("uuid");

                    b.Property<Guid?>("WorkspaceId")
                        .HasColumnName("workspace_id")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("EventTemplateId");

                    b.ToTable("events");
                });

            modelBuilder.Entity("Alloy.Api.Data.Models.EventTemplateEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasColumnType("uuid")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid>("CreatedBy")
                        .HasColumnName("created_by")
                        .HasColumnType("uuid");

                    b.Property<DateTime>("DateCreated")
                        .HasColumnName("date_created")
                        .HasColumnType("timestamp without time zone");

                    b.Property<DateTime?>("DateModified")
                        .HasColumnName("date_modified")
                        .HasColumnType("timestamp without time zone");

                    b.Property<string>("Description")
                        .HasColumnName("description")
                        .HasColumnType("text");

                    b.Property<Guid?>("DirectoryId")
                        .HasColumnName("directory_id")
                        .HasColumnType("uuid");

                    b.Property<int>("DurationHours")
                        .HasColumnName("duration_hours")
                        .HasColumnType("integer");

                    b.Property<bool>("IsPublished")
                        .HasColumnName("is_published")
                        .HasColumnType("boolean");

                    b.Property<Guid?>("ModifiedBy")
                        .HasColumnName("modified_by")
                        .HasColumnType("uuid");

                    b.Property<string>("Name")
                        .HasColumnName("name")
                        .HasColumnType("text");

                    b.Property<Guid?>("ScenarioTemplateId")
                        .HasColumnName("scenario_template_id")
                        .HasColumnType("uuid");

                    b.Property<bool>("UseDynamicHost")
                        .HasColumnName("use_dynamic_host")
                        .HasColumnType("boolean");

                    b.Property<Guid?>("ViewId")
                        .HasColumnName("view_id")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.ToTable("event_templates");
                });

            modelBuilder.Entity("Alloy.Api.Data.Models.EventEntity", b =>
                {
                    b.HasOne("Alloy.Api.Data.Models.EventTemplateEntity", "EventTemplate")
                        .WithMany()
                        .HasForeignKey("EventTemplateId");
                });
#pragma warning restore 612, 618
        }
    }
}
