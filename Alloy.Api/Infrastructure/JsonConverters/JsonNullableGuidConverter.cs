// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alloy.Api.Infrastructure.JsonConverters
{
    class JsonNullableGuidConverter : JsonConverter<Guid?>
    {
        public override Guid? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string chkValue = reader.GetString();
            if (string.IsNullOrWhiteSpace(chkValue))
            {
                return null;
            }
            return Guid.Parse(chkValue);
        }

        public override void Write( Utf8JsonWriter writer, Guid? value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case null:
                    writer.WriteStringValue("");
                    break;
                default:
                    writer.WriteStringValue(value.ToString());
                    break;
              
            }
        }

    }
}