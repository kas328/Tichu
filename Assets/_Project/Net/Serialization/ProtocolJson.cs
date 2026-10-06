using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Tichu.Net
{
    /// <summary>
    /// 프로토콜 메시지 ↔ JSON 텍스트. 서버(dotnet)와 유니티(IL2CPP)가 이 한 곳을 공유한다.
    /// WebSocket 텍스트 프레임 하나 = 메시지 하나이고 봉투가 없다 — 두 메시지 모두 Kind 구분자를 가진다.
    /// 열거형은 이름 문자열로 싣는다(로그 판독 + 모르는 값을 예외로 거른다).
    /// 해석 실패는 전부 ProtocolException 으로 바꾼다.
    /// </summary>
    public static class ProtocolJson
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter(), new CardJsonConverter(), new CombinationJsonConverter() },
            NullValueHandling = NullValueHandling.Ignore,
            // 기본값(Auto)은 생성자가 만든 빈 리스트에 이어 붙인다 — 받은 값으로 갈아끼운다.
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        private static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        public static string Serialize(ServerEvent ev) => JsonConvert.SerializeObject(ev, Settings);

        public static string Serialize(ClientAction a) => JsonConvert.SerializeObject(a, Settings);

        public static ServerEvent DeserializeServerEvent(string json) => Deserialize<ServerEvent, ServerEventKind>(json);

        public static ClientAction DeserializeClientAction(string json) => Deserialize<ClientAction, ClientActionKind>(json);

        private static T Deserialize<T, TKind>(string json) where T : class where TKind : struct
        {
            try
            {
                if (!(JToken.Parse(json) is JObject o)) throw new ProtocolException("메시지는 JSON 객체여야 합니다");
                var kind = o["Kind"] ?? throw new ProtocolException("Kind 가 없습니다");
                CardJsonConverter.ParseEnum<TKind>(kind);
                return o.ToObject<T>(Serializer) ?? throw new ProtocolException("빈 메시지");
            }
            catch (ProtocolException) { throw; }
            catch (Exception e) { throw new ProtocolException(e.Message, e); }
        }
    }
}
