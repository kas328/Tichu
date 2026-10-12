using System;
using Tichu.Server;

ServerOptions opt;
try { opt = ServerOptions.Parse(args); }
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine("사용법: --difficulty Hard --target 1000 --port 7777 [--host 0.0.0.0] [--fill-after 30] [--max-rooms 2] [--turn-seconds 20] [--empty-room-seconds 120] [--ai-delay 900] [--seed N]");
    return 2;
}

var app = ServerApp.Build(opt, $"http://{opt.Host}:{opt.Port}");
Console.WriteLine($"Tichu 서버 — 랜덤 매칭 · {opt.Difficulty} · 목표 {opt.Target} · 충원 {opt.FillAfterSeconds}초 · 방 상한 {opt.MaxRooms} · 턴 {opt.TurnSeconds}초");
Console.WriteLine($"접속 주소: ws://<이 PC 의 IP>:{opt.Port}/ws  (같은 Wi-Fi)");
Console.WriteLine("Ctrl+C 로 종료");
await app.RunAsync();
return 0;
