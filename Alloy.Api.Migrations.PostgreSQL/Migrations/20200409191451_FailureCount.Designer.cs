﻿// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
// <auto-generated />
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
    [Migration("20200409191451_FailureCount")]
    partial class FailureCount
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("Npgsql:PostgresExtension:uuid-ossp", ",,")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn)
                .HasAnnotation("ProductVersion", "2.2.6-servicing-10079")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            modelBuilder.Entity("Alloy.Api.Data.Models.DefinitionEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid>("CreatedBy")
                        .HasColumnName("created_by");

                    b.Property<DateTime>("DateCreated")
                        .HasColumnName("date_created");

                    b.Property<DateTime?>("DateModified")
                        .HasColumnName("date_modified");

                    b.Property<string>("Description")
                        .HasColumnName("description");

                    b.Property<Guid?>("DirectoryId")
                        .HasColumnName("directory_id");

                    b.Property<int>("DurationHours")
                        .HasColumnName("duration_hours");

                    b.Property<Guid?>("ExerciseId")
                        .HasColumnName("exercise_id");

                    b.Property<bool>("IsPublished")
                        .HasColumnName("is_published");

                    b.Property<Guid?>("ModifiedBy")
                        .HasColumnName("modified_by");

                    b.Property<string>("Name")
                        .HasColumnName("name");

                    b.Property<Guid?>("ScenarioId")
                        .HasColumnName("scenario_id");

                    b.Property<bool>("UseDynamicHost")
                        .HasColumnName("use_dynamic_host");

                    b.HasKey("Id");

                    b.ToTable("definitions");
                });

            modelBuilder.Entity("Alloy.Api.Data.Models.ImplementationEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid>("CreatedBy")
                        .HasColumnName("created_by");

                    b.Property<DateTime>("DateCreated")
                        .HasColumnName("date_created");

                    b.Property<DateTime?>("DateModified")
                        .HasColumnName("date_modified");

                    b.Property<Guid?>("DefinitionId")
                        .HasColumnName("definition_id");

                    b.Property<string>("Description")
                        .HasColumnName("description");

                    b.Property<DateTime?>("EndDate")
                        .HasColumnName("end_date");

                    b.Property<Guid?>("ExerciseId")
                        .HasColumnName("exercise_id");

                    b.Property<DateTime?>("ExpirationDate")
                        .HasColumnName("expiration_date");

                    b.Property<int>("FailureCount")
                        .HasColumnName("failure_count");

                    b.Property<int>("InternalStatus")
                        .HasColumnName("internal_status");

                    b.Property<int>("LastEndInternalStatus")
                        .HasColumnName("last_end_internal_status");

                    b.Property<int>("LastEndStatus")
                        .HasColumnName("last_end_status");

                    b.Property<int>("LastLaunchInternalStatus")
                        .HasColumnName("last_launch_internal_status");

                    b.Property<int>("LastLaunchStatus")
                        .HasColumnName("last_launch_status");

                    b.Property<DateTime?>("LaunchDate")
                        .HasColumnName("launch_date");

                    b.Property<Guid?>("ModifiedBy")
                        .HasColumnName("modified_by");

                    b.Property<string>("Name")
                        .HasColumnName("name");

                    b.Property<Guid?>("RunId")
                        .HasColumnName("run_id");

                    b.Property<Guid?>("SessionId")
                        .HasColumnName("session_id");

                    b.Property<int>("Status")
                        .HasColumnName("status");

                    b.Property<DateTime>("StatusDate")
                        .HasColumnName("status_date");

                    b.Property<Guid>("UserId")
                        .HasColumnName("user_id");

                    b.Property<string>("Username")
                        .HasColumnName("username");

                    b.Property<Guid?>("WorkspaceId")
                        .HasColumnName("workspace_id");

                    b.HasKey("Id");

                    b.HasIndex("DefinitionId");

                    b.ToTable("implementations");
                });

            modelBuilder.Entity("Alloy.Api.Data.Models.ImplementationEntity", b =>
                {
                    b.HasOne("Alloy.Api.Data.Models.DefinitionEntity", "Definition")
                        .WithMany()
                        .HasForeignKey("DefinitionId");
                });
#pragma warning restore 612, 618
        }
    }
}
