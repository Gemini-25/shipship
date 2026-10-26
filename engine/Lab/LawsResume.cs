using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>
/// 이어 보기 법칙 (docs/OBSERVER.md §16 S1 · S2 — 묶음 '이어 보기'): 항해 묶음 (세계 · 연출 기록 · 손 기록 · 화면)을 쓰고 되살려 이어 돌리면
/// 이어 보지 않은 쪽과 세계 바이트 · 하이라이트 · 장 · 연출 기록 · 손 기록이 같다 · 다시보기는 한 손 장부 256을 넘은 손도 항해 손 기록에서 먹인다 ·
/// 주컴퓨터 일꾼이 셈 중인 틱에 저장해도 받을 틱 · 결과가 같다.
/// </summary>
public static partial class Program
{
    /// <summary>연출 기록 바이트 (두 연출이 같은 상태인가 — 사전 · 모음은 열쇠 차례로 쓴다).</summary>
    internal static byte[] RecordOf(Director d) { var w = new StateWriter(1 << 16); d.SaveRecord(w); return w.ToArray(); }

    /// <summary>하이라이트 한 줄씩 (정점 · 시작 · 끝 · 칸 · 점수 · 갈래 · 제목 · 박자 수).</summary>
    internal static string HighlightsOf(Director d) => string.Join(" | ", d.Highlights.Select(x => $"{x.Tick}/{x.StartTick}/{x.Last}/{x.Cell}/{x.Score:0.###}/{x.Kind}/{x.Title}/{x.Beats?.Count ?? -1}"));

    /// <summary>손 기록 한 줄씩 (차례 · 틱 · 갈래 · 칸 · 원인).</summary>
    internal static string HandsOf(IEnumerable<HandOrder> l) => string.Join(" ", l.Select(o => $"{o.Seq}@{o.Tick}:{o.Kind}/{o.Cell}/{o.Cause}"));

    /// <summary>시험용 항해 곳간 (임시 폴더 — 끝나면 지운다).</summary>
    internal static string TempVoyages() => Path.Combine(Path.GetTempPath(), $"shipship-voyages-{Environment.ProcessId}-{Guid.NewGuid():N}");

