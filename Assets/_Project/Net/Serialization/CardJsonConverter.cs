using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tichu.Core.Cards;

namespace Tichu.Net
{
    /// <summary>
    /// Card(readonly 필드 struct) ↔ {"r":Rank,"s":Suit,"x":Special,"p":Points}.
    /// 네 필드를 다 싣는다 — 봉황(Rank 0, Points −25)처럼 Rank 로 다른 값을 유도할 수 없는 카드가 있다.
    /// </summary>
    public sealed class CardJsonConverter : JsonConverter<Card>
    {
        public override void WriteJson(JsonWriter writer, Card value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("r"); writer.WriteValue(value.Rank);
            writer.WritePropertyName("s"); writer.WriteValue(value.Suit.ToString());
            writer.WritePropertyName("x"); writer.WriteValue(value.Special.ToString());
            writer.WritePropertyName("p"); writer.WriteValue(value.Points);
            writer.WriteEndObject();
        }

        public override Card ReadJson(JsonReader reader, Type objectType, Card existingValue, bool hasExistingValue,
            JsonSerializer serializer)
        {
            var o = JObject.Load(reader);
            return new Card(
                Required(o, "r").Value<int>(),
                ParseEnum<Suit>(Required(o, "s")),
                ParseEnum<SpecialKind>(Required(o, "x")),
                Required(o, "p").Value<int>());
        }

        internal static JToken Required(JObject o, string key) =>
            o[key] ?? throw new ProtocolException($"필드 '{key}' 가 없습니다");

        internal static T ParseEnum<T>(JToken token) where T : struct
        {
            if (token.Type != JTokenType.String || !Enum.TryParse(token.Value<string>(), false, out T value)
                || !Enum.IsDefined(typeof(T), value))
                throw new ProtocolException($"{typeof(T).Name} 값이 올바르지 않습니다: {token}");
            return value;
        }
    }
}
