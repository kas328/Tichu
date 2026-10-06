using System;
using Tichu.Server;

var opt = ServerOptions.Parse(args);
Console.WriteLine($"Tichu 서버 설정: 좌석 {string.Join(",", opt.Seats)} · {opt.Difficulty} · 목표 {opt.Target} · 포트 {opt.Port}");
