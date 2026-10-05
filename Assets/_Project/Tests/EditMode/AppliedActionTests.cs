using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// StateUpdate 가 자기를 유발한 액션을 싣는지, 그리고 교환 페이로드를 싣지 않는지.
    /// 네 좌석 모두 AI 라 원격 좌석이 없고(OnAwait 미발화) OnApply 경로만 돈다.
    /// </summary>
    [TestFixture]
    public class AppliedActionTests
    {
        private static List<ServerEvent> RunOneRound(out LoopbackTransport transport)
        {
            transport = new LoopbackTransport();
            var agents = new IAgent?[] { new AiAgent(1UL, 0), new AiAgent(1UL, 1), new AiAgent(1UL, 2), new AiAgent(1UL, 3) };
            var session = new MatchSession(agents, transport, masterSeed: 20261005UL, targetScore: 1);
            session.Start();
            Assert.That(session.Join(TimeSpan.FromSeconds(30)), Is.True, "세션이 30초 안에 끝나야 한다");
            return new List<ServerEvent>(transport.Inbox(0));
        }

        [Test]
        public void StateUpdate_carries_the_action_that_caused_it()
        {
            var events = RunOneRound(out _);

            var plays = new List<ServerEvent>();
            foreach (var ev in events)
                if (ev.Kind == ServerEventKind.StateUpdate
                    && ev.Applied != null
                    && ev.Applied.Kind == GameActionKind.Play)
                    plays.Add(ev);

            Assert.That(plays.Count, Is.GreaterThan(0), "한 라운드에 Play 가 한 번도 없을 수 없다");
            foreach (var ev in plays)
            {
                Assert.That(ev.Applied!.Cards, Is.Not.Null, "Play 는 카드를 실어야 한다");
                Assert.That(ev.Applied!.Cards!.Count, Is.GreaterThan(0));
                Assert.That(ev.Applied!.Seat, Is.InRange(0, 3));
            }
        }

        [Test]
        public void Pass_carries_no_cards_but_is_still_reported()
        {
            var events = RunOneRound(out _);

            int passes = 0;
            foreach (var ev in events)
            {
                if (ev.Kind != ServerEventKind.StateUpdate || ev.Applied == null) continue;
                if (ev.Applied.Kind != GameActionKind.Pass) continue;
                passes++;
                Assert.That(ev.Applied.Cards, Is.Null, "Pass 는 카드가 없다");
            }

            Assert.That(passes, Is.GreaterThan(0), "한 라운드에 패스가 한 번도 없을 수 없다");
        }

        [Test]
        public void Exchange_never_carries_card_payload()
        {
            var events = RunOneRound(out _);

            int exchanges = 0;
            foreach (var ev in events)
            {
                if (ev.Kind != ServerEventKind.StateUpdate || ev.Applied == null) continue;
                if (ev.Applied.Kind != GameActionKind.Exchange) continue;
                exchanges++;
                Assert.That(ev.Applied.Cards, Is.Null,
                    "교환 카드를 액션 요약에 실으면 좌석별 마스킹이 우회된다");
            }

            // 경로가 실제로 돌았는지 먼저 확인한다 — 이 단언이 없으면 공허한 가드가 된다.
            Assert.That(exchanges, Is.EqualTo(4), "교환은 좌석별로 네 번 적용된다");
        }

        [Test]
        public void Inter_round_pause_is_cancellable()
        {
            var transport = new LoopbackTransport();
            var agents = new IAgent?[] { new AiAgent(1UL, 0), new AiAgent(1UL, 1), new AiAgent(1UL, 2), new AiAgent(1UL, 3) };
            // targetScore 1000 → 여러 라운드. 멈춤을 길게 둬 첫 라운드 뒤에 반드시 그 안에서 잡힌다.
            var session = new MatchSession(agents, transport, masterSeed: 7UL, targetScore: 1000,
                interRoundPause: TimeSpan.FromSeconds(30));
            session.Start();

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            bool scored = false;
            while (!scored && DateTime.UtcNow < deadline)
            {
                foreach (var ev in transport.Drain(0))
                    if (ev.Kind == ServerEventKind.RoundScored) scored = true;
            }
            Assert.That(scored, Is.True, "첫 라운드가 정산돼야 한다");

            session.Stop();
            // Thread.Sleep 으로 멈추면 30초를 다 기다리므로 여기서 false 가 된다.
            Assert.That(session.Join(TimeSpan.FromSeconds(5)), Is.True,
                "라운드 간 멈춤은 Stop() 으로 깨울 수 있어야 한다");
        }
    }
}
