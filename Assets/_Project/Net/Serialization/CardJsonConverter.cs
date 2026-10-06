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
            int rank = Required(o, "r").Value<int>();
            var suit = ParseEnum<Suit>(Required(o, "s"));
            var special = ParseEnum<SpecialKind>(Required(o, "x"));
            Required(o, "p"); // 형식 유지용으로 읽기만 한다 — 값은 신뢰하지 않는다(Card.Equals 가 Points 를 보지 않아 위조 가능)

            // 정규 카드를 (r, s, x) 로 다시 만든다. 클라이언트가 보낸 Points 는 버린다.
            Card canon;
            switch (special)
            {
                case SpecialKind.Mahjong: canon = Card.Mahjong; break;
                case SpecialKind.Dog: canon = Card.Dog; break;
                case SpecialKind.Phoenix: canon = Card.Phoenix; break;
                case SpecialKind.Dragon: canon = Card.Dragon; break;
                default:
                    // 안 쓰는 교환 슬롯의 default(Card)(r0·Jade·None). 실제 카드가 아니라 어느 손패와도 일치하지 않는다.
                    if (rank == 0 && suit == Suit.Jade) return default(Card);
                    if (suit == Suit.Special || rank < 2 || rank > 14)
                        throw new ProtocolException($"올바르지 않은 카드입니다: {o}");
                    return Card.Normal(rank, suit);
            }
            if (suit != Suit.Special || rank != canon.Rank)
                throw new ProtocolException($"올바르지 않은 특수 카드입니다: {o}");
            return canon;
        }
        internal static JToken Required(JObject o, string key) =>
            o[key] ?? throw new ProtocolException($"필드 '{key}' 가 없습니다");

        internal static T ParseEnum<T>(JToken token) where T : struct
        {
            if (token.Type != JTokenType.String || !Enum.TryParse(token.Value<string>(), false, out T value)
                || !Enum.IsDefined(typeof(T), token.Value<string>())
                || !Enum.IsDefined(typeof(T), value))
                throw new ProtocolException($"{typeof(T).Name} 값이 올바르지 않습니다: {token}");
            return value;
        }
    }
}

