// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.Data.SqlClient;
using Alloy.Api.Infrastructure.Exceptions;

namespace Alloy.Api.Infrastructure.Exceptions.Middleware
{
    public class ExceptionMiddleware
    {
        private readonly IWebHostEnvironment _env;
        private readonly RequestDelegate _next;
        private readonly ILogger _logger;

        public ExceptionMiddleware(
            RequestDelegate next,
            ILogger<ExceptionMiddleware> logger,
            IWebHostEnvironment env)
        {
            _logger = logger;
            _next = next;
            _env = env;
        }

        public async Task InvokeAsync(HttpContext httpContext)
        {
            try
            {
                await _next(httpContext);
            }
            catch (Exception ex)
            {
                // Transform PostgreSQL errors into clear messages
                if (ex is DbUpdateException dbEx && dbEx.InnerException is PostgresException pgEx)
                {
                    ex = TransformPostgresException(pgEx);
                }
                else if (ex is DbUpdateException dbEx2 && dbEx2.InnerException is SqlException sqlEx)
                {
                    ex = TransformSqlServerException(sqlEx);
                }

                _logger.LogError(ex, "Unhandled Exception");

                await HandleExceptionAsync(httpContext, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            int statusCode = GetStatusCodeFromException(exception);

            var error = new ProblemDetails();
            error.Status = statusCode;

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/problem+json";

            if (statusCode == (int)HttpStatusCode.InternalServerError)
            {
                if (_env.IsDevelopment())
                {
                    error.Title = exception.Message;
                    error.Detail = exception.ToString();
                }
                else
                {
                    error.Title = "A server error occurred.";
                    error.Detail = exception.Message;
                }
            }
            else
            {
                error.Title = exception.Message;
            }

            await context.Response.WriteAsync(JsonSerializer.Serialize(error));
        }

        /// <summary>
        /// map all custom exceptions to proper http status code
        /// </summary>
        /// <returns></returns>
        private int GetStatusCodeFromException(Exception exception)
        {
            HttpStatusCode statusCode = HttpStatusCode.InternalServerError;

            if (exception is IApiException)
            {
                statusCode = (exception as IApiException).GetStatusCode();
            }

            return (int)statusCode;
        }

        /// <summary>
        /// Transform PostgreSQL exceptions into user-friendly messages.
        /// Logs detailed error information while returning generic messages to prevent
        /// exposing database internals to users.
        /// </summary>
        private Exception TransformPostgresException(PostgresException pgEx)
        {
            // Log detailed error for developers/ops
            _logger.LogError(pgEx, "PostgreSQL {SqlState}: Table={TableName}, Constraint={ConstraintName}",
                pgEx.SqlState, pgEx.TableName, pgEx.ConstraintName);

            // Always return generic user-friendly messages
            return pgEx.SqlState switch
            {
                "23505" => // unique_violation
                    new InvalidOperationException("A record with this identifier already exists."),

                "23503" => // foreign_key_violation
                    new InvalidOperationException("Referenced entity does not exist. Please verify all referenced entities exist."),

                "23514" => // check_violation
                    new InvalidOperationException("Data validation failed."),

                _ => new InvalidOperationException("A database error occurred.")
            };
        }

        /// <summary>
        /// Transform SQL Server exceptions into user-friendly messages.
        /// Logs detailed error information while returning generic messages to prevent
        /// exposing database internals to users.
        /// </summary>
        private Exception TransformSqlServerException(SqlException sqlEx)
        {
            // Log detailed error for developers/ops
            _logger.LogError(sqlEx, "SQL Server Error {ErrorNumber}", sqlEx.Number);

            // Always return generic user-friendly messages
            return sqlEx.Number switch
            {
                2601 or 2627 => // unique constraint violation
                    new InvalidOperationException("A record with this identifier already exists."),

                547 => // foreign key violation
                    new InvalidOperationException("Referenced entity does not exist. Please verify all referenced entities exist."),

                _ => new InvalidOperationException("A database error occurred.")
            };
        }
    }
}
