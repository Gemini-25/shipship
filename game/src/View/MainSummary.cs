using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v12.8 요약 진행: 평온한 날들은 화면 없이 빨리 감고 (한 프레임에 몇천 틱), 큰 일이 나면 그 자리에서 멈춘다.
// 멈추면 그동안 무엇이 있었는지를 한 장으로 — 며칠, 고장·수리, 실수·다툼, 기술, 지나온 구간, 교역.
public partial class Main
{
    public bool Summarizing { get; private set; }
    private long _sumStart, _sumUntil;
    private int _sumIncidents, _sumFaults, _sumMistakes, _sumArgs, _sumTechs, _sumLeg, _sumVoyage, _sumDeaths, _sumHarvest;

    private void StartSummaryDeferred(float days) => ToggleSummary(days);

    public void ToggleSummary(float days = 3f)
    {
        if (Summarizing) { StopSummary("멈춤 — 직접 멈췄다"); return; }
        if (Replaying != null) return;
        Summarizing = true;
        Paused = false;
        _sumStart = Sim.Tick;
        _sumUntil = Sim.Tick + (long)(days * SimTime.TicksPerDay);
        _sumIncidents = Sim.Causes.Incidents.Count;
        _sumFaults = Sim.Ship.Machines.Sum(m => m.FaultCount);
        _sumMistakes = Sim.Life.Stats.Mistakes;
        _sumArgs = Sim.Life.Stats.Arguments;
        _sumTechs = Sim.Eras.Known.Count;
        _sumLeg = Sim.Voyage.Index;
        _sumVoyage = Sim.Voyage.Number;
        _sumDeaths = Sim.History.Deaths;
        _sumHarvest = Sim.Crew.Sum(c => c.Stats.Harvests);
        Hud.SummaryLines = null;
        ShowNotice($"빨리 감기 — 큰 일이 날 때까지, 길어야 {days:0}일");
    }

    /// <summary>요약 진행 중이면 이 프레임을 대신 돌린다 (true면 보통 진행은 건너뛴다).</summary>
    private bool StepSummary()
    {
        if (!Summarizing) return false;
        for (int chunk = 0; chunk < 40; chunk++)
        {
            for (int i = 0; i < 100; i++) Sim.Step();
            var big = Sim.Causes.Incidents.Skip(_sumIncidents).FirstOrDefault(i => i.Open && i.Weight(Sim.Causes) >= 3);
            if (big != null) { StopSummary($"멈춤 — {Sim.Causes.Node(big.Root).Text}"); return true; }
            if (Sim.Tick >= _sumUntil) { StopSummary("다 감았다"); return true; }
        }
        return true;
    }

    private void StopSummary(string why)
    {
        Summarizing = false;
        SetSpeed(0);
        float days = (Sim.Tick - _sumStart) / (float)SimTime.TicksPerDay;
        var lines = new List<string>
        {
            $"{days:0.0}일을 감았다 — 지금 {Sim.Day}일차 {Sim.Clock}",
            $"사고 {Sim.Causes.Incidents.Count - _sumIncidents} · 고장 {Sim.Ship.Machines.Sum(m => m.FaultCount) - _sumFaults} · 수확 {Sim.Crew.Sum(c => c.Stats.Harvests) - _sumHarvest}",
            $"실수 {Sim.Life.Stats.Mistakes - _sumMistakes} · 말다툼 {Sim.Life.Stats.Arguments - _sumArgs}" + (Sim.History.Deaths > _sumDeaths ? $" · 사망 {Sim.History.Deaths - _sumDeaths}" : ""),
        };
        int legs = (Sim.Voyage.Number - _sumVoyage) * 100 + Sim.Voyage.Index - _sumLeg;
        if (Sim.Voyage.Number > _sumVoyage) lines.Add($"항해를 마치고 {Sim.Voyage.Number}번째 항해에 나섰다");
        else if (legs > 0) lines.Add($"구간 {legs}개를 지났다 — 지금 {VoyageSystem.KindName(Sim.Voyage.Current.Kind)}");
        if (Sim.Eras.Known.Count > _sumTechs) lines.Add($"새 기술 {Sim.Eras.Known.Count - _sumTechs}: " + string.Join(", ", Sim.Eras.Order.Skip(_sumTechs).Select(id => EraSystem.Find(id)?.Name ?? id)));
        lines.Add(why);
        Hud.SummaryLines = lines.ToArray();
        WatchAlerts();
    }
}
