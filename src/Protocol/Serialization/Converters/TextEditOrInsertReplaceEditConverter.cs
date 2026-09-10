using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace OmniSharp.Extensions.LanguageServer.Protocol.Serialization.Converters
{
    public class TextEditOrInsertReplaceEditConverter : JsonConverter<TextEditOrInsertReplaceEdit>
    {
        public override void WriteJson(JsonWriter writer, TextEditOrInsertReplaceEdit value, JsonSerializer serializer)
        {
            if (value.IsTextEdit)
            {
                serializer.Serialize(writer, value.TextEdit);
            }
            else if (value.IsInsertReplaceEdit)
            {
                serializer.Serialize(writer, value.InsertReplaceEdit);
            }
            else
            {
                writer.WriteNull();
            }
        }

        public override TextEditOrInsertReplaceEdit ReadJson(JsonReader reader, Type objectType, TextEditOrInsertReplaceEdit existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null!;
            }

            var result = JObject.Load(reader);

            // A TextEdit is `{ range, newText }`, an InsertReplaceEdit is `{ insert, replace, newText }`.
            // Both `insert` and `replace` are Range *objects*, so the union has to be discriminated on
            // their presence rather than on any value they might hold.
            if (result["insert"] is JObject || result["replace"] is JObject)
            {
                return new TextEditOrInsertReplaceEdit(result.ToObject<InsertReplaceEdit>(serializer)!);
            }

            return new TextEditOrInsertReplaceEdit(result.ToObject<TextEdit>(serializer)!);
        }

        public override bool CanRead => true;
    }
}
