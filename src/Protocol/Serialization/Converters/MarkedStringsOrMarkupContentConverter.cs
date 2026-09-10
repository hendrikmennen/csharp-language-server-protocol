using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace OmniSharp.Extensions.LanguageServer.Protocol.Serialization.Converters
{
    public class MarkedStringsOrMarkupContentConverter : JsonConverter<MarkedStringsOrMarkupContent>
    {
        public override void WriteJson(JsonWriter writer, MarkedStringsOrMarkupContent value, JsonSerializer serializer)
        {
            if (value.HasMarkupContent)
            {
                serializer.Serialize(writer, value.MarkupContent);
            }
            else
            {
                serializer.Serialize(writer, value.MarkedStrings);
            }
        }

        public override MarkedStringsOrMarkupContent ReadJson(
            JsonReader reader, Type objectType, MarkedStringsOrMarkupContent existingValue, bool hasExistingValue, JsonSerializer serializer
        )
        {
            if (reader.TokenType == JsonToken.StartObject)
            {
                var result = JObject.Load(reader);

                // `Hover.contents` allows a *single* MarkedString object as well as an array of them,
                // and both MarkedString and MarkupContent are `{ ..., "value": string }`. They can only
                // be told apart by their sibling key: MarkedString has `language`, MarkupContent has `kind`.
                if (result["language"] != null && result["kind"] == null)
                {
                    return new MarkedStringsOrMarkupContent(result.ToObject<MarkedString>(serializer)!);
                }

                return new MarkedStringsOrMarkupContent(result.ToObject<MarkupContent>(serializer)!);
            }

            if (reader.TokenType == JsonToken.StartArray)
            {
                var result = JArray.Load(reader);
                return new MarkedStringsOrMarkupContent(result.ToObject<Container<MarkedString>>(serializer));
            }

            if (reader.TokenType == JsonToken.String)
            {
                return new MarkedStringsOrMarkupContent((reader.Value as string)!);
            }

            return new MarkedStringsOrMarkupContent("");
        }

        public override bool CanRead => true;
    }
}