    private static void ResumeLaws()
    {
        if (!Do("이어 보기")) return;
        Console.WriteLine("\n[이어 보기 — 항해 묶음 왕복 · 항해 손 기록 · 일꾼 셈 중 저장]");
        int keep = Par.Threads;
        string dir = TempVoyages();
        try
        {
            // S1: 왕복 — 디딤 6 · 로봇 2 · 손 둘 (40 불씨 · 120 운석) 낀 260틱 → 묶음 → 곳간 → 이어 보기 → 둘 다 150틱 (설계의 1500 · 600틱을 줄였다 — 불이 타고 운석 사건이 열린 채 · 미리 보기 세계가 있는 때에 찍는다)
            //     (한 줄로: 주컴퓨터 일꾼을 기다린 틱에는 연출이 미리 보기 복제를 한 틱 미룬다 — 벽시계라 왕복과 상관없이 두 쪽이 갈린다 · 일꾼 셈 중 저장은 S2)
            {
                Par.Threads = 1;
                var a = new LiveShow(HandShip(51), "디딤호");
                var aim = HandTargets(a.Sim); var h = a.Sim.Get<Hand>();
                a.FastForward(40 - a.Sim.Tick); h.Enqueue(Order(HandKind.Fire, aim.Bed, 2));
                a.FastForward(120 - a.Sim.Tick); h.Enqueue(Order(HandKind.Rock, aim.Hull, 1));
                a.FastForward(260 - a.Sim.Tick);
                var store = new VoyageStore(dir);
                var meta = store.Write(a.SaveBundle(SaveKind.Quit));
                var b = LiveShow.Resume(store, store.Latest() ?? meta, out string why);
                bool at = b != null && b.Sim.Tick == a.Sim.Tick && b.Dir.Highlights.Count == a.Dir.Highlights.Count;
                bool ahead = a.Dir.Ahead != null && b?.Dir.Ahead != null;   // 미리 보기 세계도 담았다
                a.FastForward(150); b?.FastForward(150);
                bool world = b != null && a.Sim.Save().AsSpan().SequenceEqual(b.Sim.Save());
                bool record = b != null && RecordOf(a.Dir).AsSpan().SequenceEqual(RecordOf(b.Dir));
                string ha = HighlightsOf(a.Dir), hb = b != null ? HighlightsOf(b.Dir) : "";
                string ca = a.Dir.ChapterNow(a.Sim) is { } cha ? cha.Title + " / " + string.Join(" / ", cha.Lines) : "", cb = b != null && b.Dir.ChapterNow(b.Sim) is { } chb ? chb.Title + " / " + string.Join(" / ", chb.Lines) : "";
                string la = HandsOf(a.HandLog), lb = b != null ? HandsOf(b.HandLog) : "";
                Check("이어 보기 S1: 왕복 — 디딤호 6명 · 로봇 2 · 손 둘 (40 불씨 · 120 운석) 낀 260틱을 묶음으로 곳간에 쓰고 이어 본 뒤 둘 다 150틱: 세계 바이트 · 하이라이트 · 오늘 장 · 연출 기록 · 손 기록이 같다",
                    at && ahead && world && record && a.Dir.Highlights.Count > 0 && ha == hb && ca == cb && a.HandLog.Count == 2 && la == lb,
                    $"이어 봄 {(b != null ? "됨" : "안 됨 — " + why)} · 미리 보기 세계 {ahead} · 세계 같음 {world} · 연출 기록 같음 {record} · 하이라이트 {a.Dir.Highlights.Count} 같음 {ha == hb} · 장 같음 {ca == cb} '{(ca.Length > 60 ? ca[..60] : ca)}' · 손 기록 {a.HandLog.Count} 같음 {la == lb}");
            }
            // S1 (불씨 원인): 불씨를 붙인 틱 바로 뒤에 찍어 되살려도 다음 틱 찍기 바이트가 같다 — 불씨 원인의 두 버퍼 가운데 다음 칸을 틱마다 비운다
            //     (고치기 전: 불씨 칸만 비우고 원인 칸은 두 틱 전 값을 그대로 두어 · 찍지 않는 버퍼라 되살린 세계와 다음 틱 찍기가 갈렸다 — 이어 본 연출의 미리 보기 세계에서 드러났다)
            {
                Par.Threads = 1;
                var s = HandShip(54); s.Run(4);
                var aim = HandTargets(s);
                s.Get<Hand>().Enqueue(Order(HandKind.Fire, aim.Bed, 2)); s.Step();
                var c = s.Clone();
                int firstBad = -1;
                for (int k = 0; k < 4 && firstBad < 0; k++) { s.Step(); c.Step(); if (!s.Save().AsSpan().SequenceEqual(c.Save())) firstBad = k + 1; }
                Check("이어 보기 S1 (불씨 원인): 불씨를 붙인 틱 바로 뒤에 찍어 되살린 세계와 원래 세계가 네 틱 동안 찍기 바이트까지 같다 (찍지 않는 다음 버퍼에 지난 원인을 남기지 않는다)",
                    firstBad < 0, firstBad < 0 ? "같음" : $"{firstBad}틱 뒤 갈라짐");
            }
            // S1 (다시보기): 고리 찍기 뒤 불씨 하나 + 헛손 320 (한 손 장부 256을 넘긴다) — 다시보면 되살린 세계가 항해 손 기록에서 손을 먹어 같은 틱 찍기 바이트가 같다
            //     (고치기 전: 한 손 장부만 먹여 먼저 들어온 손 64개를 잃고 받은 차례 · 장부가 달랐다)
            {
                Par.Threads = 1;
                var show = new LiveShow(HandShip(52), "디딤호") { FrameBudget = 1000, SpeedOverride = 1 };
                for (int i = 0; i < 3; i++) show.Advance(1);
                long t0 = show.Dir.Ring[^1].tick;
                var aim = HandTargets(show.Sim); var hand = show.Sim.Get<Hand>();
                int junk = 0;
                while (show.Sim.Tick < t0 + 60)
                {
                    long t = show.Sim.Tick - t0;
                    if (t == 3 && hand.Done.Count == 0 && hand.Waiting.Count == 0) hand.Enqueue(Order(HandKind.Fire, aim.Bed, 2));
                    if (t >= 5 && junk < 320 && hand.Waiting.Count == 0) for (int k = 0; k < Hand.MaxWaiting && junk < 320; k++, junk++) hand.Enqueue(Order(HandKind.PowerCut, -1));   // 헛손 (전선이 없는 칸 — 장부에만 남는다)
                    show.Advance(1);
                }
                while (show.Sim.Get<Hand>().Waiting.Count > 0) show.Advance(1);
                show.Sim.Get<ShipMind>().WaitThoughts();
                var want = show.Sim.Save(); long tAt = show.Sim.Tick;
                for (int i = 0; i < 2000 && show.Sim.Tick < tAt + 5; i++) show.Advance(1);
                show.StartReplay(new Highlight { Tick = show.Sim.Tick, StartTick = t0, Last = show.Sim.Tick, Title = "헛손" });
                while (show.Replaying && show.World.Tick < tAt) show.Advance(1);
                if (show.Replaying) show.World.Get<ShipMind>().WaitThoughts();
                bool same = show.Replaying && show.World.Tick == tAt && show.World.Save().AsSpan().SequenceEqual(want);
                Check("이어 보기 S1 (다시보기): 고리 찍기 뒤 불씨 하나 + 헛손 320 (한 손 장부 256을 넘긴다) — 다시보면 되살린 세계가 항해 손 기록에서 손을 먹어 같은 틱 찍기 바이트가 같다",
                    junk == 320 && show.HandLog.Count == 321 && same, $"손 기록 {show.HandLog.Count} (한 손 장부 {hand.Done.Count}) · 다시보기 {show.World.Tick}/{tAt}틱 · 같음 {same}");
            }
            // S2: 주컴퓨터 일꾼이 셈 중인 틱에 저장 (일꾼을 0.3초 늦춘다) → 묶음 바이트로 되살려 둘 다 1분: 받을 틱 · 그려 본 수 · 세계 바이트가 같다 (계산 일꾼 법칙의 뚫린 방 차림)
            {
                Par.Threads = Math.Max(2, keep); ShipMind.WorkerDelayMs = 300;
                var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
                int At(int x, int y) => Worlds.At(lay, x, y);
                var w = Worlds.Full(lay, 1);
                var mw = w.Get<Mind>(); var lw = w.Get<Life>();
                int pa = mw.AddPerson(w, At(6, 2)), pb = mw.AddPerson(w, At(13, 2));
                foreach (int p in new[] { pa, pb }) { ref var body = ref lw[mw[p].C.Body]; body.Conscious = 0; body.Hypoxia = 0.6f; }
                w.Get<Structure>().RequestDoor(lay.DoorAt[At(10, 3)], 1); w.Run(3);
                w.Get<Field>().SetHole(At(0, 2), 0.05f);
                var a = new LiveShow(w, "시험호"); var cm = w.Get<ShipMind>();
                for (int k = 0; k < Time.Minutes(2) && cm.Thinking == 0; k++) a.FastForward(1);
                bool busy = cm.WorkerBusy;
                long saveAt = a.Sim.Tick;
                var bytes = a.SaveBundle(SaveKind.Quit).ToBytes();
                var bundle = VoyageBundle.FromBytes(bytes, out string why);
                var b = bundle != null ? LiveShow.Resume(bundle, w.Setup) : null;
                long Took(LiveShow s) { var m = s.Sim.Get<ShipMind>(); int n0 = m.Imaginations.Count; for (int k = 0; k < Time.Minutes(1); k++) { s.FastForward(1); if (m.Imaginations.Count > n0) return s.Sim.Tick; } return -1; }
                long ta = Took(a), tb = b != null ? Took(b) : -2;
                if (b != null) { long end = saveAt + Time.Minutes(1); a.FastForward(end - a.Sim.Tick); b.FastForward(end - b.Sim.Tick); }
                bool same = b != null && a.Sim.Save().AsSpan().SequenceEqual(b.Sim.Save());
                int ia = cm.Imaginations.Count, ib = b?.Sim.Get<ShipMind>().Imaginations.Count ?? -1;
                Check("이어 보기 S2: 주컴퓨터 일꾼이 셈 중인 틱에 묶음을 짓고 (찍기가 셈 끝을 기다려 결과를 함께 담는다) 되살려 둘 다 1분 — 받을 틱 · 그려 본 수 · 세계 바이트가 같다",
                    busy && ta > 0 && ta == tb && ia == ib && ia >= 2 && same,
                    $"저장 때 셈 중 {busy} · 받은 틱 {ta}/{tb} (저장 {saveAt}) · 그려 봄 {ia}/{ib} · 세계 같음 {same}{(bundle == null ? " · 묶음 읽기 실패: " + why : "")}");
            }
        }
        finally
        {
            Par.Threads = keep; ShipMind.WorkerDelayMs = 0;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
