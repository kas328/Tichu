using System;
using Tichu.Server;

ServerOptions opt;
try { opt = ServerOptions.Parse(args); }
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine("사용법: --seats 0,2 --difficulty Hard --target 1000 --port 7777 [--ai-delay 900] [--seed N]");
    return 2;
}

var app = ServerApp.Build(opt, $"http://0.0.0.0:{opt.Port}");
Console.WriteLine($"Tichu 서버 — 인간 좌석 {string.Join(",", opt.Seats)} · {opt.Difficulty} · 목표 {opt.Target}");
Console.WriteLine($"접속 주소: ws://<이 PC 의 IP>:{opt.Port}/ws  (같은 Wi-Fi)");
Console.WriteLine("매치가 끝나면 새 대기실을 연다(Ctrl+C로 종료)");
await app.RunAsync();
return 0;
