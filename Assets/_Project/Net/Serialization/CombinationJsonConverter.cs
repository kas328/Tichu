using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.Net
{
    /// <summary>Combination(생성자 전용 불변 클래스) ↔ {"t":Type,"c":[카드],"l":Length,"r":Rank,"p":PointsInPlay}.</summary>
    public sealed class CombinationJsonConverter : JsonConverter<Combination>
    {
        public override void WriteJson(JsonWriter writer, Combination? value, JsonSerializer serializer)
        {
            if (value == null) { writer.WriteNull(); return; }
            writer.WriteStartObject();
            writer.WritePropertyName("t"); writer.WriteValue(value.Type.ToString());
            writer.WritePropertyName("c"); serializer.Serialize(writer, value.Cards);
            writer.WritePropertyName("l"); writer.WriteValue(value.Length);
            writer.WritePropertyName("r"); writer.WriteValue(value.Rank);
            writer.WritePropertyName("p"); writer.WriteValue(value.PointsInPlay);
            writer.WriteEndObject();
        }

        public override Combination? ReadJson(JsonReader reader, Type objectType, Combination? existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var o = JObject.Load(reader);
            var cards = CardJsonConverter.Required(o, "c").ToObject<List<Card>>(serializer)
                        ?? throw new ProtocolException("조합 카드가 없습니다");
            return new Combination(
                CardJsonConverter.ParseEnum<CombinationType>(CardJsonConverter.Required(o, "t")),
                cards,
                CardJsonConverter.Required(o, "l").Value<int>(),
                CardJsonConverter.Required(o, "r").Value<int>(),
                CardJsonConverter.Required(o, "p").Value<int>());
        }
    }
}
